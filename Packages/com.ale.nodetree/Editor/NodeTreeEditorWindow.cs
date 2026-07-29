using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Ale.NodeTree.Runtime;
using Ale.Toolkit.Runtime;
using Ale.Toolkit.Editor;

namespace Ale.NodeTree.Editor
{
    /// <summary>
    /// 节点树可视化编辑器主窗口（IMGUI + GL）。
    /// 通过菜单 Tools/NodeTree/Node Tree Editor 或 NodeTreeData Inspector 按钮打开。
    /// 三列布局：左侧节点/条件类型管理 | 中央画布（节点拖拽/缩放/平移/连线） | 右侧节点属性面板。
    /// 所有修改通过 Undo.RecordObject + EditorUtility.SetDirty 支持撤销并触发资产保存。
    /// </summary>
    public class NodeTreeEditorWindow : EditorWindow
    {
        // ── 布局常量 ──
        private static readonly Vector2 WindowDefaultSize = new Vector2(1600f, 900f); // 首次打开时的默认窗口尺寸
        private static readonly Vector2 WindowMinSize     = new Vector2(800f,  500f); // 窗口最小尺寸限制
        private const float ToolbarHeight    = 44f;  // 工具栏高度（像素）
        private const float LeftPanelWidth   = 260f; // 左侧面板宽度（像素）
        private const float RightPanelWidth  = 380f; // 右侧面板宽度（像素）
        private const float GridSpacing      = 20f;  // 背景网格基础间距（画布单位）
        private const float NodeAutoSpacingX = 160f; // 自动布局水平间距（画布单位）
        private const float NodeAutoSpacingY = 120f; // 自动布局垂直间距（画布单位）
        
        #region 基础功能
        // ── 数据 ──
        private NodeTreeData _config;                              // 当前编辑的节点树配置资产
        private NodeTreeCanvasState _canvas = new NodeTreeCanvasState(); // 画布交互状态（平移/缩放/选中/拖拽）
        // ── 持久化键（EditorPrefs：编辑器窗口状态，不污染游戏 PlayerPrefs） ──
        private const string PrefKeyConfigPath = "NodeTreeSystem.ConfigPath"; // 上次打开的配置文件资产路径
        private const string PrefKeyPanX       = "NodeTreeSystem.PanX";      // 画布平移 X 分量
        private const string PrefKeyPanY       = "NodeTreeSystem.PanY";      // 画布平移 Y 分量
        private const string PrefKeyZoom       = "NodeTreeSystem.Zoom";      // 画布缩放比例
        // ── 列表编辑器 ──
        private ReorderableList _nodeTypeList;      // 左侧节点类型可重排列表
        private ReorderableList _conditionTypeList; // 左侧条件类型可重排列表
        private Vector2 _leftScroll;                // 左侧面板滚动位置
        private Vector2 _rightScroll;               // 右侧面板滚动位置
        private int _leftTab;                       // 左侧标签页索引（0=节点类型, 1=条件类型）
        // ── Layout/Repaint 快照（防止 IMGUI 控件数量不一致） ──
        private string _snapSelectedId;      // Layout 事件时快照的选中节点 ID
        private bool   _snapIsDragging;      // Layout 事件时快照的拖拽状态
        private int    _snapNodeTypeIdx = -1; // Layout 事件时快照的节点类型选中索引

        // ── 缓存的 GUIStyle / 数组（避免每次 OnGUI 重新分配）──
        private static readonly string[] LayoutDirLabels =
            { "→ 左到右", "← 右到左", "↓ 上到下", "↑ 下到上" };
        private static readonly ELayoutDirection[] LayoutDirValues =
        {
            ELayoutDirection.Left2Right,
            ELayoutDirection.Right2Left,
            ELayoutDirection.Top2Bottom,
            ELayoutDirection.Bottom2Top
        };
        private GUIStyle _snapStyle;
        private GUIStyle _warnLabelStyle;
        private GUIStyle _redButtonStyle;
        private GUIStyle _duplicateLabelStyle;
        private GUIStyle _connectionHintStyle;
        
        /// <summary>通过菜单打开编辑器窗口（无配置文件预加载）。</summary>
        [MenuItem("Tools/NodeTree/Node Tree Editor")]
        public static void Open()
        {
            var window = OpenWindow();
            window.Show();
        }

        /// <summary>
        /// 打开编辑器窗口并加载指定配置文件。
        /// 由 NodeTreeDataEditor Inspector 按钮调用。
        /// </summary>
        public static void Open(NodeTreeData config)
        {
            var window = OpenWindow();
            if (config)
            {
                window._config = config;
                EditorPrefs.SetString(PrefKeyConfigPath, AssetDatabase.GetAssetPath(config));
                window.RebuildLists();
            }
            window.Show();
            window.Focus();
        }

        /// <summary>
        /// 获取（或创建）编辑器窗口实例，设置最小尺寸。
        /// 首次创建时居中显示并应用默认尺寸。
        /// </summary>
        private static NodeTreeEditorWindow OpenWindow()
        {
            bool isNew = !HasOpenInstances<NodeTreeEditorWindow>();
            var window = GetWindow<NodeTreeEditorWindow>("Node Tree Editor");
            window.minSize = WindowMinSize;
            if (isNew)
            {
                // 首次创建：居中并应用默认尺寸
                var res = EditorGUIUtility.GetMainWindowPosition();
                float x = res.x + (res.width  - WindowDefaultSize.x) * 0.5f;
                float y = res.y + (res.height - WindowDefaultSize.y) * 0.5f;
                window.position = new Rect(x, y, WindowDefaultSize.x, WindowDefaultSize.y);
            }
            return window;
        }
        
        // ── 生命周期 ──
        /// <summary>窗口启用时从 EditorPrefs 恢复画布状态，并尝试加载上次打开的配置文件。</summary>
        private void OnEnable()
        {
            // 恢复画布状态
            _canvas.PanOffset = new Vector2(
                EditorPrefs.GetFloat(PrefKeyPanX, 0f),
                EditorPrefs.GetFloat(PrefKeyPanY, 0f));
            _canvas.Zoom = EditorPrefs.GetFloat(PrefKeyZoom, 1f);
            _canvas.Zoom = Mathf.Clamp(_canvas.Zoom, NodeTreeCanvasState.MinZoom, NodeTreeCanvasState.MaxZoom);
            wantsMouseMove = true; // 鼠标移动时触发事件，用于连线悬停检测
            Undo.undoRedoPerformed += OnUndoRedoPerformed;

            // 恢复上次打开的配置
            string path = EditorPrefs.GetString(PrefKeyConfigPath, "");
            if (!string.IsNullOrEmpty(path))
            {
                _config = AssetDatabase.LoadAssetAtPath<NodeTreeData>(path);
                if (_config) RebuildLists();
            }
        }

        /// <summary>窗口禁用时将画布状态保存到 EditorPrefs，并取消 Undo 回调订阅。</summary>
        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            EditorPrefs.SetFloat(PrefKeyPanX, _canvas.PanOffset.x);
            EditorPrefs.SetFloat(PrefKeyPanY, _canvas.PanOffset.y);
            EditorPrefs.SetFloat(PrefKeyZoom, _canvas.Zoom);
        }

        /// <summary>
        /// Undo/Redo 执行后的回调：重建 ReorderableList 并刷新画布。
        /// Unity 已将 ScriptableObject 数据回滚/恢复，此处只需同步 UI 状态。
        /// </summary>
        private void OnUndoRedoPerformed()
        {
            if (!_config) return;
            RebuildLists(); // 同时会将 _needDuplicateCheck 置 true
            Repaint();
        }

        /// <summary>
        /// IMGUI 主绘制入口。
        /// Layout 事件时快照影响控件分支的状态，随后依次绘制工具栏、左/中/右三列面板。
        /// </summary>
        private void OnGUI()
        {
            // Layout 事件时快照状态，防止 Repaint 时控件数量不一致
            if (Event.current.type == EventType.Layout)
            {
                _snapSelectedId         = _canvas.SelectedNodeId;
                _snapIsDragging         = _canvas.IsDraggingNode;
                _snapNodeTypeIdx        = _selectedNodeTypeIdx;
                _snapIdDuplicateWarning = _idDuplicateWarning;
                _snapHoveredNodeId      = _hoveredNodeId;
                _snapSelectedConnFrom   = _selectedConnFrom;
                _snapSelectedConnTo     = _selectedConnTo;
                _snapHoveredConnFrom    = _hoveredConnFrom;
                _snapHoveredConnTo      = _hoveredConnTo;
                _snapIsAddingConnection = _isAddingConnection;
                _snapConnectionSourceId = _connectionSourceId;
            }

            DrawToolbar();

            if (!_config)
            {
                DrawNoConfigMessage();
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawLeftPanel();
                DrawViewport();
                DrawRightPanel();
            }
        }
        #endregion

        #region 标脏刷新
        // ── 节点类型/条件类型下拉缓存（避免每帧重建字符串数组） ──
        private string[] _nodeTypeNames      = Array.Empty<string>(); // 节点类型名称数组（用于 Popup）
        private string[] _conditionTypeNames = Array.Empty<string>(); // 条件类型名称数组（第 0 项为"无条件"）
        
        /// <summary>
        /// 标记配置资产为已修改（触发 Unity 自动保存机制）。
        /// 不调用 AssetDatabase.SaveAssets()，避免每次修改都阻塞编辑器。
        /// 同时标记需要重新扫描重名节点。
        /// </summary>
        private void MarkDirty()
        {
            if (!_config) return;
            _needDuplicateCheck = true; // 节点数据已变，下一帧重新检查重名
            EditorUtility.SetDirty(_config);
            // AssetDatabase.SaveAssets() 太频繁会卡顿，改为实时 SetDirty，Unity 自动在适当时机写盘
        }

        // ── AttributeFieldDrawer 绘制上下文（供节点名/描述、节点自定义属性 AttributeValue 使用）──
        private AttrEditorContext _attrCtx;
        private AttrEditorContext AttrCtx => _attrCtx ??= new AttrEditorContext(this);

        // ── 节点类型「自定义属性字段」schema 列表绘制器（持久实例：按绑定的 attributes 引用变更自动重建）──
        private readonly AttributeDefinitionListDrawer _nodeTypeAttrListDrawer = new AttributeDefinitionListDrawer();

        /// <summary>
        /// 供 <see cref="AttributeFieldDrawer"/> 绘制节点名/描述（<see cref="AttributeValue"/> Text）的最小
        /// <see cref="IEditorContext"/> 适配器。AttributeFieldDrawer 仅用 RecordUndo / MarkDirty / Repaint，
        /// 不访问 Serialized / Resolver，故二者返回 null。
        /// </summary>
        private sealed class AttrEditorContext : IEditorContext
        {
            private readonly NodeTreeEditorWindow _owner;
            public AttrEditorContext(NodeTreeEditorWindow owner) => _owner = owner;

            public SerializedObject  Serialized => null;
            public IAssetRefResolver Resolver   => null;

            public void RecordUndo(string actionName)
            {
                if (_owner._config) Undo.RecordObject(_owner._config, actionName);
            }
            public void MarkDirty() => _owner.MarkDirty();
            public void Repaint()   => _owner.Repaint();
        }
        
