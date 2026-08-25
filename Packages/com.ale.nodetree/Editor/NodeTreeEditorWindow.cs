using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Ale.NodeTree.Runtime;
using Ale.Toolkit.Runtime;
using Ale.Toolkit.Editor;
using Ale.Condition;

namespace Ale.NodeTree.Editor
{
    /// <summary>
    /// 节点树可视化编辑器主窗口（IMGUI + GL）。
    /// 通过菜单 Tools/NodeTree/Node Tree Editor 或 NodeTreeData Inspector 按钮打开。
    /// 三列布局：左侧节点类型/标签管理 | 中央画布（节点拖拽/缩放/平移/连线） | 右侧节点属性面板。
    /// 所有修改通过 Undo.RecordObject + EditorUtility.SetDirty 支持撤销并触发资产保存。
    /// </summary>
    public partial class NodeTreeEditorWindow : EditorWindow
    {
        // ── 布局常量 ──
        private static readonly Vector2 WindowDefaultSize = new Vector2(1600f, 900f); // 首次打开时的默认窗口尺寸
        private static readonly Vector2 WindowMinSize     = new Vector2(800f,  500f); // 窗口最小尺寸限制
        private const float ToolbarHeight    = 44f;  // 工具栏高度（像素）
        private const float LeftPanelWidth   = 260f; // 左侧面板宽度（像素）
        private const float RightPanelWidth  = 380f; // 右侧面板宽度（像素）
        private const float GridFallbackSize = 20f;  // 未加载配置资产时的网格尺寸回退值
        private const float NodeAutoSpacingX = 160f; // 自动布局水平间距（画布单位）
        private const float NodeAutoSpacingY = 120f; // 自动布局垂直间距（画布单位）
        private static readonly Vector2 NodeFallbackSize = new Vector2(80f, 80f); // 节点类型引用悬空时的回退显示尺寸

        /// <summary>
        /// 当前网格的最小单位格长度（画布单位）。取自配置资产的 <see cref="NodeTreeData.GridSize"/>（已保证 ≥ 1），
        /// 未加载配置时回退到 <see cref="GridFallbackSize"/>。
        /// 背景网格间距、拖拽吸附与方向键移动步长共用此值。
        /// </summary>
        private float GridSpacing => _config ? _config.GridSize : GridFallbackSize;
        
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
        private ReorderableList _tagList;           // 左侧状态标签可重排列表
        private Vector2 _leftScroll;                // 左侧面板滚动位置
        private Vector2 _rightScroll;               // 右侧面板滚动位置
        private int _leftTab;                       // 左侧标签页索引（0=节点类型, 1=标签）
        // ── Layout/Repaint 快照（防止 IMGUI 控件数量不一致） ──
        private string _snapSelectedId;      // Layout 事件时快照的主选中节点 ID
        private int    _snapNodeTypeIdx = -1; // Layout 事件时快照的节点类型选中索引
        // 选中集合快照：复用同一个 HashSet（Layout 时 Clear + 重填），避免每帧分配
        private readonly HashSet<string> _snapSelectedIds = new HashSet<string>();
        private bool _snapIsMarquee; // Layout 事件时快照的框选状态
        // 框选合成结果快照：框选进行中时节点高亮改读它，即「松手后会变成的选中」
        private readonly HashSet<string> _snapMarqueeResult = new HashSet<string>();

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
        // 标签「自动」勾选的悬停提示（缓存 GUIContent，避免每帧重建）。
        private static readonly GUIContent TagAutoRefreshLabel = new GUIContent(
            "自动",
            "自动刷新：开启后，打开 UI / 调用 RefreshAllNodeStates 时会按各节点该标签的条件重算并挂上" +
            "（达成即挂、单调不摘，支持链式解锁）；关闭则该标签仅由业务代码主动设置（如 Finished 在阅读完成后 TrySetFinished）。");
        // 「标签」页签顶部「自动写入 Unlock 条件」开关的提示（缓存 GUIContent）。
        private static readonly GUIContent AutoWriteUnlockLabel = new GUIContent(
            "自动写入 Unlock 条件",
            "开启后：在画布上「添加子节点」或「添加连线」时，自动向子节点的 Unlock 规则写入" +
            "「前置节点已完成（NodeTree.NodeFinished）」条件；关闭后则不自动写入，需手动配置。" +
            "（项目级设置，记录于 ProjectSettings/NodeTreeEditorSettings.asset，随版本库共享）");
        private GUIStyle _snapStyle;
        private GUIContent _gridGroupLabel; // 工具栏「网格：」分组标签（缓存，避免每帧重建）
        private GUIStyle _warnLabelStyle;
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
                window.ResetSelectionState(); // 切换配置：清理旧配置残留选中状态
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
            wantsMouseMove = true;            // 鼠标移动时触发事件，用于连线悬停检测
            wantsMouseEnterLeaveWindow = true; // 需要 MouseLeaveWindow 事件：鼠标拖出窗口时兜底结束框选/拖拽（默认关闭则该事件不会送达 OnGUI）
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
            // 目标未变则 Update() 重新同步已回滚的数据（保留条件折叠态 isExpanded）；否则重建
            if (_configSo != null && _configSo.targetObject == _config)
                _configSo.Update();
            else
                _configSo = null;
            // 撤销/重做后选中节点可能已不存在（如 ID 改名被回滚），逐个剔除避免右侧面板陈旧空白
            _canvas.PruneSelection(_config);
            RebuildLists(); // 同时会将 _needDuplicateCheck 置 true
            Repaint();
        }

        /// <summary>
        /// 切换/加载配置后，清理指向旧配置实体的残留选中/悬停/连线/拖拽状态，
        /// 避免右侧面板与画布显示陈旧内容（Draw 路径已能容错，但不清理会显示旧选中直到用户点击）。
        /// </summary>
        private void ResetSelectionState()
        {
            _selectedNodeTypeIdx   = -1;
            _canvas.SelectedNodeId = null;
            _canvas.IsDraggingNode = false; // 配置切换可能发生在拖拽手势中途，不清会残留拖拽态
            _selectedConnFrom      = null;
            _selectedConnTo        = null;
            _hoveredNodeId         = null;
            _hoveredConnFrom       = null;
            _hoveredConnTo         = null;
            _isAddingConnection    = false;
            _connectionSourceId    = null;
            _idDuplicateWarning    = null;
            _lastPropertyNodeId    = null;
            _pendingCollapseNodeId = null;
            _nodeDragMoved         = false;
            _dragStartPositions.Clear();
            _pendingDeleteIds.Clear();
            ClearMarqueeState();
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
                // 待执行的删除放在快照之前：本帧快照与后续绘制直接反映删除后的状态，
                // 避免「面板画到一半数据没了」的控件数分歧。
                if (_pendingDeleteIds.Count > 0)
                {
                    var ids = new List<string>(_pendingDeleteIds);
                    _pendingDeleteIds.Clear();
                    // 单节点沿用既有实现，完整保持它在多父 DAG 下「只提升到主父节点」的语义；
                    // 两个及以上才走批量路径（二者的差异见 DeleteNodes 的说明）。
                    if (ids.Count == 1) DeleteNode(ids[0]);
                    else                DeleteNodes(ids);
                }

                _snapSelectedId         = _canvas.SelectedNodeId;
                _snapSelectedIds.Clear();
                foreach (var selId in _canvas.SelectedNodeIds) _snapSelectedIds.Add(selId);
                SnapshotMultiEditState(); // 批量编辑面板的快照（见 NodeTreeEditorWindow.MultiEdit.cs）
                _snapIsMarquee = _isMarquee;
                _snapMarqueeResult.Clear();
                if (_isMarquee)
                    foreach (var mqId in _marqueeResult) _snapMarqueeResult.Add(mqId);
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
        
        /// <summary>
        /// 标记配置资产为已修改（触发 Unity 自动保存机制）。
        /// 不调用 AssetDatabase.SaveAssets()，避免每次修改都阻塞编辑器。
        /// 同时标记需要重新扫描重名节点。
        /// </summary>
        private void MarkDirty(bool rescanDuplicates = true)
        {
            if (!_config) return;
            if (rescanDuplicates) _needDuplicateCheck = true; // 仅结构/ID 可能变化时重扫重名；纯位置变化（拖拽）不需
            EditorUtility.SetDirty(_config);
            // AssetDatabase.SaveAssets() 太频繁会卡顿，改为实时 SetDirty，Unity 自动在适当时机写盘
        }

        // ── AttributeFieldDrawer 绘制上下文（供节点名/描述、节点自定义属性 AttributeValue 使用）──
        private AttrEditorContext _attrCtx;
        private AttrEditorContext AttrCtx => _attrCtx ??= new AttrEditorContext(this);

        // ── 节点类型「自定义属性字段」schema 列表绘制器（持久实例：按绑定的 attributes 引用变更自动重建）──
        private readonly AttributeDefinitionListDrawer _nodeTypeAttrListDrawer = new AttributeDefinitionListDrawer();

        // ── SerializedObject 桥：Toolkit 的 ConditionExpression 内联绘制器（PropertyDrawer）需 SerializedProperty，
        //    而本窗口以 POCO 直编。缓存一个包裹 _config 的 SerializedObject，绘制条件时 Update()→PropertyField→ApplyModifiedProperties()。──
        private SerializedObject _configSo;
        private SerializedObject ConfigSo
        {
            get
            {
                if (_configSo == null || _configSo.targetObject != _config)
                    _configSo = _config ? new SerializedObject(_config) : null;
                return _configSo;
            }
        }

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

            /// <summary>
            /// 标脏计数：每次 <see cref="MarkDirty"/> 递增。
            /// 批量编辑面板据此判定 <see cref="AttributeFieldDrawer"/> 内部是否真的改动了值：
            /// 该绘制器的普通编辑、数组增删、Ctrl+C/V 与右键粘贴全部经由 MarkDirty，
            /// 而它对空值的 EnsureCount 补位不经过，故不会被误判为「已改动」。
            /// </summary>
            public int DirtyTick { get; private set; }

            public void RecordUndo(string actionName)
            {
                if (_owner._config) Undo.RecordObject(_owner._config, actionName);
            }
            public void MarkDirty()
            {
                DirtyTick++;
                _owner.MarkDirty();
            }
            public void Repaint() => _owner.Repaint();
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
                if (list.index < 0 || list.index >= _config.nodeTypes.Count) return; // 无选中/越界守护
                Undo.RecordObject(_config, "删除节点类型");
                string removedType = _config.nodeTypes[list.index]?.typeName;
                _config.nodeTypes.RemoveAt(list.index);

                // 清理引用了被删类型的节点：置空 nodeTypeRef（回落默认外观），避免悬空引用
                if (!string.IsNullOrEmpty(removedType))
                {
                    int affected = 0;
                    foreach (var n in _config.nodes)
                        if (n != null && n.nodeTypeRef == removedType) { n.nodeTypeRef = null; affected++; }
                    if (affected > 0)
                        Debug.LogWarning($"[NodeTree] 删除节点类型「{removedType}」后，{affected} 个引用该类型的节点已置空 nodeTypeRef（回落默认外观），请重新指定类型。", _config);
                }

                RebuildTypeNameCache();
                MarkDirty();
            };
            // 拖拽重排：Undo 由 DoLayoutList 前的 RecordObject 覆盖，此处仅标脏保存。
            _nodeTypeList.onReorderCallback = _ => MarkDirty();

