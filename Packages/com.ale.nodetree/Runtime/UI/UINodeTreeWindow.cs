using System.Collections.Generic;
using Ale.Toolkit.Runtime;
using UnityEngine;

namespace Ale.NodeTree.Runtime
{
    /// <summary>
    /// 游戏运行时节点树UI主窗口（独立 MonoBehaviour）。
    /// 负责：
    ///  - 根据所有节点的位置和尺寸自动计算并设置 nodeTreeRoot 的 Size；
    ///  - 按节点类型为节点UI维护 ToolkitGameObjectPool 对象池；
    ///  - 通过视口裁剪按需 Spawn/Despawn 节点UI；
    ///  - 为每种节点类型合并生成连线 Mesh（减少 DrawCall），
    ///    UV.x 用于边缘渐变，UV.y 用于流动效果（配合 NodeLineFlow.shader）；
    ///  - 在 LateUpdate 中按脏标记按需重建连线 Mesh。
    /// </summary>
    public class UINodeTreeWindow : MonoBehaviour
    {
        [Header("基础配置")]
        [Tooltip("节点树配置资产：包含节点实例、节点类型定义等数据。")]
        [SerializeField] private NodeTreeData config;

        [Tooltip("节点树UI根容器（通常为 ScrollView 的 Content 节点）。\n" +
                 "运行时会在此节点下自动创建 LineContainer（连线层，最底层）\n" +
                 "和 NodeContainer（节点层，最顶层），并自动计算根容器的 Size。")]
        [SerializeField] private RectTransform nodeTreeRoot;
        [Tooltip("节点树四周内边距（x=左, y=上, z=右, w=下）。\n" +
                 "在所有节点的外围增加留白，避免节点被 ScrollView 边缘裁剪。")]
        [SerializeField] private Vector4 nodeTreePadding = new Vector4(100f, 100f, 100f, 100f);

        [Tooltip("打开（InitTree）时自动刷新所有节点的自动状态标签（如 Unlock）：\n" +
                 "按各节点条件重算并挂上（达成即挂、单调不摘、支持链式解锁）。\n" +
                 "这是最简单直接的刷新方式；也可关闭后由业务在合适时机手动调用 RefreshAllNodeStates()。")]
        [SerializeField] private bool refreshStatesOnInit = true;

        [Tooltip("【测试用】全部解锁：InitTree 时为每个节点直接挂 Unlock 标签（绕过条件判定），\n" +
                 "便于查看整棵树的完整结构。标签只增不改存档数据本身；发布前应保持关闭。")]
        [SerializeField] private bool unlockAllForTest;

        /// <summary>
        /// 将编辑器画布坐标系下的节点位置整体偏移到 nodeTreeRoot 局部坐标系。
        /// 由 CalcAndSetRootSize() 计算，使最顶/最左节点的边缘紧贴内边距。
        /// </summary>
        private Vector2 _nodePositionOffset;
        // 当前选中的节点 ID
        private string _selectedNodeId;

        // 视口裁剪脏标记与上次刷新时的容器世界坐标 / 屏幕尺寸（用于按需刷新，避免每帧遍历全部节点）。
        private bool    _visibilityDirty = true;
        private Vector3 _lastContainerPos;
        private Vector3 _lastContainerScale; // 上次刷新时容器世界缩放（缩放变化也触发重算）
        private Vector2 _lastScreenSize;

        // ── Unity 生命周期 ──

        private void Awake()
        {
            EnsureCamera();
            if (config) InitTree();
        }

        private void LateUpdate()
        {
            if (_lineMeshDirty)
            {
                RebuildAllLineMeshes();
                _lineMeshDirty = false;
            }

            // 视口裁剪仅在容器移动/缩放（_nodeContainer 世界坐标变化）或屏幕尺寸变化时刷新，
            // 静止时不再每帧遍历全部节点。
            if (_nodeContainer)
            {
                Vector3 pos    = _nodeContainer.position;
                Vector3 scale  = _nodeContainer.lossyScale;
                Vector2 screen = new Vector2(Screen.width, Screen.height);
                // 位置、缩放（如缩放画布 localScale）或屏幕尺寸变化时才重算，静止时不遍历全部节点
                if (_visibilityDirty || pos != _lastContainerPos || scale != _lastContainerScale
                    || screen != _lastScreenSize)
                {
                    RefreshVisibility();
                    _lastContainerPos   = pos;
                    _lastContainerScale = scale;
                    _lastScreenSize     = screen;
                    _visibilityDirty    = false;
                }
            }
        }

