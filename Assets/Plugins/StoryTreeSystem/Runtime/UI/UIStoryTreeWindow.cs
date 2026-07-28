using System.Collections.Generic;
using Ale.Toolkit.Runtime;
using UnityEngine;

namespace StoryTreeSystem
{
    /// <summary>
    /// 游戏运行时故事树UI主窗口（独立 MonoBehaviour）。
    /// 负责：
    ///  - 根据所有节点的位置和尺寸自动计算并设置 storyNodeTreeRoot 的 Size；
    ///  - 按节点类型为节点UI维护 ToolkitGameObjectPool 对象池；
    ///  - 通过视口裁剪按需 Spawn/Despawn 节点UI；
    ///  - 为每种节点类型合并生成连线 Mesh（减少 DrawCall），
    ///    UV.x 用于边缘渐变，UV.y 用于流动效果（配合 StoryLineFlow.shader）；
    ///  - 在 LateUpdate 中按脏标记按需重建连线 Mesh。
    /// </summary>
    public class UIStoryTreeWindow : MonoBehaviour
    {
        [Header("基础配置")]
        [Tooltip("故事树配置资产：包含节点实例、节点类型定义等数据。")]
        [SerializeField] private ConfigStoryTree config;

        [Tooltip("故事节点树UI根容器（通常为 ScrollView 的 Content 节点）。\n" +
                 "运行时会在此节点下自动创建 LineContainer（连线层，最底层）\n" +
                 "和 NodeContainer（节点层，最顶层），并自动计算根容器的 Size。")]
        [SerializeField] private RectTransform storyNodeTreeRoot;
        [Tooltip("故事节点树四周内边距（x=左, y=上, z=右, w=下）。\n" +
                 "在所有节点的外围增加留白，避免节点被 ScrollView 边缘裁剪。")]
        [SerializeField] private Vector4 storyNodeTreePadding = new Vector4(100f, 100f, 100f, 100f);

        /// <summary>
        /// 将编辑器画布坐标系下的节点位置整体偏移到 storyNodeTreeRoot 局部坐标系。
        /// 由 CalcAndSetRootSize() 计算，使最顶/最左节点的边缘紧贴内边距。
        /// </summary>
        private Vector2 _nodePositionOffset;

        private string _selectedNodeId; // 当前选中的节点 ID

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
            RefreshVisibility();
        }

        private void OnDestroy()
        {
            foreach (var mat in _lineMaterialInstances.Values)
                if (mat) Destroy(mat);
            _lineMaterialInstances.Clear();

            foreach (var cr in _lineCanvasRenderers.Values)
            {
                if (!cr) continue;
                var mesh = cr.GetMesh();
                if (mesh) Destroy(mesh);
            }
        }

        #region 公开接口
        /// <summary>
        /// 初始化整棵故事树UI：
        /// 1. 校验配置数据；2. 创建容器；3. 计算并设置根容器 Size；
        /// 4. 初始化对象池；5. 初始化连线 Mesh 渲染器；6. 标记连线脏。
        /// configOverride 不为 null 时替换当前配置资产。
        /// </summary>
        public void InitTree(ConfigStoryTree configOverride = null)
        {
            if (configOverride) config = configOverride;
            if (!config)
            {
                Debug.LogWarning("[UIStoryTreeWindow] config（ConfigStoryTree）未赋值，无法初始化故事树。", this);
                return;
            }

            ValidateConfig();
            EnsureContainers();
            CalcAndSetRootSize(); // 计算 sizeDelta 与节点偏移量（依赖 EnsureContainers 已执行）
            EnsureCamera();
            InitPools();
            InitLineMeshRenderers();
            _lineMeshDirty = true;
        }

        /// <summary>
        /// 选中指定节点。取消前一个选中节点的高亮，并通知新节点被选中。
        /// 若传入相同 nodeId 则无操作。
        /// </summary>
        public void SelectNode(string nodeId)
        {
            if (_selectedNodeId == nodeId) return;

            if (_activeNodes.TryGetValue(_selectedNodeId, out var prev))
                prev.OnNodeDeselected();

            _selectedNodeId = nodeId;

            if (_activeNodes.TryGetValue(_selectedNodeId, out var next))
                next.OnNodeSelected();
        }
        
        /// <summary>
        /// 将节点的编辑器画布坐标（已加 _nodePositionOffset）转换为连线 Mesh 顶点局部坐标。
        /// LineContainer 和 NodeContainer 均以 stretch 模式附着于 storyNodeTreeRoot，
        /// 因此三者局部坐标系完全一致，直接使用偏移后的 UI 坐标即可。
        /// </summary>
        private Vector3 NodeDataToLineLocalPos(StoryNodeData node)
        {
            return new Vector3(
                node.position.x + _nodePositionOffset.x,
                node.position.y + _nodePositionOffset.y,
                0f);
        }
        #endregion
        