            // 状态标签列表（每项两行：标签名 + 颜色 + 自动刷新 / 描述）
            _tagList = new ReorderableList(_config.tags,
                typeof(NodeTagData), true, true, true, true);
            _tagList.drawHeaderCallback    = r => EditorGUI.LabelField(r, "状态标签");
            _tagList.elementHeightCallback = _ => EditorGUIUtility.singleLineHeight * 2f + 8f;
            _tagList.drawElementCallback   = (r, idx, _, _) =>
            {
                if (idx >= _config.tags.Count) return;
                var tg   = _config.tags[idx];
                float line = EditorGUIUtility.singleLineHeight;
                float y0 = r.y + 3f;
                float y1 = y0 + line + 2f;
                const float autoW = 54f, colorW = 42f, gap = 4f;
                float nameW = r.width - autoW - colorW - gap * 2f;

                EditorGUI.BeginChangeCheck();
                // 第 1 行：标签名 + 颜色 + 自动刷新
                tg.tagName     = EditorGUI.TextField(new Rect(r.x, y0, nameW, line), tg.tagName);
                tg.color       = EditorGUI.ColorField(new Rect(r.x + nameW + gap, y0, colorW, line),
                    GUIContent.none, tg.color, false, false, false);
                tg.autoRefresh = EditorGUI.ToggleLeft(new Rect(r.x + nameW + colorW + gap * 2f, y0, autoW, line),
                    TagAutoRefreshLabel, tg.autoRefresh);
                // 第 2 行：描述
                tg.description = EditorGUI.TextField(new Rect(r.x, y1, r.width, line), tg.description);
                if (EditorGUI.EndChangeCheck())
                    MarkDirty();
            };
            _tagList.onAddCallback = _ =>
            {
                Undo.RecordObject(_config, "添加标签");
                _config.tags.Add(new NodeTagData { tagName = "新标签" });
                MarkDirty();
            };
            _tagList.onRemoveCallback = list =>
            {
                if (list.index < 0 || list.index >= _config.tags.Count) return; // 无选中/越界守护
                Undo.RecordObject(_config, "删除标签");
                _config.tags.RemoveAt(list.index);
                MarkDirty();
            };
            // 拖拽重排：Undo 由 DoLayoutList 前的 RecordObject 覆盖，此处仅标脏保存。
            _tagList.onReorderCallback = _ => MarkDirty();

