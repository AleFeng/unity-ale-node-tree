using UnityEngine;

namespace Ale.NodeTree.Editor
{
    /// <summary>
    /// 故事树编辑器画布交互状态。
    /// 维护平移偏移、缩放比例、当前选中节点及拖拽状态，
    /// 并提供画布坐标与屏幕像素坐标之间的互转方法。
    ///
    /// 坐标系约定（Y 轴向上）：
    ///   画布坐标 —— 原点位于左下角，X 向右，Y 向上。
    ///   屏幕坐标 —— IMGUI 原生坐标，原点位于左上角，Y 向下。
    ///   转换公式：
    ///     screenPos.x = canvasPos.x * zoom + panOffset.x
    ///     screenPos.y = canvasHeight - (canvasPos.y * zoom + panOffset.y)
    /// </summary>
    public class NodeTreeCanvasState
    {
        public Vector2 PanOffset = Vector2.zero; // 画布平移偏移（像素），鼠标中键拖拽修改
        public float Zoom = 1.0f;                // 画布缩放比例，由 Alt+滚轮或工具栏按钮修改
        public float CanvasHeight;               // 画布像素高度，由 DrawCanvas 每帧写入，Y 轴翻转转换所需
        public string SelectedNodeId;            // 当前选中节点的 ID，为 null 时无选中
        public bool IsDraggingNode;              // 是否正在拖拽节点
        public Vector2 DragNodeStartPos;         // 拖拽开始时节点的画布坐标（用于计算拖拽增量）
        public Vector2 DragMouseStartPos;        // 拖拽开始时鼠标在画布坐标系中的位置

        public const float MinZoom = 0.2f; // 最小缩放比例
        public const float MaxZoom = 3.0f; // 最大缩放比例

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
    }
}