        #region 初始化
        // 两者均使用 RectTransform stretch 模式，自动跟随 storyNodeTreeRoot 的 Size 变化。
        private RectTransform _lineContainer; // 连线 Mesh 层，firstSibling（渲染在节点之下）
        private RectTransform _nodeContainer; // 节点 UI 层，lastSibling（渲染在连线之上）
        
        /// <summary>
        /// 确保 LineContainer 和 NodeContainer 已在 storyNodeTreeRoot 下创建。
        /// 两者均挂载 RectTransform 并设置为 stretch 模式，自动适应 storyNodeTreeRoot 的 Size。
        /// 层级顺序：LineContainer（firstSibling）在下，NodeContainer（lastSibling）在上。
        /// </summary>
        private void EnsureContainers()
        {
            if (!storyNodeTreeRoot) return;

            // ── 连线容器（最先渲染，在节点之下）──
            var lineT = storyNodeTreeRoot.Find("LineContainer");
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
                _lineContainer.SetParent(storyNodeTreeRoot, false);
                _lineContainer.SetAsFirstSibling();
            }
            // stretch 模式：锚点铺满父容器，自动跟随 storyNodeTreeRoot.sizeDelta 变化
            // pivot=(0,0) 使局部坐标原点位于左下角，与节点 anchoredPosition 坐标系保持一致
            _lineContainer.pivot     = Vector2.zero;
            _lineContainer.anchorMin = Vector2.zero;
            _lineContainer.anchorMax = Vector2.one;
            _lineContainer.offsetMin = Vector2.zero;
            _lineContainer.offsetMax = Vector2.zero;

            // ── 节点容器（最后渲染，在连线之上）──
            var nodeT = storyNodeTreeRoot.Find("NodeContainer");
            if (nodeT)
            {
                _nodeContainer = nodeT.GetComponent<RectTransform>()
                              ?? nodeT.gameObject.AddComponent<RectTransform>();
            }
            else
            {
                var go = new GameObject("NodeContainer");
                _nodeContainer = go.AddComponent<RectTransform>();
                _nodeContainer.SetParent(storyNodeTreeRoot, false);
                _nodeContainer.SetAsLastSibling();
            }
            // stretch 模式
            // pivot=(0,0) 使局部坐标原点位于左下角，与节点 anchoredPosition 坐标系保持一致
            _nodeContainer.pivot     = Vector2.zero;
            _nodeContainer.anchorMin = Vector2.zero;
            _nodeContainer.anchorMax = Vector2.one;
            _nodeContainer.offsetMin = Vector2.zero;
            _nodeContainer.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// 遍历所有节点，计算其在编辑器画布坐标系中的总包围盒，
        /// 加上四周内边距后设置 storyNodeTreeRoot.sizeDelta，
        /// 同时计算 _nodePositionOffset（使各节点在 storyNodeTreeRoot 局部空间内正确排布）。
        ///
        /// storyNodeTreePadding 布局：x=左, y=上, z=右, w=下。
        /// 偏移量 = (-minX + padLeft, -minY + padTop)，
        /// 使最小包围盒的左上角节点从内边距起始位置开始。
        /// </summary>
        private void CalcAndSetRootSize()
        {
            if (!storyNodeTreeRoot || config == null
                || config.nodes == null || config.nodes.Count == 0)
            {
                _nodePositionOffset = Vector2.zero;
                return;
            }

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;

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
            }

            // storyNodeTreePadding: x=左, y=上, z=右, w=下
            float padL = storyNodeTreePadding.x, padT = storyNodeTreePadding.y;
            float padR = storyNodeTreePadding.z, padB = storyNodeTreePadding.w;

            float contentW = (maxX - minX) + padL + padR;
            float contentH = (maxY - minY) + padT + padB;
            storyNodeTreeRoot.sizeDelta = new Vector2(contentW, contentH);

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
            string cfgName = $"[UIStoryTreeWindow ({config.name})]";

            if (!storyNodeTreeRoot)
                Debug.LogWarning($"{cfgName} storyNodeTreeRoot 未赋值，节点UI将无法正确挂载。", this);

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
                Debug.LogWarning($"{cfgName} 节点列表（nodes）为空，故事树中没有任何节点。", this);
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
        
        #region 故事节点
        // 对象池：key = typeName
        private readonly Dictionary<string, ToolkitGameObjectPool> _pools
            = new Dictionary<string, ToolkitGameObjectPool>();