        private void OnDestroy()
        {
            // 1) 归还所有激活节点克隆到各自对象池：清除 ToolkitPool.Links 静态引用，
            //    并把散落在（可能位于本组件之外的）nodeTreeRoot 下的克隆收回，随后随池一并销毁。
            foreach (var pool in _pools.Values)
                if (pool) pool.DespawnAll();
            _activeNodes.Clear();

            // 2) 销毁连线材质实例与合并 Mesh（原生对象不被 GC，否则每次销毁泄漏一个/类型）。
            foreach (var mat in _lineMaterialInstances.Values)
                if (mat) Destroy(mat);
            _lineMaterialInstances.Clear();

            foreach (var cr in _lineCanvasRenderers.Values)
            {
                if (!cr) continue;
                var mesh = cr.GetMesh();
                if (mesh) Destroy(mesh);
            }
            _lineCanvasRenderers.Clear();

            // 3) 销毁本窗口在 nodeTreeRoot 下创建的容器与对象池 GameObject。
            //    nodeTreeRoot 若挂在本组件之外（如 ScrollView-Content 用法），这些子物体不会随本组件销毁，
            //    需显式清理，避免残留空层级与孤儿克隆。
            if (_nodeContainer) Destroy(_nodeContainer.gameObject);
            if (_lineContainer) Destroy(_lineContainer.gameObject);
            _nodeContainer = null;
            _lineContainer = null;

            foreach (var pool in _pools.Values)
                if (pool) Destroy(pool.gameObject);
            _pools.Clear();
        }

        #region 公开接口
        /// <summary>
        /// 初始化整棵节点树UI：
        /// 1. 校验配置数据；2. 创建容器；3. 计算并设置根容器 Size；
        /// 4. 初始化对象池；5. 初始化连线 Mesh 渲染器；6. 标记连线脏。
        /// configOverride 不为 null 时替换当前配置资产。
        /// </summary>
        public void InitTree(NodeTreeData configOverride = null)
        {
            if (configOverride) config = configOverride;
            if (!config)
            {
                Debug.LogWarning("[UINodeTreeWindow] config（NodeTreeData）未赋值，无法初始化节点树。", this);
                return;
            }

            config.InvalidateLookup(); // 重建查找缓存，确保反映最新（或切换后的）配置数据
            ValidateConfig();
            EnsureContainers();
            CalcAndSetRootSize(); // 计算 sizeDelta 与节点偏移量（依赖 EnsureContainers 已执行）
            EnsureCamera();
            InitPools();
            InitLineMeshRenderers();
            _lineMeshDirty   = true;
            _visibilityDirty = true; // 强制下一帧刷新一次可见性

            // 【测试用】全部解锁：绕过条件为每个节点直接挂 Unlock 标签。
            // 放在状态刷新之前，链式条件（NodeUnlocked 等）能立即看到这些标签。
            if (unlockAllForTest)
            {
                foreach (var node in config.nodes)
                    NodeTreeSaveDataManager.Instance.AddTag(node.nodeId, NodeTreeTags.Unlock);
            }

            // 打开时刷新所有节点的自动状态标签（最简单直接的刷新方式）
            if (refreshStatesOnInit)
                NodeTreeSaveDataManager.Instance.RefreshAllNodeStates(config);
        }

        /// <summary>
        /// 刷新所有节点的自动状态标签（转调 <see cref="NodeTreeSaveDataManager.RefreshAllNodeStates"/>）。
        /// 供业务在合适时机（如打开面板、加载存档后）手动触发；InitTree 亦会按 refreshStatesOnInit 自动调用。
        /// </summary>
        public void RefreshAllNodeStates()
        {
            if (config) NodeTreeSaveDataManager.Instance.RefreshAllNodeStates(config);
        }

        /// <summary>
        /// 选中指定节点。取消前一个选中节点的高亮，并通知新节点被选中。
        /// 若传入相同 nodeId 则无操作。
        /// </summary>
        public void SelectNode(string nodeId)
        {
            if (_selectedNodeId == nodeId) return;

            // _selectedNodeId 初始为 null，Dictionary.TryGetValue(null) 会抛 ArgumentNullException，
            // 故先判空再查激活列表。
            if (!string.IsNullOrEmpty(_selectedNodeId)
                && _activeNodes.TryGetValue(_selectedNodeId, out var prev))
                prev.OnNodeDeselected();

            _selectedNodeId = nodeId;

            if (!string.IsNullOrEmpty(_selectedNodeId)
                && _activeNodes.TryGetValue(_selectedNodeId, out var next))
                next.OnNodeSelected();
        }
        