        /// <summary>
        /// 根据当前 _config 重建 左侧节点类型列表 和 条件类型列表的 ReorderableList，
        /// 并刷新类型名称缓存数组。由 NodeTreeDataEditor 的 internal 可见性调用。
        /// </summary>
        internal void RebuildLists()
        {
            if (!_config) return;

            // 节点类型列表
            _nodeTypeList = new ReorderableList(_config.nodeTypes,
                typeof(NodeTypeData), true, true, true, true);
            _nodeTypeList.drawHeaderCallback  = r => EditorGUI.LabelField(r, "节点类型");
            _nodeTypeList.drawElementCallback = (r, idx, _, _) =>
            {
                if (idx >= _config.nodeTypes.Count) return;
                var t    = _config.nodeTypes[idx];
                r.height = EditorGUIUtility.singleLineHeight;
                float half = r.width * 0.5f;

                // 类型名称（inline 快速编辑）
                EditorGUI.BeginChangeCheck();
                t.typeName = EditorGUI.TextField(new Rect(r.x, r.y, half - 4f, r.height), t.typeName);
                if (EditorGUI.EndChangeCheck())
                {
                    RebuildTypeNameCache();
                    MarkDirty();
                }

                // 颜色预览色块
                EditorGUI.DrawRect(new Rect(r.x + half, r.y + 2f, 18f, r.height - 4f), t.color);

                // 编辑按钮：在右侧面板展示该类型的完整属性
                if (GUI.Button(new Rect(r.x + half + 22f, r.y, half - 22f, r.height), "编辑"))
                {
                    _selectedNodeTypeIdx   = idx;
                    _canvas.SelectedNodeId = null; // 切换到节点类型属性面板
                    GUI.FocusControl(null);
                }
            };
            _nodeTypeList.onAddCallback = _ =>
            {
                Undo.RecordObject(_config, "添加节点类型");
                _config.nodeTypes.Add(new NodeTypeData { typeName = "新类型" });
                RebuildTypeNameCache();
                MarkDirty();
            };
            _nodeTypeList.onRemoveCallback = list =>
            {
                Undo.RecordObject(_config, "删除节点类型");
                _config.nodeTypes.RemoveAt(list.index);
                RebuildTypeNameCache();
                MarkDirty();
            };
            // 拖拽重排：Undo 由 DoLayoutList 前的 RecordObject 覆盖，此处仅标脏保存。
            _nodeTypeList.onReorderCallback = _ => MarkDirty();

            // 条件类型列表
            _conditionTypeList = new ReorderableList(_config.conditionTypes,
                typeof(NodeConditionTypeData), true, true, true, true);
            _conditionTypeList.drawHeaderCallback  = r => EditorGUI.LabelField(r, "条件类型");
            _conditionTypeList.drawElementCallback = (r, idx, _, _) =>
            {
                if (idx >= _config.conditionTypes.Count) return;
                var c     = _config.conditionTypes[idx];
                r.height  = EditorGUIUtility.singleLineHeight;
                float third = r.width / 3f;
                EditorGUI.BeginChangeCheck();
                c.conditionType = EditorGUI.TextField(new Rect(r.x, r.y, third - 2f, r.height), c.conditionType);
                c.description   = EditorGUI.TextField(new Rect(r.x + third, r.y, third * 2f - 2f, r.height), c.description);
                if (EditorGUI.EndChangeCheck())
                {
                    RebuildTypeNameCache(); // 条件类型名变化时刷新 Popup 缓存
                    MarkDirty();
                }
            };
            _conditionTypeList.onAddCallback = _ =>
            {
                Undo.RecordObject(_config, "添加条件类型");
                _config.conditionTypes.Add(new NodeConditionTypeData { conditionType = "新条件" });
                RebuildTypeNameCache();
                MarkDirty();
            };
            _conditionTypeList.onRemoveCallback = list =>
            {
                Undo.RecordObject(_config, "删除条件类型");
                _config.conditionTypes.RemoveAt(list.index);
                RebuildTypeNameCache();
                MarkDirty();
            };
            // 拖拽重排：Undo 由 DoLayoutList 前的 RecordObject 覆盖，此处仅标脏保存。
            _conditionTypeList.onReorderCallback = _ => MarkDirty();

            RebuildTypeNameCache();
            _needDuplicateCheck = true; // 加载新配置后重新检查重名
        }
        
        /// <summary>
        /// 重建 节点类型名称数组 和 条件类型名称数组（用于右侧属性面板的 Popup）。
        /// 条件类型数组第 0 项固定为"（无条件）"。
        /// </summary>
        private void RebuildTypeNameCache()
        {
            if (!_config) return;

            _nodeTypeNames = new string[_config.nodeTypes.Count];
            for (int i = 0; i < _config.nodeTypes.Count; i++)
                _nodeTypeNames[i] = _config.nodeTypes[i].typeName ?? $"类型{i}";

            // 条件类型下拉：第一项为"无条件"
            _conditionTypeNames = new string[_config.conditionTypes.Count + 1];
            _conditionTypeNames[0] = "（无条件）";
            for (int i = 0; i < _config.conditionTypes.Count; i++)
                _conditionTypeNames[i + 1] = _config.conditionTypes[i].conditionType ?? $"条件{i}";
        }
        #endregion
        
        #region 顶部工具栏
        private bool _snapToGrid = true; // 节点拖拽时自动吸附到网格，默认开启
        