            RebuildTypeNameCache();
            _needDuplicateCheck = true; // 加载新配置后重新检查重名
        }
        
        /// <summary>
        /// 重建 节点类型名称数组（用于右侧属性面板节点类型 Popup）。
        /// </summary>
        private void RebuildTypeNameCache()
        {
            if (!_config) return;

            _nodeTypeNames = new string[_config.nodeTypes.Count];
            for (int i = 0; i < _config.nodeTypes.Count; i++)
                _nodeTypeNames[i] = _config.nodeTypes[i].typeName ?? $"类型{i}";
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
                    ResetSelectionState(); // 切换配置：清理旧配置残留选中状态
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
                    ResetViewport(); // 缩放归 1 + 视口对准起始节点（逻辑抽出，与画布右键菜单复用）
                    Repaint();
                }

                GUILayout.Space(8f);
                // ── 网格：吸附开关 + 最小单位格长度 ──
                _gridGroupLabel ??= new GUIContent("网格：",
                    "画布背景网格。开启「吸附网格」后，拖拽与方向键移动会对齐到网格交叉点；" +
                    "右侧数值为网格的最小单位格长度（画布单位），存于配置资产、随版本库共享。");
                GUILayout.Label(_gridGroupLabel, GUILayout.Width(40f));

                // 吸附网格开关：开启时拖拽始终吸附；关闭时按住 Shift 可临时吸附
                var snapStyle = _snapStyle ??= new GUIStyle(EditorStyles.toolbarButton);
                snapStyle.normal.textColor   = _snapToGrid ? new Color(0.2f, 0.85f, 0.2f) : new Color(0.5f, 0.5f, 0.5f);
                snapStyle.onNormal.textColor = new Color(0.2f, 0.85f, 0.2f);
                snapStyle.hover.textColor    = snapStyle.normal.textColor;
                snapStyle.active.textColor   = snapStyle.normal.textColor;
                _snapToGrid = GUILayout.Toggle(_snapToGrid, "吸附网格",
                    snapStyle, GUILayout.Width(64f));

                // 网格尺寸输入框：恒定绘制，未加载配置时灰显并显示回退值
                using (new EditorGUI.DisabledScope(!_config))
                {
                    EditorGUI.BeginChangeCheck();
                    float newGrid = EditorGUILayout.FloatField(
                        _config ? _config.gridSize : GridFallbackSize, GUILayout.Width(44f));
                    if (EditorGUI.EndChangeCheck() && _config)
                    {
                        Undo.RecordObject(_config, "修改网格尺寸");
                        // 下限 1 防止网格计算发散；上限 500 防止误输入超大值把节点甩出视口
                        _config.gridSize = Mathf.Clamp(newGrid, 1f, 500f);
                        MarkDirty(false); // 不改节点数据，无需重扫重名
                    }
                }

                GUILayout.FlexibleSpace();

                // 对齐 / 分布（作用于当前选中的多个节点）
                DrawAlignToolbar();

                // 自动布局
                if (_config)
                {
                    if (GUILayout.Button("自动布局", GUILayout.Width(70f)))
                        RunAutoLayout();
                }
            }
        }

        #region 对齐 / 分布
        /// <summary>对齐与等距分布的方式。X 组作用于画布 X 轴，Y 组作用于画布 Y 轴。</summary>
        private enum EAlignMode
        {
            LeftX       = 0, // 全部对齐到最小 x
            CenterX     = 1, // 全部对齐到 x 跨度中点
            RightX      = 2, // 全部对齐到最大 x
            TopY        = 3, // 全部对齐到最大 y（画布 Y 轴向上，「上」= max）
            CenterY     = 4, // 全部对齐到 y 跨度中点
            BottomY     = 5, // 全部对齐到最小 y
            DistributeX = 6, // X 轴等距
            DistributeY = 7, // Y 轴等距
        }

        // 对齐 / 分布按钮文案（缓存 GUIContent，避免每帧重建）
        private GUIContent[] _alignLabels;
        private GUIContent   _alignGroupLabel;
        private GUIContent   _distributeGroupLabel;

        /// <summary>
        /// 工具栏上的「对齐 / 分布」按钮组，作用于当前选中的节点。
        /// <para>按钮<b>恒定绘制</b>，仅按 Layout 快照 <c>_snapMultiNodes</c> 的数量灰显 ——
        /// 若按选中数决定「画不画」，控件数量会随选中变化而与本帧 Layout 不一致。</para>
        /// <para>对齐一律按<b>节点中心</b>（<c>NodeData.position</c> 本身即中心），不提供边缘对齐。</para>
        /// </summary>
        private void DrawAlignToolbar()
        {
            _alignLabels ??= new[]
            {
                new GUIContent("左", "左对齐：全部选中节点的中心 X 对齐到最左者"),
                new GUIContent("中", "水平居中：全部选中节点的中心 X 对齐到左右两端的中点"),
                new GUIContent("右", "右对齐：全部选中节点的中心 X 对齐到最右者"),
                new GUIContent("上", "上对齐：全部选中节点的中心 Y 对齐到最上者（画布 Y 轴向上）"),
                new GUIContent("中", "垂直居中：全部选中节点的中心 Y 对齐到上下两端的中点"),
                new GUIContent("下", "下对齐：全部选中节点的中心 Y 对齐到最下者（画布 Y 轴向上）"),
                new GUIContent("横", "水平等距：两端节点不动，中间节点在 X 轴上均分间隔（需选中 3 个及以上）"),
                new GUIContent("纵", "垂直等距：两端节点不动，中间节点在 Y 轴上均分间隔（需选中 3 个及以上）"),
            };
            _alignGroupLabel      ??= new GUIContent("对齐:", "作用于当前选中的多个节点，按节点中心对齐");
            _distributeGroupLabel ??= new GUIContent("分布:", "在选中节点的当前跨度内等距排列，两端节点保持不动");

            bool canAlign      = _snapMultiNodes.Count >= 2;
            bool canDistribute = _snapMultiNodes.Count >= 3;

            GUILayout.Label(_alignGroupLabel, GUILayout.Width(36f));
            using (new EditorGUI.DisabledScope(!canAlign))
            {
                for (int i = 0; i < 3; i++)
                    if (GUILayout.Button(_alignLabels[i], EditorStyles.toolbarButton, GUILayout.Width(24f)))
                        AlignSelectedNodes((EAlignMode)i);
                GUILayout.Space(4f);
                for (int i = 3; i < 6; i++)
                    if (GUILayout.Button(_alignLabels[i], EditorStyles.toolbarButton, GUILayout.Width(24f)))
                        AlignSelectedNodes((EAlignMode)i);
            }

            GUILayout.Space(8f);
            GUILayout.Label(_distributeGroupLabel, GUILayout.Width(36f));
            using (new EditorGUI.DisabledScope(!canDistribute))
            {
                for (int i = 6; i < 8; i++)
                    if (GUILayout.Button(_alignLabels[i], EditorStyles.toolbarButton, GUILayout.Width(24f)))
                        AlignSelectedNodes((EAlignMode)i);
            }
            GUILayout.Space(8f);
        }

        /// <summary>
        /// 按指定方式对齐 / 分布当前选中的节点（取 Layout 快照 <c>_snapMultiNodes</c>，
        /// 与按钮的灰显条件同源，保证「按钮可点」与「确实有节点可动」一致）。
        /// 一次操作一次 Undo；仅改变坐标，故 MarkDirty 跳过重名重扫。
        /// </summary>
        private void AlignSelectedNodes(EAlignMode mode)
        {
            bool distribute = mode == EAlignMode.DistributeX || mode == EAlignMode.DistributeY;
            if (!_config || _snapMultiNodes.Count < (distribute ? 3 : 2)) return;

            Undo.RecordObject(_config, distribute ? "等距分布节点" : "对齐节点");

            if (distribute)
            {
                DistributeSelectedNodes(mode == EAlignMode.DistributeX);
            }
            else if (mode <= EAlignMode.RightX)
            {
                float min = float.MaxValue, max = float.MinValue;
                foreach (var node in _snapMultiNodes)
                {
                    min = Mathf.Min(min, node.position.x);
                    max = Mathf.Max(max, node.position.x);
                }
                float target = mode == EAlignMode.LeftX ? min
                             : mode == EAlignMode.RightX ? max
                             : (min + max) * 0.5f;
                foreach (var node in _snapMultiNodes)
                    node.position = new Vector2(target, node.position.y);
            }
            else
            {
                float min = float.MaxValue, max = float.MinValue;
                foreach (var node in _snapMultiNodes)
                {
                    min = Mathf.Min(min, node.position.y);
                    max = Mathf.Max(max, node.position.y);
                }
                // 画布 Y 轴向上：「上对齐」取最大 y，「下对齐」取最小 y —— 与屏幕直觉相反，勿颠倒
                float target = mode == EAlignMode.TopY ? max
                             : mode == EAlignMode.BottomY ? min
                             : (min + max) * 0.5f;
                foreach (var node in _snapMultiNodes)
                    node.position = new Vector2(node.position.x, target);
            }

            MarkDirty(false); // 仅位置变化，跳过重名重扫
            Repaint();
        }

        /// <summary>
        /// 在选中节点当前跨度内等距分布：两端节点保持不动，中间节点按序均分间隔。
        /// 先按该轴坐标排序再分配，因此不会打乱视觉上的先后关系。
        /// </summary>
        private void DistributeSelectedNodes(bool horizontal)
        {
            int count = _snapMultiNodes.Count;
            if (count < 3) return;

            var ordered = new List<NodeData>(_snapMultiNodes);
            if (horizontal) ordered.Sort((a, b) => a.position.x.CompareTo(b.position.x));
            else            ordered.Sort((a, b) => a.position.y.CompareTo(b.position.y));

            float first = horizontal ? ordered[0].position.x : ordered[0].position.y;
            float last  = horizontal ? ordered[count - 1].position.x : ordered[count - 1].position.y;
            float step  = (last - first) / (count - 1);

            for (int i = 1; i < count - 1; i++)
            {
                var p = ordered[i].position;
                ordered[i].position = horizontal
                    ? new Vector2(first + step * i, p.y)
                    : new Vector2(p.x, first + step * i);
            }
        }
        #endregion

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
        
        #region 视口导航 / 画布右键菜单

        private Vector2 _ctxCanvasPos; // 画布右键菜单打开时的画布坐标（用于「在此处新建节点」）

        /// <summary>
        /// 计算当前画布视口的像素尺寸。不使用 _canvasRect（Layout/工具栏阶段其宽高可能为 0），
        /// 改用窗口尺寸 position 与布局常量推算，任何时刻都可靠。
        /// </summary>
        private Vector2 GetCanvasViewSize()
            => new Vector2(
                Mathf.Max(1f, position.width  - LeftPanelWidth - RightPanelWidth),
                Mathf.Max(1f, position.height - ToolbarHeight));

        /// <summary>取起始节点：第一个根节点；无根节点则取列表首个；空树返回 null。</summary>
        private NodeData GetStartNode()
        {
            if (!_config || _config.nodes.Count == 0) return null;
            var roots = _config.GetRootNodes();
            return (roots != null && roots.Count > 0) ? roots[0] : _config.nodes[0];
        }

        /// <summary>
        /// 平移画布使指定画布坐标点对准视口中心（不改变缩放）。
        /// 令 CanvasToScreen(canvasPos) == 视口中心（X/Y 皆成立，Y 轴翻转在公式内自洽）。
        /// </summary>
        private void CenterOn(Vector2 canvasPos)
        {
            var view   = GetCanvasViewSize();
            var center = new Vector2(view.x * 0.5f, view.y * 0.5f);
            _canvas.PanOffset = center - canvasPos * _canvas.Zoom;
        }

        /// <summary>重置视口：缩放归 1，并将视口中心对准起始节点（空树则归零平移）。</summary>
        private void ResetViewport()
        {
            _canvas.Zoom = 1f;
            var start = GetStartNode();
            if (start != null) CenterOn(start.position);
            else               _canvas.PanOffset = Vector2.zero;
        }

        /// <summary>缩放并平移视口，使所有节点恰好框入视口（四周留白）。空树等同重置视口。</summary>
        private void FrameAllNodes()
        {
            if (GetStartNode() == null) { ResetViewport(); return; }

            // 求所有节点的画布包围盒（按各自尺寸的半宽/半高外扩，含节点整体而非仅中心）
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (var node in _config.nodes)
            {
                if (node == null) continue;
                Vector2 half = GetNodeSize(node) * 0.5f;
                minX = Mathf.Min(minX, node.position.x - half.x);
                maxX = Mathf.Max(maxX, node.position.x + half.x);
                minY = Mathf.Min(minY, node.position.y - half.y);
                maxY = Mathf.Max(maxY, node.position.y + half.y);
            }
            if (minX > maxX || minY > maxY) { ResetViewport(); return; }

            float boxW = Mathf.Max(1f, maxX - minX);
            float boxH = Mathf.Max(1f, maxY - minY);
            var   view = GetCanvasViewSize();

            const float fill = 0.9f; // 留白系数：四周各留约 5%
            float zoom = Mathf.Min(view.x / boxW, view.y / boxH) * fill;
            _canvas.Zoom = Mathf.Clamp(zoom, NodeTreeCanvasState.MinZoom, NodeTreeCanvasState.MaxZoom);
            CenterOn(new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f));
        }

        /// <summary>
        /// 在指定画布坐标新建一个独立节点（无父链、无解锁条件）：
        /// 生成唯一 ID、默认类型取 nodeTypes[0]，加入配置并选中。参照 <see cref="AddChildNode"/>。
        /// </summary>
        private void AddNodeAt(Vector2 canvasPos)
        {
            if (!_config) return;

            Undo.RecordObject(_config, "新建节点");

            string typeRef = (_config.nodeTypes != null && _config.nodeTypes.Count > 0)
                ? _config.nodeTypes[0].typeName
                : null;

            var newNode = new NodeData
            {
                nodeId      = GenerateStandaloneId(),
                nodeTypeRef = typeRef,
                position    = canvasPos
            };
            newNode.RebuildTagRules(_config);

            _config.nodes.Add(newNode);
            _canvas.SelectedNodeId = newNode.nodeId;
            _selectedNodeTypeIdx   = -1;
            MarkDirty();
        }

        /// <summary>生成独立节点的唯一 ID（基名「节点」，冲突时追加序号）。</summary>
        private string GenerateStandaloneId()
        {
            const string baseName = "节点";
            if (_config.GetNode(baseName) == null) return baseName;
            for (int i = 1; ; i++)
            {
                string candidate = $"{baseName}_{i}";
                if (_config.GetNode(candidate) == null) return candidate;
            }
        }

        /// <summary>
        /// 在画布空白处弹出右键菜单：重置视口 / 显示全部节点 / 定位起始节点，
        /// 在光标处新建节点 / 自动布局，吸附网格开关。localMouse 为画布局部坐标。
        /// </summary>
        private void ShowCanvasContextMenu(Vector2 localMouse)
        {
            _ctxCanvasPos = _canvas.ScreenToCanvas(localMouse);

            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("重置视口"),     false, () => { ResetViewport(); Repaint(); });
            menu.AddItem(new GUIContent("显示全部节点"), false, () => { FrameAllNodes();  Repaint(); });

            var start = GetStartNode();
            if (start != null)
                menu.AddItem(new GUIContent("定位到起始节点"), false, () => { CenterOn(start.position); Repaint(); });
            else
                menu.AddDisabledItem(new GUIContent("定位到起始节点"));

            menu.AddSeparator("");
            if (_config)
                menu.AddItem(new GUIContent("在此处新建节点"), false, () => { AddNodeAt(_ctxCanvasPos); Repaint(); });
            else
                menu.AddDisabledItem(new GUIContent("在此处新建节点"));
            menu.AddItem(new GUIContent("自动布局"), false, () => { RunAutoLayout(); Repaint(); });

            menu.AddSeparator("");
            menu.AddItem(new GUIContent(_snapToGrid ? "吸附网格：开" : "吸附网格：关"),
                _snapToGrid, () => { _snapToGrid = !_snapToGrid; Repaint(); });

            menu.ShowAsContext();
        }

        #endregion

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
        /// 绘制左侧面板：两个标签页（节点类型 / 标签），各自对应一个 ReorderableList。
        /// </summary>
        private void DrawLeftPanel()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(LeftPanelWidth)))
            {
                // Tab 切换
                _leftTab = GUILayout.Toolbar(_leftTab, new[] { "节点类型", "标签" });

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
                        // 顶部：标签设置区（标题 + 自动写入 Unlock 条件开关，项目级设置，随 ProjectSettings 版本同步）
                        EditorGUILayout.LabelField("标签设置", EditorStyles.boldLabel);
                        var settings = NodeTreeEditorSettings.Instance;
                        EditorGUI.BeginChangeCheck();
                        bool autoWrite = EditorGUILayout.ToggleLeft(
                            AutoWriteUnlockLabel, settings.autoWriteUnlockCondition);
                        if (EditorGUI.EndChangeCheck())
                        {
                            settings.autoWriteUnlockCondition = autoWrite;
                            settings.Save();
                        }
                        EditorGUILayout.Space(4f);

                        if (_tagList != null)
                        {
                            if (_config) Undo.RecordObject(_config, "编辑标签列表");
                            _tagList.DoLayoutList();
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
        /// 绘制右侧属性面板（四选一，优先级自上而下）：
        /// 节点类型被"编辑"选中时显示节点类型属性；
        /// 画布多选出两个及以上节点时显示批量编辑面板；
        /// 画布单选一个节点时显示节点属性；
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
                    else if (_snapSelectedOrder.Count > 1)
                    {
                        DrawMultiNodeProperties();
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
        // ── 右侧面板节点 ID 重名警告 ──
        private string _idDuplicateWarning; // 当前检测到的重复 ID 输入值（非空时显示警告）
        private string _snapIdDuplicateWarning; // Layout 快照，保证 Repaint 控件数量与 Layout 完全一致
        private string _lastPropertyNodeId; // 上次 DrawNodeProperties 渲染的节点 ID（节点切换时清除警告）
        private GUIContent _tagRuleLabel;   // 标签条件行标签（复用，避免每标签每帧 new GUIContent）
        
        /// <summary>
        /// 右侧面板 节点 可编辑属性：
        /// 节点ID、节点类型、描述、图标、条件类型/参数、自定义键值对数据。
        /// 修改 ID 时同步更新所有父节点的 childNodeIds 引用。
        /// </summary>
        private void DrawNodeProperties(NodeData node)
        {
            // 合并本帧内所有 Undo 操作（顶部 RecordObject 与条件 SerializedObject.ApplyModifiedProperties 等）
            // 为一步，使一次逻辑编辑对应一次 Ctrl+Z（末尾 CollapseUndoOperations 收束）。
            int undoGroup = Undo.GetCurrentGroup();

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
            
            // ── 状态标签条件（逐标签：挂载该标签的门槛，用 Toolkit 条件编辑器内联绘制）──
            DrawNodeTagRules(node);

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

            Undo.CollapseUndoOperations(undoGroup);
        }

        /// <summary>
        /// 绘制节点的「状态标签条件」：先按标签词表同步 tagRules，再对每个标签用 Toolkit 的
        /// ConditionExpression 内联绘制器（PropertyDrawer）编辑其挂载条件。
        /// 经 SerializedObject 桥读写（Update → PropertyField → ApplyModifiedProperties），Undo/脏标记自动处理。
        /// </summary>
        private void DrawNodeTagRules(NodeData node)
        {
            EditorGUILayout.LabelField("状态标签条件", EditorStyles.boldLabel);

            // 按标签词表同步该节点的标签规则（补新增 / 删移除）；任一变化（含计数中性的标签重命名）都落盘
            if (node.RebuildTagRules(_config)) MarkDirty();

            if (node.tagRules.Count == 0)
            {
                EditorGUILayout.LabelField("（未定义任何标签。可在左侧「标签」面板添加。）",
                    EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var so = ConfigSo;
            if (so == null) return;
            so.Update();

            int nodeIdx   = _config.nodes.IndexOf(node);
            var nodesProp = so.FindProperty("nodes");
            if (nodeIdx < 0 || nodesProp == null || nodeIdx >= nodesProp.arraySize)
                return;
            var tagRulesProp = nodesProp.GetArrayElementAtIndex(nodeIdx).FindPropertyRelative("tagRules");
            if (tagRulesProp == null) return;

            _tagRuleLabel ??= new GUIContent(); // 复用，避免每标签每帧 new GUIContent
            for (int j = 0; j < node.tagRules.Count && j < tagRulesProp.arraySize; j++)
            {
                var rule = node.tagRules[j];
                var meta = FindTag(rule.tagName);
                string autoHint = meta != null && meta.autoRefresh ? " (自动)" : "";
                var condProp = tagRulesProp.GetArrayElementAtIndex(j).FindPropertyRelative("condition");
                if (condProp == null) continue;
                _tagRuleLabel.text = $"标签「{rule.tagName}」{autoHint}";
                EditorGUILayout.PropertyField(condProp, _tagRuleLabel, true);
                EditorGUILayout.Space(2f);
            }

            so.ApplyModifiedProperties();
        }

        /// <summary>按标签名在词表中查找标签定义，未找到返回 null。</summary>
        private NodeTagData FindTag(string tagName)
        {
            if (_config == null || string.IsNullOrEmpty(tagName)) return null;
            foreach (var t in _config.tags)
                if (t != null && t.tagName == tagName) return t;
            return null;
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
            // 下限 1：0 / 负尺寸会让节点矩形退化，令 Rect.Contains（悬停 / 点击）与 Rect.Overlaps（框选）同时失效
            type.resolution = Vector2.Max(EditorGUILayout.Vector2Field("节点尺寸", type.resolution), Vector2.one);
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

        #region 节点尺寸
        /// <summary>
        /// 取节点的显示尺寸（画布单位）：来自其节点类型的 <c>resolution</c>，
        /// 类型引用悬空时回退到 <see cref="NodeFallbackSize"/>。
        /// 全窗口统一走此入口，避免回退默认值散落各处。
        /// </summary>
        private Vector2 GetNodeSize(NodeData node)
        {
            if (node == null) return NodeFallbackSize;
            var type = _config ? _config.GetNodeType(node.nodeTypeRef) : null;
            return type != null ? type.resolution : NodeFallbackSize;
        }

        /// <summary>
        /// 取节点在画布局部屏幕坐标系中的矩形（已含缩放与平移）。
        /// 与 <see cref="DrawNodes"/> 的绘制、悬停、点击命中共用同一矩形，
        /// 框选亦据此判定相交，保证四者判定完全一致。
        /// </summary>
        private Rect GetNodeRect(NodeData node)
            => _canvas.GetNodeScreenRect(node.position, GetNodeSize(node));
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

            // 为画布内所有 GL 绘制建立硬件裁剪视口：GUI.BeginClip 只裁剪 IMGUI，原始 GL 会溢出到侧面板，
            // 故用 GL.Viewport 将绘制收窄到画布区（物理像素、底左原点；LoadPixelMatrix 边界用逻辑尺寸）。
            float ppp = EditorGUIUtility.pixelsPerPoint;
            var glClip = new Rect(
                canvasRect.x * ppp,
                (position.height - canvasRect.yMax) * ppp,
                canvasRect.width  * ppp,
                canvasRect.height * ppp);
            var glFull = new Rect(0f, 0f, position.width * ppp, position.height * ppp);
            NodeDrawer.SetGLClip(glClip, glFull, canvasRect.width, canvasRect.height);

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

            // 框选矩形（覆盖节点与箭头，位于底部提示栏之下）
            DrawMarquee();

            // 底部常驻操作说明栏（黑底白字，位于连线模式提示之下）
            DrawOperationHint(localRect);

            // 连线添加模式提示条（最顶层覆盖显示）
            DrawAddConnectionModeHint(localRect);

            // 重名警告（最顶层，始终可见）
            DrawDuplicateWarning(localRect);

            GUI.EndClip();
            NodeDrawer.ClearGLClip(); // 画布 GL 裁剪仅作用于本区，结束即复原，避免影响后续面板/工具栏绘制

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
            // 网格尺寸可配置后，小格 + 小缩放会让线数暴涨（如 1 格 × 0.2 缩放 ≈ 5000 条 DrawRect）。
            // 屏幕间距不足 3px 时本来就糊成一片，直接不画，同时兜住性能。
            if (spacing < 3f) return;

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
        /// 并处理左键点击（选中/加选/反选 + 开始拖拽）和右键点击（弹出上下文菜单）。
        /// 左键修饰键：Shift = 加选，Ctrl/Cmd = 反选，无修饰键 = 收敛为单选
        /// （点中已在多选集内的节点时收敛推迟到 MouseUp，以免破坏批量拖拽）。
        /// 节点矩形基于 _canvas.GetNodeScreenRect 计算（含缩放）。
        /// 节点 GL 形状由画布 GL 视口（见 DrawViewport 的 SetGLClip）在边缘做硬件裁切；
        /// 此处的 Overlaps 仅作「完全在外」粗剔除：节点矩形（含下方标签区）完全落在
        /// localBounds 外时跳过绘制与交互。localBounds 由调用方（DrawViewport）传入
        /// 当前帧真实的画布局部矩形。
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
                var nodeType  = _config.GetNodeType(node.nodeTypeRef);
                Rect nodeRect = GetNodeRect(node);

                // 视口剔除：将节点矩形向下扩展 20px（容纳节点下方的 ID 标签），
                // 完全在画布外的节点不绘制，也不参与鼠标事件检测
                var cullingRect = new Rect(nodeRect.x, nodeRect.y, nodeRect.width, nodeRect.height + 20f);
                if (!cullingRect.Overlaps(localBounds)) continue;

                // 悬停检测：记录当前帧鼠标所在节点
                if (isMouseEvent && nodeRect.Contains(Event.current.mousePosition))
                    newHoveredId = node.nodeId;

                // 框选进行中：高亮直接预览「松手后的选中」，使 Shift 加选 / Ctrl 反选所见即所得
                bool isSelected = _snapIsMarquee
                    ? _snapMarqueeResult.Contains(node.nodeId)
                    : _snapSelectedIds.Contains(node.nodeId);
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
                        // Shift = 加选；Ctrl/Cmd = 反选（切换）；无修饰键 = 收敛为单选
                        bool addMode    = Event.current.shift;
                        bool toggleMode = Event.current.control || Event.current.command;
                        bool startDrag  = true;

                        if (toggleMode)
                        {
                            _canvas.ToggleSelection(node.nodeId);
                            // 切换结果为「未选中」时不进入拖拽，避免拖动一个刚被取消选中的节点
                            startDrag = _canvas.IsSelected(node.nodeId);
                        }
                        else if (addMode)
                        {
                            _canvas.AddToSelection(node.nodeId);
                        }
                        else if (_canvas.IsSelected(node.nodeId) && _canvas.SelectedCount > 1)
                        {
                            // 已在多选集内且无修饰键：此刻不收敛，否则批量拖拽会在按下瞬间丢掉多选。
                            // 仅提升为主选中（作为拖拽增量的参考节点），并记下待收敛节点，
                            // 等 MouseUp 时若整个手势未发生拖动，再收敛为单选。
                            _canvas.AddToSelection(node.nodeId);
                            _pendingCollapseNodeId = node.nodeId;
                        }
                        else
                        {
                            _canvas.SelectSingle(node.nodeId);
                        }

                        if (startDrag)
                        {
                            _canvas.IsDraggingNode    = true;
                            _canvas.DragNodeStartPos  = node.position;
                            _canvas.DragMouseStartPos = _canvas.ScreenToCanvas(Event.current.mousePosition);
                            _nodeDragMoved            = false;
                            CaptureDragStartPositions(); // 批量拖拽：记录全部选中节点的起始位置
                        }
                        _selectedNodeTypeIdx = -1;
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
                    // 右键命中的节点已在选中集内：保留整个选中集（供后续批量操作），仅提升为主选中；
                    // 否则收敛为单选。
                    if (_canvas.IsSelected(node.nodeId)) _canvas.AddToSelection(node.nodeId);
                    else                                 _canvas.SelectSingle(node.nodeId);
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
        // ── 节点拖拽手势状态（按下时于 DrawNodes 写入，结算于 HandleCanvasInput） ──
        private string _pendingCollapseNodeId; // 按下时点中的多选集内节点（无修饰键）：MouseUp 时若未拖动则收敛为单选
        private bool   _nodeDragMoved;         // 本次左键手势是否真正拖动过节点
        // 批量拖拽：MouseDown 时快照全部选中节点的起始画布坐标（键 = 节点 ID）。
        // 必须一次性取好 —— 拖拽途中逐帧读当前位置会产生累积漂移。
        private readonly Dictionary<string, Vector2> _dragStartPositions = new Dictionary<string, Vector2>();

        // ── 框选（marquee）──
        /// <summary>框选与起框时既有选中的合成方式，由按下瞬间的修饰键决定。</summary>
        private enum EMarqueeMode
        {
            Replace = 0, // 无修饰键：仅保留框内命中
            Add     = 1, // Shift：并集加选
            Toggle  = 2, // Ctrl/Cmd：对称差反选
        }
        private bool         _isMarquee;          // 是否正在框选
        private Vector2      _marqueeStartCanvas; // 起点（画布坐标：框选途中缩放/平移时锚点不漂移）
        private Vector2      _marqueeCurCanvas;   // 当前点（画布坐标）
        private EMarqueeMode _marqueeMode;        // 合成模式
        private readonly List<string>    _marqueeBase       = new List<string>();    // 起框时的选中快照（有序）
        private readonly HashSet<string> _marqueeBaseSet    = new HashSet<string>(); // 同上，O(1) 判定
        private readonly HashSet<string> _marqueeHits       = new HashSet<string>(); // 框内命中（中间量）
        private readonly List<string>    _marqueeResult     = new List<string>();    // 合成后的最终选中（按 nodes 顺序）
        private const float MarqueeClickThreshold = 3f; // 位移小于此值（屏幕像素）视为「纯点击」而非框选
        private static readonly Color MarqueeFillColor   = new Color(0.30f, 0.60f, 1f, 0.15f); // 框选填充
        private static readonly Color MarqueeBorderColor = new Color(0.40f, 0.70f, 1f, 0.90f); // 框选边框

        /// <summary>
        /// 处理画布鼠标/滚轮交互：
        /// 滚轮以光标为中心缩放，中键拖拽平移，
        /// 左键在空白处按下并拖拽进行框选（位移不足阈值时退化为「点击空白取消选中」），
        /// 左键拖拽节点移动节点（多选时全部选中节点按同一位移整体跟随），
        /// 左键释放结束拖拽 / 提交框选，右键空白弹出画布菜单，
        /// 方向键按网格步长移动全部选中节点。
        /// </summary>
        private void HandleCanvasInput(Rect canvasRect)
        {
            var evt        = Event.current;
            var localMouse = evt.mousePosition - canvasRect.min; // 鼠标相对画布左上角的坐标

            // 只在画布区域内响应
            bool inCanvas = canvasRect.Contains(evt.mousePosition);

            // 兜底：本窗口未使用 GUIUtility.hotControl，鼠标移出窗口后松开时 MouseUp 会被转成 Ignore，
            // 只有 rawType 仍为 MouseUp；鼠标直接拖出窗口则靠 MouseLeaveWindow
            //（需 OnEnable 里的 wantsMouseEnterLeaveWindow = true，否则该事件不会送达）。
            // 不兜底会导致框选矩形常驻、拖拽态永久残留（后者是既有隐患）。
            if ((evt.rawType == EventType.MouseUp && evt.type != EventType.MouseUp)
                || evt.type == EventType.MouseLeaveWindow)
            {
                if (_isMarquee)             { FinishMarquee(true);          Repaint(); }
                if (_canvas.IsDraggingNode) { _canvas.IsDraggingNode = false; Repaint(); }
                _pendingCollapseNodeId = null;
                _nodeDragMoved         = false;
                _dragStartPositions.Clear();
            }

            switch (evt.type)
            {
                case EventType.ScrollWheel:
                    if (!inCanvas) break;
                    {
                        // 滚轮：以光标为中心缩放（平移改由中键拖拽完成）
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
                    break;

                case EventType.MouseDown:
                    if (!inCanvas) break;
                    if (evt.button == 2)
                    {
                        // 中键：开始平移
                        evt.Use();
                    }
                    else if (evt.button == 1)
                    {
                        // 右键空白处：弹出画布上下文菜单。
                        // 节点右键在 DrawNodes、连线右键在 HandleConnectionInteraction 已先行消费事件，
                        // 故事件能到达此处即代表点击落在空白区域。
                        ShowCanvasContextMenu(localMouse);
                        evt.Use();
                    }
                    else if (evt.button == 0 && !_isAddingConnection)
                    {
                        // 左键落在空白处 = 开始框选。节点右键/左键在 DrawNodes、连线在
                        // HandleConnectionInteraction 已先行消费事件，故能到达此处即代表未命中任何节点或连线。
                        // 位移不足阈值时 MouseUp 会退化为「纯点击空白取消选中」，保持既有行为。
                        // 连线添加模式下不启用框选，避免与连线目标点选冲突。
                        _canvas.IsDraggingNode = false; // 自愈：上次 MouseUp 若丢失会残留拖拽态
                        BeginMarquee(localMouse, evt);
                        evt.Use();
                        Repaint();
                    }
                    break;

                case EventType.MouseDrag:
                    // 框选时也要放行：向侧面板方向扫选时鼠标会越出画布边缘，早退会让选框卡住不动
                    if (!inCanvas && !_canvas.IsDraggingNode && !_isMarquee) break;
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
                        // 左键拖拽：移动主拖拽节点，其余选中节点按同一位移整体跟随（可选吸附到网格）
                        var node = _config.GetNode(_canvas.SelectedNodeId);
                        if (node != null)
                        {
                            var curCanvas = _canvas.ScreenToCanvas(localMouse);
                            var rawPos    = _canvas.DragNodeStartPos + (curCanvas - _canvas.DragMouseStartPos);
                            // XOR：开关开 + Shift → 临时关闭；开关关 + Shift → 临时开启
                            bool shouldSnap = _snapToGrid ^ evt.shift;
                            var  newPos     = shouldSnap ? SnapToGrid(rawPos) : rawPos;

                            // 整段手势只在第一次真正移动时记一次 Undo，使一次拖拽对应一次 Ctrl+Z
                            if (!_nodeDragMoved)
                                Undo.RecordObject(_config,
                                    _dragStartPositions.Count > 1 ? "批量移动节点" : "移动节点");

                            node.position = newPos;
                            if (_dragStartPositions.Count > 1)
                            {
                                // 吸附只对主拖拽节点算一次，再把「同一个位移」施加给其余选中节点。
                                // 逐节点各自吸附会把原本不在网格上的节点互相拉拢，破坏选中集的相对布局。
                                var delta = newPos - _canvas.DragNodeStartPos;
                                foreach (var kv in _dragStartPositions)
                                {
                                    if (kv.Key == node.nodeId) continue;
                                    var other = _config.GetNode(kv.Key);
                                    if (other != null) other.position = kv.Value + delta;
                                }
                            }

                            _nodeDragMoved = true; // 本次手势确实拖动过，MouseUp 据此跳过延迟收敛
                            MarkDirty(false); // 仅位置变化，跳过重名重扫（避免每拖拽帧分配 Dictionary/HashSet）
                            evt.Use();
                            Repaint();
                        }
                    }
                    else if (evt.button == 0 && _isMarquee)
                    {
                        // 框选拖拽：更新终点并重算合成结果（与节点拖拽互斥，二者不会同时为真）
                        _marqueeCurCanvas = _canvas.ScreenToCanvas(localMouse);
                        UpdateMarqueeResult();
                        evt.Use();
                        Repaint();
                    }
                    break;

                case EventType.MouseUp:
                    if (evt.button == 0 && _canvas.IsDraggingNode)
                    {
                        _canvas.IsDraggingNode = false;
                        evt.Use();
                    }
                    if (evt.button == 0 && _isMarquee)
                    {
                        FinishMarquee(true);
                        evt.Use();
                        Repaint();
                    }
                    if (evt.button == 0)
                    {
                        // 延迟收敛结算：按下时点的是多选集内的节点且无修饰键，
                        // 整个手势未发生拖动才收敛为单选（保证「按下即拖」不丢多选）。
                        if (_pendingCollapseNodeId != null)
                        {
                            if (!_nodeDragMoved) { _canvas.SelectSingle(_pendingCollapseNodeId); Repaint(); }
                            _pendingCollapseNodeId = null;
                        }
                        _nodeDragMoved = false;
                        _dragStartPositions.Clear();
                    }
                    break;

                case EventType.KeyDown:
                    // Esc：取消进行中的框选。必须排在最前 —— 本 switch 分支是严格的 if/else if 链，
                    // 挂到链尾永远会被前面的连线模式 / Delete 判定挡住。
                    if (_isMarquee && evt.keyCode == KeyCode.Escape)
                    {
                        FinishMarquee(false);
                        evt.Use();
                        Repaint();
                    }
                    // Esc：退出连线添加模式
                    else if (_isAddingConnection && evt.keyCode == KeyCode.Escape)
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
                        else if (_canvas.SelectedCount > 0)
                        {
                            // 删除全部选中节点（单次二次确认；实际删除延后到下一个 Layout）
                            RequestDeleteSelection();
                            evt.Use();
                        }
                    }
                    // 方向键：按网格步长移动全部选中节点。
                    // 正在编辑文本框时让给文本框 —— 本方法在 DrawRightPanel 之前执行，
                    // 若无条件消费，右侧面板输入框内的光标就再也移不动了。
                    else if (!_isAddingConnection && IsArrowKey(evt.keyCode)
                             && _canvas.SelectedCount > 0 && !EditorGUIUtility.editingTextField)
                    {
                        MoveSelectionByArrow(evt.keyCode);
                        evt.Use();
                    }
                    break;
            }
        }

        /// <summary>是否为四个方向键之一。</summary>
        private static bool IsArrowKey(KeyCode key)
            => key == KeyCode.LeftArrow || key == KeyCode.RightArrow
            || key == KeyCode.UpArrow   || key == KeyCode.DownArrow;

        /// <summary>
        /// 方向键移动全部选中节点，步长为网格的最小单位格长度（<see cref="GridSpacing"/>）。
        /// <para>开启「吸附网格」时移动到该方向上的<b>下一条网格线</b> —— 节点若不在网格交叉点上，
        /// 这一次按键只走到最近的那条线（即「先对齐」），再按才走整格；关闭时单纯 ± 一格。</para>
        /// <para>位移按<b>主选中节点</b>计算一次再整体施加，与批量拖拽同源：
        /// 逐节点各自对齐会把原本不在网格上的节点互相拉拢，破坏选中集的相对布局。</para>
        /// <para>画布 Y 轴向上，故 ↑ 增大 y、↓ 减小 y —— 与屏幕直觉相反，勿颠倒。</para>
        /// </summary>
        private void MoveSelectionByArrow(KeyCode key)
        {
            if (!_config) return;
            var primary = _config.GetNode(_canvas.SelectedNodeId);
            if (primary == null) return;

            bool  horizontal = key == KeyCode.LeftArrow || key == KeyCode.RightArrow;
            int   dir        = (key == KeyCode.RightArrow || key == KeyCode.UpArrow) ? 1 : -1;
            float grid       = GridSpacing;
            float cur        = horizontal ? primary.position.x : primary.position.y;
            float target     = _snapToGrid ? NextGridLine(cur, grid, dir) : cur + grid * dir;

            var delta = horizontal ? new Vector2(target - cur, 0f) : new Vector2(0f, target - cur);
            if (delta == Vector2.zero) return;

            Undo.RecordObject(_config, "移动节点");
            foreach (var nodeId in _canvas.SelectedNodeIds)
            {
                var node = _config.GetNode(nodeId);
                if (node != null) node.position += delta;
            }
            MarkDirty(false); // 仅位置变化，跳过重名重扫
            Repaint();
        }

        /// <summary>
        /// 取 <paramref name="value"/> 在 <paramref name="dir"/>（+1 / -1）方向上的下一条网格线。
        /// 已落在网格线上时走整格，不在则只走到最近的那条线。
        /// 容差用于抵消浮点误差 —— 否则 value 恰在网格上时 value / grid 可能算成 19.999999，
        /// 取整后会少走一格。
        /// </summary>
        private static float NextGridLine(float value, float grid, int dir)
        {
            const float eps = 1e-4f;
            float k = value / grid;
            return dir > 0
                ? (Mathf.Floor(k + eps) + 1f) * grid
                : (Mathf.Ceil(k - eps) - 1f) * grid;
        }

        /// <summary>
        /// 记录当前全部选中节点的起始画布坐标，供批量拖拽按整体位移还原。
        /// 在 MouseDown 时一次性取好；拖拽途中逐帧读当前位置会产生累积漂移。
        /// </summary>
        private void CaptureDragStartPositions()
        {
            _dragStartPositions.Clear();
            if (!_config) return;
            foreach (var nodeId in _canvas.SelectedNodeIds)
            {
                var node = _config.GetNode(nodeId);
                if (node != null) _dragStartPositions[nodeId] = node.position;
            }
        }

        /// <summary>
        /// 在画布空白处按下左键：进入框选。
        /// 起点记录为画布坐标，使框选途中滚轮缩放 / 中键平移时锚点不漂移。
        /// 修饰键决定合成模式：Shift = 并集加选，Ctrl/Cmd = 对称差反选，无修饰键 = 替换。
        /// 此处不清空选中 —— 若最终位移不足阈值（纯点击），由 <see cref="FinishMarquee"/> 按替换模式清空。
        /// </summary>
        private void BeginMarquee(Vector2 localMouse, Event evt)
        {
            _isMarquee          = true;
            _marqueeStartCanvas = _canvas.ScreenToCanvas(localMouse);
            _marqueeCurCanvas   = _marqueeStartCanvas;
            _marqueeMode        = evt.shift                    ? EMarqueeMode.Add
                                : evt.control || evt.command   ? EMarqueeMode.Toggle
                                                               : EMarqueeMode.Replace;

            _marqueeBase.Clear();
            _marqueeBaseSet.Clear();
            foreach (var id in _canvas.SelectedNodeIds) { _marqueeBase.Add(id); _marqueeBaseSet.Add(id); }

            // 右侧面板分支（无选中 / 单选 / 多选）会随选中数量在手势中途切换，
            // 残留的输入框焦点会把未提交文本串到接手同一控件 ID 的其它控件上。
            GUI.FocusControl(null);
            UpdateMarqueeResult();
        }

        /// <summary>
        /// 结束框选。<paramref name="commit"/> 为 true 时写入合成结果；为 false 时还原起框前的选中（Esc 取消）。
        /// 位移不足 <see cref="MarqueeClickThreshold"/> 时视为「空白处纯点击」：
        /// 替换模式清空选中（与框选功能引入前的行为一致），加选 / 反选模式维持原选中。
        /// 阈值按屏幕像素判定，因此不随缩放变化（画布单位判定会在 Zoom 0.2 时变成 15px 死区）。
        /// </summary>
        private void FinishMarquee(bool commit)
        {
            if (commit)
            {
                var a = _canvas.CanvasToScreen(_marqueeStartCanvas);
                var b = _canvas.CanvasToScreen(_marqueeCurCanvas);
                if ((b - a).magnitude < MarqueeClickThreshold)
                {
                    if (_marqueeMode == EMarqueeMode.Replace) _canvas.ClearSelection();
                    else                                      _canvas.SetSelection(_marqueeBase);
                }
                else
                {
                    _canvas.SetSelection(_marqueeResult);
                }
            }
            else
            {
                _canvas.SetSelection(_marqueeBase);
            }
            ClearMarqueeState();
        }

        /// <summary>清空框选运行态（不改动选中集）。</summary>
        private void ClearMarqueeState()
        {
            _isMarquee = false;
            _marqueeHits.Clear();
            _marqueeResult.Clear();
            _marqueeBase.Clear();
            _marqueeBaseSet.Clear();
        }

        /// <summary>
        /// 框选矩形（画布局部屏幕坐标，已归一化为非负宽高）。
        /// 纯水平 / 垂直扫选会得到零宽或零高矩形，而Rect.Overlaps 用严格不等式判定、
        /// 零尺寸恒不相交，故各轴至少补足 1px。
        /// </summary>
        private Rect GetMarqueeScreenRect()
        {
            var a = _canvas.CanvasToScreen(_marqueeStartCanvas);
            var b = _canvas.CanvasToScreen(_marqueeCurCanvas);
            float xMin = Mathf.Min(a.x, b.x), xMax = Mathf.Max(a.x, b.x);
            float yMin = Mathf.Min(a.y, b.y), yMax = Mathf.Max(a.y, b.y);
            if (xMax - xMin < 1f) xMax = xMin + 1f;
            if (yMax - yMin < 1f) yMax = yMin + 1f;
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        /// <summary>
        /// 按当前框选矩形重算「最终选中集」<see cref="_marqueeResult"/>（触碰即选）。
        /// 直接算出合成结果而非仅命中集，使拖拽途中的高亮预览即为松手后的实际选中。
        /// 结果按 <c>_config.nodes</c> 顺序排列，保证主选中（末位）稳定可复现。
        /// 注意：框选途中只写本字段，绝不实时改动 <c>_canvas</c> 的选中集 ——
        /// HandleCanvasInput 与 DrawRightPanel 处于同一 OnGUI pass，实时改选中会让
        /// 右侧面板分支与本帧 Layout 算出的控件数量不一致。
        /// </summary>
        private void UpdateMarqueeResult()
        {
            _marqueeHits.Clear();
            _marqueeResult.Clear();
            if (!_config) return;

            var box = GetMarqueeScreenRect();
            foreach (var node in _config.nodes)
                if (node != null && box.Overlaps(GetNodeRect(node)))
                    _marqueeHits.Add(node.nodeId);

            bool replace = _marqueeMode == EMarqueeMode.Replace;
            // 起框时已选中的：Add 全部保留；Toggle 仅保留「未被框中」的（框中即取消）
            if (!replace)
                foreach (var id in _marqueeBase)
                    if (_marqueeMode == EMarqueeMode.Add || !_marqueeHits.Contains(id))
                        _marqueeResult.Add(id);

            foreach (var node in _config.nodes)
            {
                if (node == null || !_marqueeHits.Contains(node.nodeId)) continue;
                if (!replace && _marqueeBaseSet.Contains(node.nodeId)) continue; // 上一循环已处理
                _marqueeResult.Add(node.nodeId);
            }
        }

        /// <summary>
        /// 绘制框选矩形（半透明填充 + 1px 边框）。
        /// 仅使用 <see cref="EditorGUI.DrawRect"/>，不产生任何 IMGUI 控件 ID，
        /// 因此无需 Layout 快照，也不影响控件数量一致性。
        /// </summary>
        private void DrawMarquee()
        {
            if (!_isMarquee || Event.current.type != EventType.Repaint) return;

            var r = GetMarqueeScreenRect();
            EditorGUI.DrawRect(r, MarqueeFillColor);
            EditorGUI.DrawRect(new Rect(r.x,        r.y,         r.width, 1f),      MarqueeBorderColor); // 上
            EditorGUI.DrawRect(new Rect(r.x,        r.yMax - 1f, r.width, 1f),      MarqueeBorderColor); // 下
            EditorGUI.DrawRect(new Rect(r.x,        r.y,         1f,      r.height), MarqueeBorderColor); // 左
            EditorGUI.DrawRect(new Rect(r.xMax - 1f, r.y,        1f,      r.height), MarqueeBorderColor); // 右
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
            // 右键命中的节点必定已在选中集内（见 DrawNodes 的右键分支），故直接删「当前选中」。
            int selCount = _canvas.SelectedCount;
            menu.AddItem(new GUIContent(selCount > 1
                    ? $"删除选中的 {selCount} 个节点（子节点上移）"
                    : "删除节点（子节点上移）"),
                false, RequestDeleteSelection);
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
            
            Vector2 parentSize = GetNodeSize(parent);

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
                    Vector2 lastSize = GetNodeSize(lastChild);
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

            // 默认解锁条件：要求父节点已完成（节点树的基本推进逻辑）——写入子节点 Unlock 规则
            AddUnlockFinishedItem(newNode, parentId);

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
        // 待执行的删除。EditorUtility.DisplayDialog 会在 OnGUI 中途泵消息循环，并带着已变更的
        // _config 返回；同一 pass 里后续面板会因节点已消失而少发控件，与本帧 Layout 不一致。
        // 故触发时只登记 ID，实际删除延后到下一个 Layout 事件的开头统一执行。
        private readonly List<string> _pendingDeleteIds = new List<string>();

        /// <summary>
        /// 请求删除当前全部选中节点：单次二次确认后登记待删列表，
        /// 实际删除延后到下一个 Layout 事件开头（见 <see cref="_pendingDeleteIds"/>）。
        /// </summary>
        private void RequestDeleteSelection()
        {
            int count = _canvas.SelectedCount;
            if (count == 0) return;

            bool single = count == 1;
            string opTitle = single ? "删除节点" : "批量删除节点";
            string message = single
                ? $"删除节点 [{_canvas.SelectedNodeId}]？\n其子节点将提升到父节点下。"
                : $"删除选中的 {count} 个节点？\n它们的子节点将提升到最近的存活父节点下。";
            if (!EditorUtility.DisplayDialog(opTitle, message, "删除", "取消")) return;

            _pendingDeleteIds.Clear();
            _pendingDeleteIds.AddRange(_canvas.SelectedNodeIds);
            Repaint();
        }

        /// <summary>
        /// 批量删除节点：单次 Undo，一次性完成父子提升与 Unlock 条件清理。
        /// <para>不能循环调用 <see cref="DeleteNode"/> —— 它每次自带确认弹窗与 Undo 记录，
        /// 且「先删父再删子」与「先删子再删父」会得到不同的提升结果（顺序依赖）。</para>
        /// <para>被删集合内部的父子关系在此一次性解开：连续多层都被删时，
        /// 存活的后代按原顺序提升到最近的存活祖先下。节点树至少保留一个节点。</para>
        /// <para>与 <see cref="DeleteNode"/> 的一处语义差异：被删节点有多个父节点（多父 DAG）时，
        /// 本方法把存活后代提升到<b>每一个</b>存活父节点下（每条路径都不断），
        /// 而 <see cref="DeleteNode"/> 只提升到 <c>GetParentId</c> 返回的主父节点下。
        /// 故单节点删除仍走 <see cref="DeleteNode"/>，行为保持不变。</para>
        /// </summary>
        private void DeleteNodes(IReadOnlyList<string> nodeIds)
        {
            if (!_config || nodeIds == null || nodeIds.Count == 0) return;

            // 去重并剔除已不存在的 ID
            var toDelete = new HashSet<string>();
            foreach (var id in nodeIds)
                if (!string.IsNullOrEmpty(id) && _config.GetNode(id) != null) toDelete.Add(id);
            if (toDelete.Count == 0) return;

            if (_config.nodes.Count - toDelete.Count < 1)
            {
                EditorUtility.DisplayDialog("无法删除", "节点树至少需要保留一个节点。", "确定");
                return;
            }

            Undo.RecordObject(_config, toDelete.Count > 1 ? "批量删除节点" : "删除节点");

            // 重建每个存活节点的子列表：被删的子节点原位替换为它自己的存活后代（可跨多层），顺序不变。
            // 必须在从 nodes 移除之前做完，否则层级关系已断、无从推导提升目标。
            var promotions = new List<KeyValuePair<string, string>>(); // (存活父 ID, 被提升的子 ID)
            var visited    = new HashSet<string>();
            foreach (var node in _config.nodes)
            {
                if (node?.childNodeIds == null || toDelete.Contains(node.nodeId)) continue;

                bool hasDeleted = false;
                foreach (var childId in node.childNodeIds)
                    if (toDelete.Contains(childId)) { hasDeleted = true; break; }
                if (!hasDeleted) continue;

                var rebuilt = new List<string>(node.childNodeIds.Count);
                foreach (var childId in node.childNodeIds)
                {
                    if (!toDelete.Contains(childId))
                    {
                        if (!rebuilt.Contains(childId)) rebuilt.Add(childId);
                        continue;
                    }
                    visited.Clear();
                    CollectSurvivingDescendants(childId, toDelete, visited, rebuilt, node.nodeId, promotions);
                }
                node.childNodeIds = rebuilt;
            }

            // 多父 DAG：移除所有节点中残留的（可能悬空的）被删引用
            foreach (var n in _config.nodes)
                n?.childNodeIds?.RemoveAll(id => toDelete.Contains(id));

            _config.nodes.RemoveAll(n => n == null || toDelete.Contains(n.nodeId));

            // 清理剩余节点 Unlock 条件中指向被删节点的 NodeFinished 项：引用已失效，
            // 不清会让被提升的子节点因永远无法满足「已删节点已完成」而永久锁死。
            foreach (var n in _config.nodes)
                foreach (var deletedId in toDelete)
                    RemoveUnlockFinishedItem(n, deletedId);

            // 被提升的节点重接到存活父节点，保持解锁链；受「自动写入 Unlock 条件」开关控制
            foreach (var kv in promotions)
                AddUnlockFinishedItem(_config.GetNode(kv.Value), kv.Key);

            foreach (var deletedId in toDelete)
                _canvas.RemoveFromSelection(deletedId);

            MarkDirty();
        }

        /// <summary>
        /// 递归收集 <paramref name="deletedId"/> 之下最近一层的存活后代，按原顺序追加到
        /// <paramref name="output"/>，并登记它们与存活父节点的提升关系。
        /// 子节点同样在删除集内时继续下钻；<paramref name="visited"/> 防止环形引用无限递归。
        /// </summary>
        private void CollectSurvivingDescendants(string deletedId, HashSet<string> toDelete,
            HashSet<string> visited, List<string> output, string survivingParentId,
            List<KeyValuePair<string, string>> promotions)
        {
            if (!visited.Add(deletedId)) return;
            var node = _config.GetNode(deletedId);
            if (node?.childNodeIds == null) return;

            foreach (var childId in node.childNodeIds)
            {
                if (toDelete.Contains(childId))
                {
                    CollectSurvivingDescendants(childId, toDelete, visited, output, survivingParentId, promotions);
                    continue;
                }
                if (output.Contains(childId)) continue;
                output.Add(childId);
                promotions.Add(new KeyValuePair<string, string>(survivingParentId, childId));
            }
        }

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

            // 提升的子节点（删除前快照）与主父节点
            var promoted    = new List<string>(target.childNodeIds);
            string parentId = _config.GetParentId(nodeId);
            var parent      = parentId != null ? _config.GetNode(parentId) : null;

            if (parent != null)
            {
                int idx = parent.childNodeIds.IndexOf(nodeId);
                if (idx >= 0)
                {
                    parent.childNodeIds.RemoveAt(idx);
                    // 子节点提升到父节点，插入原位置
                    parent.childNodeIds.InsertRange(idx, promoted);
                }
            }

            // 多父 DAG：移除所有节点 childNodeIds 中对已删节点的（可能悬空的）引用
            foreach (var n in _config.nodes)
                n?.childNodeIds?.RemoveAll(id => id == nodeId);

            _config.nodes.Remove(target);

            // 清理剩余节点 Unlock 条件中指向已删节点的 NodeFinished 项：引用已失效，必须清除，
            // 否则被提升的子节点会因永远无法满足「已删节点已完成」而永久锁死。
            foreach (var n in _config.nodes)
                RemoveUnlockFinishedItem(n, nodeId);

            // 提升的子节点重接到祖父：保持解锁链（祖父完成→子解锁）；受「自动写入 Unlock 条件」开关控制
            if (parentId != null)
                foreach (var childId in promoted)
                    AddUnlockFinishedItem(_config.GetNode(childId), parentId);

            // 已删除的节点从选中集移除（多选下不得波及其余选中项）
            _canvas.RemoveFromSelection(nodeId);

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

            // 多父 DAG：从所有（子树外的）父节点移除对被切除子树各节点的引用
            foreach (var n in _config.nodes)
                if (n != null && !toDelete.Contains(n.nodeId))
                    n.childNodeIds?.RemoveAll(id => toDelete.Contains(id));

            // 删除所有收集到的节点
            _config.nodes.RemoveAll(n => toDelete.Contains(n.nodeId));

            // 清理剩余节点 Unlock 条件中指向被切除节点的 NodeFinished 项（引用已失效）
            foreach (var n in _config.nodes)
                foreach (var deletedId in toDelete)
                    RemoveUnlockFinishedItem(n, deletedId);

            // 被切除的节点逐个从选中集移除（多选下不得波及子树外的选中项）
            foreach (var deletedId in toDelete)
                _canvas.RemoveFromSelection(deletedId);

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

            var fromScreen = _canvas.CanvasToScreen(sourceNode.position);
            var toScreen   = Event.current.mousePosition; // BeginClip 内已是局部坐标

            var nodeType  = _config.GetNodeType(sourceNode.nodeTypeRef);
            var lineStyle = nodeType?.line ?? new LineTypeData();
            // 端点保持真实，超出画布部分由 GL 视口硬件裁剪（见 DrawViewport 的 SetGLClip）
            NodeDrawer.DrawLineConnection(fromScreen, toScreen, lineStyle,
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
            // Y 上移一个操作说明栏高度，避免与底部常驻说明栏重叠
            var   rect  = new Rect((localBounds.width - w) * 0.5f,
                                   localBounds.height - h - 12f - OperationHintHeight, w, h);

            EditorGUI.DrawRect(rect, new Color(0.08f, 0.06f, 0f, 0.92f));
            var bc = new Color(1f, 0.75f, 0.1f, 0.85f);
            EditorGUI.DrawRect(new Rect(rect.x,         rect.y,         rect.width, 1f), bc);
            EditorGUI.DrawRect(new Rect(rect.x,         rect.yMax - 1f, rect.width, 1f), bc);
            EditorGUI.DrawRect(new Rect(rect.x,         rect.y,         1f, rect.height), bc);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y,         1f, rect.height), bc);
            GUI.Label(rect, content, style);
        }

        // ── 底部操作说明栏 ──
        private const float OperationHintHeight = 22f;   // 底部操作说明栏高度（像素）
        private GUIStyle    _operationHintStyle;         // 说明栏样式（缓存）
        private GUIContent  _operationHintContent;       // 说明文案（恒定，缓存）

        /// <summary>
        /// 在画布底部绘制一行常驻操作说明（全宽、黑色半透明底、白字居中）。
        /// 仅 Repaint 时绘制；坐标为画布局部坐标（GUI.BeginClip 内）。
        /// </summary>
        private void DrawOperationHint(Rect localBounds)
        {
            if (Event.current.type != EventType.Repaint) return;

            _operationHintContent ??= new GUIContent(
                "中键：平移　·　滚轮：缩放　·　左键：选中（Shift 加选 / Ctrl 反选）　·　空白拖拽：框选　·　方向键：移动　·　Delete：删除　·　右键：菜单");
            var style = _operationHintStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize  = 11,
                normal    = { textColor = new Color(1f, 1f, 1f, 0.9f) }
            };

            var rect = new Rect(0f, localBounds.height - OperationHintHeight,
                                localBounds.width, OperationHintHeight);
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.6f)); // 黑色半透明底
            GUI.Label(rect, _operationHintContent, style);
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
        /// 2. 在 toNode 的 Unlock 规则条件中追加一项 NodeTree.NodeFinished(target=fromId)（组间 OR，任一前置完成即解锁）。
        /// </summary>
        private void AddConnection(string fromId, string toId)
        {
            if (!_config || string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(toId)) return;
            var fromNode = _config.GetNode(fromId);
            var toNode   = _config.GetNode(toId);
            if (fromNode == null || toNode == null) return;
            if (fromNode.childNodeIds.Contains(toId)) return; // 连线已存在

            // 环检测：若 toId 能到达 fromId（fromId 在 toId 的子树内），添加 from→to 会形成环，拒绝。
            var reach = new HashSet<string>();
            CollectSubtreeIds(toId, reach);
            if (reach.Contains(fromId))
            {
                EditorUtility.DisplayDialog("无法连线",
                    $"添加从 [{fromId}] 到 [{toId}] 的连线会形成环（[{toId}] 已能到达 [{fromId}]）。", "确定");
                return;
            }

            Undo.RecordObject(_config, "添加连线");
            fromNode.childNodeIds.Add(toId);

            // 追加解锁条件：要求起点节点已完成（多前置为“任一完成即解锁”，组间 OR）
            AddUnlockFinishedItem(toNode, fromId);
            MarkDirty();
        }
        #endregion

        #region 删除 连线
        /// <summary>
        /// 删除从 fromId 到 toId 的连线：
        /// 1. 从 fromNode.childNodeIds 中移除 toId。
        /// 2. 若「自动写入 Unlock 条件」开启，则从 toNode 的 Unlock 规则条件中删除指向 fromId 的
        ///    NodeTree.NodeFinished 项（连同因此变空的组）；关闭时保留，避免误删手工条件。
        /// </summary>
        private void RemoveConnection(string fromId, string toId)
        {
            if (!_config || string.IsNullOrEmpty(fromId)) return;
            var fromNode = _config.GetNode(fromId);
            var toNode   = _config.GetNode(toId);
            if (fromNode == null) return;

            Undo.RecordObject(_config, "删除连线");
            fromNode.childNodeIds.Remove(toId);

            // 删除 toNode Unlock 规则中指向 fromId 的 NodeFinished 项——仅在「自动写入 Unlock 条件」开启时执行，
            // 与添加侧一致：fromId 节点仍存在，其条件可能是用户手动配置的，关闭开关时不应误删。
            if (NodeTreeEditorSettings.Instance.autoWriteUnlockCondition)
                RemoveUnlockFinishedItem(toNode, fromId);

            MarkDirty();
        }
        #endregion
        #endregion

        #region 条件接线辅助
        // NodeTree.NodeFinished 判定器键（与 NodeFinishedEvaluator.Key 对应）。
        private const string NodeFinishedKey = "NodeTree.NodeFinished";

        /// <summary>
        /// 向节点的 Unlock 规则条件追加一项「前置节点已完成」：NodeTree.NodeFinished(target=fromId)。
        /// 多前置各占一组、组间 OR（任一前置完成即解锁）；已存在同 target 项则跳过。Unlock 标签不存在则不接线。
        /// 受「标签」页签的「自动写入 Unlock 条件」开关控制（关闭时为空操作）。
        /// </summary>
        private void AddUnlockFinishedItem(NodeData node, string fromId)
        {
            if (node == null || string.IsNullOrEmpty(fromId)) return;
            if (!NodeTreeEditorSettings.Instance.autoWriteUnlockCondition) return; // 开关关闭：不自动写入 Unlock 条件
            node.RebuildTagRules(_config);
            var rule = node.GetTagRule(NodeTreeTags.Unlock);
            if (rule == null) return;
            if (rule.condition == null) rule.condition = new ConditionExpression();
            if (ConditionHasFinishedTarget(rule.condition, fromId)) return;

            rule.condition.groupOperator = ConditionLogicOp.Or; // 多前置：任一完成即解锁
            var group = new ConditionGroup { itemOperator = ConditionLogicOp.And };
            group.items.Add(MakeNodeFinishedItem(fromId));
            rule.condition.groups.Add(group);
        }

        /// <summary>从节点的 Unlock 规则条件中删除指向 fromId 的 NodeTree.NodeFinished 项（连同因此变空的组）。</summary>
        private void RemoveUnlockFinishedItem(NodeData node, string fromId)
        {
            var rule = node?.GetTagRule(NodeTreeTags.Unlock);
            var groups = rule?.condition?.groups;
            if (groups == null) return;

            foreach (var g in groups)
                g?.items?.RemoveAll(it => it != null && it.key == NodeFinishedKey && ItemTargetEquals(it, fromId));
            // 移除因此变空的组（仅保留仍有条件项的组）
            groups.RemoveAll(g => g == null || g.items == null || g.items.Count == 0);
        }

        // 构造一项 NodeTree.NodeFinished(target=targetId)。
        private static ConditionItem MakeNodeFinishedItem(string targetId)
        {
            var item = new ConditionItem(NodeFinishedKey);
            var p = new ConditionParam("target", ConditionParamType.String);
            p.SetString(targetId);
            item.parameters.Add(p);
            return item;
        }

        // 判断某条件项的 target 参数是否等于 targetId。
        private static bool ItemTargetEquals(ConditionItem item, string targetId)
        {
            if (item?.parameters == null) return false;
            foreach (var p in item.parameters)
                if (p != null && p.id == "target") return p.GetString() == targetId;
            return false;
        }

        // 判断条件表达式中是否已存在指向 targetId 的 NodeTree.NodeFinished 项。
        private static bool ConditionHasFinishedTarget(ConditionExpression expr, string targetId)
        {
            if (expr?.groups == null) return false;
            foreach (var g in expr.groups)
                if (g?.items != null)
                    foreach (var it in g.items)
                        if (it != null && it.key == NodeFinishedKey && ItemTargetEquals(it, targetId))
                            return true;
            return false;
        }
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
            var visited = new HashSet<string>(); // 跨所有根共享：防回边成环无限递归，且多父钻石节点只布局一次

            foreach (var root in roots)
            {
                LayoutSubtree(root, 0, horizontal, ref crossCursor, visited);
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
        private void LayoutSubtree(NodeData node, int depth, bool horizontal, ref float crossCursor,
                                   HashSet<string> visited)
        {
            // 已访问即跳过：防回边（环）无限递归导致 StackOverflow，且令多父钻石节点只布局一次（避免后写覆盖）
            if (node == null || !visited.Add(node.nodeId)) return;

            Vector2 nodeSize = GetNodeSize(node);
            float nodeW = nodeSize.x;
            float nodeH = nodeSize.y;

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
                LayoutSubtree(child, depth + 1, horizontal, ref crossCursor, visited);
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
        private Vector2 SnapToGrid(Vector2 pos)
        {
            float grid = GridSpacing;
            return new Vector2(
                Mathf.Round(pos.x / grid) * grid,
                Mathf.Round(pos.y / grid) * grid);
        }
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