        /// <summary>
        /// 将节点的编辑器画布坐标（已加 _nodePositionOffset）转换为连线 Mesh 顶点局部坐标。
        /// LineContainer 和 NodeContainer 均以 stretch 模式附着于 nodeTreeRoot，
        /// 因此三者局部坐标系完全一致，直接使用偏移后的 UI 坐标即可。
        /// </summary>
        private Vector3 NodeDataToLineLocalPos(NodeData node)
        {
            return new Vector3(
                node.position.x + _nodePositionOffset.x,
                node.position.y + _nodePositionOffset.y,
                0f);
        }
        #endregion
        
        #region 初始化
        // 两者均使用 RectTransform stretch 模式，自动跟随 nodeTreeRoot 的 Size 变化。
        private RectTransform _lineContainer; // 连线 Mesh 层，firstSibling（渲染在节点之下）
        private RectTransform _nodeContainer; // 节点 UI 层，lastSibling（渲染在连线之上）
        private Canvas        _rootCanvas;    // 所属 Canvas（缓存）：视口裁剪据其渲染模式选相机
        
        /// <summary>
        /// 确保 LineContainer 和 NodeContainer 已在 nodeTreeRoot 下创建。
        /// 两者均挂载 RectTransform 并设置为 stretch 模式，自动适应 nodeTreeRoot 的 Size。
        /// 层级顺序：LineContainer（firstSibling）在下，NodeContainer（lastSibling）在上。
        /// </summary>
        private void EnsureContainers()
        {
            if (!nodeTreeRoot) return;

            // ── 连线容器（最先渲染，在节点之下）──
            var lineT = nodeTreeRoot.Find("LineContainer");
            if (lineT)
            {
                // 已存在：确保有 RectTransform 组件
                _lineContainer = lineT.GetComponent<RectTransform>()
                              ?? lineT.gameObject.AddComponent<RectTransform>();
            }
            else
            {
                var go = new GameObject("LineContainer");
                _lineContainer = go.AddComponent<RectTransform>();
                _lineContainer.SetParent(nodeTreeRoot, false);
                _lineContainer.SetAsFirstSibling();
            }
            // stretch 模式：锚点铺满父容器，自动跟随 nodeTreeRoot.sizeDelta 变化
            // pivot=(0,0) 使局部坐标原点位于左下角，与节点 anchoredPosition 坐标系保持一致
            _lineContainer.pivot     = Vector2.zero;
            _lineContainer.anchorMin = Vector2.zero;
            _lineContainer.anchorMax = Vector2.one;
            _lineContainer.offsetMin = Vector2.zero;
            _lineContainer.offsetMax = Vector2.zero;

            // ── 节点容器（最后渲染，在连线之上）──
            var nodeT = nodeTreeRoot.Find("NodeContainer");
            if (nodeT)
            {
                _nodeContainer = nodeT.GetComponent<RectTransform>()
                              ?? nodeT.gameObject.AddComponent<RectTransform>();
            }
            else
            {
                var go = new GameObject("NodeContainer");
                _nodeContainer = go.AddComponent<RectTransform>();
                _nodeContainer.SetParent(nodeTreeRoot, false);
                _nodeContainer.SetAsLastSibling();
            }
            // stretch 模式
            // pivot=(0,0) 使局部坐标原点位于左下角，与节点 anchoredPosition 坐标系保持一致
            _nodeContainer.pivot     = Vector2.zero;
            _nodeContainer.anchorMin = Vector2.zero;
            _nodeContainer.anchorMax = Vector2.one;
            _nodeContainer.offsetMin = Vector2.zero;
            _nodeContainer.offsetMax = Vector2.zero;

            // 缓存所属 Canvas：视口裁剪据其渲染模式选择相机（Overlay=null，Camera/World=worldCamera）
            _rootCanvas = _nodeContainer.GetComponentInParent<Canvas>();
        }

