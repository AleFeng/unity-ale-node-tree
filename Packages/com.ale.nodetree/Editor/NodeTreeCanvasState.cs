using System.Collections.Generic;
using Ale.NodeTree.Runtime;
using UnityEngine;

namespace Ale.NodeTree.Editor
{
    /// <summary>
    /// 节点树编辑器画布交互状态。
    /// 维护平移偏移、缩放比例、当前选中节点集合及拖拽状态，
    /// 并提供画布坐标与屏幕像素坐标之间的互转方法。
    ///
    /// 坐标系约定（Y 轴向上）：
    ///   画布坐标 —— 原点位于左下角，X 向右，Y 向上。
    ///   屏幕坐标 —— IMGUI 原生坐标，原点位于左上角，Y 向下。
    ///   转换公式：
    ///     screenPos.x = canvasPos.x * zoom + panOffset.x
    ///     screenPos.y = canvasHeight - (canvasPos.y * zoom + panOffset.y)
    ///
    /// 选中约定：
    ///   选中为「有序集合」，末位元素即主选中节点（最后一次点选 / 加选的节点）。
    ///   <see cref="SelectedNodeId"/> 是面向单选调用方的兼容属性：读取返回主选中，
    ///   写入等价于「收敛为单选」（写 null 即清空），语义与多选改造前完全一致。
    /// </summary>
    public class NodeTreeCanvasState
    {
        public Vector2 PanOffset = Vector2.zero; // 画布平移偏移（像素），鼠标中键拖拽修改
        public float Zoom = 1.0f;                // 画布缩放比例，由 Alt+滚轮或工具栏按钮修改
        public float CanvasHeight;               // 画布像素高度，由 DrawCanvas 每帧写入，Y 轴翻转转换所需
        public bool IsDraggingNode;              // 是否正在拖拽节点
        public Vector2 DragNodeStartPos;         // 拖拽开始时节点的画布坐标（用于计算拖拽增量）
        public Vector2 DragMouseStartPos;        // 拖拽开始时鼠标在画布坐标系中的位置

        public const float MinZoom = 0.2f; // 最小缩放比例
        public const float MaxZoom = 3.0f; // 最大缩放比例

        #region 选中集合
        // 有序列表保证「主选中 = 末位」与右侧面板的稳定遍历顺序；
        // HashSet 仅作 O(1) 判定的镜像，二者必须成对增删，故列表不对外暴露可变引用。
        private readonly List<string> _selectedIds    = new List<string>();
        private readonly HashSet<string> _selectedSet = new HashSet<string>();

        /// <summary>当前选中的全部节点 ID（只读、有序；末位为主选中）。</summary>
        public IReadOnlyList<string> SelectedNodeIds => _selectedIds;

        /// <summary>当前选中的节点数量。</summary>
        public int SelectedCount => _selectedIds.Count;

        /// <summary>
        /// 主选中节点 ID（选中集末位），无选中时为 null。
        /// 写入语义为「收敛为单选」：写非空值等价于 <see cref="SelectSingle"/>，
        /// 写 null / 空串等价于 <see cref="ClearSelection"/>。
        /// </summary>
        public string SelectedNodeId
        {
            get => _selectedIds.Count > 0 ? _selectedIds[_selectedIds.Count - 1] : null;
            set
            {
                if (string.IsNullOrEmpty(value)) ClearSelection();
                else SelectSingle(value);
            }
        }

        /// <summary>指定节点是否在选中集内。</summary>
        public bool IsSelected(string nodeId)
            => !string.IsNullOrEmpty(nodeId) && _selectedSet.Contains(nodeId);

        /// <summary>收敛为单选：清空原有选中，仅选中指定节点。</summary>
        public void SelectSingle(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) { ClearSelection(); return; }
            // 已是唯一选中则直接返回，避免无谓的集合重建
            if (_selectedIds.Count == 1 && _selectedIds[0] == nodeId) return;
            _selectedIds.Clear();
            _selectedSet.Clear();
            _selectedIds.Add(nodeId);
            _selectedSet.Add(nodeId);
        }

