using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Ale.NodeTree.Runtime;

namespace Ale.NodeTree.Editor
{
    /// <summary>
    /// <see cref="NodeTreeEditorWindow"/> 的右侧「批量编辑」面板部分（partial 拆分）。
    /// 画布多选（空白拖拽框选 / Shift 加选 / Ctrl 反选）出两个及以上节点时，
    /// 右侧面板由单节点属性切换为本面板，集中编辑全部选中节点共有的字段。
    ///
    /// <para>IMGUI 约束：本面板发出的控件数量完全由 Layout 事件时的快照
    /// （<c>_snapSelectedOrder</c> / <c>_snapMultiNodes</c> / <c>_snapIdListExpanded</c>）决定，
    /// 绘制期间绝不读取实时状态 —— 否则 Repaint 与 Layout 的控件数量不一致会抛 GUI Layout mismatch。
    /// 快照由 <see cref="SnapshotMultiEditState"/> 统一写入。</para>
    ///
    /// <para>写入约定：全部选中节点同属一个 <c>_config</c> 资产，故一次
    /// <c>Undo.RecordObject</c> 即可覆盖 N 个节点；每个字段各自 BeginChangeCheck / EndChangeCheck，
    /// 只有用户真正改动的字段才写回，避免一次编辑把其余字段一并按「代表值」刷掉。</para>
    /// </summary>
    public partial class NodeTreeEditorWindow
    {
        #region 右侧面板-批量编辑
        // ── 选中节点列表折叠态 ──
        private bool _idListExpanded     = true; // 实时值，由 Foldout 写入
        private bool _snapIdListExpanded = true; // Layout 快照：决定列表子行是否发出控件

        // ── Layout 快照 ──
        private readonly List<string>   _snapSelectedOrder = new List<string>();   // 选中节点 ID（保持选中顺序，末位为主选中）
        private readonly List<NodeData> _snapMultiNodes    = new List<NodeData>(); // 上表中仍存在于配置的节点实体
        private int _snapMissingSelected;                                          // 二者之差：已失效的选中项数量

        // ── 缓存的 GUIStyle / GUIContent（避免每次 OnGUI 重新分配）──
        private GUIStyle   _multiCountStyle; // 标题行右上角选中计数
        private GUIStyle   _multiWarnStyle;  // 提示占位行
        private GUIContent _multiPosLabel;   // 画布坐标标签（含分轴对齐说明）

        /// <summary>
        /// Layout 事件时快照批量面板所依赖的全部状态。
        /// 由 <c>OnGUI</c> 的 Layout 分支调用；绘制期间只读这些快照。
        /// </summary>
        private void SnapshotMultiEditState()
        {
            _snapIdListExpanded = _idListExpanded;

            _snapSelectedOrder.Clear();
            _snapMultiNodes.Clear();
            foreach (var nodeId in _canvas.SelectedNodeIds)
            {
                _snapSelectedOrder.Add(nodeId);
                var node = _config ? _config.GetNode(nodeId) : null;
                if (node != null) _snapMultiNodes.Add(node);
            }
            _snapMissingSelected = _snapSelectedOrder.Count - _snapMultiNodes.Count;
        }