        /// <summary>
        /// 遍历所有节点，计算其在编辑器画布坐标系中的总包围盒，
        /// 加上四周内边距后设置 nodeTreeRoot.sizeDelta，
        /// 同时计算 _nodePositionOffset（使各节点在 nodeTreeRoot 局部空间内正确排布）。
        ///
        /// nodeTreePadding 布局：x=左, y=上, z=右, w=下。
        /// 偏移量 = (-minX + padLeft, -minY + padTop)，
        /// 使最小包围盒的左上角节点从内边距起始位置开始。
        /// </summary>
        private void CalcAndSetRootSize()
        {
            if (!nodeTreeRoot || config == null
                || config.nodes == null || config.nodes.Count == 0)
            {
                _nodePositionOffset = Vector2.zero;
                return;
            }

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            bool  anyProcessed = false;

            foreach (var node in config.nodes)
            {
                if (node == null) continue;
                var nodeType = config.GetNodeType(node.nodeTypeRef);
                Vector2 size  = nodeType?.resolution ?? new Vector2(80f, 80f);
                float   halfW = size.x * 0.5f;
                float   halfH = size.y * 0.5f;

                minX = Mathf.Min(minX, node.position.x - halfW);
                maxX = Mathf.Max(maxX, node.position.x + halfW);
                minY = Mathf.Min(minY, node.position.y - halfH);
                maxY = Mathf.Max(maxY, node.position.y + halfH);
                anyProcessed = true;
            }

            // 列表非空但所有节点均为 null：无有效包围盒（minX/maxX 仍为极值），
            // 继续计算会得到垃圾 sizeDelta，故回落为零偏移并直接返回。
            if (!anyProcessed)
            {
                _nodePositionOffset = Vector2.zero;
                return;
            }

            // nodeTreePadding: x=左, y=上, z=右, w=下
            float padL = nodeTreePadding.x, padT = nodeTreePadding.y;
            float padR = nodeTreePadding.z, padB = nodeTreePadding.w;

            float contentW = (maxX - minX) + padL + padR;
            float contentH = (maxY - minY) + padT + padB;
            nodeTreeRoot.sizeDelta = new Vector2(contentW, contentH);

            // 整体偏移量：使最小包围盒的边缘紧贴内边距。
            // Unity UI 中 Y 轴向上，minX/minY 对应包围盒的左/下边缘，
            // 因此 X 偏移对齐左边距（padL），Y 偏移对齐下边距（padB）。
            _nodePositionOffset = new Vector2(-minX + padL, -minY + padB);
        }

        /// <summary>
        /// 确保 uiCamera 已赋值：
        /// 1. Inspector 已手动指定 → 直接使用；
        /// 2. 向上查找父级 Canvas 的 worldCamera；
        /// 3. 回落 Camera.main。
        /// </summary>
        private void EnsureCamera()
        {
            if (uiCamera) return;

            var canvas = GetComponentInParent<Canvas>();
            if (canvas && canvas.worldCamera)
            {
                uiCamera = canvas.worldCamera;
                return;
            }

            uiCamera = Camera.main;
        }
        
