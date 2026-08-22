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
    /// （<c>_snapSelectedOrder</c> / <c>_snapIdListExpanded</c> / <c>_snapMissingSelected</c>）决定，
    /// 绘制期间绝不读取实时状态 —— 否则 Repaint 与 Layout 的控件数量不一致会抛 GUI Layout mismatch。
    /// 快照由 <see cref="SnapshotMultiEditState"/> 统一写入。</para>
    /// </summary>
    public partial class NodeTreeEditorWindow
    {
        #region 右侧面板-批量编辑
        // ── 选中节点列表折叠态 ──
        private bool _idListExpanded     = true; // 实时值，由 Foldout 写入
        private bool _snapIdListExpanded = true; // Layout 快照：决定列表子行是否发出控件

        // ── Layout 快照 ──
        private readonly List<string> _snapSelectedOrder = new List<string>(); // 选中节点 ID（保持选中顺序，末位为主选中）
        private int _snapMissingSelected;                                      // 其中已在配置里不存在的数量

        // ── 缓存的 GUIStyle（避免每次 OnGUI 重新分配）──
        private GUIStyle _multiCountStyle; // 标题行右上角选中计数
        private GUIStyle _multiWarnStyle;  // 提示占位行

        /// <summary>
        /// Layout 事件时快照批量面板所依赖的全部状态。
        /// 由 <c>OnGUI</c> 的 Layout 分支调用；绘制期间只读这些快照。
        /// </summary>
        private void SnapshotMultiEditState()
        {
            _snapIdListExpanded = _idListExpanded;

            _snapSelectedOrder.Clear();
            _snapMissingSelected = 0;
            foreach (var nodeId in _canvas.SelectedNodeIds)
            {
                _snapSelectedOrder.Add(nodeId);
                if (!_config || _config.GetNode(nodeId) == null) _snapMissingSelected++;
            }
        }

        /// <summary>
        /// 绘制右侧「批量编辑」面板：
        /// 标题行与选中计数、可折叠的选中节点只读列表（每行可定位）、以及一行恒定占位的提示行。
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

            EditorGUILayout.Space(8f);
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