        /// <summary>加选：追加到选中集末位（使其成为主选中）；已选中时仅提升为主选中。</summary>
        public void AddToSelection(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return;
            if (_selectedSet.Add(nodeId))
            {
                _selectedIds.Add(nodeId);
                return;
            }
            // 已在集内：移到末位，保证「最后操作的节点 = 主选中」
            if (_selectedIds[_selectedIds.Count - 1] == nodeId) return;
            _selectedIds.Remove(nodeId);
            _selectedIds.Add(nodeId);
        }

        /// <summary>从选中集移除指定节点；不在集内时无副作用。</summary>
        public void RemoveFromSelection(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return;
            if (_selectedSet.Remove(nodeId)) _selectedIds.Remove(nodeId);
        }

        /// <summary>切换选中状态：已选中则移除，未选中则加选。</summary>
        public void ToggleSelection(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return;
            if (_selectedSet.Contains(nodeId)) RemoveFromSelection(nodeId);
            else AddToSelection(nodeId);
        }

        /// <summary>清空选中集。</summary>
        public void ClearSelection()
        {
            if (_selectedIds.Count == 0) return;
            _selectedIds.Clear();
            _selectedSet.Clear();
        }

        /// <summary>整体替换选中集（保持传入顺序，自动去重并剔除空 ID；末位为主选中）。</summary>
        public void SetSelection(IEnumerable<string> nodeIds)
        {
            _selectedIds.Clear();
            _selectedSet.Clear();
            if (nodeIds == null) return;
            foreach (var id in nodeIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (_selectedSet.Add(id)) _selectedIds.Add(id);
            }
        }

        /// <summary>
        /// 剔除选中集中在配置里已不存在的节点 ID。
        /// 用于删除节点、切除子树、撤销 / 重做之后，避免右侧面板解析到空节点。
        /// </summary>
        public void PruneSelection(NodeTreeData config)
        {
            if (_selectedIds.Count == 0) return;
            if (config == null) { ClearSelection(); return; }
            for (int i = _selectedIds.Count - 1; i >= 0; i--)
            {
                if (config.GetNode(_selectedIds[i]) != null) continue;
                _selectedSet.Remove(_selectedIds[i]);
                _selectedIds.RemoveAt(i);
            }
        }
        #endregion

        #region 坐标转换
        /// <summary>
        /// 将节点画布坐标（Y 向上）转换为屏幕像素偏移（相对于 canvasRect.min，Y 向下）。
        /// X 公式：screenPos.x = canvasPos.x * zoom + panOffset.x
        /// Y 公式：screenPos.y = canvasHeight - (canvasPos.y * zoom + panOffset.y)
        /// </summary>
        public Vector2 CanvasToScreen(Vector2 canvasPos)
            => new Vector2(
                canvasPos.x * Zoom + PanOffset.x,
                CanvasHeight - (canvasPos.y * Zoom + PanOffset.y));

        /// <summary>
        /// 将屏幕像素偏移（相对于 canvasRect.min，Y 向下）转换为节点画布坐标（Y 向上）。
        /// X 公式：canvasPos.x = (screenPos.x - panOffset.x) / zoom
        /// Y 公式：canvasPos.y = (canvasHeight - screenPos.y - panOffset.y) / zoom
        /// </summary>
        public Vector2 ScreenToCanvas(Vector2 screenPos)
            => new Vector2(
                (screenPos.x - PanOffset.x) / Zoom,
                (CanvasHeight - screenPos.y - PanOffset.y) / Zoom);

        /// <summary>
        /// 计算节点在屏幕空间中的矩形（相对于 canvasRect.min）。
        /// 节点以 nodePos（画布坐标，Y 向上）为中心，尺寸为 nodeSize * zoom。
        /// 返回 Rect 的左上角 = screenCenter - halfSize，符合 IMGUI Rect 约定（Y 向下）。
        /// </summary>
        public Rect GetNodeScreenRect(Vector2 nodePos, Vector2 nodeSize)
        {
            var screenPos  = CanvasToScreen(nodePos);
            var screenSize = nodeSize * Zoom;
            return new Rect(screenPos - screenSize * 0.5f, screenSize);
        }
        #endregion
    }
}