        #region 配置数据校验
        /// <summary>
        /// 在 InitTree 开始时对 config 进行完整性校验，发现问题时输出 LogWarning。
        /// 校验范围：根容器、节点类型列表、节点实例列表（含子节点引用）。
        /// </summary>
        private void ValidateConfig()
        {
            string cfgName = $"[UINodeTreeWindow ({config.name})]";

            if (!nodeTreeRoot)
                Debug.LogWarning($"{cfgName} nodeTreeRoot 未赋值，节点UI将无法正确挂载。", this);

            if (config.nodeTypes == null || config.nodeTypes.Count == 0)
            {
                Debug.LogWarning($"{cfgName} 未定义任何节点类型（nodeTypes 为空），所有节点将无法显示。", this);
            }
            else
            {
                var typeNameSet = new HashSet<string>();
                for (int i = 0; i < config.nodeTypes.Count; i++)
                {
                    var t = config.nodeTypes[i];
                    if (t == null) { Debug.LogWarning($"{cfgName} nodeTypes[{i}] 为 null，已跳过。", this); continue; }

                    if (string.IsNullOrEmpty(t.typeName))
                        Debug.LogWarning($"{cfgName} nodeTypes[{i}] 的 typeName 为空，将导致类型无法被节点引用。", this);
                    else if (!typeNameSet.Add(t.typeName))
                        Debug.LogWarning($"{cfgName} 节点类型 typeName 重复：\"{t.typeName}\"（index {i}）。", this);

                    if (!t.uiPrefab)
                        Debug.LogWarning($"{cfgName} 节点类型 \"{t.typeName}\" 未设置 uiPrefab，该类型节点无法 Spawn。", this);

                    if (t.line == null)
                        Debug.LogWarning($"{cfgName} 节点类型 \"{t.typeName}\" 未配置连线样式（line 为 null）。", this);
                    else if (!t.line.material)
                        Debug.LogWarning($"{cfgName} 节点类型 \"{t.typeName}\" 的连线材质未赋值，连线将不会渲染。", this);
                }
            }

            if (config.nodes == null || config.nodes.Count == 0)
            {
                Debug.LogWarning($"{cfgName} 节点列表（nodes）为空，节点树中没有任何节点。", this);
                return;
            }

            var allNodeIds = new HashSet<string>();
            foreach (var n in config.nodes)
                if (n != null && !string.IsNullOrEmpty(n.nodeId))
                    allNodeIds.Add(n.nodeId);

            var nodeIdSet = new HashSet<string>();
            for (int i = 0; i < config.nodes.Count; i++)
            {
                var n = config.nodes[i];
                if (n == null) { Debug.LogWarning($"{cfgName} nodes[{i}] 为 null，已跳过。", this); continue; }

                if (string.IsNullOrEmpty(n.nodeId))
                    Debug.LogWarning($"{cfgName} nodes[{i}] 的 nodeId 为空。", this);
                else if (!nodeIdSet.Add(n.nodeId))
                    Debug.LogWarning($"{cfgName} 节点 nodeId 重复：\"{n.nodeId}\"（index {i}）。", this);

                if (string.IsNullOrEmpty(n.nodeTypeRef))
                    Debug.LogWarning($"{cfgName} 节点 \"{n.nodeId}\" 未指定节点类型（nodeTypeRef 为空）。", this);
                else if (config.GetNodeType(n.nodeTypeRef) == null)
                    Debug.LogWarning($"{cfgName} 节点 \"{n.nodeId}\" 的 nodeTypeRef \"{n.nodeTypeRef}\" 在类型列表中不存在。", this);

                if (n.childNodeIds != null)
                {
                    for (int ci = 0; ci < n.childNodeIds.Count; ci++)
                    {
                        var childId = n.childNodeIds[ci];
                        if (string.IsNullOrEmpty(childId))
                            Debug.LogWarning($"{cfgName} 节点 \"{n.nodeId}\" 的 childNodeIds[{ci}] 为空字符串。", this);
                        else if (childId == n.nodeId)
                            Debug.LogWarning($"{cfgName} 节点 \"{n.nodeId}\" 的 childNodeIds[{ci}] 指向自身（自引用）。", this);
                        else if (!allNodeIds.Contains(childId))
                            Debug.LogWarning($"{cfgName} 节点 \"{n.nodeId}\" 的子节点 ID \"{childId}\" 在节点列表中不存在。", this);
                    }
                }
            }
        }
        #endregion
        #endregion
        
        #region 节点
        // 对象池：key = typeName
        private readonly Dictionary<string, ToolkitGameObjectPool> _pools
            = new Dictionary<string, ToolkitGameObjectPool>();

        /// <summary>
        /// 根据配置中的节点类型定义初始化各类型的 ToolkitGameObjectPool。
        /// 同时清理已存在的激活节点。
        /// </summary>
        private void InitPools()
        {
            // 销毁旧对象池 GameObject（连同其池内非激活克隆），避免重复 InitTree 累积孤儿层级。
            // DespawnAll 仅把激活克隆归还池、不销毁 Pool_* 对象本身，故需显式 Destroy。
            foreach (var pool in _pools.Values)
            {
                if (!pool) continue;
                pool.DespawnAll();
                Destroy(pool.gameObject);
            }
            _pools.Clear();
            _activeNodes.Clear();

            if (config.nodeTypes == null) return;

            foreach (var nodeType in config.nodeTypes)
            {
                // 跳过坏条目（null 类型 / 空 typeName），避免 Dictionary 空键抛异常中断 InitTree
                if (nodeType == null || string.IsNullOrEmpty(nodeType.typeName)) continue;
                if (!nodeType.uiPrefab) continue;
                if (_pools.ContainsKey(nodeType.typeName)) continue;

                var poolGo = new GameObject($"Pool_{nodeType.typeName}");
                poolGo.transform.SetParent(transform);
                var pool = poolGo.AddComponent<ToolkitGameObjectPool>();
                pool.Prefab        = nodeType.uiPrefab;
                pool.Notification  = ToolkitGameObjectPool.PoolNotificationType.IPoolable;
                pool.Preload       = 3;
                _pools[nodeType.typeName] = pool;
            }
        }

