using System.Collections.Generic;
using Ale.Toolkit.Runtime;
using UnityEngine;
using UnityEngine.Serialization;

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

        [Tooltip("【测试用】强制解锁：InitTree 时给每个节点挂上下方列表里的标签，\n" +
                 "绕过解锁条件与存档状态，便于查看整棵树的完整结构。\n" +
                 "只往内存态里加标签，不改存档文件本身；发布前应保持关闭。")]
        [FormerlySerializedAs("unlockAllForTest")]
        [SerializeField] private bool forceUnlockForTest;

        [Tooltip("强制解锁时要挂上的标签名。\n" +
                 "留空 = 挂上标签词表（NodeTreeData.tags）里的全部标签：零配置即生效，\n" +
                 "宿主改了标签名也不会让这个开关静默失效。\n" +
                 "只想挂其中几个时再逐条填写（例如某个标签另有副作用，不该被测试开关带上）。")]
        [SerializeField] private List<string> forceUnlockTags = new List<string>();

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

            // 弹窗跟随节点移动（ScrollRect 滚动 / 画布缩放），并回收淡出完毕的实例。
            // 只遍历当前激活的极少数弹窗，静止时开销可忽略。
            UpdateInfoPanels();
        }

        /// <summary>
        /// 本窗口停用时立即收起全部信息弹窗。
        /// <para>必要性：toolkit 的补间<b>不随 GameObject 停用而停止</b>，在途的淡入会一路跑到全亮；
        /// 而 <c>LateUpdate</c> 随之停摆，轮询回收也不会再发生 —— 再次打开面板就是一片常亮的残留弹窗。
        /// 这与 1.5.0 修掉的「面板停用致弹窗常亮」是同一类问题，只是弹窗换了归属。</para>
        /// </summary>
        private void OnDisable() => HideAllNodeInfoPanels(instant: true);

        private void OnDestroy()
        {
            // 1) 归还所有激活节点克隆到各自对象池：清除 ToolkitPool.Links 静态引用，
            //    并把散落在（可能位于本组件之外的）nodeTreeRoot 下的克隆收回，随后随池一并销毁。
            foreach (var pool in _pools.Values)
                if (pool) pool.DespawnAll();
            _activeNodes.Clear();

            // 1.5) 收起并归还全部信息弹窗：DespawnAll 会触发各弹窗的 OnDespawn，
            //      在其中解绑数据并打断在途补间（补间不随停用停止，不打断会残留到下次复用）。
            HideAllNodeInfoPanels(instant: true);
            if (_infoPanelPool) _infoPanelPool.DespawnAll();
            _activeInfoPanels.Clear();

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

            if (_infoPanelPool) Destroy(_infoPanelPool.gameObject);
            _infoPanelPool = null;

            // 只销毁本窗口自己建的弹窗层；Inspector 手动指定的层归使用者所有，不能替他销毁。
            if (_ownsInfoPanelLayer && _infoPanelLayer) Destroy(_infoPanelLayer.gameObject);
            _infoPanelLayer = null;
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
            // 先建池再建弹窗层：池对象也挂在本窗口下，反过来会把弹窗层挤出末位
            InitInfoPanelPool();
            EnsureInfoPanelLayer();
            InitLineMeshRenderers();
            _lineMeshDirty   = true;
            _visibilityDirty = true; // 强制下一帧刷新一次可见性

            // 【测试用】强制解锁。放在状态刷新之前，链式条件（NodeUnlocked / NodeFinished 等）
            // 能立即看到这些标签。
            if (forceUnlockForTest) ApplyForceUnlockTags();

            // 打开时刷新所有节点的自动状态标签（最简单直接的刷新方式）
            if (refreshStatesOnInit)
                NodeTreeSaveDataManager.Instance.RefreshAllNodeStates(config);
        }

        /// <summary>
        /// 【测试用】给每个节点挂上强制解锁标签，绕过解锁条件与存档状态。
        ///
        /// <para><b>挂哪些标签由 <c>forceUnlockTags</c> 决定，留空则挂词表里的全部标签。</b>
        /// 「留空即全挂」是刻意的默认：节点能不能进由宿主说了算，而宿主常常用自己定义的标签来判
        /// （如「本周目读完」「跨周目读过」）。若默认只挂 <see cref="NodeTreeTags.Unlock"/>，
        /// 那类宿主勾上开关会毫无反应——一个测试开关最不该有的表现就是「点了没动静」。</para>
        ///
        /// <para>只往内存态里加标签，不写存档文件。</para>
        /// </summary>
        private void ApplyForceUnlockTags()
        {
            var save = NodeTreeSaveDataManager.Instance;
            var useVocabulary = forceUnlockTags == null || forceUnlockTags.Count == 0;

            foreach (var node in config.nodes)
            {
                if (node == null || string.IsNullOrEmpty(node.nodeId)) continue;

                if (!useVocabulary)
                {
                    // 显式配了就严格照配的来：写了几个挂几个，不替使用者补
                    foreach (var tagName in forceUnlockTags)
                        if (!string.IsNullOrEmpty(tagName)) save.AddTag(node.nodeId, tagName);
                    continue;
                }

                // Unlock 无条件挂：词表被改过、没有这一项的工程也要保持既有行为
                save.AddTag(node.nodeId, NodeTreeTags.Unlock);
                if (config.tags == null) continue;
                foreach (var nodeTagData in config.tags)
                    if (nodeTagData != null && !string.IsNullOrEmpty(nodeTagData.tagName))
                        save.AddTag(node.nodeId, nodeTagData.tagName);
            }
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

            // 先注入回指再绑定：OnBindData 是虚方法，子类可能在其中就要用到窗口
            nodeUI.OwnerWindow = this;
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

            // 节点被视口裁掉，其弹窗不能继续留在屏上
            HideNodeInfoPanel(nodeId);

            // Despawn 会同步触发 OnDespawn → OnUnbindData，其中仍要用 OwnerWindow 收弹窗，故归还后再清
            ToolkitPool.Despawn(nodeUI.gameObject);
            nodeUI.OwnerWindow = null;
            _activeNodes.Remove(nodeId);
        }
        #endregion
        
        #region 信息弹窗
        [Header("信息弹窗")]
        [Tooltip("信息弹窗预制体（需挂 UINodeInfoPanel）。留空则整套弹窗功能静默关闭。\n" +
                 "弹窗由本窗口统一用对象池管理，不再由每个节点预制体各挂一份。")]
        [SerializeField] private UINodeInfoPanel infoPanelPrefab;

        [Tooltip("弹窗相对【节点中心】的偏移（节点树容器单位，Y 轴向上）。\n" +
                 "默认 (0, 120)：节点默认尺寸 100×100，即悬于节点上边缘之上 70，弹窗在节点上方。\n" +
                 "偏移施加在弹窗层空间，故缩放节点树画布时它保持不变。")]
        [SerializeField] private Vector2 infoPanelOffset = new Vector2(0f, 120f);

        [Tooltip("弹窗所在的层。留空则在本窗口下自动创建 InfoPanelLayer（最后一个兄弟，压在 ScrollView 之上）。\n" +
                 "手动指定时务必选在 ScrollView 的 Viewport 之外，否则弹窗会被其 Mask 裁剪。")]
        [SerializeField] private RectTransform infoPanelLayer;

        [Tooltip("弹窗对象池的预热数量，默认 2（同时显示多个弹窗时才会用到更多）。")]
        [SerializeField] private int infoPanelPreload = 2;

        // 弹窗对象池：全窗口共用一个预制体，故只需一个池。
        private ToolkitGameObjectPool _infoPanelPool;

        // 运行时实际使用的弹窗层，以及它是否由本窗口创建（决定销毁时是否该动它）。
        private RectTransform _infoPanelLayer;
        private bool          _ownsInfoPanelLayer;

        // 当前激活（显示中或正在淡出）的弹窗。数量极少（通常 0~2），用 List 顺序查找即可，
        // 也便于回收时倒序遍历原地移除。
        private readonly List<UINodeInfoPanel> _activeInfoPanels = new List<UINodeInfoPanel>();

        /// <summary>
        /// 确保弹窗层就绪：Inspector 指定了就用指定的，否则在本窗口下自动创建 <c>InfoPanelLayer</c>。
        ///
        /// <para><b>为什么必须在 ScrollView 之外</b>：弹窗若挂在 <c>nodeTreeRoot</c>（Content）下，
        /// 一来会被 Viewport 的 <c>Mask</c> 在视口边缘切掉，二来又回到了「渲染顺序跟着节点兄弟序走、
        /// 被后 Spawn 的节点盖住」的老问题 —— 这正是把弹窗从节点里搬出来要解决的两件事。</para>
        ///
        /// <para>层的 <c>pivot</c> 取 (0.5, 0.5) 并铺满父级，使弹窗（anchor 亦为 0.5）的
        /// <c>localPosition</c> 与 <c>anchoredPosition</c> 等价，定位时只写 <c>localPosition</c> 即可。</para>
        /// </summary>
        private void EnsureInfoPanelLayer()
        {
            // Inspector 显式指定：直接采用，且不视为本窗口所有。
            if (infoPanelLayer)
            {
                _infoPanelLayer     = infoPanelLayer;
                _ownsInfoPanelLayer = false;
                return;
            }

            if (_infoPanelLayer && _ownsInfoPanelLayer)
            {
                _infoPanelLayer.SetAsLastSibling(); // 复用路径也重排，见方法末尾
                return;
            }

            var parent = transform as RectTransform;
            if (!parent)
            {
                Debug.LogWarning(
                    "[UINodeTreeWindow] 本组件不在 RectTransform 上，无法自动创建弹窗层，" +
                    "已回落到 nodeTreeRoot —— 弹窗会被 ScrollView 的 Mask 裁剪。" +
                    "请手动指定 infoPanelLayer 到 Viewport 之外的某个 RectTransform。", this);
                parent = nodeTreeRoot;
            }
            if (!parent) return;

            var existing = parent.Find("InfoPanelLayer");
            if (existing)
            {
                // 不能写成 GetComponent<T>() ?? AddComponent<T>()：组件缺失时 GetComponent 返回的是
                // Unity 的"伪 null"包装对象，而 ?? 只看引用 null、不走 UnityEngine.Object 的 == 重载，
                // 于是会短路成那个伪 null，随后一用就抛 MissingComponentException。
                _infoPanelLayer = existing.GetComponent<RectTransform>();
                if (!_infoPanelLayer) _infoPanelLayer = existing.gameObject.AddComponent<RectTransform>();
            }
            else
            {
                var go = new GameObject("InfoPanelLayer");
                _infoPanelLayer = go.AddComponent<RectTransform>();
                _infoPanelLayer.SetParent(parent, false);
            }
            _ownsInfoPanelLayer = true;

            // 铺满父级；pivot 居中，见方法注释
            _infoPanelLayer.pivot     = new Vector2(0.5f, 0.5f);
            _infoPanelLayer.anchorMin = Vector2.zero;
            _infoPanelLayer.anchorMax = Vector2.one;
            _infoPanelLayer.offsetMin = Vector2.zero;
            _infoPanelLayer.offsetMax = Vector2.zero;
            // 压在 ScrollView 之上。对象池 GameObject 也挂在本窗口下，但它们不渲染任何东西，
            // 其池内克隆在取用时会被改挂到本层，故它们的兄弟序无关紧要。
            _infoPanelLayer.SetAsLastSibling();

            // 整层不阻挡射线：各弹窗自身也会关，这里再兜一道，防止子类改坏了单个弹窗的设置后
            // 弹窗挡住光标 —— 那会造成 PointerExit / PointerEnter 反复互触的闪烁死循环。
            var layerGroup = _infoPanelLayer.GetComponent<CanvasGroup>();
            if (!layerGroup) layerGroup = _infoPanelLayer.gameObject.AddComponent<CanvasGroup>();
            layerGroup.blocksRaycasts = false;
        }

        /// <summary>
        /// 初始化弹窗对象池。与 <see cref="InitPools"/> 同构：先清旧池（连同池内非激活克隆），
        /// 避免重复 <see cref="InitTree"/> 累积孤儿层级。未配置 <c>infoPanelPrefab</c> 时直接返回。
        /// </summary>
        private void InitInfoPanelPool()
        {
            if (_infoPanelPool)
            {
                _infoPanelPool.DespawnAll(); // 触发各弹窗 OnDespawn：解绑数据 + 打断在途补间
                Destroy(_infoPanelPool.gameObject);
                _infoPanelPool = null;
            }
            _activeInfoPanels.Clear();

            if (!infoPanelPrefab) return;

            var poolGo = new GameObject("Pool_InfoPanel");
            poolGo.transform.SetParent(transform);
            _infoPanelPool = poolGo.AddComponent<ToolkitGameObjectPool>();
            _infoPanelPool.Prefab       = infoPanelPrefab.gameObject;
            _infoPanelPool.Notification = ToolkitGameObjectPool.PoolNotificationType.IPoolable;
            _infoPanelPool.Preload      = Mathf.Max(0, infoPanelPreload);
        }

        /// <summary>
        /// 显示指定节点的信息弹窗并淡入。已在显示（或正在淡出）的节点复用同一实例，不会重复取用。
        /// <para>悬停时由 <see cref="UINodeBase"/> 转调；也可由外部直接调用，
        /// 例如「把某条路径上的几个节点的弹窗一起钉住」。</para>
        /// <para><b>描述为空的节点不弹</b>：见 <see cref="HasInfoPanelText"/>。</para>
        /// </summary>
        /// <param name="nodeId">节点 ID。</param>
        /// <returns>
        /// 该节点的弹窗实例；未配置预制体 / 弹窗层缺失 / 节点不存在 / <b>节点描述为空</b>时返回 null。
        /// </returns>
        public UINodeInfoPanel ShowNodeInfoPanel(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId) || !config) return null;

            var node = config.GetNode(nodeId);
            if (node == null) return null;

            // 描述为空就别弹：一个只有内边距的空框既给不出信息，又会挡住下面的节点。
            // 已经弹出来的也要收回去 —— 覆盖「描述在运行时被清空后又悬停一次」这条路径。
            if (!HasInfoPanelText(node))
            {
                HideNodeInfoPanel(nodeId);
                return null;
            }

            var panel = FindInfoPanel(nodeId);
            if (!panel)
            {
                if (!_infoPanelPool || !_infoPanelLayer) return null;

                var clone = _infoPanelPool.Spawn(Vector3.zero, Quaternion.identity, _infoPanelLayer);
                if (!clone) return null;

                panel = clone.GetComponent<UINodeInfoPanel>();
                if (!panel)
                {
                    // 预制体缺少 UINodeInfoPanel：立即归还，避免激活实例泄漏且永不被回收。
                    ToolkitPool.Despawn(clone);
                    return null;
                }
                _activeInfoPanels.Add(panel);
            }

            panel.Bind(node, config.GetNodeType(node.nodeTypeRef));
            PositionInfoPanel(panel);
            if (panel.Rect) panel.Rect.SetAsLastSibling(); // 后显示的压在最上
            panel.Show();
            return panel;
        }

        /// <summary>
        /// 隐藏指定节点的信息弹窗（淡出）。
        /// <para><b>不在此归还对象池</b>：实例留在激活列表里，等 <see cref="UpdateInfoPanels"/> 轮询到
        /// 淡出结束（<see cref="UINodeInfoPanel.IsRecyclable"/>）再回收。这样淡出途中重新悬停能直接
        /// 复用同一实例淡回去，不必处理「淡出完成回调」与「新的显示请求」互相打架。</para>
        /// </summary>
        public void HideNodeInfoPanel(string nodeId)
        {
            var panel = FindInfoPanel(nodeId);
            if (panel) panel.Hide();
        }

        /// <summary>
        /// 隐藏全部信息弹窗。<paramref name="instant"/> 为 true 时瞬间收起并<b>立即</b>回收 ——
        /// 停用 / 销毁路径必须用它，那之后 <c>LateUpdate</c> 不会再来做轮询回收了。
        /// </summary>
        public void HideAllNodeInfoPanels(bool instant = false)
        {
            for (int i = 0; i < _activeInfoPanels.Count; i++)
                if (_activeInfoPanels[i]) _activeInfoPanels[i].Hide(instant);

            if (instant) UpdateInfoPanels();
        }

        /// <summary>
        /// 逐帧维护激活弹窗：回收淡出完毕的，其余重新定位以跟随节点。
        /// <para><b>回收走轮询而非补间的完成回调</b>：完成回调在「时长 ≤ 0」与「Kill(true)」两条路径上是
        /// <b>同步</b>触发的，若在其中归还对象池并改动 <see cref="_activeInfoPanels"/>，
        /// 就会在 <see cref="HideAllNodeInfoPanels"/> 的遍历中途改集合。轮询模型下集合只在这一处被改。</para>
        /// </summary>
        private void UpdateInfoPanels()
        {
            // 倒序：原地移除不影响尚未遍历到的下标
            for (int i = _activeInfoPanels.Count - 1; i >= 0; i--)
            {
                var panel = _activeInfoPanels[i];
                if (!panel) { _activeInfoPanels.RemoveAt(i); continue; }

                if (panel.IsRecyclable)
                {
                    _activeInfoPanels.RemoveAt(i);
                    ToolkitPool.Despawn(panel.gameObject);
                    continue;
                }

                PositionInfoPanel(panel);
            }
        }

        /// <summary>
        /// 把弹窗摆到其绑定节点的<b>中心</b>再加上 <see cref="infoPanelOffset"/>。
        ///
        /// <para>换算走「节点容器局部 → 世界 → 弹窗层局部」：两个 Transform 同处一个 Canvas，
        /// 因此<b>无需相机、无需屏幕坐标往返</b>，对 Overlay / Camera / World 三种 RenderMode 一律成立。</para>
        ///
        /// <para>偏移施加在<b>弹窗层</b>空间而非节点容器空间，故缩放画布（<c>localScale</c>）时
        /// 弹窗自身尺寸与偏移保持不变 —— 这是有意为之：弹窗是给人读的，不该跟着缩小到看不清。</para>
        /// <para>节点坐标（<c>NodeData.position</c>）本身就是节点<b>中心</b>，故无需再按节点尺寸折算。</para>
        /// </summary>
        private void PositionInfoPanel(UINodeInfoPanel panel)
        {
            if (!panel || !panel.Rect || !_nodeContainer || !_infoPanelLayer) return;

            var node = panel.BoundNode;
            if (node == null) return;

            Vector3 local = new Vector3(node.position.x + _nodePositionOffset.x,
                                        node.position.y + _nodePositionOffset.y, 0f);
            Vector3 world      = _nodeContainer.TransformPoint(local);
            Vector3 layerLocal = _infoPanelLayer.InverseTransformPoint(world);

            panel.Rect.localPosition = new Vector3(layerLocal.x + infoPanelOffset.x,
                                                   layerLocal.y + infoPanelOffset.y, 0f);
        }

        /// <summary>
        /// 节点是否有可显示的描述文本。<see cref="ShowNodeInfoPanel"/> 在取用实例<b>之前</b>调用，
        /// 为 false 就完全不弹（不占池、不建实例）。
        ///
        /// <para>取 <see cref="NodeData.nodeDesc"/> 经 <c>ResolveText()</c> 的结果 —— 启用 toolkit 本地化时
        /// 是当前语言的译文，否则是纯文本 fallback；两者皆空时它返回空串。</para>
        ///
        /// <para>纯空白（全是空格 / 换行）与空串<b>同等对待</b>：它们在观感上没有区别，却同样会弹出一个
        /// 只有内边距的空框。</para>
        ///
        /// <para>⚠ 判据只看 <c>nodeDesc</c>。若弹窗的文案<b>不是</b>由它驱动（例如挂
        /// <c>LocalizeStringEvent</c> 显示固定文本），那么节点的 <c>nodeDesc</c> 仍需填写，否则一律不弹。</para>
        /// </summary>
        private static bool HasInfoPanelText(NodeData node)
        {
            if (node == null || node.nodeDesc == null) return false;

            return !string.IsNullOrWhiteSpace(node.nodeDesc.ResolveText());
        }

        /// <summary>在激活弹窗中按节点 ID 查找；没有则返回 null。数量极少，顺序查找即可。</summary>
        private UINodeInfoPanel FindInfoPanel(string nodeId)
        {
            for (int i = 0; i < _activeInfoPanels.Count; i++)
            {
                var panel = _activeInfoPanels[i];
                if (panel && panel.BoundNode != null && panel.BoundNode.nodeId == nodeId)
                    return panel;
            }
            return null;
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
                Vector2 sp    = cullCam ? cullCam.WorldToScreenPoint(world)
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
