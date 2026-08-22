using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Ale.NodeTree.Runtime;
using Ale.Toolkit.Runtime;
using Ale.Toolkit.Editor;

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

        // ── 自定义属性交集快照（决定字段区控件数量，必须在 Layout 时算好）──
        private readonly List<string>         _snapCommonAttrIds    = new List<string>();         // 全部选中节点共有的属性 id
        private readonly List<AttributeValue> _snapCommonAttrValues = new List<AttributeValue>(); // 与上表一一对应的代表节点属性值实例
        private int _snapExcludedAttrCount;                                                       // 因节点类型不同而被排除的属性数
        private readonly HashSet<string> _multiAttrUnion = new HashSet<string>();                 // 并集暂存（复用，避免每帧分配）
        private static readonly List<AnimationCurve> CurveScratch = new List<AnimationCurve>();   // 曲线深拷贝暂存

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

            SnapshotCommonAttributes();
        }

        /// <summary>
        /// 计算「全部选中节点共有的自定义属性」并快照（Layout 时调用）。
        /// 属性 schema 来自各节点 <c>nodeTypeRef</c> 对应的 <see cref="NodeTypeData.attributes"/>；
        /// 判同标准与 <c>AttributeSync</c> 内部一致 —— id 相同且 (type, isArray, 枚举类型) 一致才算同一字段，
        /// 该谓词在 toolkit 内为 private，此处按同一规则本地复刻。
        /// </summary>
        private void SnapshotCommonAttributes()
        {
            _snapCommonAttrIds.Clear();
            _snapCommonAttrValues.Clear();
            _multiAttrUnion.Clear();
            _snapExcludedAttrCount = 0;
            if (!_config || _snapMultiNodes.Count < 2) return;

            // 并集：仅用于报告「有多少字段因类型不同而未显示」
            foreach (var node in _snapMultiNodes)
            {
                var t = _config.GetNodeType(node.nodeTypeRef);
                if (t?.attributes == null) continue;
                foreach (var d in t.attributes)
                    if (d != null && !string.IsNullOrEmpty(d.id)) _multiAttrUnion.Add(d.id);
            }

            // 只对代表节点做 schema 同步：AttributeSync.Sync 每次分配 Dictionary + HashSet，
            // 叠加 wantsMouseMove 的高频重绘，N 个节点每帧同步是实打实的 GC 压力；
            // 其余节点在真正发生传播时才惰性同步。
            var head = _snapMultiNodes[0];
            int beforeCount = head.attributeValues.Count;
            head.RebuildAttributes(_config);
            if (head.attributeValues.Count != beforeCount) MarkDirty();

            var headDefs = _config.GetNodeType(head.nodeTypeRef)?.attributes;
            if (headDefs == null) return;

            foreach (var def in headDefs)
            {
                if (def == null || string.IsNullOrEmpty(def.id)) continue;

                bool common = true;
                for (int i = 1; i < _snapMultiNodes.Count && common; i++)
                {
                    var defs = _config.GetNodeType(_snapMultiNodes[i].nodeTypeRef)?.attributes;
                    common = defs != null && HasSameShapeDef(defs, def);
                }
                if (!common) continue;

                var entry = head.GetEntry(def.id);
                if (entry?.value == null) continue; // RebuildAttributes 之后理论上不会发生
                _snapCommonAttrIds.Add(def.id);
                _snapCommonAttrValues.Add(entry.value);
            }
            _snapExcludedAttrCount = Mathf.Max(0, _multiAttrUnion.Count - _snapCommonAttrIds.Count);
        }

        /// <summary>defs 中是否存在与 target 同 id 且形态（类型 / 数组 / 枚举类型）完全一致的定义。</summary>
        private static bool HasSameShapeDef(List<AttributeDefinition> defs, AttributeDefinition target)
        {
            foreach (var d in defs)
            {
                if (d == null || d.id != target.id) continue;
                if (d.type != target.type || d.isArray != target.isArray) return false;
                if ((d.type == EFieldType.Enum || d.type == EFieldType.EnumIntPair)
                    && d.enumTypeRef != target.enumTypeRef) return false;
                return true;
            }
            return false;
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

            // ── 节点名称 / 描述（AttributeValue.Text）──
            // AttributeFieldDrawer 无多目标能力，故只绘制代表节点 [0] 的实例（不显示「多值」），
            // 检测到改动后再原地覆写其余选中节点的对应值。
            EditorGUILayout.LabelField("文本（显示 [0] 的值，改动即写入全部选中）", EditorStyles.miniBoldLabel);
            DrawMultiTextAttr("节点名称", true);
            EditorGUILayout.Space(2f);
            DrawMultiTextAttr("节点描述", false);

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

            // ── 自定义属性（全部选中节点类型 schema 的交集）──
            EditorGUILayout.LabelField("自定义属性（共有字段）", EditorStyles.boldLabel);
            for (int i = 0; i < _snapCommonAttrIds.Count; i++)
                DrawMultiCustomAttr(i);

            // 恒定占位行：三种文案共用同一个控件，控件数量不随内容变化
            GUILayout.Label(
                _snapExcludedAttrCount > 0
                    ? $"（另有 {_snapExcludedAttrCount} 个字段因所选节点的类型不同而未显示）"
                    : _snapCommonAttrIds.Count == 0
                        ? "（所选节点的类型未定义属性字段）"
                        : "",
                EditorStyles.centeredGreyMiniLabel);

            EditorGUILayout.Space(8f);

            Undo.CollapseUndoOperations(undoGroup);
        }

        /// <summary>
        /// 绘制代表节点的 <c>nodeName</c> / <c>nodeDesc</c>，改动后传播到其余选中节点。
        /// 改动信号取 <c>AttrCtx.DirtyTick</c> 与 BeginChangeCheck 的并集，而<b>不是</b> JSON 前后比对：
        /// <see cref="AttributeFieldDrawer"/> 会对空值调用 EnsureCount 补一个元素，
        /// 而 nodeName / nodeDesc 的初值恰为空 —— JSON 比对会在首帧误判「已改动」，
        /// 把代表节点的值静默覆盖到其余全部选中节点。
        /// </summary>
        private void DrawMultiTextAttr(string label, bool isName)
        {
            var head = _snapMultiNodes[0];
            var src  = isName ? head.nodeName : head.nodeDesc;

            int tick = AttrCtx.DirtyTick;
            EditorGUI.BeginChangeCheck();
            AttributeFieldDrawer.Draw(AttrCtx, label, src, null);
            if (!EditorGUI.EndChangeCheck() && AttrCtx.DirtyTick == tick) return;

            for (int i = 1; i < _snapMultiNodes.Count; i++)
            {
                var n = _snapMultiNodes[i];
                CopyAttrValue(src, isName ? n.nodeName : n.nodeDesc);
            }
            MarkDirty();
        }

        /// <summary>
        /// 绘制交集中第 <paramref name="index"/> 个自定义属性（取代表节点的值），改动后传播到其余选中节点。
        /// 其余节点在此刻才做 schema 同步（惰性），避免每帧为 N 个节点重复分配同步用的临时集合。
        /// </summary>
        private void DrawMultiCustomAttr(int index)
        {
            string attrId = _snapCommonAttrIds[index];
            var    src    = _snapCommonAttrValues[index];

            int tick = AttrCtx.DirtyTick;
            EditorGUI.BeginChangeCheck();
            AttributeFieldDrawer.Draw(AttrCtx, attrId, src, null);
            if (!EditorGUI.EndChangeCheck() && AttrCtx.DirtyTick == tick) return;

            for (int i = 1; i < _snapMultiNodes.Count; i++)
            {
                var other = _snapMultiNodes[i];
                other.RebuildAttributes(_config);
                var entry = other.GetEntry(attrId);
                if (entry != null) CopyAttrValue(src, entry.value);
            }
            MarkDirty();
        }

        /// <summary>
        /// 用 <paramref name="src"/> 的原始后备数据<b>原地覆写</b> <paramref name="dst"/>。
        /// 必须原地覆写而不是替换实例：<see cref="AttributeFieldDrawer"/> 按 AttributeValue 的
        /// 实例身份缓存本地化 Holder 与拖拽状态，换实例会造成缓存泄漏与错配。
        /// 曲线需额外深拷贝 —— <c>SetRaw</c> 对列表是逐元素浅拷贝，直接传入会让多个节点
        /// 共用同一条 <see cref="AnimationCurve"/>，之后改一个即改全部。
        /// </summary>
        private static void CopyAttrValue(AttributeValue src, AttributeValue dst)
        {
            if (src == null || dst == null || ReferenceEquals(src, dst)) return;

            var srcCurves = src.RawCurves;
            CurveScratch.Clear();
            for (int i = 0; i < srcCurves.Count; i++)
                CurveScratch.Add(srcCurves[i] != null ? new AnimationCurve(srcCurves[i].keys) : new AnimationCurve());

            dst.SetRaw(src.Type, src.IsArray, src.EnumTypeRef,
                       src.RawInts, src.RawFloats, src.RawStrings, src.RawObjects,
                       CurveScratch, src.RawObjAddresses);
            CurveScratch.Clear(); // 不长期持有曲线引用
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