        /// <summary>
        /// 从对应类型的对象池 Spawn 一个节点UI，绑定数据并设置 RectTransform 位置。
        /// anchoredPosition = 节点画布坐标 + _nodePositionOffset，使坐标系与 nodeTreeRoot 一致。
        /// nodeData.nodeTypeRef 在 _pools 中不存在时返回 null。
        /// </summary>
        private void SpawnNode(NodeData nodeData)
        {
            // 空节点 / 空 nodeId / 空 nodeTypeRef 直接跳过（后者会令 Dictionary 空键抛异常）
            if (nodeData == null || string.IsNullOrEmpty(nodeData.nodeId)
                || string.IsNullOrEmpty(nodeData.nodeTypeRef)) return;
            if (!_pools.TryGetValue(nodeData.nodeTypeRef, out var pool)) return;

            var parent = _nodeContainer ? (Transform)_nodeContainer : nodeTreeRoot;
            var nodeInstance = pool.Spawn(Vector3.zero, Quaternion.identity, parent);
            if (!nodeInstance) return;

            var nodeUI = nodeInstance.GetComponent<UINodeBase>();
            if (!nodeUI)
            {
                // 预制体缺少 UINodeBase 组件：立即归还实例，避免激活实例泄漏且永不再被裁剪。
                ToolkitPool.Despawn(nodeInstance);
                return;
            }

            var nodeType = config.GetNodeType(nodeData.nodeTypeRef);
            nodeUI.OnBindData(nodeData, nodeType);

            // anchoredPosition = 节点画布坐标 + 整体偏移量
            // 偏移量由 CalcAndSetRootSize() 计算，确保所有节点位于 nodeTreeRoot 范围内
            if (nodeInstance.TryGetComponent<RectTransform>(out var rt))
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.zero;
                rt.anchoredPosition = nodeData.position + _nodePositionOffset;
            }

            _activeNodes[nodeData.nodeId] = nodeUI;

            if (nodeData.nodeId == _selectedNodeId)
                nodeUI.OnNodeSelected();
        }

        /// <summary>
        /// 将指定节点UI归还到对象池，并从激活列表中移除。
        /// </summary>
        private void DespawnNode(string nodeId)
        {
            if (!_activeNodes.TryGetValue(nodeId, out var nodeUI)) return;
            ToolkitPool.Despawn(nodeUI.gameObject);
            _activeNodes.Remove(nodeId);
        }
        #endregion
        
        #region 节点连线
        // 连线 Mesh：key = typeName
        // 使用 CanvasRenderer 而非 MeshFilter/MeshRenderer，才能在 Canvas 中正确渲染。
        private readonly Dictionary<string, CanvasRenderer> _lineCanvasRenderers   = new Dictionary<string, CanvasRenderer>();
        private readonly Dictionary<string, Material>       _lineMaterialInstances = new Dictionary<string, Material>();

        // 连线段分组缓存（按子节点类型名）：复用字典与列表，每次重建仅清空列表内容，避免重复 new 造成 GC
        private readonly Dictionary<string, List<(Vector3 from, Vector3 to)>> _segmentsByType
            = new Dictionary<string, List<(Vector3 from, Vector3 to)>>();

        /// <summary>标记连线 Mesh 为脏，将在下一次 LateUpdate 中重建。</summary>
        public void MarkLineDirty() => _lineMeshDirty = true;
        private bool _lineMeshDirty = true;