        /// <summary>
        /// 绘制顶部工具栏：配置文件选择、布局方向切换、缩放控制、自动布局按钮。
        /// </summary>
        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar, GUILayout.Height(ToolbarHeight)))
            {
                // 配置文件选择
                GUILayout.Label("配置:", GUILayout.Width(36f));
                var newConfig = (NodeTreeData)EditorGUILayout.ObjectField(
                    _config, typeof(NodeTreeData), false, GUILayout.Width(200f));
                if (newConfig != _config)
                {
                    _config = newConfig;
                    if (_config)
                    {
                        EditorPrefs.SetString(PrefKeyConfigPath,
                            AssetDatabase.GetAssetPath(_config));
                        RebuildLists();
                    }
                }

                GUILayout.Space(12f);
                GUILayout.Label("布局方向:", GUILayout.Width(60f));

                if (_config)
                {
                    int curIdx = Array.IndexOf(LayoutDirValues, _config.layoutDirection);
                    int newIdx = GUILayout.Toolbar(curIdx < 0 ? 0 : curIdx, LayoutDirLabels, GUILayout.Width(260f));
                    if (newIdx != curIdx)
                    {
                        Undo.RecordObject(_config, "修改布局方向");
                        var oldDir = _config.layoutDirection;
                        _config.layoutDirection = LayoutDirValues[newIdx];
                        RotateNodesForLayoutChange(oldDir, _config.layoutDirection);
                        MarkDirty();
                    }
                }

                GUILayout.Space(12f);

                // 缩放控制
                GUILayout.Label("缩放:", GUILayout.Width(36f));
                if (GUILayout.Button("-", GUILayout.Width(22f)))
                    ZoomBy(-0.1f);
                GUILayout.Label($"{Mathf.RoundToInt(_canvas.Zoom * 100f)}%", GUILayout.Width(40f));
                if (GUILayout.Button("+", GUILayout.Width(22f)))
                    ZoomBy(0.1f);
                if (GUILayout.Button("重置", GUILayout.Width(42f)))
                {
                    _canvas.Zoom = 1f;

                    // 将视口中心对准开始节点（第一个根节点；若无根节点则取列表第一个节点）
                    if (_config && _config.nodes.Count > 0)
                    {
                        var roots     = _config.GetRootNodes();
                        var startNode = (roots != null && roots.Count > 0)
                            ? roots[0]
                            : _config.nodes[0];

                        // 不使用 _canvasRect（DrawToolbar 先于 DrawCanvas 执行，
                        // 点击事件发生时 _canvasRect 尚未被当前帧更新，宽高可能为 0）。
                        // 改用 position（EditorWindow 窗口尺寸）和布局常量直接推算画布中心，
                        // 这些值任何时刻都可靠。
                        float canvasW = position.width  - LeftPanelWidth - RightPanelWidth;
                        float canvasH = position.height - ToolbarHeight;
                        var center    = new Vector2(canvasW * 0.5f, canvasH * 0.5f);

                        // panOffset 使 CanvasToScreen(startNode.position) == 画布中心
                        // 即：startNode.position * zoom + panOffset = center
                        _canvas.PanOffset = center - startNode.position * _canvas.Zoom;
                    }
                    else
                    {
                        _canvas.PanOffset = Vector2.zero;
                    }

                    Repaint();
                }

                GUILayout.Space(8f);
                // 吸附网格开关：开启时拖拽始终吸附；关闭时按住 Shift 可临时吸附
                var snapStyle = _snapStyle ??= new GUIStyle(EditorStyles.toolbarButton);
                snapStyle.normal.textColor   = _snapToGrid ? new Color(0.2f, 0.85f, 0.2f) : new Color(0.5f, 0.5f, 0.5f);
                snapStyle.onNormal.textColor = new Color(0.2f, 0.85f, 0.2f);
                snapStyle.hover.textColor    = snapStyle.normal.textColor;
                snapStyle.active.textColor   = snapStyle.normal.textColor;
                _snapToGrid = GUILayout.Toggle(_snapToGrid, "吸附网格",
                    snapStyle, GUILayout.Width(64f));

                GUILayout.FlexibleSpace();

                // 自动布局
                if (_config)
                {
                    if (GUILayout.Button("自动布局", GUILayout.Width(70f)))
                        RunAutoLayout();
                }
            }
        }

        /// <summary>未加载配置文件时在画布区域居中显示提示信息。</summary>
        private void DrawNoConfigMessage()
        {
            GUILayout.FlexibleSpace();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("请在工具栏中选择或创建一个 ConfigNodeTree 资产", EditorStyles.centeredGreyMiniLabel);
                GUILayout.FlexibleSpace();
            }
            GUILayout.FlexibleSpace();
        }
        
        #region 视口方向
        /// <summary>
        /// 切换布局方向时，以第一个根节点（开始节点）为轴心，
        /// 将所有节点位置旋转到新方向对应的角度。
        ///
        /// 方向按顺时针赋索引：L→R=0, T→B=1, R→L=2, B→T=3。
        /// 旋转步数 = (newIdx - oldIdx + 4) % 4，每步 90° 顺时针。
        /// 屏幕坐标系（Y 向下）90° CW：(x,y) → (-y, x)。
        /// </summary>
        private void RotateNodesForLayoutChange(ELayoutDirection oldDir, ELayoutDirection newDir)
        {
            if (oldDir == newDir || !_config || _config.nodes.Count == 0) return;

            int steps = (DirToClockwiseIndex(newDir) - DirToClockwiseIndex(oldDir) + 4) % 4;
            if (steps == 0) return;

            // 轴心：第一个根节点（即开始节点）
            var roots    = _config.GetRootNodes();
            var pivot    = (roots != null && roots.Count > 0) ? roots[0] : _config.nodes[0];
            var pivotPos = pivot.position;

            foreach (var node in _config.nodes)
            {
                var delta    = node.position - pivotPos;
                node.position = pivotPos + RotateBySteps(delta, steps);
            }
        }

        /// <summary>
        /// 在画布坐标系（Y 向上）中对向量 v 执行 steps 步 90° 顺时针旋转。
        /// 方向向量对应关系（Y 向上，顺时针排列）：
        ///   L→R (1,0) → T→B (0,-1) → R→L (-1,0) → B→T (0,1) → L→R
        /// 1步 CW: (x,y) → ( y, -x)   [右→下→左→上，Y 向上]
        /// 2步 CW: (x,y) → (-x, -y)   [180°，与方向无关]
        /// 3步 CW: (x,y) → (-y,  x)   [等价于 90° 逆时针，Y 向上]
        /// </summary>
        private static Vector2 RotateBySteps(Vector2 v, int steps)
        {
            switch (steps % 4)
            {
                case 1: return new Vector2( v.y, -v.x);
                case 2: return new Vector2(-v.x, -v.y);
                case 3: return new Vector2(-v.y,  v.x);
                default: return v;
            }
        }
        
        /// <summary>
        /// 将布局方向映射到顺时针索引（L→R=0, T→B=1, R→L=2, B→T=3）。
        /// 相邻索引差 1 对应 90° 顺时针旋转。
        /// </summary>
        private static int DirToClockwiseIndex(ELayoutDirection dir)
        {
            switch (dir)
            {
                case ELayoutDirection.Left2Right:  return 0;
                case ELayoutDirection.Top2Bottom:  return 1;
                case ELayoutDirection.Right2Left:  return 2;
                case ELayoutDirection.Bottom2Top:  return 3;
                default:                           return 0;
            }
        }
        #endregion

        #region 视口缩放
        /// <summary>将画布缩放按 delta 步长调整，并限制在 [MinZoom, MaxZoom] 范围内。</summary>
        private void ZoomBy(float delta)
        {
            _canvas.Zoom = Mathf.Clamp(_canvas.Zoom + delta,
                NodeTreeCanvasState.MinZoom, NodeTreeCanvasState.MaxZoom);
            Repaint();
        }
        #endregion
        #endregion

        #region 左侧面板
        /// <summary>
        /// 绘制左侧面板：两个标签页（节点类型 / 条件类型），各自对应一个 ReorderableList。
        /// </summary>
        private void DrawLeftPanel()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(LeftPanelWidth)))
            {
                // Tab 切换
                _leftTab = GUILayout.Toolbar(_leftTab, new[] { "节点类型", "条件类型" });

                using (var scroll = new EditorGUILayout.ScrollViewScope(
                    _leftScroll,
                    false, false,
                    GUIStyle.none,
                    GUI.skin.verticalScrollbar,
                    GUI.skin.scrollView))
                {
                    _leftScroll = scroll.scrollPosition;
                    // DoLayoutList 前记录 Undo：覆盖内联编辑与拖拽重排（每帧记录快照，实际改变时才生成撤销条目）。
                    if (_leftTab == 0)
                    {
                        if (_nodeTypeList != null)
                        {
                            if (_config) Undo.RecordObject(_config, "编辑节点类型列表");
                            _nodeTypeList.DoLayoutList();
                        }
                    }
                    else
                    {
                        if (_conditionTypeList != null)
                        {
                            if (_config) Undo.RecordObject(_config, "编辑条件类型列表");
                            _conditionTypeList.DoLayoutList();
                        }
                    }
                }
            }
        }
        #endregion

        #region 右侧面板
        // ── 右侧面板节点类型编辑 ──
        private int _selectedNodeTypeIdx = -1; // 右侧面板当前编辑的节点类型索引，-1 表示未选中
        
        /// <summary>
        /// 绘制右侧属性面板：
        /// 节点类型被"编辑"选中时显示节点类型属性；
        /// 画布节点被选中时显示节点属性；
        /// 均未选中时显示提示文字。
        /// 使用 Layout 快照值避免 Repaint 期间控件数量变化。
        /// </summary>
        private void DrawRightPanel()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(RightPanelWidth)))
            {
                // alwaysShowHorizontal=false + horizontalScrollbar=GUIStyle.none：
                // 彻底移除横向滚动条，内容宽度被限制在面板宽度内自动换行/截断，
                // 避免 ScrollViewScope 默认行为在内容略宽时出现底部横向滚动条。
                using (var scroll = new EditorGUILayout.ScrollViewScope(
                    _rightScroll,
                    false, false,
                    GUIStyle.none,
                    GUI.skin.verticalScrollbar,
                    GUI.skin.scrollView))
                {
                    _rightScroll = scroll.scrollPosition;

                    if (_snapNodeTypeIdx >= 0 && _snapNodeTypeIdx < _config.nodeTypes.Count)
                    {
                        DrawNodeTypeProperties(_config.nodeTypes[_snapNodeTypeIdx]);
                    }
                    else if (!string.IsNullOrEmpty(_snapSelectedId))
                    {
                        var node = _config.GetNode(_snapSelectedId);
                        if (node != null)
                            DrawNodeProperties(node);
                    }
                    else
                    {
                        GUILayout.Label("点击节点以编辑属性", EditorStyles.centeredGreyMiniLabel);
                    }
                }
            }
        }

        #region 节点 编辑面板
        // ── 条件系统下拉标签（静态，避免每帧重建） ──
        private static readonly string[] SatisfyTypeLabels    = { "满足所有", "满足任意" };
        private static readonly string[] ComparisonLabels     = { "等于", "不等于", "大于", "小于" };
        // ── 右侧面板节点 ID 重名警告 ──
        private string _idDuplicateWarning; // 当前检测到的重复 ID 输入值（非空时显示警告）
        private string _snapIdDuplicateWarning; // Layout 快照，保证 Repaint 控件数量与 Layout 完全一致
        private string _lastPropertyNodeId; // 上次 DrawNodeProperties 渲染的节点 ID（节点切换时清除警告）
        
        /// <summary>
        /// 右侧面板 节点 可编辑属性：
        /// 节点ID、节点类型、描述、图标、条件类型/参数、自定义键值对数据。
        /// 修改 ID 时同步更新所有父节点的 childNodeIds 引用。
        /// </summary>
        private void DrawNodeProperties(NodeData node)
        {
            // 节点切换时清除上一个节点遗留的重名警告
            if (_lastPropertyNodeId != node.nodeId)
            {
                _lastPropertyNodeId = node.nodeId;
                _idDuplicateWarning = null;
            }

            // ── 标题行 + 右上角重名警告 ──
            // 三个控件（Label / FlexibleSpace / Label）在 Layout 与 Repaint 均恒定调用，
            // 警告文字非空时显示黄色警告，空时不占高度仅以空字符串占位，
            // 保证 IMGUI 控件数量在两个事件中完全一致。
            GUILayout.BeginHorizontal();
            GUILayout.Label("节点属性", EditorStyles.boldLabel, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            var warnLabelStyle = _warnLabelStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                wordWrap  = false,
                normal    = { textColor = new Color(1f, 0.85f, 0.1f, 1f) }
            };
            // 使用 Layout 快照值（_snapIdDuplicateWarning）确保 Repaint 与 Layout 显示同一内容。
            // ExpandWidth(false)：不自动扩展宽度，避免撑宽右侧面板容器；
            // MaxWidth：限制警告文字最大宽度，防止长 ID 导致面板横向溢出。
            GUILayout.Label(
                _snapIdDuplicateWarning != null
                    ? $"⚠ 存在重复的节点ID [{_snapIdDuplicateWarning}]，不可使用"
                    : "",
                warnLabelStyle,
                GUILayout.ExpandWidth(false),
                GUILayout.MaxWidth(RightPanelWidth - 80f));
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(4f);

            // 记录 Undo（每次绘制记录快照，实际改变时才生成撤销条目），使右侧面板节点编辑可撤销。
            Undo.RecordObject(_config, "编辑节点属性");
            EditorGUI.BeginChangeCheck();

            // ID（修改时须保证在配置内唯一；重复时阻止写入并记录警告，有效时同步更新所有引用）
            string newId = EditorGUILayout.TextField("节点 ID", node.nodeId);
            if (newId != node.nodeId)
            {
                if (!string.IsNullOrEmpty(newId) && _config.GetNode(newId) == null)
                {
                    // 有效的新 ID：同步所有引用了旧 ID 的 childNodeIds
                    foreach (var n in _config.nodes)
                    {
                        int idx = n.childNodeIds.IndexOf(node.nodeId);
                        if (idx >= 0) n.childNodeIds[idx] = newId;
                    }
                    node.nodeId             = newId;
                    _canvas.SelectedNodeId  = newId;
                    _lastPropertyNodeId     = newId; // 同步节点切换检测基准
                    _idDuplicateWarning     = null;  // 有效重命名，清除警告
                }
                else if (!string.IsNullOrEmpty(newId))
                {
                    // 重复 ID：阻止写入，记录当前输入值以在下一帧标题行显示警告
                    _idDuplicateWarning = newId;
                }
            }
            else
            {
                // 输入值与当前 ID 一致（用户未改动或已还原），清除警告
                _idDuplicateWarning = null;
            }
            // 节点类型：节点类型引用，决定外观与连线
            int typeIdx    = Array.IndexOf(_nodeTypeNames, node.nodeTypeRef);
            int newTypeIdx = EditorGUILayout.Popup("节点类型 (nodeTypeRef)",
                typeIdx < 0 ? 0 : typeIdx, _nodeTypeNames);
            if (newTypeIdx >= 0 && newTypeIdx < _nodeTypeNames.Length)
                node.nodeTypeRef = _nodeTypeNames[newTypeIdx];

            EditorGUILayout.Space(4f);
            // 描述（多行文本）—— 顺序与 NodeData 字段定义一致
            EditorGUILayout.LabelField("备注 (仅编辑器使用)");
            node.comment = EditorGUILayout.TextArea(node.comment, GUILayout.MinHeight(60f));
            
            // ── 节点条件（conditionSatisfyType + conditionGroups） ──
            if (node.conditionGroups == null) node.conditionGroups = new List<ConditionGroupData>();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("节点条件", EditorStyles.boldLabel, GUILayout.ExpandWidth(false));
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("条件组 要求:", GUILayout.Width(52f));
                node.conditionSatisfyType = (EConditionSatisfyType)EditorGUILayout.Popup(
                    (int)node.conditionSatisfyType, SatisfyTypeLabels, GUILayout.Width(72f));
            }

            // ── 条件组列表 ──
            var redButtonStyle = _redButtonStyle ??= new GUIStyle(GUI.skin.button)
            {
                normal = { textColor = new Color(1f, 0.3f, 0.3f) },
                hover  = { textColor = new Color(1f, 0.6f, 0.6f) },
                active = { textColor = Color.red }
            };
            int removeGroupIdx = -1;
            for (int gi = 0; gi < node.conditionGroups.Count; gi++)
            {
                var group = node.conditionGroups[gi];
                if (group == null) { node.conditionGroups[gi] = group = new ConditionGroupData(); }
                if (group.conditions == null) group.conditions = new List<ConditionData>();

                using (new EditorGUILayout.VerticalScope(GUI.skin.box))
                {
                    // 条件组标题行：序号 + 组内满足类型 + 删除按钮
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField($"条件组 {gi + 1}",
                            EditorStyles.miniBoldLabel, GUILayout.ExpandWidth(false));
                        GUILayout.FlexibleSpace();
                        EditorGUILayout.LabelField("条件 要求:", GUILayout.Width(44f));
                        group.satisfyType = (EConditionSatisfyType)EditorGUILayout.Popup(
                            (int)group.satisfyType, SatisfyTypeLabels, GUILayout.Width(72f));
                        if (GUILayout.Button("×", redButtonStyle, GUILayout.Width(22f)))
                            removeGroupIdx = gi;
                    }

                    EditorGUILayout.Space(2f);

                    // 条件列表（每条占一行：类型 + 比较 + 参数 + 删除）
                    int removeCondIdx = -1;
                    for (int ci = 0; ci < group.conditions.Count; ci++)
                    {
                        var cond = group.conditions[ci];
                        if (cond == null) { group.conditions[ci] = cond = new ConditionData(); }

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            int condTypeIdx    = Array.IndexOf(_conditionTypeNames, cond.conditionType);
                            int newCondTypeIdx = EditorGUILayout.Popup(
                                condTypeIdx < 0 ? 0 : condTypeIdx, _conditionTypeNames);
                            cond.conditionType = newCondTypeIdx > 0 && newCondTypeIdx < _conditionTypeNames.Length
                                ? _conditionTypeNames[newCondTypeIdx] : "";

                            cond.comparison = (EConditionComparison)EditorGUILayout.Popup(
                                (int)cond.comparison, ComparisonLabels, GUILayout.Width(54f));

                            cond.conditionParam = EditorGUILayout.TextField(cond.conditionParam);

                            if (GUILayout.Button("×", redButtonStyle, GUILayout.Width(22f)))
                                removeCondIdx = ci;
                        }
                    }

                    if (removeCondIdx >= 0)
                        group.conditions.RemoveAt(removeCondIdx);

                    if (GUILayout.Button("＋ 添加条件", GUILayout.Height(20f)))
                        group.conditions.Add(new ConditionData());
                }

                EditorGUILayout.Space(2f);
            }

            if (removeGroupIdx >= 0)
                node.conditionGroups.RemoveAt(removeGroupIdx);

            if (GUILayout.Button("＋ 添加条件组"))
                node.conditionGroups.Add(new ConditionGroupData());

            EditorGUILayout.Space(8f);

            // ── 节点结构（nodeTypeRef / childNodeIds / position） ──
            EditorGUILayout.LabelField("UI设置", EditorStyles.boldLabel);
            // 图标（uiIcon）
            node.uiIcon = (Sprite)EditorGUILayout.ObjectField(
                "中心图标 (uiIcon)", node.uiIcon, typeof(Sprite), false);
            EditorGUILayout.Space(4f);

            // ── 节点名称 / 描述（AttributeValue.Text：纯文本 fallback + 可选本地化引用）──
            // 用 toolkit AttributeFieldDrawer 绘制（含原生本地化选择器）；Undo / 标脏经 AttrCtx。
            AttributeFieldDrawer.Draw(AttrCtx, "节点名称", node.nodeName, null);
            EditorGUILayout.Space(2f);
            AttributeFieldDrawer.Draw(AttrCtx, "节点描述", node.nodeDesc, null);
            EditorGUILayout.Space(4f);

            // position：节点在编辑器画布中的位置（像素坐标）
            node.position = EditorGUILayout.Vector2Field("画布坐标 (position)", node.position);
            EditorGUILayout.Space(8f);
            
            // ── 自定义属性值（来自节点类型的 attributes 定义）──
            // 先按 schema 幂等同步（补默认/删多余/类型漂移重置），再逐条用 AttributeFieldDrawer 绘制值。
            // AttributeFieldDrawer 自带 Undo/标脏/复制粘贴，独立于上方节点编辑的 BeginChangeCheck。
            EditorGUILayout.LabelField("自定义属性", EditorStyles.boldLabel);
            int attrCountBefore = node.attributeValues.Count;
            node.RebuildAttributes(_config);
            if (node.attributeValues.Count != attrCountBefore) MarkDirty(); // schema 结构变化时落盘
            if (node.attributeValues.Count == 0)
            {
                EditorGUILayout.LabelField(
                    "（该节点类型未定义属性字段。可在左侧「节点类型」→「编辑」中添加。）",
                    EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                foreach (var entry in node.attributeValues)
                    AttributeFieldDrawer.Draw(AttrCtx, entry.id, entry.value, null);
            }
            EditorGUILayout.Space(8f);
            
            // childNodeIds：有序子节点 ID 列表（只读显示）
            // 子节点结构由画布的"添加子节点"/"添加连线"操作管理，此处仅供查看与快速定位。
            EditorGUILayout.LabelField("子节点列表", EditorStyles.miniBoldLabel);
            if (node.childNodeIds == null) node.childNodeIds = new List<string>();
            if (node.childNodeIds.Count == 0)
            {
                EditorGUILayout.LabelField("（无子节点）", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                for (int i = 0; i < node.childNodeIds.Count; i++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        // 序号
                        EditorGUILayout.LabelField($"[{i}]", GUILayout.Width(28f));
                        // 子节点 ID（只读文本框样式，不可编辑）
                        EditorGUILayout.LabelField(node.childNodeIds[i]);
                        // 定位：将画布视口中心对准该子节点并选中
                        if (GUILayout.Button("定位", GUILayout.Width(36f)))
                            NavigateToNode(node.childNodeIds[i]);
                    }
                }
            }
            
            EditorGUILayout.Space(8f);

            if (EditorGUI.EndChangeCheck())
                MarkDirty();
        }
        #endregion

        #region 节点类型 编辑面板
        /// <summary>
        /// 右侧面板 节点类型 可编辑属性：
        /// 类型名称、显示标签、节点尺寸、节点形状、颜色、图标、UI 预制体、连线样式。
        /// typeName 变化时同步刷新下拉缓存。
        /// </summary>
        private void DrawNodeTypeProperties(NodeTypeData type)
        {
            EditorGUILayout.LabelField("节点类型", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            // 记录 Undo，使右侧面板类型属性编辑可撤销。
            Undo.RecordObject(_config, "编辑节点类型属性");
            EditorGUI.BeginChangeCheck();

            type.typeName = EditorGUILayout.TextField("类型名称", type.typeName);
            type.label    = EditorGUILayout.TextField("显示标签", type.label);

            EditorGUILayout.Space(4f);
            type.resolution = EditorGUILayout.Vector2Field("节点尺寸", type.resolution);
            type.shape      = (ENodeShape)EditorGUILayout.EnumPopup("节点形状", type.shape);
            type.color      = EditorGUILayout.ColorField("节点颜色", type.color);
            type.icon       = (Sprite)EditorGUILayout.ObjectField(
                "节点图标", type.icon, typeof(Sprite), false);
            type.uiPrefab   = (GameObject)EditorGUILayout.ObjectField(
                "UI 预制体", type.uiPrefab, typeof(GameObject), false);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("连线样式", EditorStyles.boldLabel);

            if (type.line == null) type.line = new LineTypeData();
            type.line.lineType  = (ELineType)EditorGUILayout.EnumPopup("连线类型", type.line.lineType);
            type.line.lineWidth = EditorGUILayout.FloatField("连线宽度", type.line.lineWidth);
            type.line.material  = (Material)EditorGUILayout.ObjectField(
                "连线材质", type.line.material, typeof(Material), false);

            if (EditorGUI.EndChangeCheck())
            {
                RebuildTypeNameCache(); // typeName 变化时同步刷新下拉缓存
                MarkDirty();
            }

            // ── 自定义属性字段（schema）：本类型的节点实例据此生成可配置的属性值 ──
            // AttributeDefinitionListDrawer 自带添加/删除/拖拽重排与 Undo（经 AttrCtx）；node-tree 无枚举源，src 传 null。
            EditorGUILayout.Space(8f);
            if (type.attributes == null) type.attributes = new List<AttributeDefinition>();
            _nodeTypeAttrListDrawer.Draw(AttrCtx, null, type.attributes, "自定义属性字段");
        }
        #endregion
        #endregion

        #region 中央视口
        /// <summary>
        /// 绘制 中央视口。
        /// 背景网格 → 连线（GL）→ 节点（IMGUI+GL）。
        /// Layout 事件时记录画布矩形，Repaint 时绘制背景与内容，之后处理鼠标输入。
        /// </summary>
        private void DrawViewport()
        {
            // 按需重新扫描重名节点（节点数据修改后 _needDuplicateCheck 被置 true）
            if (_needDuplicateCheck) UpdateDuplicateCheck();

            var canvasRect = GUILayoutUtility.GetRect(
                GUIContent.none,
                GUIStyle.none,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));

            // 裁剪画布区域（节点/连线均在此 clip 内绘制，超出部分不可见）
            GUI.BeginClip(canvasRect);
            var localRect = new Rect(0f, 0f, canvasRect.width, canvasRect.height);
            // Y 轴向上坐标系：CanvasToScreen / ScreenToCanvas 需要知道当前画布高度
            _canvas.CanvasHeight = localRect.height;

            // 背景
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(localRect, new Color(0.18f, 0.18f, 0.18f));
                DrawGridBackground(localRect);
            }

            // 先画连线（在节点下方）
            DrawAllLineConnections(localRect);

            // 添加连线模式预览线（在连线之上、节点之下）
            DrawPreviewConnection();

            // 再画节点（节点覆盖连线端点）；传入当前帧真实的局部边界用于视口剔除
            DrawNodes(localRect);

            // 连线点击/悬停检测（DrawNodes 之后执行，节点优先消费鼠标事件）
            HandleConnectionInteraction();

            // 最后画箭头（在节点之上，保证不被节点形状遮挡）
            DrawConnectionArrows(localRect);

            // 连线添加模式提示条（最顶层覆盖显示）
            DrawAddConnectionModeHint(localRect);

            // 重名警告（最顶层，始终可见）
            DrawDuplicateWarning(localRect);

            GUI.EndClip();

            // 处理输入（在 clip 外，坐标需要使用 canvasRect 偏移）
            HandleCanvasInput(canvasRect);
        }

        #region 背景网格
        /// <summary>
        /// 绘制 背景网格。
        /// 细线间隔 GridSpacing，每5格绘制一条较亮的主线。
        /// 网格随画布平移/缩放自动偏移，锚定在画布坐标 (0,0) 上。
        ///
        /// Y 轴向上坐标系说明：
        ///   画布 (0,0) 映射到屏幕 Y = canvasHeight - panOffset.y（称为 originScreenY）。
        ///   水平线在屏幕 Y = originScreenY, originScreenY ± spacing, ... 处出现。
        ///   从屏幕顶部往下遍历时，每递增 i，画布 Y 网格索引递减（-i）。
        /// </summary>
        private void DrawGridBackground(Rect rect)
        {
            float spacing = GridSpacing * _canvas.Zoom;

            // ── 垂直线（X 轴，Y 轴向上不影响 X 方向）──
            // 锚定点：画布 X=0 对应屏幕 X = panOffset.x
            float offsetX   = ((_canvas.PanOffset.x % spacing) + spacing) % spacing;
            int startGridX  = Mathf.RoundToInt((offsetX - _canvas.PanOffset.x) / spacing);

            // ── 水平线（Y 轴向上）──
            // 画布 Y=0 在屏幕上的 Y 坐标
            float originScreenY = rect.height - _canvas.PanOffset.y;
            // 第一条线从屏幕顶端往下的偏移（保证非负）
            float offsetY   = ((originScreenY % spacing) + spacing) % spacing;
            // 该首条线对应的画布 Y 网格索引（往下遍历时索引递减）
            int startGridY  = Mathf.RoundToInt((originScreenY - offsetY) / spacing);

            var gridColor      = new Color(0.25f, 0.25f, 0.25f, 1f); // 普通网格线颜色
            var gridColorMajor = new Color(0.3f,  0.3f,  0.3f,  1f); // 主网格线颜色（每5格）

            int countX = Mathf.CeilToInt(rect.width  / spacing) + 1;
            int countY = Mathf.CeilToInt(rect.height / spacing) + 1;

            for (int i = 0; i <= countX; i++)
            {
                float x     = offsetX + i * spacing;
                bool  major = ((startGridX + i) % 5 + 5) % 5 == 0; // 正确处理负数模运算
                EditorGUI.DrawRect(new Rect(x, 0f, 1f, rect.height), major ? gridColorMajor : gridColor);
            }
            for (int i = 0; i <= countY; i++)
            {
                float y = offsetY + i * spacing;
                // Y 轴向上：往下遍历时画布 Y 索引递减，用 startGridY - i 判断主线
                bool  major = ((startGridY - i) % 5 + 5) % 5 == 0;
                EditorGUI.DrawRect(new Rect(0f, y, rect.width, 1f), major ? gridColorMajor : gridColor);
            }
        }
        #endregion

        #region 绘制 节点
        // ── 节点悬停 ──
        private string _hoveredNodeId;       // 当前鼠标悬停的节点 ID（null = 无悬停）
        private string _snapHoveredNodeId;   // Layout 快照
        
        /// <summary>
        /// 遍历所有节点，通过 NodeDrawer.DrawNode 绘制节点，
        /// 并处理左键点击（选中/开始拖拽）和右键点击（弹出上下文菜单）。
        /// 节点矩形基于 _canvas.GetNodeScreenRect 计算（含缩放）。
        /// GL 绘制会绕过 IMGUI BeginClip 裁剪，因此在 CPU 侧做视口剔除：
        /// 节点矩形（含下方标签区）完全落在 localBounds 外时跳过绘制与交互。
        /// localBounds 由调用方（DrawCanvas）传入当前帧真实的画布局部矩形，
        /// 不能使用 _canvasRect 字段（Layout 阶段返回占位尺寸，远小于实际画布）。
        /// </summary>
        private void DrawNodes(Rect localBounds)
        {
            if (!_config) return;

            // 非 Repaint/Layout/Used 事件时追踪鼠标悬停的节点
            bool isMouseEvent = Event.current.type != EventType.Repaint
                             && Event.current.type != EventType.Layout
                             && Event.current.type != EventType.Used;
            string newHoveredId = null;

            foreach (var node in _config.nodes)
            {
                var nodeType = _config.GetNodeType(node.nodeTypeRef);
                Vector2 nodeSize = nodeType != null ? nodeType.resolution : new Vector2(80f, 80f);
                Rect nodeRect    = _canvas.GetNodeScreenRect(node.position, nodeSize);

                // 视口剔除：将节点矩形向下扩展 20px（容纳节点下方的 ID 标签），
                // 完全在画布外的节点不绘制，也不参与鼠标事件检测
                var cullingRect = new Rect(nodeRect.x, nodeRect.y, nodeRect.width, nodeRect.height + 20f);
                if (!cullingRect.Overlaps(localBounds)) continue;

                // 悬停检测：记录当前帧鼠标所在节点
                if (isMouseEvent && nodeRect.Contains(Event.current.mousePosition))
                    newHoveredId = node.nodeId;

                bool isSelected = node.nodeId == _snapSelectedId;
                bool isHovered  = node.nodeId == _snapHoveredNodeId;

                NodeDrawer.DrawNode(node, nodeType, isSelected, isHovered, nodeRect);

                // 左键点击：连线添加模式下完成连线；否则选中并开始拖拽
                if (Event.current.type == EventType.MouseDown
                    && Event.current.button == 0
                    && nodeRect.Contains(Event.current.mousePosition))
                {
                    if (_isAddingConnection)
                    {
                        CompleteAddConnection(node.nodeId);
                        Event.current.Use();
                        Repaint();
                    }
                    else
                    {
                        _canvas.SelectedNodeId    = node.nodeId;
                        _canvas.IsDraggingNode    = true;
                        _canvas.DragNodeStartPos  = node.position;
                        _canvas.DragMouseStartPos = _canvas.ScreenToCanvas(Event.current.mousePosition);
                        _selectedNodeTypeIdx      = -1;
                        GUI.FocusControl(null);
                        Event.current.Use();
                        Repaint();
                    }
                }

                // 右键点击：弹出上下文菜单；若处于连线添加模式则先退出
                if (Event.current.type == EventType.MouseDown
                    && Event.current.button == 1
                    && nodeRect.Contains(Event.current.mousePosition))
                {
                    if (_isAddingConnection) { _isAddingConnection = false; _connectionSourceId = null; }
                    _canvas.SelectedNodeId = node.nodeId;
                    _selectedNodeTypeIdx   = -1;
                    _ctxNodeId             = node.nodeId;
                    ShowContextMenu();
                    Event.current.Use();
                }
            }

            // 悬停节点变化时请求重绘（确保高亮即时响应）
            if (isMouseEvent && newHoveredId != _hoveredNodeId)
            {
                _hoveredNodeId = newHoveredId;
                Repaint();
            }
        }
        #endregion

        #region 绘制 连线
        /// <summary>
        /// 遍历所有节点，为每条父→子关系绘制连线。
        /// 委托给 <see cref="NodeDrawer.DrawAllLineConnections"/> 实现，
        /// 包含 Liang-Barsky 裁剪、颜色高亮等逻辑。
        /// </summary>
        private void DrawAllLineConnections(Rect localBounds)
            => NodeDrawer.DrawAllLineConnections(
                localBounds, _config, _canvas,
                _snapSelectedConnFrom, _snapSelectedConnTo,
                _snapHoveredConnFrom,  _snapHoveredConnTo);

        /// <summary>
        /// 在每条父→子连线终点（子节点边缘）绘制方向箭头。
        /// 委托给 <see cref="NodeDrawer.DrawConnectionArrows"/> 实现。
        /// </summary>
        private void DrawConnectionArrows(Rect localBounds)
            => NodeDrawer.DrawConnectionArrows(localBounds, _config, _canvas);
        #endregion
        
        #region 节点-重名检查
        private readonly List<string> _duplicateNodeIds    = new List<string>(); // 存在重复的节点名称（去重后）
        private readonly List<int>    _duplicateNavIndices = new List<int>(); // 全部重复节点在 _config.nodes 中的位置索引
        private int                   _duplicateNavIdx; // 当前导航游标（点击警告时循环推进）
        private bool                  _needDuplicateCheck  = true; // 是否需要重新扫描重名（修改节点数据后置 true）
        
        /// <summary>
        /// 提示信息。
        /// 在画布右上角绘制重名警告条（仅当存在重复节点名称时显示）。
        /// 点击后调用 NavigateToDuplicate() 循环定位到各重名节点。
        /// 须在 GUI.BeginClip 内调用，坐标为画布局部坐标。
        /// </summary>
        private void DrawDuplicateWarning(Rect localBounds)
        {
            if (_duplicateNodeIds.Count == 0) return;

            string names = string.Join("、", _duplicateNodeIds);
            string text  = $"⚠  存在重复的节点名称 [{names}]，请进行修改（点击定位）";

            // 使用无背景的 Label 样式（背景由 DrawRect 单独绘制，避免 GUIStyle 纹理干扰）
            var labelStyle = _duplicateLabelStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                wordWrap  = false,
                fontSize  = 11,
                padding   = new RectOffset(8, 8, 0, 0),
            };
            labelStyle.normal.textColor = new Color(1f, 0.85f, 0.1f, 1f);
            labelStyle.hover.textColor  = new Color(1f, 1f,    0.4f, 1f);

            var content  = new GUIContent(text);
            Vector2 size = labelStyle.CalcSize(content);
            float   w    = Mathf.Min(size.x + 16f, localBounds.width - 16f);
            float   h    = Mathf.Max(size.y +  8f, 28f);
            const float padding = 8f;
            var warningRect = new Rect(localBounds.width - w - padding, padding, w, h);

            // 深色背景 + 橙色描边（Repaint 阶段绘制，不影响事件处理）
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(warningRect, new Color(0.12f, 0.06f, 0f, 0.93f));
                // 描边（上/下/左/右各 1px）
                var bc = new Color(1f, 0.6f, 0.1f, 0.8f);
                EditorGUI.DrawRect(new Rect(warningRect.x, warningRect.y,          warningRect.width,  1f), bc);
                EditorGUI.DrawRect(new Rect(warningRect.x, warningRect.yMax - 1f,  warningRect.width,  1f), bc);
                EditorGUI.DrawRect(new Rect(warningRect.x, warningRect.y,          1f, warningRect.height), bc);
                EditorGUI.DrawRect(new Rect(warningRect.xMax - 1f, warningRect.y,  1f, warningRect.height), bc);
            }

            // 透明按钮（响应点击），文字通过 Label 绘制
            if (GUI.Button(warningRect, GUIContent.none, GUIStyle.none))
                NavigateToDuplicate();

            GUI.Label(warningRect, content, labelStyle);
        }
        #endregion
        
        #region 中央视口操作
        /// <summary>
        /// 处理画布鼠标/滚轮交互：
        /// Alt+滚轮缩放（以鼠标位置为中心），滚轮垂直平移，中键拖拽平移，
        /// 左键点击空白取消选中，左键拖拽移动节点，左键释放结束拖拽。
        /// </summary>
        private void HandleCanvasInput(Rect canvasRect)
        {
            var evt        = Event.current;
            var localMouse = evt.mousePosition - canvasRect.min; // 鼠标相对画布左上角的坐标

            // 只在画布区域内响应
            bool inCanvas = canvasRect.Contains(evt.mousePosition);

            switch (evt.type)
            {
                case EventType.ScrollWheel:
                    if (!inCanvas) break;
                    if (evt.alt)
                    {
                        // Alt+滚轮缩放，以鼠标位置为中心
                        float delta   = -evt.delta.y * 0.05f;
                        float newZoom = Mathf.Clamp(_canvas.Zoom + delta,
                            NodeTreeCanvasState.MinZoom, NodeTreeCanvasState.MaxZoom);

                        // 保持鼠标下方的画布点不动
                        var beforeCanvas = _canvas.ScreenToCanvas(localMouse);
                        _canvas.Zoom = newZoom;
                        var afterScreen = _canvas.CanvasToScreen(beforeCanvas);
                        _canvas.PanOffset += localMouse - afterScreen;

                        evt.Use();
                        Repaint();
                    }
                    else if (!evt.alt && !evt.control)
                    {
                        // 普通滚轮：垂直平移
                        // Y 轴向上：滚轮向下（delta.y > 0）应让内容向上移动，需增大 panOffset.y
                        _canvas.PanOffset.y += evt.delta.y * 3f;
                        evt.Use();
                        Repaint();
                    }
                    break;

                case EventType.MouseDown:
                    if (!inCanvas) break;
                    if (evt.button == 2)
                    {
                        // 中键：开始平移
                        evt.Use();
                    }
                    else if (evt.button == 0 && !_snapIsDragging)
                    {
                        // 点击空白处取消选中（若未命中任何节点）
                        bool hitNode = false;
                        if (_config)
                        {
                            foreach (var node in _config.nodes)
                            {
                                var nodeType = _config.GetNodeType(node.nodeTypeRef);
                                Vector2 size = nodeType != null ? nodeType.resolution : new Vector2(80f, 80f);
                                var nodeRect = _canvas.GetNodeScreenRect(node.position, size);
                                if (nodeRect.Contains(localMouse)) { hitNode = true; break; }
                            }
                        }
                        if (!hitNode)
                        {
                            _canvas.SelectedNodeId = null;
                            Repaint();
                        }
                    }
                    break;

                case EventType.MouseDrag:
                    if (!inCanvas && !_canvas.IsDraggingNode) break;
                    if (evt.button == 2)
                    {
                        // 中键拖拽：平移画布
                        // Y 轴向上：鼠标向下（delta.y > 0）需减小 panOffset.y 才能让内容跟随
                        _canvas.PanOffset += new Vector2(evt.delta.x, -evt.delta.y);
                        evt.Use();
                        Repaint();
                    }
                    else if (evt.button == 0 && _canvas.IsDraggingNode
                             && !string.IsNullOrEmpty(_canvas.SelectedNodeId) && _config)
                    {
                        // 左键拖拽：移动节点（可选吸附到网格）
                        var node = _config.GetNode(_canvas.SelectedNodeId);
                        if (node != null)
                        {
                            var curCanvas = _canvas.ScreenToCanvas(localMouse);
                            var rawPos    = _canvas.DragNodeStartPos + (curCanvas - _canvas.DragMouseStartPos);
                            // XOR：开关开 + Shift → 临时关闭；开关关 + Shift → 临时开启
                            bool shouldSnap = _snapToGrid ^ evt.shift;
                            node.position = shouldSnap ? SnapToGrid(rawPos) : rawPos;
                            MarkDirty();
                            evt.Use();
                            Repaint();
                        }
                    }
                    break;

                case EventType.MouseUp:
                    if (evt.button == 0 && _canvas.IsDraggingNode)
                    {
                        _canvas.IsDraggingNode = false;
                        evt.Use();
                    }
                    break;

                case EventType.KeyDown:
                    // Esc：退出连线添加模式
                    if (_isAddingConnection && evt.keyCode == KeyCode.Escape)
                    {
                        _isAddingConnection = false;
                        _connectionSourceId = null;
                        evt.Use();
                        Repaint();
                    }
                    // Delete：删除选中的连线，或删除选中的节点
                    else if (!_isAddingConnection && evt.keyCode == KeyCode.Delete)
                    {
                        if (_selectedConnFrom != null)
                        {
                            // 优先删除选中的连线
                            RemoveConnection(_selectedConnFrom, _selectedConnTo);
                            _selectedConnFrom = null;
                            _selectedConnTo   = null;
                            evt.Use();
                            Repaint();
                        }
                        else if (!string.IsNullOrEmpty(_canvas.SelectedNodeId))
                        {
                            // 删除选中的节点（二次确认）
                            string nodeId = _canvas.SelectedNodeId;
                            if (EditorUtility.DisplayDialog("删除节点",
                                $"删除节点 [{nodeId}]？\n其子节点将提升到父节点下。", "删除", "取消"))
                                DeleteNode(nodeId);
                            evt.Use();
                        }
                    }
                    break;
            }
        }
        #endregion
        
        #region 节点操作
        #region 节点-右键菜单
        // ── 右键菜单目标 ──
        private string _ctxNodeId; // 右键菜单操作的目标节点 ID
        
        /// <summary>
        /// 在 _ctxNodeId 节点上弹出 GenericMenu 上下文菜单。
        /// 提供：添加子节点、在前方插入节点、删除节点（子节点上移）、切除子树。
        /// 删除和切除操作带有二次确认对话框。
        /// </summary>
        private void ShowContextMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("添加子节点"), false, () => AddChildNode(_ctxNodeId));
            menu.AddItem(new GUIContent("添加连线"), false, () =>
            {
                _isAddingConnection = true;
                _connectionSourceId = _ctxNodeId;
                _selectedConnFrom   = null;
                _selectedConnTo     = null;
                Repaint();
            });
            menu.AddItem(new GUIContent("在前方插入节点"), false, () => InsertNodeBefore(_ctxNodeId));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("删除节点（子节点上移）"), false, () =>
            {
                if (EditorUtility.DisplayDialog("删除节点",
                        $"删除节点 [{_ctxNodeId}]？\n其子节点将提升到父节点下。", "删除", "取消"))
                    DeleteNode(_ctxNodeId);
            });
            menu.AddItem(new GUIContent("切除子树（含所有子节点）"), false, () =>
            {
                if (EditorUtility.DisplayDialog("切除子树",
                        $"切除节点 [{_ctxNodeId}] 及其所有子节点？\n可通过 Ctrl+Z 撤销。", "切除", "取消"))
                    CutSubtree(_ctxNodeId);
            });
            menu.ShowAsContext();
        }
        #endregion
        
        #region 新增 子节点
        /// <summary>
        /// 新增 子节点。
        /// 在指定父节点下新增一个子节点，自动生成 ID，继承父节点类型。
        /// 第一个子节点按当前布局方向放置在父节点"前侧"；
        /// 后续子节点沿 cross 轴依次放置在最后一个子节点的相邻位置。
        /// </summary>
        private void AddChildNode(string parentId)
        {
            if (!_config || string.IsNullOrEmpty(parentId)) return;

            var parent = _config.GetNode(parentId);
            if (parent == null) return;

            Undo.RecordObject(_config, "添加子节点");

            var parentType = _config.GetNodeType(parent.nodeTypeRef);
            Vector2 parentSize = parentType?.resolution ?? new Vector2(80f, 80f);

            Vector2 newPos;
            if (parent.childNodeIds.Count == 0)
            {
                // 第一个子节点：按布局方向放置在父节点"前侧"
                newPos = parent.position + GetForwardOffset(_config.layoutDirection, parentSize);
            }
            else
            {
                // 后续子节点：沿 cross 轴放置在最后一个子节点的相邻位置
                var lastChild = _config.GetNode(parent.childNodeIds[parent.childNodeIds.Count - 1]);
                if (lastChild != null)
                {
                    var lastType = _config.GetNodeType(lastChild.nodeTypeRef);
                    Vector2 lastSize = lastType?.resolution ?? new Vector2(80f, 80f);
                    newPos = lastChild.position + GetCrossOffset(_config.layoutDirection, lastSize);
                }
                else
                {
                    newPos = parent.position + GetForwardOffset(_config.layoutDirection, parentSize);
                }
            }

            var newNode = new NodeData
            {
                // 命名格式：{父节点名称}_{当前子节点数量}，冲突时追加 (n)
                nodeId      = GenerateChildId(parentId, parent.childNodeIds.Count),
                nodeTypeRef = parent.nodeTypeRef,
                position    = newPos
            };

            // 默认条件组：要求父节点已完成（节点树的基本推进逻辑）
            // conditionType = "NodeFinished"（内置检查器），conditionParam = 父节点 ID
            newNode.conditionGroups.Add(new ConditionGroupData
            {
                satisfyType = EConditionSatisfyType.All,
                conditions  = new List<ConditionData>
                {
                    new ConditionData
                    {
                        conditionType  = "NodeFinished",
                        comparison     = EConditionComparison.Equal,
                        conditionParam = parentId
                    }
                }
            });

            _config.nodes.Add(newNode);
            parent.childNodeIds.Add(newNode.nodeId);

            _canvas.SelectedNodeId = newNode.nodeId;
            MarkDirty();
        }
        
        /// <summary>
        /// 生成子节点 ID，格式为 <c>{父节点ID}_{当前子节点数量}</c>。
        /// 若该名称已被占用，依次尝试追加 <c>(1)</c>、<c>(2)</c>… 直到找到空闲名称。
        /// </summary>
        private string GenerateChildId(string parentId, int childCount)
        {
            string baseName = $"{parentId}_{childCount}";
            if (_config.GetNode(baseName) == null) return baseName;

            int repeat = 1;
            string candidate;
            do { candidate = $"{baseName}({repeat++})"; }
            while (_config.GetNode(candidate) != null);
            return candidate;
        }
        
        /// <summary>
        /// 按布局方向返回"前进"偏移——父节点到第一个子节点的放置偏移。
        /// Left2Right→+X（右），Right2Left→-X（左），Top2Bottom→-Y（下），Bottom2Top→+Y（上）。
        /// Y 轴向上：向下 = 负 Y，向上 = 正 Y。
        /// 间距 = 节点尺寸 + 自动布局间距常量。
        /// </summary>
        private static Vector2 GetForwardOffset(ELayoutDirection dir, Vector2 nodeSize)
        {
            switch (dir)
            {
                case ELayoutDirection.Left2Right:  return new Vector2( nodeSize.x + NodeAutoSpacingX,  0f);
                case ELayoutDirection.Right2Left:  return new Vector2(-(nodeSize.x + NodeAutoSpacingX), 0f);
                case ELayoutDirection.Top2Bottom:  return new Vector2(0f, -(nodeSize.y + NodeAutoSpacingY)); // Y 向上：向下 = 负 Y
                case ELayoutDirection.Bottom2Top:  return new Vector2(0f,  nodeSize.y + NodeAutoSpacingY);  // Y 向上：向上 = 正 Y
                default:                           return new Vector2( nodeSize.x + NodeAutoSpacingX,  0f);
            }
        }
        
        /// <summary>
        /// 按布局方向返回"cross 轴"偏移——后续子节点相对于前一个子节点的排列偏移。
        /// 水平布局（Left/Right）→ 向下（-Y，Y 轴向上时向下 = 负 Y）；
        /// 垂直布局（Top/Bottom）→ 向右（+X，X 方向不变）。
        /// </summary>
        private static Vector2 GetCrossOffset(ELayoutDirection dir, Vector2 nodeSize)
        {
            switch (dir)
            {
                case ELayoutDirection.Left2Right:
                case ELayoutDirection.Right2Left:
                    return new Vector2(0f, -(nodeSize.y + NodeAutoSpacingY)); // Y 向上：向下 = 负 Y
                case ELayoutDirection.Top2Bottom:
                case ELayoutDirection.Bottom2Top:
                    return new Vector2(nodeSize.x + NodeAutoSpacingX, 0f);
                default:
                    return new Vector2(0f, -(nodeSize.y + NodeAutoSpacingY));
            }
        }
        #endregion

        #region 插入 父节点
        /// <summary>
        /// 插入 父节点。
        /// 在指定节点前插入一个新节点：新节点继承目标节点的父节点位置，
        /// 并将目标节点设为新节点的子节点。
        /// </summary>
        private void InsertNodeBefore(string nodeId)
        {
            if (!_config || string.IsNullOrEmpty(nodeId)) return;

            var target = _config.GetNode(nodeId);
            if (target == null) return;

            Undo.RecordObject(_config, "插入节点");

            string parentId = _config.GetParentId(nodeId);
            var parent      = parentId != null ? _config.GetNode(parentId) : null;

            var newNode = new NodeData
            {
                nodeId      = GenerateId(),
                nodeTypeRef = target.nodeTypeRef,
                position    = target.position + new Vector2(-NodeAutoSpacingX * 0.5f, 0f)
            };
            newNode.childNodeIds.Add(nodeId);
            _config.nodes.Add(newNode);

            // 将父节点对目标节点的引用替换为新节点
            if (parent != null)
            {
                int idx = parent.childNodeIds.IndexOf(nodeId);
                if (idx >= 0)
                    parent.childNodeIds[idx] = newNode.nodeId;
            }

            _canvas.SelectedNodeId = newNode.nodeId;
            MarkDirty();
        }
        
        /// <summary>生成 8 位十六进制随机节点 ID（取 GUID 前 8 字符，足够在单配置内唯一）。</summary>
        private static string GenerateId()
            => Guid.NewGuid().ToString("N").Substring(0, 8);
        #endregion

        #region 删除 节点
        /// <summary>
        /// 删除 指定节点。
        /// 其直接子节点提升到父节点下（保持相对顺序）。
        /// 选中的节点被删除后取消选中。
        /// 节点树至少保留一个节点，仅剩一个时拒绝删除并弹出提示。
        /// </summary>
        private void DeleteNode(string nodeId)
        {
            if (!_config || string.IsNullOrEmpty(nodeId)) return;

            if (_config.nodes.Count <= 1)
            {
                EditorUtility.DisplayDialog("无法删除", "节点树至少需要保留一个节点。", "确定");
                return;
            }

            var target = _config.GetNode(nodeId);
            if (target == null) return;

            Undo.RecordObject(_config, "删除节点");

            string parentId = _config.GetParentId(nodeId);
            var parent      = parentId != null ? _config.GetNode(parentId) : null;

            if (parent != null)
            {
                int idx = parent.childNodeIds.IndexOf(nodeId);
                if (idx >= 0)
                {
                    parent.childNodeIds.RemoveAt(idx);
                    // 子节点提升到父节点，插入原位置
                    parent.childNodeIds.InsertRange(idx, target.childNodeIds);
                }
            }

            _config.nodes.Remove(target);

            if (_canvas.SelectedNodeId == nodeId)
                _canvas.SelectedNodeId = null;

            MarkDirty();
        }
        #endregion

        #region 切除 节点树
        /// <summary>
        /// 切除 所有子节点。
        /// 切除以指定节点为根的整棵子树（含该节点及其所有后代），
        /// 并将该节点从其父节点的 childNodeIds 中移除。
        /// 若子树涵盖所有节点（即会清空节点树），则拒绝操作并弹出提示。
        /// </summary>
        private void CutSubtree(string nodeId)
        {
            if (!_config || string.IsNullOrEmpty(nodeId)) return;

            // 预先收集要删除的节点 ID，检查是否会清空整棵树
            var toDelete = new HashSet<string>();
            CollectSubtreeIds(nodeId, toDelete);

            if (toDelete.Count >= _config.nodes.Count)
            {
                EditorUtility.DisplayDialog("无法切除", "节点树至少需要保留一个节点。", "确定");
                return;
            }

            Undo.RecordObject(_config, "切除子树");

            // 从父节点的 childNodeIds 中移除根节点
            string parentId = _config.GetParentId(nodeId);
            var parent      = parentId != null ? _config.GetNode(parentId) : null;
            parent?.childNodeIds.Remove(nodeId);

            // 删除所有收集到的节点
            _config.nodes.RemoveAll(n => toDelete.Contains(n.nodeId));

            if (toDelete.Contains(_canvas.SelectedNodeId))
                _canvas.SelectedNodeId = null;

            MarkDirty();
        }
        
        /// <summary>
        /// 收集 所有子节点。
        /// 递归收集以 nodeId 为根的子树中所有节点ID（包含自身），结果追加到 result 集合。
        /// 已在 result 中的节点不再递归（防止循环引用导致无限递归）。
        /// </summary>
        private void CollectSubtreeIds(string nodeId, HashSet<string> result)
        {
            if (result.Contains(nodeId)) return;
            result.Add(nodeId);
            var node = _config.GetNode(nodeId);
            if (node == null) return;
            foreach (var childId in node.childNodeIds)
                CollectSubtreeIds(childId, result);
        }
        #endregion
        #endregion

        #region 连线操作
        #region 连线-交互
        // ── 连线交互 ──
        private bool   _isAddingConnection;  // 是否处于连线添加模式
        private string _connectionSourceId;  // 连线添加的起点节点 ID
        private string _selectedConnFrom;    // 当前选中连线的起点节点 ID（null = 无选中）
        private string _selectedConnTo;      // 当前选中连线的终点节点 ID
        private string _hoveredConnFrom;     // 鼠标悬停连线的起点节点 ID（null = 无悬停）
        private string _hoveredConnTo;       // 鼠标悬停连线的终点节点 ID
        // Layout/Repaint 快照
        private string _snapSelectedConnFrom;
        private string _snapSelectedConnTo;
        private string _snapHoveredConnFrom;
        private string _snapHoveredConnTo;
        private bool   _snapIsAddingConnection;
        private string _snapConnectionSourceId;
        
        /// <summary>
        /// 处理连线的点击与悬停交互（在 DrawNodes 之后调用，节点优先消费鼠标事件）。
        /// 若鼠标事件已被节点消费（EventType.Used），此方法不做任何处理。
        /// 坐标为画布局部坐标（GUI.BeginClip 内）。
        /// </summary>
        private void HandleConnectionInteraction()
        {
            if (!_config) return;

            var evt = Event.current;

            // 连线添加模式：鼠标移动/拖拽时触发重绘，使预览线终点实时跟随鼠标
            if (_isAddingConnection)
            {
                if (evt.type == EventType.MouseMove || evt.type == EventType.MouseDrag)
                    Repaint();
                return;
            }

            var localMouse = evt.mousePosition;

            // 非 Repaint/Layout/Used 事件时更新悬停状态，有变化则请求重绘
            if (evt.type != EventType.Repaint && evt.type != EventType.Layout
                && evt.type != EventType.Used)
            {
                GetConnectionHit(localMouse, 8f, out string hFrom, out string hTo);
                bool changed = _hoveredConnFrom != hFrom || _hoveredConnTo != hTo;
                _hoveredConnFrom = hFrom;
                _hoveredConnTo   = hTo;
                if (changed) Repaint();
            }

            if (evt.type == EventType.MouseDown)
            {
                GetConnectionHit(localMouse, 8f, out string hFrom, out string hTo);
                if (hFrom != null)
                {
                    if (evt.button == 0)
                    {
                        // 左键：选中连线
                        _selectedConnFrom      = hFrom;
                        _selectedConnTo        = hTo;
                        _canvas.SelectedNodeId = null;
                        _selectedNodeTypeIdx   = -1;
                        evt.Use();
                        Repaint();
                    }
                    else if (evt.button == 1)
                    {
                        // 右键：弹出连线上下文菜单
                        _selectedConnFrom      = hFrom;
                        _selectedConnTo        = hTo;
                        _canvas.SelectedNodeId = null;
                        _selectedNodeTypeIdx   = -1;
                        ShowConnectionContextMenu(hFrom, hTo);
                        evt.Use();
                    }
                }
                else if (evt.button == 0)
                {
                    // 左键点击空白：取消连线选中
                    _selectedConnFrom = null;
                    _selectedConnTo   = null;
                }
            }
        }
        
        /// <summary>
        /// Repaint 时在画布内绘制连线添加模式的预览线（起点节点中心 → 当前鼠标位置）。
        /// 坐标为画布局部坐标（GUI.BeginClip 内）。
        /// </summary>
        private void DrawPreviewConnection()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (!_snapIsAddingConnection || string.IsNullOrEmpty(_snapConnectionSourceId)) return;
            if (!_config) return;

            var sourceNode = _config.GetNode(_snapConnectionSourceId);
            if (sourceNode == null) return;

            var fromScreen  = _canvas.CanvasToScreen(sourceNode.position);
            var toScreen    = Event.current.mousePosition; // BeginClip 内已是局部坐标

            var clippedFrom = fromScreen;
            var clippedTo   = toScreen;
            
            //if (!ClipLineToRect(localBounds, ref clippedFrom, ref clippedTo)) return;

            var nodeType  = _config.GetNodeType(sourceNode.nodeTypeRef);
            var lineStyle = nodeType?.line ?? new LineTypeData();
            NodeDrawer.DrawLineConnection(clippedFrom, clippedTo, lineStyle,
                _config.layoutDirection, new Color(1f, 1f, 0.2f, 0.75f), _canvas.Zoom);
        }

        /// <summary>
        /// Repaint 时在画布底部居中绘制连线添加模式的提示条。
        /// 坐标为画布局部坐标（GUI.BeginClip 内）。
        /// </summary>
        private void DrawAddConnectionModeHint(Rect localBounds)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (!_snapIsAddingConnection) return;

            string text  = $"连线添加模式  起点: [{_snapConnectionSourceId}]  |  点击目标节点完成连线  |  Esc 取消";
            var style    = _connectionHintStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = new Color(1f, 0.9f, 0.2f, 1f) }
            };
            var content = new GUIContent(text);
            var size    = style.CalcSize(content);
            float w     = Mathf.Min(size.x + 24f, localBounds.width - 20f);
            float h     = size.y + 12f;
            var   rect  = new Rect((localBounds.width - w) * 0.5f, localBounds.height - h - 12f, w, h);

            EditorGUI.DrawRect(rect, new Color(0.08f, 0.06f, 0f, 0.92f));
            var bc = new Color(1f, 0.75f, 0.1f, 0.85f);
            EditorGUI.DrawRect(new Rect(rect.x,         rect.y,         rect.width, 1f), bc);
            EditorGUI.DrawRect(new Rect(rect.x,         rect.yMax - 1f, rect.width, 1f), bc);
            EditorGUI.DrawRect(new Rect(rect.x,         rect.y,         1f, rect.height), bc);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y,         1f, rect.height), bc);
            GUI.Label(rect, content, style);
        }
        
        /// <summary>
        /// 查找距离 localMouse 最近且在 threshold 像素内的连线，输出起点/终点节点 ID。
        /// 未命中时输出 null 并返回 false。
        /// </summary>
        private void GetConnectionHit
        (
            Vector2 localMouse, float threshold,
            out string fromId, out string toId
        )
        {
            fromId = null; toId = null;
            if (!_config) return;

            float minDist = threshold;
            foreach (var node in _config.nodes)
            {
                var fromScreen = _canvas.CanvasToScreen(node.position);
                foreach (var childId in node.childNodeIds)
                {
                    var child = _config.GetNode(childId);
                    if (child == null) continue;
                    var   toScreen = _canvas.CanvasToScreen(child.position);
                    float dist     = PointToSegmentDistance(localMouse, fromScreen, toScreen);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        fromId  = node.nodeId;
                        toId    = childId;
                    }
                }
            }
        }
        
        /// <summary>计算点 p 到线段 [a, b] 的最短距离。sqrMagnitude 为 0 时退化为点到点距离。</summary>
        private static float PointToSegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            var   ab    = b - a;
            float sqLen = ab.sqrMagnitude;
            if (sqLen < 1e-6f) return (p - a).magnitude;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / sqLen);
            return (a + t * ab - p).magnitude;
        }
        #endregion
        
        #region 连线-右键菜单
        /// <summary>弹出连线上下文菜单，提供删除连线和修改连线（重新连接）选项。</summary>
        private void ShowConnectionContextMenu(string fromId, string toId)
        {
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("删除连线"), false, () =>
            {
                if (EditorUtility.DisplayDialog("删除连线",
                        $"删除从 [{fromId}] 到 [{toId}] 的连线？\n同时会删除目标节点中与该连线关联的条件组。",
                        "删除", "取消"))
                {
                    RemoveConnection(fromId, toId);
                    if (_selectedConnFrom == fromId && _selectedConnTo == toId)
                    { _selectedConnFrom = null; _selectedConnTo = null; }
                }
            });

            menu.AddSeparator("");

            menu.AddItem(new GUIContent("修改连线（重新连接）"), false, () =>
            {
                // 删除旧连线，进入添加模式让用户选择新终点
                RemoveConnection(fromId, toId);
                _selectedConnFrom   = null;
                _selectedConnTo     = null;
                _isAddingConnection = true;
                _connectionSourceId = fromId;
                Repaint();
            });

            menu.ShowAsContext();
        }
        #endregion
        
        #region 添加 连线
        /// <summary>
        /// 完成连线添加：创建从 _connectionSourceId 到 toId 的连线并退出添加模式。
        /// 自连（起点 == 终点）时静默取消。
        /// </summary>
        private void CompleteAddConnection(string toId)
        {
            string fromId = _connectionSourceId;
            _isAddingConnection = false;
            _connectionSourceId = null;
            if (string.IsNullOrEmpty(fromId) || fromId == toId) return;
            AddConnection(fromId, toId);
            _canvas.SelectedNodeId = toId;
        }

        /// <summary>
        /// 添加从 fromId 到 toId 的连线：
        /// 1. 将 toId 加入 fromNode.childNodeIds（已存在则跳过）。
        /// 2. 将 toNode.conditionSatisfyType 设为 Any（任意条件组满足即解锁）。
        /// 3. 在 toNode.conditionGroups 中新增条件组：NodeFinished == fromId。
        /// </summary>
        private void AddConnection(string fromId, string toId)
        {
            if (!_config || string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(toId)) return;
            var fromNode = _config.GetNode(fromId);
            var toNode   = _config.GetNode(toId);
            if (fromNode == null || toNode == null) return;
            if (fromNode.childNodeIds.Contains(toId)) return; // 连线已存在

            Undo.RecordObject(_config, "添加连线");
            fromNode.childNodeIds.Add(toId);

            // 终点节点：条件满足类型改为 Any（任一前置条件满足即可解锁）
            toNode.conditionSatisfyType = EConditionSatisfyType.Any;
            // 新增条件组：要求起点节点已完成
            toNode.conditionGroups.Add(new ConditionGroupData
            {
                satisfyType = EConditionSatisfyType.All,
                conditions  = new List<ConditionData>
                {
                    new ConditionData
                    {
                        conditionType  = "NodeFinished",
                        comparison     = EConditionComparison.Equal,
                        conditionParam = fromId
                    }
                }
            });
            MarkDirty();
        }
        #endregion

        #region 删除 连线
        /// <summary>
        /// 删除从 fromId 到 toId 的连线：
        /// 1. 从 fromNode.childNodeIds 中移除 toId。
        /// 2. 从 toNode.conditionGroups 中删除含有 NodeFinished==fromId 条件的条件组。
        /// </summary>
        private void RemoveConnection(string fromId, string toId)
        {
            if (!_config || string.IsNullOrEmpty(fromId)) return;
            var fromNode = _config.GetNode(fromId);
            var toNode   = _config.GetNode(toId);
            if (fromNode == null) return;

            Undo.RecordObject(_config, "删除连线");
            fromNode.childNodeIds.Remove(toId);

            // 删除 toNode 中与该连线关联的条件组（含 NodeFinished==fromId 的组）
            toNode?.conditionGroups.RemoveAll(g =>
                g?.conditions != null &&
                g.conditions.Exists(c =>
                    c != null &&
                    c.conditionType  == "NodeFinished" &&
                    c.conditionParam == fromId));

            MarkDirty();
        }
        #endregion
        #endregion
        
        #region 自动布局
        /// <summary>
        /// 对所有根节点执行自动布局（Walker 算法简化版）。
        /// 先正向布局（从左/上开始），若方向为 Right2Left 或 Bottom2Top 则翻转主轴坐标。
        /// </summary>
        private void RunAutoLayout()
        {
            if (!_config) return;
            Undo.RecordObject(_config, "自动布局");

            var roots = _config.GetRootNodes();
            if (roots == null || roots.Count == 0) return;

            bool horizontal = _config.layoutDirection == ELayoutDirection.Left2Right
                           || _config.layoutDirection == ELayoutDirection.Right2Left;
            bool reverse    = _config.layoutDirection == ELayoutDirection.Right2Left
                           || _config.layoutDirection == ELayoutDirection.Bottom2Top;

            float crossCursor = 0f;

            foreach (var root in roots)
            {
                LayoutSubtree(root, 0, horizontal, ref crossCursor);
                crossCursor += horizontal ? NodeAutoSpacingY : NodeAutoSpacingX; // 多根节点之间留间距
            }

            // 反转方向（Right2Left / Bottom2Top）
            if (reverse)
            {
                float maxMain = 0f;
                foreach (var n in _config.nodes)
                    maxMain = Mathf.Max(maxMain, horizontal ? n.position.x : n.position.y);
                foreach (var n in _config.nodes)
                {
                    if (horizontal) n.position.x = maxMain - n.position.x;
                    else            n.position.y = maxMain - n.position.y;
                }
            }

            MarkDirty();
        }

        /// <summary>
        /// 递归布局以 node 为根的子树（Y 轴向上坐标系）。
        /// 叶节点直接在 crossCursor 处放置；内部节点以所有子节点占据范围的中心为其位置。
        /// horizontal=true 时主轴为 X（层级方向），cross 轴为 Y；反之互换。
        /// crossCursor 通过 ref 传递，处理完后指向已用空间的末尾（始终递增，为正值的量）。
        ///
        /// Y 轴向上适配：
        ///   - 垂直布局（horizontal=false）：主轴为 Y，"向下" = 负 Y 方向，
        ///     因此 mainPos = -depth * (nodeMain + mainSpacing)（负值）。
        ///   - 水平布局（horizontal=true）：cross 轴为 Y，节点从上往下展开 = 负 Y 方向，
        ///     因此 position.y = -(crossCursor + nodeCross * 0.5f)。
        ///   - Bottom2Top / Right2Left 的 reverse 翻转逻辑在 RunAutoLayout 中处理，此处无需关心。
        /// </summary>
        private void LayoutSubtree(NodeData node, int depth, bool horizontal, ref float crossCursor)
        {
            if (node == null) return;

            var nodeType = _config.GetNodeType(node.nodeTypeRef);
            float nodeW = nodeType?.resolution.x ?? 80f;
            float nodeH = nodeType?.resolution.y ?? 80f;

            float mainSpacing  = horizontal ? NodeAutoSpacingX : NodeAutoSpacingY; // 层级间距（主轴）
            float crossSpacing = horizontal ? NodeAutoSpacingY : NodeAutoSpacingX; // 节点间距（cross 轴）
            float nodeCross    = horizontal ? nodeH : nodeW;                       // 节点在 cross 轴的尺寸
            float nodeMain     = horizontal ? nodeW : nodeH;                       // 节点在主轴的尺寸

            // 主轴位置（Y 轴向上：垂直布局的 Y 取负，令深度越大 Y 越小 = 越靠下）
            float mainPos = horizontal
                ?  depth * (nodeMain + mainSpacing)   // X 轴：正向 = 向右 ✓
                : -depth * (nodeMain + mainSpacing);  // Y 轴：负向 = 向下 ✓

            if (node.childNodeIds == null || node.childNodeIds.Count == 0)
            {
                // 叶节点：直接放置在当前 crossCursor
                // 水平布局 cross 轴（Y）：取负使节点从上往下展开（正 crossCursor = 向下 = 负 Y）
                node.position = horizontal
                    ? new Vector2(mainPos, -(crossCursor + nodeCross * 0.5f))
                    : new Vector2(crossCursor + nodeCross * 0.5f, mainPos);
                crossCursor += nodeCross + crossSpacing;
                return;
            }

            float startCross = crossCursor;

            // 递归布局所有子节点
            foreach (var childId in node.childNodeIds)
            {
                var child = _config.GetNode(childId);
                if (child == null) continue;
                LayoutSubtree(child, depth + 1, horizontal, ref crossCursor);
            }

            // 以子节点占据范围的中心（去掉末尾间距）作为父节点的 cross 轴位置
            float childrenSpan = crossCursor - startCross - crossSpacing;
            float parentCross  = startCross + childrenSpan * 0.5f;
            // 同上：水平布局 cross 轴取负，垂直布局 cross 轴（X）保持正值不变
            node.position = horizontal
                ? new Vector2(mainPos, -parentCross)
                : new Vector2(parentCross, mainPos);
        }
        
        /// <summary>
        /// 将画布坐标吸附到最近的 GridSpacing 整数倍网格点。
        /// </summary>
        private static Vector2 SnapToGrid(Vector2 pos)
            => new Vector2(
                Mathf.Round(pos.x / GridSpacing) * GridSpacing,
                Mathf.Round(pos.y / GridSpacing) * GridSpacing);
        #endregion

        #region 节点导航
        /// <summary>
        /// 扫描所有节点 ID，找出重复项并重建导航列表。
        /// 由 _needDuplicateCheck 标记控制，仅在节点数据变化后执行一次。
        /// </summary>
        private void UpdateDuplicateCheck()
        {
            _needDuplicateCheck = false;
            _duplicateNodeIds.Clear();
            _duplicateNavIndices.Clear();

            if (!_config) return;

            // 统计每个 nodeId 的出现次数
            var counts = new Dictionary<string, int>(_config.nodes.Count);
            foreach (var node in _config.nodes)
            {
                if (string.IsNullOrEmpty(node.nodeId)) continue;
                counts.TryGetValue(node.nodeId, out int c);
                counts[node.nodeId] = c + 1;
            }

            // 收集出现超过一次的 nodeId（去重集合）
            var dupeSet = new HashSet<string>();
            foreach (var kv in counts)
                if (kv.Value > 1) dupeSet.Add(kv.Key);

            _duplicateNodeIds.AddRange(dupeSet);

            // 按 nodes 列表顺序记录全部重复节点的索引（用于循环定位）
            for (int i = 0; i < _config.nodes.Count; i++)
                if (dupeSet.Contains(_config.nodes[i].nodeId))
                    _duplicateNavIndices.Add(i);

            // 节点集合变化后重置导航游标
            _duplicateNavIdx = 0;
        }
        
        /// <summary>
        /// 循环定位到下一个重名节点：选中该节点并将视口中心对准，
        /// 每次调用推进到下一个，到达末尾后回到第一个。
        /// </summary>
        private void NavigateToDuplicate()
        {
            if (!_config || _duplicateNavIndices.Count == 0) return;

            // 防止索引越界（节点列表可能在两次检查之间发生变化）
            _duplicateNavIdx = _duplicateNavIdx % _duplicateNavIndices.Count;
            int listIdx = _duplicateNavIndices[_duplicateNavIdx];
            if (listIdx < 0 || listIdx >= _config.nodes.Count) return;

            var node = _config.nodes[listIdx];
            _canvas.SelectedNodeId = node.nodeId;

            // 视口中心对准该节点（同 Reset 按钮逻辑，使用 position 而非 _canvasRect）
            float canvasW     = position.width  - LeftPanelWidth - RightPanelWidth;
            float canvasH     = position.height - ToolbarHeight;
            _canvas.PanOffset = new Vector2(canvasW * 0.5f, canvasH * 0.5f)
                                - node.position * _canvas.Zoom;

            // 推进游标（循环）
            _duplicateNavIdx = (_duplicateNavIdx + 1) % _duplicateNavIndices.Count;
            Repaint();
        }

        /// <summary>
        /// 将画布视口中心对准指定节点，并将其设为当前选中节点。
        /// 右侧属性面板"子节点列表"中的【定位】按钮调用此方法。
        /// panOffset 计算与"重置视口"按钮和 NavigateToDuplicate 保持一致。
        /// </summary>
        private void NavigateToNode(string nodeId)
        {
            if (!_config || string.IsNullOrEmpty(nodeId)) return;
            var target = _config.GetNode(nodeId);
            if (target == null) return;

            _canvas.SelectedNodeId = nodeId;
            float canvasW     = position.width  - LeftPanelWidth - RightPanelWidth;
            float canvasH     = position.height - ToolbarHeight;
            _canvas.PanOffset = new Vector2(canvasW * 0.5f, canvasH * 0.5f)
                                - target.position * _canvas.Zoom;
            Repaint();
        }
        #endregion
        #endregion
    }
}