        /// <summary>
        /// 绘制右侧「批量编辑」面板：
        /// 标题行与选中计数、可折叠的选中节点只读列表（每行可定位）、恒定占位的提示行，
        /// 以及全部选中节点共有字段的批量编辑区。
        /// </summary>
        private void DrawMultiNodeProperties()
        {
            int count = _snapSelectedOrder.Count;

            // ── 标题行 + 右上角选中计数 ──
            // 三个控件（Label / FlexibleSpace / Label）在 Layout 与 Repaint 均恒定调用，
            // 写法与单节点属性面板的标题行保持一致。
            GUILayout.BeginHorizontal();
            GUILayout.Label("批量编辑", EditorStyles.boldLabel, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            var countStyle = _multiCountStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                wordWrap  = false,
                normal    = { textColor = new Color(0.55f, 0.80f, 1f, 1f) }
            };
            GUILayout.Label($"已选中 {count} 个节点", countStyle,
                GUILayout.ExpandWidth(false), GUILayout.MaxWidth(RightPanelWidth - 80f));
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(4f);

            // ── 选中节点列表（只读）──
            // Foldout 自身恒定发出 1 个控件；其子行数量取自 Layout 快照 _snapIdListExpanded，
            // 直接读实时值会在「展开的那一次 MouseDown」里造成控件数分歧。
            _idListExpanded = EditorGUILayout.Foldout(_snapIdListExpanded, $"选中节点 ({count})", true);
            if (_snapIdListExpanded)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    for (int i = 0; i < count; i++)
                    {
                        string nodeId = _snapSelectedOrder[i];
                        // 节点是否仍存在只影响文案与按钮可用性，不影响控件数量，
                        // 保证控件数只由快照的元素个数决定。
                        bool alive = _config && _config.GetNode(nodeId) != null;
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField($"[{i}]", GUILayout.Width(32f));
                            EditorGUILayout.LabelField(alive ? nodeId : $"{nodeId}（已不存在）");
                            using (new EditorGUI.DisabledScope(!alive))
                            {
                                // 【定位】只移动视口，不改动选中集（详见 CenterOnNode）
                                if (GUILayout.Button("定位", GUILayout.Width(36f)))
                                    CenterOnNode(nodeId);
                            }
                        }
                    }
                }
            }

            EditorGUILayout.Space(4f);

            // ── 提示占位行 ──
            // 无条件发出，无内容时传空字符串占位（同标题行的处理），保证控件数量恒等。
            var warnStyle = _multiWarnStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                normal   = { textColor = new Color(1f, 0.85f, 0.10f, 1f) }
            };
            GUILayout.Label(
                _snapMissingSelected > 0
                    ? $"⚠ 其中 {_snapMissingSelected} 个节点已不存在，将在下次选中变更时自动剔除"
                    : "",
                warnStyle);

            // 选中项全部失效时不绘制字段区。分支条件取自 Layout 快照，pass 内恒定，不会造成控件数分歧。
            if (_snapMultiNodes.Count == 0) return;

            EditorGUILayout.Space(4f);
            DrawMultiCommonFields();
        }

        /// <summary>
        /// 绘制全部选中节点共有字段的批量编辑区（节点类型 / 备注 / 图标 / 画布坐标）。
        /// 值不一致的字段以 <see cref="EditorGUI.showMixedValue"/> 显示为「—」；
        /// 每个字段独立做变更检查，只有被真正改动的字段才写回全部选中节点。
        /// </summary>
        private void DrawMultiCommonFields()
        {
            var head = _snapMultiNodes[0]; // 代表值来源（主选中之外的任一节点亦可，取首个即可）

            // 一次遍历求出各字段是否为「多值」，避免每个字段各扫一遍
            bool mixedType = false, mixedComment = false, mixedIcon = false, mixedX = false, mixedY = false;
            for (int i = 1; i < _snapMultiNodes.Count; i++)
            {
                var n = _snapMultiNodes[i];
                if (n.nodeTypeRef != head.nodeTypeRef)                    mixedType    = true;
                if ((n.comment ?? "") != (head.comment ?? ""))            mixedComment = true;
                if (n.uiIcon != head.uiIcon)                              mixedIcon    = true;
                if (!Mathf.Approximately(n.position.x, head.position.x))  mixedX       = true;
                if (!Mathf.Approximately(n.position.y, head.position.y))  mixedY       = true;
            }

            EditorGUILayout.LabelField("共有字段（修改将写入全部选中节点）", EditorStyles.miniBoldLabel);

            // 合并本帧内所有 Undo 操作为一步，使一次批量编辑对应一次 Ctrl+Z。
            // 全部选中节点同属一个 _config 资产，故一次 RecordObject 即覆盖 N 个节点。
            int undoGroup = Undo.GetCurrentGroup();
            Undo.RecordObject(_config, "批量编辑节点属性");

            // ── 节点类型 ──
            // 必须先变更检查再写：单节点面板是无条件写回（顺带把悬空引用修复成 nodeTypes[0]），
            // 批量下沿用那种写法会在「刚选中」的瞬间把 N 个节点静默改成同一类型。
            using (new EditorGUI.DisabledScope(_nodeTypeNames.Length == 0))
            {
                int curIdx = mixedType ? -1 : Array.IndexOf(_nodeTypeNames, head.nodeTypeRef);
                EditorGUI.showMixedValue = mixedType;
                EditorGUI.BeginChangeCheck();
                int newIdx = EditorGUILayout.Popup("节点类型 (nodeTypeRef)", curIdx, _nodeTypeNames);
                bool typeChanged = EditorGUI.EndChangeCheck();
                EditorGUI.showMixedValue = false; // 必须立即复位：该标志不会自动清除，泄漏会污染后续控件乃至下一帧
                if (typeChanged && newIdx >= 0 && newIdx < _nodeTypeNames.Length)
                {
                    string newType = _nodeTypeNames[newIdx];
                    foreach (var n in _snapMultiNodes)
                    {
                        n.nodeTypeRef = newType;
                        n.RebuildAttributes(_config); // 按新类型的 schema 同步自定义属性值
                    }
                    MarkDirty();
                }
            }

            EditorGUILayout.Space(4f);

            // ── 备注 ──
            // TextArea 不响应 showMixedValue，故多值时以空内容 + 标签提示表达；一旦输入即覆盖全部选中。
            EditorGUILayout.LabelField(mixedComment
                ? "备注 (仅编辑器使用)　（多值·输入覆盖全部）"
                : "备注 (仅编辑器使用)");
            EditorGUI.BeginChangeCheck();
            string newComment = EditorGUILayout.TextArea(
                mixedComment ? "" : head.comment ?? "", GUILayout.MinHeight(60f));
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var n in _snapMultiNodes) n.comment = newComment;
                MarkDirty();
            }

            EditorGUILayout.Space(8f);

            // ── UI 设置 ──
            EditorGUILayout.LabelField("UI设置", EditorStyles.boldLabel);
            EditorGUI.showMixedValue = mixedIcon;
            EditorGUI.BeginChangeCheck();
            var newIcon = (Sprite)EditorGUILayout.ObjectField(
                "中心图标 (uiIcon)", mixedIcon ? null : head.uiIcon, typeof(Sprite), false);
            bool iconChanged = EditorGUI.EndChangeCheck();
            EditorGUI.showMixedValue = false;
            if (iconChanged)
            {
                foreach (var n in _snapMultiNodes) n.uiIcon = newIcon;
                MarkDirty();
            }

            EditorGUILayout.Space(4f);

            // ── 画布坐标 ──
            // 拆成两个独立 FloatField，不能用 Vector2Field：后者只有一个 showMixedValue，
            // 且整体回写 Vector2 —— 用户只改 Y 时会把 X 的显示值当作真值一并写回，静默篡改 N 个节点的 X。
            // 分轴之后语义即「对齐」：改哪个轴，全部选中节点就在该轴对齐，另一轴各自保持原值。
            _multiPosLabel ??= new GUIContent("画布坐标 (position)",
                "分轴填写：填入 X 即让全部选中节点在 X 轴对齐；填入 Y 同理。未改动的轴保持各节点原值。");
            EditorGUILayout.LabelField(_multiPosLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("X", GUILayout.Width(14f));
                EditorGUI.showMixedValue = mixedX;
                EditorGUI.BeginChangeCheck();
                float newX = EditorGUILayout.FloatField(head.position.x);
                bool xChanged = EditorGUI.EndChangeCheck();
                EditorGUI.showMixedValue = false;

                GUILayout.Space(8f);

                EditorGUILayout.LabelField("Y", GUILayout.Width(14f));
                EditorGUI.showMixedValue = mixedY;
                EditorGUI.BeginChangeCheck();
                float newY = EditorGUILayout.FloatField(head.position.y);
                bool yChanged = EditorGUI.EndChangeCheck();
                EditorGUI.showMixedValue = false;

                if (xChanged)
                    foreach (var n in _snapMultiNodes) n.position = new Vector2(newX, n.position.y);
                if (yChanged)
                    foreach (var n in _snapMultiNodes) n.position = new Vector2(n.position.x, newY);
                if (xChanged || yChanged)
                    MarkDirty(false); // 仅位置变化，跳过重名重扫
            }

            EditorGUILayout.Space(8f);

            Undo.CollapseUndoOperations(undoGroup);
        }

        /// <summary>
        /// 将视口中心对准指定节点，但<b>不改动选中集</b>。
        /// 与 <see cref="NavigateToNode"/> 的区别：后者会把选中收敛为该单个节点，
        /// 在批量编辑中会让用户刚框好的多选付诸东流。
        /// </summary>
        private void CenterOnNode(string nodeId)
        {
            if (!_config || string.IsNullOrEmpty(nodeId)) return;
            var target = _config.GetNode(nodeId);
            if (target == null) return;
            CenterOn(target.position);
            Repaint();
        }
        #endregion
    }
}