        /// <summary>
        /// 为配置中每种有效节点类型（需有 line.material）创建对应的 CanvasRenderer，
        /// 克隆材质实例（避免污染原始资产），并清理旧的对象。
        /// CanvasRenderer 是 Unity UI 的底层渲染器，SetMesh()+SetMaterial() 可在 Canvas 中渲染自定义 Mesh。
        /// </summary>
        private void InitLineMeshRenderers()
        {
            // 清理旧的材质实例和连线 GameObject
            foreach (var mat in _lineMaterialInstances.Values)
                if (mat) Destroy(mat);
            _lineMaterialInstances.Clear();

            foreach (var cr in _lineCanvasRenderers.Values)
            {
                if (cr)
                {
                    // 先销毁 CanvasRenderer 持有的合并 Mesh（原生对象不被 GC，否则每次重建泄漏一个/类型），
                    // 再销毁承载它的 GameObject。
                    var mesh = cr.GetMesh();
                    if (mesh) Destroy(mesh);
                    cr.SetMesh(null);
                    Destroy(cr.gameObject);
                }
            }
            _lineCanvasRenderers.Clear();

            if (config.nodeTypes == null) return;

            foreach (var nodeType in config.nodeTypes)
            {
                // 跳过坏条目（null 类型 / 空 typeName）——`?.` 只护 line，不护 nodeType 本身
                if (nodeType == null || string.IsNullOrEmpty(nodeType.typeName)) continue;
                if (!nodeType.line?.material) continue;
                if (_lineCanvasRenderers.ContainsKey(nodeType.typeName)) continue;

                // 连线 GameObject 挂载在 LineContainer 下
                var lineParent = _lineContainer ? _lineContainer : transform;
                var go = new GameObject($"Line_{nodeType.typeName}");
                go.transform.SetParent(lineParent, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale    = Vector3.one;

                // CanvasRenderer：Unity UI 的底层渲染器，在 Canvas 中渲染自定义 Mesh
                var cr = go.AddComponent<CanvasRenderer>();

                var matInstance = Instantiate(nodeType.line.material); // 克隆，避免污染原始资产
                cr.SetMaterial(matInstance, null);

                _lineCanvasRenderers[nodeType.typeName]  = cr;
                _lineMaterialInstances[nodeType.typeName] = matInstance;
            }
        }

        /// <summary>
        /// 重建所有节点类型的连线合并 Mesh。
        /// 顶点坐标使用 NodeDataToLineLocalPos()，与 LineContainer 的局部坐标系一致，
        /// 和节点 RectTransform.anchoredPosition 所对应的局部位置相同。
        /// 最终通过 CanvasRenderer.SetMesh() 提交，在 Canvas 中正确渲染。
        /// </summary>
        private void RebuildAllLineMeshes()
        {
            if (!config) return;

            // 按子节点类型分组收集所有连线段（局部空间坐标）。复用缓存字典与列表：仅清空列表内容、
            // 不重建对象，避免每次重建产生 GC。
            foreach (var kv in _segmentsByType) kv.Value.Clear();

            foreach (var node in config.nodes)
            {
                if (node == null || node.childNodeIds == null) continue;
                foreach (var childId in node.childNodeIds)
                {
                    var child = config.GetNode(childId);
                    if (child == null) continue;

                    // 连线样式归子节点类型（入边样式）：按子节点类型分组、取其 line
                    var childType = config.GetNodeType(child.nodeTypeRef);
                    if (childType == null) continue;

                    var typeName = child.nodeTypeRef;
                    if (!_segmentsByType.TryGetValue(typeName, out var list))
                    {
                        list = new List<(Vector3, Vector3)>();
                        _segmentsByType[typeName] = list;
                    }

                    // 使用局部空间坐标（与节点 anchoredPosition 在同一坐标系）
                    list.Add((NodeDataToLineLocalPos(node), NodeDataToLineLocalPos(child)));
                }
            }

            // 为每种类型重建 Mesh 并交给 CanvasRenderer
            foreach (var kv in _lineCanvasRenderers)
            {
                var typeName = kv.Key;
                var cr       = kv.Value;

                // 销毁旧 Mesh
                var oldMesh = cr.GetMesh();
                if (oldMesh) Destroy(oldMesh);

                if (!_segmentsByType.TryGetValue(typeName, out var segments) || segments.Count == 0)
                {
                    cr.SetMesh(null);
                    continue;
                }

                var nodeType = config.GetNodeType(typeName);
                var lineData = nodeType?.line;
                var newMesh  = BuildCombinedLineMesh(segments, lineData, config.layoutDirection);
                cr.SetMesh(newMesh);
            }
        }

        /// <summary>
        /// 将一批连线段合并为一个 Mesh，委托给共用工具类 <see cref="NodeLineBuilder"/>。
        /// UV.x（0→1）对应线宽方向（边缘渐变），UV.y（弧长/100f）用于流动效果，纹理密度与线长无关。
        /// </summary>
        private static Mesh BuildCombinedLineMesh(
            List<(Vector3 from, Vector3 to)> segments,
            LineTypeData lineData,
            ELayoutDirection dir)
            => NodeLineBuilder.BuildCombinedLineMesh(segments, lineData, dir);
        #endregion
        
        #region 视口裁剪
        [Header("视口裁剪")]
        [Tooltip("UI摄像机：留空则自动从父级 Canvas.worldCamera 获取，仍为空则回落 Camera.main。")]
        [SerializeField] private Camera uiCamera;

        [Tooltip("视口裁剪边缘缓冲（像素）：将裁剪范围向外扩展，避免节点在边缘出现闪烁。")]
        [SerializeField] private float cullPadding = 100f;

        // 滞回额外边距（像素）：已激活节点的 despawn 边界 = cullPadding + 此值，比 spawn 边界更宽，消除边界抖动
        private const float DespawnHysteresis = 80f;

        // 当前激活的节点UI：key = nodeId
        private readonly Dictionary<string, UINodeBase> _activeNodes
            = new Dictionary<string, UINodeBase>();

        // 每次可见性刷新的去重集合（避免重复 nodeId 造成 Spawn/Despawn 抖动）；复用以免每帧分配
        private readonly HashSet<string> _visibilitySeen = new HashSet<string>();

        /// <summary>
        /// 刷新所有节点的可见性。
        /// </summary>
        public void RefreshVisibility()
        {
            if (!config || !nodeTreeRoot || !_nodeContainer) return;
            if (config.nodes == null) return;

            // 按 Canvas 渲染模式选裁剪相机：Overlay 用 null（世界坐标即屏幕像素），
            // Camera/World 模式用 worldCamera 将节点世界坐标投影到屏幕。
            Camera cullCam = (_rootCanvas && _rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
                ? _rootCanvas.worldCamera
                : null;

            // 容器世界缩放（CanvasScaler scaleFactor≠1 或缩放画布时 ≠1），用于把节点半尺寸换算到屏幕像素
            Vector3 lossy = _nodeContainer.lossyScale;

            // 检查所有节点是否在屏幕边界内，只为可见节点分配 UI 实例进行显示。
            _visibilitySeen.Clear();
            foreach (var node in config.nodes)
            {
                // 空节点 / 空 nodeId 跳过（后者会令 _activeNodes.ContainsKey 空键抛异常，破坏整轮裁剪）
                if (node == null || string.IsNullOrEmpty(node.nodeId)) continue;
                // 重复 nodeId：仅处理首个，避免同一 id 的屏外副本把屏内实例反复 Spawn/Despawn 抖动
                if (!_visibilitySeen.Add(node.nodeId)) continue;

                // 节点局部坐标 → 世界坐标（经容器世界矩阵，正确反映缩放/旋转）→ 屏幕像素（按渲染模式）。
                // 不再把局部坐标直接加到容器世界坐标：旧法在 scaleFactor≠1 / Screen Space-Camera 下错位且随距离放大。
                Vector3 local = new Vector3(node.position.x + _nodePositionOffset.x,
                                            node.position.y + _nodePositionOffset.y, 0f);
                Vector3 world = _nodeContainer.TransformPoint(local);
                Vector2 sp    = cullCam ? (Vector2)cullCam.WorldToScreenPoint(world)
                                        : new Vector2(world.x, world.y);

                // 节点半尺寸（随容器缩放）转屏幕像素，做包围盒判定而非仅中心，避免大节点边缘尚在屏内即被误剔。
                var type      = config.GetNodeType(node.nodeTypeRef);
                Vector2 half  = (type != null ? type.resolution : new Vector2(80f, 80f)) * 0.5f;
                float halfWpx = half.x * Mathf.Abs(lossy.x);
                float halfHpx = half.y * Mathf.Abs(lossy.y);

                // 滞回：已激活节点用更宽的 despawn 边距、未激活用更窄的 spawn 边距，消除边界处对象池抖动。
                bool  wasActive = _activeNodes.ContainsKey(node.nodeId);
                float margin    = cullPadding + (wasActive ? DespawnHysteresis : 0f);

                bool visible = sp.x + halfWpx >= -margin
                            && sp.x - halfWpx <= Screen.width  + margin
                            && sp.y + halfHpx >= -margin
                            && sp.y - halfHpx <= Screen.height + margin;

                if (visible && !wasActive)
                    SpawnNode(node);
                else if (!visible && wasActive)
                    DespawnNode(node.nodeId);
            }
        }
        #endregion
    }
}