        /// <summary>
        /// 根据配置中的节点类型定义初始化各类型的 ToolkitGameObjectPool。
        /// 同时清理已存在的激活节点。
        /// </summary>
        private void InitPools()
        {
            foreach (var pool in _pools.Values)
                if (pool) pool.DespawnAll();
            _pools.Clear();
            _activeNodes.Clear();

            foreach (var nodeType in config.nodeTypes)
            {
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
        /// anchoredPosition = 节点画布坐标 + _nodePositionOffset，使坐标系与 storyNodeTreeRoot 一致。
        /// nodeData.nodeTypeRef 在 _pools 中不存在时返回 null。
        /// </summary>
        private void SpawnNode(StoryNodeData nodeData)
        {
            if (!_pools.TryGetValue(nodeData.nodeTypeRef, out var pool)) return;

            var parent = _nodeContainer ? (Transform)_nodeContainer : storyNodeTreeRoot;
            var nodeInstance = pool.Spawn(Vector3.zero, Quaternion.identity, parent);
            var nodeUI = nodeInstance.GetComponent<UIStoryNodeBase>();
            if (!nodeUI) return;

            var nodeType = config.GetNodeType(nodeData.nodeTypeRef);
            nodeUI.OnBindData(nodeData, nodeType);

            // anchoredPosition = 节点画布坐标 + 整体偏移量
            // 偏移量由 CalcAndSetRootSize() 计算，确保所有节点位于 storyNodeTreeRoot 范围内
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
                    cr.SetMesh(null);
                    Destroy(cr.gameObject);
                }
            }
            _lineCanvasRenderers.Clear();

            foreach (var nodeType in config.nodeTypes)
            {
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

            // 按 typeName 分组收集所有连线段（局部空间坐标）
            var segmentsByType = new Dictionary<string, List<(Vector3 from, Vector3 to)>>();

            foreach (var node in config.nodes)
            {
                if (node == null) continue;
                foreach (var childId in node.childNodeIds)
                {
                    var child = config.GetNode(childId);
                    if (child == null) continue;

                    var parentType = config.GetNodeType(node.nodeTypeRef);
                    if (parentType == null) continue;

                    var typeName = node.nodeTypeRef;
                    if (!segmentsByType.TryGetValue(typeName, out var list))
                    {
                        list = new List<(Vector3, Vector3)>();
                        segmentsByType[typeName] = list;
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

                if (!segmentsByType.TryGetValue(typeName, out var segments) || segments.Count == 0)
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
        /// 将一批连线段合并为一个 Mesh，委托给共用工具类 <see cref="StoryLineBuilder"/>。
        /// UV.x（0→1）对应线宽方向（边缘渐变），UV.y（弧长/100f）用于流动效果，纹理密度与线长无关。
        /// </summary>
        private static Mesh BuildCombinedLineMesh(
            List<(Vector3 from, Vector3 to)> segments,
            StoryLineTypeData lineData,
            ELayoutDirection dir)
            => StoryLineBuilder.BuildCombinedLineMesh(segments, lineData, dir);
        #endregion
        
        #region 视口裁剪
        [Header("视口裁剪")]
        [Tooltip("UI摄像机：留空则自动从父级 Canvas.worldCamera 获取，仍为空则回落 Camera.main。")]
        [SerializeField] private Camera uiCamera;

        [Tooltip("视口裁剪边缘缓冲（像素）：将裁剪范围向外扩展，避免节点在边缘出现闪烁。")]
        [SerializeField] private float cullPadding = 100f;

        // 当前激活的节点UI：key = nodeId
        private readonly Dictionary<string, UIStoryNodeBase> _activeNodes
            = new Dictionary<string, UIStoryNodeBase>();

        /// <summary>
        /// 刷新所有节点的可见性。
        /// </summary>
        public void RefreshVisibility()
        {
            if (!config || !storyNodeTreeRoot || !_nodeContainer) return;

            // 屏幕边界（含裁剪缓冲）
            var screenMin = new Vector2(-cullPadding, -cullPadding);
            var screenMax = new Vector2(Screen.width + cullPadding, Screen.height + cullPadding);

            // _nodeContainer.position 为世界坐标（Screen Space Overlay 下与屏幕像素一致）。
            // 节点的 anchoredPosition = node.position + _nodePositionOffset，
            // 因此节点屏幕坐标 = _nodeContainer.position.xy + node.position + _nodePositionOffset。
            float containerX = _nodeContainer.position.x;
            float containerY = _nodeContainer.position.y;
            // 检查所有节点是否在屏幕边界内，只为可见节点分配 UI 实例进行显示。
            foreach (var node in config.nodes)
            {
                if (node == null) continue;

                // _nodeContainer.position是屏幕空间坐标，再加上 节点的局部坐标(包括局部偏移) 就能直接得到 节点的 屏幕空间坐标
                float nodePosX = containerX + node.position.x + _nodePositionOffset.x;
                float nodePosY = containerY + node.position.y + _nodePositionOffset.y;
                // 判断节点中心点是否在屏幕边界内（含缓冲区）
                bool visible = nodePosX >= screenMin.x && nodePosX <= screenMax.x && 
                               nodePosY >= screenMin.y && nodePosY <= screenMax.y;

                if (visible && !_activeNodes.ContainsKey(node.nodeId))
                    SpawnNode(node);
                else if (!visible && _activeNodes.ContainsKey(node.nodeId))
                    DespawnNode(node.nodeId);
            }
        }
        #endregion
    }
}
