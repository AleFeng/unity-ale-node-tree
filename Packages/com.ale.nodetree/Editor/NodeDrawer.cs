using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Ale.NodeTree.Runtime;

namespace Ale.NodeTree.Editor
{
    /// <summary>
    /// 节点编辑器绘制工具（静态类）。
    /// 使用 IMGUI + GL 在编辑器画布上绘制节点形状（圆形/矩形/多边形等）
    /// 以及父子节点之间的连线（直线/贝塞尔曲线/折线）。
    /// 所有 GL 绘制方法必须在 EventType.Repaint 期间调用。
    /// </summary>
    public static class NodeDrawer
    {
        private static readonly Color SelectedBorderColor = new Color(1f, 0.8f,  0f,  1f); // 选中状态边框色（金色高亮）
        private static readonly Color HoveredFillTint     = new Color(1f, 1f,   1f, 0.18f); // 悬停填充叠加白色（Alpha Blend）
        private const float BorderWidth         = 2f;  // 默认外描边宽度（像素）
        private const float SelectedBorderWidth = 3.5f; // 选中状态外描边宽度（像素）
        private const float HoveredBorderWidth  = 3f;  // 悬停状态外描边宽度（像素）

        // ── 节点绘制 ──

        /// <summary>
        /// 在指定矩形内绘制一个节点：形状背景、图标（如有）、标签，以及悬浮提示（如有描述）。
        /// 所有节点均绘制外描边：普通状态颜色为节点颜色 RGB * 0.5（加深色，Alpha 强制不透明）；
        /// 选中状态颜色替换为金色高亮。
        /// type 为 null 时直接跳过。须在 Repaint 事件中调用形状绘制部分。
        /// </summary>
        public static void DrawNode(
            NodeData node,
            NodeTypeData type,
            bool isSelected,
            bool isHovered,
            Rect rect)
        {
            if (type == null) return;

            // 填充色：悬停（且未选中）时叠加白色半透明层，产生提亮效果
            var fillColor = (isHovered && !isSelected)
                ? new Color(
                    Mathf.Clamp01(type.color.r + HoveredFillTint.r * HoveredFillTint.a),
                    Mathf.Clamp01(type.color.g + HoveredFillTint.g * HoveredFillTint.a),
                    Mathf.Clamp01(type.color.b + HoveredFillTint.b * HoveredFillTint.a),
                    type.color.a)
                : type.color;

            // 外描边：选中=金色粗线 | 悬停=节点色向白色 Lerp 50% | 普通=节点色压暗 50%
            Color borderColor;
            float bw;
            if (isSelected)
            {
                borderColor = SelectedBorderColor;
                bw          = SelectedBorderWidth;
            }
            else if (isHovered)
            {
                var hc = Color.Lerp(type.color, Color.white, 0.5f);
                hc.a        = 1f;
                borderColor = hc;
                bw          = HoveredBorderWidth;
            }
            else
            {
                borderColor = new Color(type.color.r * 0.5f, type.color.g * 0.5f, type.color.b * 0.5f, 1f);
                bw          = BorderWidth;
            }

            // 绘制节点形状（当前 Event 必须是 Repaint）
            if (Event.current.type == EventType.Repaint)
            {
                DrawNodeShape(rect, type.shape, fillColor, borderColor, bw);
            }

            // ── 图标（先画 uiIcon，后画 type.icon，保证类型图标始终在上层） ──

            // node.uiIcon：节点实例图标，渲染在节点正中心（底层，不遮盖类型图标）
            if (node.uiIcon)
            {
                float s = Mathf.Min(rect.width, rect.height) * 0.42f;
                var uiIconRect = new Rect(rect.center.x - s * 0.5f, rect.center.y - s * 0.5f, s, s);
                GUI.DrawTextureWithTexCoords(uiIconRect, node.uiIcon.texture,
                    new Rect(node.uiIcon.textureRect.x / node.uiIcon.texture.width,
                             node.uiIcon.textureRect.y / node.uiIcon.texture.height,
                             node.uiIcon.textureRect.width / node.uiIcon.texture.width,
                             node.uiIcon.textureRect.height / node.uiIcon.texture.height));
            }

            // type.icon：节点类型图标，渲染在节点上部中心（上层，覆盖在 uiIcon 之上）
            if (type.icon)
            {
                var typeIconRect = new Rect(rect.x + rect.width * 0.25f, rect.y + rect.height * 0.1f,
                                            rect.width * 0.5f, rect.height * 0.55f);
                GUI.DrawTextureWithTexCoords(typeIconRect, type.icon.texture,
                    new Rect(type.icon.textureRect.x / type.icon.texture.width,
                             type.icon.textureRect.y / type.icon.texture.height,
                             type.icon.textureRect.width / type.icon.texture.width,
                             type.icon.textureRect.height / type.icon.texture.height));
            }

            // 标签：优先用节点类型的 label，否则用 nodeId，再否则显示 "?"
            // 绘制在节点矩形正下方，避免与节点内部图标重叠
            string label = !string.IsNullOrEmpty(type.label) ? type.label
                         : !string.IsNullOrEmpty(node.nodeId) ? node.nodeId
                         : "?";

            var labelStyle = _nodeLabelStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap  = false,
                normal    = { textColor = Color.white }
            };
            // 宽度向两侧各扩 10px，给较长 ID 留出显示空间；Y 偏移 3px 保留视觉间距
            var labelRect = new Rect(rect.x - 10f, rect.yMax + 3f, rect.width + 20f, 16f);
            GUI.Label(labelRect, label, labelStyle);

            // 节点描述（悬浮提示）
            if (!string.IsNullOrEmpty(node.comment))
                GUI.tooltip = node.comment;
        }

        // ── 形状绘制（GL，调用前须在 Repaint 事件中） ──

        /// <summary>
        /// 根据节点形状枚举分派到对应的 GL 绘制方法。
        /// Square 使用 EditorGUI.DrawRect，Circle 使用 GL 椭圆，其余使用多边形顶点列表。
        /// </summary>
        private static void DrawNodeShape(Rect rect, ENodeShape shape, Color fill, Color border, float bw)
        {
            switch (shape)
            {
                case ENodeShape.Square:
                    DrawRect(rect, fill, border, bw);
                    break;
                case ENodeShape.Circle:
                    DrawCircle(rect, fill, border, bw, 32);
                    break;
                case ENodeShape.Diamond:
                    DrawPolygonShape(rect, shape, fill, border, bw);
                    break;
                case ENodeShape.Triangle:
                    DrawPolygonShape(rect, shape, fill, border, bw);
                    break;
                case ENodeShape.HorizontalCapsule:
                    DrawHorizontalCapsule(rect, fill, border, bw);
                    break;
                default:
                    DrawPolygonShape(rect, shape, fill, border, bw);
                    break;
            }
        }

        /// <summary>绘制矩形节点：填充色块 + 四条边框线（使用 EditorGUI.DrawRect）。</summary>
        private static void DrawRect(Rect rect, Color fill, Color border, float bw)
        {
            EditorGUI.DrawRect(rect, fill);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, bw), border);                    // 上边
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - bw, rect.width, bw), border);            // 下边
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, bw, rect.height), border);                   // 左边
            EditorGUI.DrawRect(new Rect(rect.xMax - bw, rect.y, bw, rect.height), border);           // 右边
        }

        /// <summary>
        /// 绘制圆形节点：GL 椭圆填充 + 椭圆边框。
        /// segments 控制多边形近似精度，默认 32 段。
        /// </summary>
        private static void DrawCircle(Rect rect, Color fill, Color border, float bw, int segments)
        {
            Vector2 center = rect.center;
            float rx = rect.width  * 0.5f;
            float ry = rect.height * 0.5f;

            BeginGLClip();
            DrawFilledEllipseGL(center, rx, ry, fill, segments);
            DrawEllipseBorderGL(center, rx, ry, border, bw, segments);
            EndGLClip();
        }

        /// <summary>
        /// 绘制水平胶囊形节点：中间矩形 + 左右两端半圆（GL 椭圆）。
        /// 上下边框使用 EditorGUI.DrawRect，左右端使用 GL 椭圆边框。
        /// </summary>
        private static void DrawHorizontalCapsule(Rect rect, Color fill, Color border, float bw)
        {
            float radius = rect.height * 0.5f; // 左右端半圆的半径
            // 中间矩形 + 两端半圆
            var midRect = new Rect(rect.x + radius, rect.y, rect.width - radius * 2f, rect.height);
            EditorGUI.DrawRect(midRect, fill);

            BeginGLClip();
            DrawFilledEllipseGL(new Vector2(rect.x + radius, rect.center.y), radius, radius, fill, 20);
            DrawFilledEllipseGL(new Vector2(rect.xMax - radius, rect.center.y), radius, radius, fill, 20);
            DrawEllipseBorderGL(new Vector2(rect.x + radius, rect.center.y), radius, radius, border, bw, 20);
            DrawEllipseBorderGL(new Vector2(rect.xMax - radius, rect.center.y), radius, radius, border, bw, 20);
            EndGLClip();

            // 边框直线部分（上下两条）
            EditorGUI.DrawRect(new Rect(rect.x + radius, rect.y, rect.width - radius * 2f, bw), border);
            EditorGUI.DrawRect(new Rect(rect.x + radius, rect.yMax - bw, rect.width - radius * 2f, bw), border);
        }

        /// <summary>
        /// 绘制多边形节点（三角形/菱形/平行四边形/五边形/六边形/八边形/星形）：
        /// 先通过 GetShapePoints 获取顶点，再用 GL 填充与描边。
        /// </summary>
        private static void DrawPolygonShape(Rect rect, ENodeShape shape, Color fill, Color border, float bw)
        {
            var points = GetShapePoints(rect, shape);
            if (points == null || points.Length < 3) return;

            BeginGLClip();
            DrawFilledPolygonGL(points, fill);
            DrawPolygonBorderGL(points, border, bw);
            EndGLClip();
        }

        // ── 多边形顶点定义 ──

        /// <summary>
        /// 根据形状枚举返回对应的屏幕像素顶点数组（顺时针）。
        /// 不支持的形状退化为外接矩形四顶点。
        /// </summary>
        private static Vector2[] GetShapePoints(Rect rect, ENodeShape shape)
        {
            Vector2 c  = rect.center;
            float hw   = rect.width  * 0.5f; // 半宽
            float hh   = rect.height * 0.5f; // 半高

            switch (shape)
            {
                case ENodeShape.Triangle:
                    return new[]
                    {
                        new Vector2(c.x, rect.y),          // 顶点
                        new Vector2(rect.xMax, rect.yMax), // 右下
                        new Vector2(rect.x, rect.yMax)     // 左下
                    };
                case ENodeShape.Diamond:
                    return new[]
                    {
                        new Vector2(c.x, rect.y),    // 上
                        new Vector2(rect.xMax, c.y), // 右
                        new Vector2(c.x, rect.yMax), // 下
                        new Vector2(rect.x, c.y)     // 左
                    };
                case ENodeShape.Parallelogram:
                    float offset = hw * 0.3f; // 平行四边形水平错位量
                    return new[]
                    {
                        new Vector2(rect.x + offset, rect.y),    // 左上
                        new Vector2(rect.xMax, rect.y),          // 右上
                        new Vector2(rect.xMax - offset, rect.yMax), // 右下
                        new Vector2(rect.x, rect.yMax)           // 左下
                    };
                case ENodeShape.Pentagon:
                    return GetRegularPolygon(c, hw, hh, 5, -Mathf.PI * 0.5f); // 顶点朝上
                case ENodeShape.Hexagon:
                    return GetRegularPolygon(c, hw, hh, 6, 0f);
                case ENodeShape.Octagon:
                    return GetRegularPolygon(c, hw, hh, 8, Mathf.PI / 8f);
                case ENodeShape.Star:
                    return GetStarPoints(c, hw, hh, 5); // 五角星
                default:
                    return new[]
                    {
                        new Vector2(rect.x, rect.y),
                        new Vector2(rect.xMax, rect.y),
                        new Vector2(rect.xMax, rect.yMax),
                        new Vector2(rect.x, rect.yMax)
                    };
            }
        }

        /// <summary>
        /// 生成正多边形顶点（以椭圆近似，支持非均匀 rx/ry 比例）。
        /// startAngle 为第一个顶点的起始弧度。
        /// </summary>
        private static Vector2[] GetRegularPolygon(Vector2 center, float rx, float ry, int sides, float startAngle)
        {
            var pts = new Vector2[sides];
            for (int i = 0; i < sides; i++)
            {
                float angle = startAngle + i * Mathf.PI * 2f / sides;
                pts[i] = new Vector2(center.x + Mathf.Cos(angle) * rx,
                                     center.y + Mathf.Sin(angle) * ry);
            }
            return pts;
        }

        /// <summary>
        /// 生成五角星顶点（内外交替，顶点朝上）。
        /// 内径为外径的 0.4 倍，生成 points*2 个顶点。
        /// </summary>
        private static Vector2[] GetStarPoints(Vector2 center, float outerRx, float outerRy, int points)
        {
            float innerRx = outerRx * 0.4f; // 内圆半径（X 轴）
            float innerRy = outerRy * 0.4f; // 内圆半径（Y 轴）
            var pts = new Vector2[points * 2];
            for (int i = 0; i < points * 2; i++)
            {
                float angle = -Mathf.PI * 0.5f + i * Mathf.PI / points;
                float rx    = (i % 2 == 0) ? outerRx : innerRx; // 偶数顶点在外圆，奇数在内圆
                float ry    = (i % 2 == 0) ? outerRy : innerRy;
                pts[i] = new Vector2(center.x + Mathf.Cos(angle) * rx,
                                     center.y + Mathf.Sin(angle) * ry);
            }
            return pts;
        }

        // ── GL 填充/描边辅助 ──

        private static Material _glMat; // GL 绘制所用材质（Hidden/Internal-Colored），延迟初始化
        private static GUIStyle _nodeLabelStyle; // 节点标签样式（缓存，避免每节点每帧新建）
        private static Mesh     _lineMesh;       // 连线绘制复用的 Mesh（Clear+重填，避免每连线每帧 new/Destroy）
        // 编辑器连线绘制复用缓冲（避免每连线每帧 new List / new Color[]）
        private static readonly List<Vector3> _edVerts  = new List<Vector3>(64);
        private static readonly List<Vector2> _edUvs    = new List<Vector2>(64);
        private static readonly List<int>     _edTris   = new List<int>(96);
        private static readonly List<Color>   _edColors = new List<Color>(64);
        private static readonly int SrcBlend = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlend = Shader.PropertyToID("_DstBlend");
        private static readonly int Cull = Shader.PropertyToID("_Cull");
        private static readonly int ZWrite = Shader.PropertyToID("_ZWrite");

        // ── 画布 GL 硬件裁剪（GUI.BeginClip 只裁 IMGUI，GL 会溢出到侧面板，故另设 GL 视口裁剪） ──
        private static bool  _glClipActive;      // 是否启用画布裁剪视口
        private static Rect  _glClipVp;          // 画布裁剪视口（物理像素，底左原点）
        private static Rect  _glFullVp;          // 整窗视口（物理像素，用于绘制后复原）
        private static float _glClipW, _glClipH; // 画布逻辑尺寸（点），供 LoadPixelMatrix 边界使用（勿乘 DPI）

        /// <summary>
        /// 获取（或延迟创建）GL 绘制材质。
        /// 使用 Hidden/Internal-Colored Shader，启用透明混合，关闭深度写入与背面剔除。
        /// </summary>
        private static Material GetGLMat()
        {
            if (!_glMat)
            {
                var shader = Shader.Find("Hidden/Internal-Colored");
                _glMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                _glMat.SetInt(SrcBlend, (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _glMat.SetInt(DstBlend, (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _glMat.SetInt(Cull,     (int)UnityEngine.Rendering.CullMode.Off);
                _glMat.SetInt(ZWrite,   0);
            }
            return _glMat;
        }

        // ── 画布 GL 裁剪：设置 / 清除 / 分段包裹 ──

        /// <summary>
        /// 设置画布 GL 裁剪视口（由编辑器窗口每帧在绘制画布前调用）。
        /// clipVp/fullVp 为物理像素、底左原点（GL 约定）；logicalW/H 为画布逻辑尺寸（点），
        /// 供 <see cref="BeginGLClip"/> 里的 GL.LoadPixelMatrix 使用（勿乘 DPI）。
        /// </summary>
        public static void SetGLClip(Rect clipVp, Rect fullVp, float logicalW, float logicalH)
        {
            _glClipActive = true;
            _glClipVp = clipVp;
            _glFullVp = fullVp;
            _glClipW  = logicalW;
            _glClipH  = logicalH;
        }

        /// <summary>清除画布 GL 裁剪（画布绘制结束后调用），后续 GL 恢复为整窗绘制。</summary>
        public static void ClearGLClip() => _glClipActive = false;

        /// <summary>
        /// 进入一段 GL 绘制：压栈并按裁剪状态设定视口与像素矩阵。
        /// 启用裁剪时用 GL.Viewport 收窄到画布区、并以逻辑尺寸建立像素矩阵，
        /// 使超出画布的几何被 GPU 裁掉；未启用时退化为原 GL.LoadPixelMatrix()。
        /// 必须与 <see cref="EndGLClip"/> 配对（含 PushMatrix）。
        /// </summary>
        private static void BeginGLClip()
        {
            GL.PushMatrix();
            if (_glClipActive)
            {
                GL.Viewport(_glClipVp);
                GL.LoadPixelMatrix(0f, _glClipW, _glClipH, 0f);
            }
            else
            {
                GL.LoadPixelMatrix();
            }
        }

        /// <summary>结束一段 GL 绘制：复原整窗视口并出栈。必须与 <see cref="BeginGLClip"/> 配对。</summary>
        private static void EndGLClip()
        {
            if (_glClipActive) GL.Viewport(_glFullVp);
            GL.PopMatrix();
        }

        /// <summary>
        /// 使用 GL.TRIANGLES 扇形三角化方式填充任意凸多边形。
        /// 须在 GL.PushMatrix / GL.LoadPixelMatrix 环境中调用。
        /// </summary>
        private static void DrawFilledPolygonGL(Vector2[] pts, Color color)
        {
            GetGLMat().SetPass(0);
            GL.Begin(GL.TRIANGLES);
            GL.Color(color);
            // 扇形三角化：以 pts[0] 为扇心
            for (int i = 1; i < pts.Length - 1; i++)
            {
                GL.Vertex3(pts[0].x,     pts[0].y,     0f);
                GL.Vertex3(pts[i].x,     pts[i].y,     0f);
                GL.Vertex3(pts[i + 1].x, pts[i + 1].y, 0f);
            }
            GL.End();
        }

        /// <summary>
        /// 使用 GL.QUADS 绘制多边形轮廓（逐边调用 DrawLineQuad）。
        /// 须在 GL.PushMatrix / GL.LoadPixelMatrix 环境中调用。
        /// </summary>
        private static void DrawPolygonBorderGL(Vector2[] pts, Color color, float width)
        {
            GetGLMat().SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(color);
            for (int i = 0; i < pts.Length; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Length]; // 循环到第一个顶点，闭合轮廓
                DrawLineQuad(a, b, width);
            }
            GL.End();
        }

        /// <summary>
        /// 使用 GL.TRIANGLES 以扇形方式绘制填充椭圆（圆形）。
        ///须在 GL.PushMatrix / GL.LoadPixelMatrix 环境中调用。
        /// </summary>
        private static void DrawFilledEllipseGL(Vector2 center, float rx, float ry, Color color, int segs)
        {
            GetGLMat().SetPass(0);
            GL.Begin(GL.TRIANGLES);
            GL.Color(color);
            for (int i = 0; i < segs; i++)
            {
                float a0 = i       * Mathf.PI * 2f / segs;
                float a1 = (i + 1) * Mathf.PI * 2f / segs;
                GL.Vertex3(center.x, center.y, 0f);
                GL.Vertex3(center.x + Mathf.Cos(a0) * rx, center.y + Mathf.Sin(a0) * ry, 0f);
                GL.Vertex3(center.x + Mathf.Cos(a1) * rx, center.y + Mathf.Sin(a1) * ry, 0f);
            }
            GL.End();
        }

        /// <summary>
        /// 使用 GL.QUADS 绘制椭圆轮廓（逐段调用 DrawLineQuad）。
        /// 须在 GL.PushMatrix / GL.LoadPixelMatrix 环境中调用。
        /// </summary>
        private static void DrawEllipseBorderGL(Vector2 center, float rx, float ry, Color color, float width, int segs)
        {
            GetGLMat().SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(color);
            for (int i = 0; i < segs; i++)
            {
                float a0 = i       * Mathf.PI * 2f / segs;
                float a1 = (i + 1) * Mathf.PI * 2f / segs;
                var p0 = new Vector2(center.x + Mathf.Cos(a0) * rx, center.y + Mathf.Sin(a0) * ry);
                var p1 = new Vector2(center.x + Mathf.Cos(a1) * rx, center.y + Mathf.Sin(a1) * ry);
                DrawLineQuad(p0, p1, width);
            }
            GL.End();
        }

        /// <summary>
        /// 在 GL.QUADS Begin/End 块中向已有线宽四边形追加一条线段（a→b）的四个顶点。
        /// 须在 GL.Begin(GL.QUADS) / GL.End() 之间调用，不应单独调用。
        /// </summary>
        private static void DrawLineQuad(Vector2 a, Vector2 b, float width)
        {
            var dir  = (b - a).normalized;
            var perp = new Vector2(-dir.y, dir.x) * (width * 0.5f); // 垂直于线方向的半宽偏移
            GL.Vertex3(a.x + perp.x, a.y + perp.y, 0f);
            GL.Vertex3(a.x - perp.x, a.y - perp.y, 0f);
            GL.Vertex3(b.x - perp.x, b.y - perp.y, 0f);
            GL.Vertex3(b.x + perp.x, b.y + perp.y, 0f);
        }

        // ── 箭头绘制 ──

        /// <summary>
        /// 在指定位置绘制填充三角形箭头，用于标识连线方向。
        /// tip 为箭头尖端坐标，direction 为归一化方向向量（从父节点指向子节点），
        /// size 为箭头长度（底边宽度 = size * 0.5）。
        /// 方法内含完整的 GL.PushMatrix / GL.PopMatrix，可在 Repaint 阶段独立调用。
        /// </summary>
        public static void DrawArrowhead(Vector2 tip, Vector2 direction, float size, Color color)
        {
            if (direction.sqrMagnitude < 0.001f || size <= 0f) return;

            var perp  = new Vector2(-direction.y, direction.x); // 与 direction 垂直的向量
            var base1 = tip - direction * size + perp * (size * 0.5f); // 左底角
            var base2 = tip - direction * size - perp * (size * 0.5f); // 右底角

            BeginGLClip();
            GetGLMat().SetPass(0);
            GL.Begin(GL.TRIANGLES);
            GL.Color(color);
            GL.Vertex3(tip.x,   tip.y,   0f);
            GL.Vertex3(base1.x, base1.y, 0f);
            GL.Vertex3(base2.x, base2.y, 0f);
            GL.End();
            EndGLClip();
        }

        // ── 箭头辅助计算 ──

        /// <summary>
        /// 根据连线类型和布局方向，计算箭头尖端坐标（arrowTip）和到达方向单位向量（arrowDir）。
        /// 所有坐标为屏幕像素坐标（与 DrawConnection 的 from/to 保持一致）。
        /// arrowTip = to − arrowDir × radius：从子节点中心沿到达方向反向退后 radius，落在节点边缘。
        /// from/to 重合或方向退化时自动回退为直线方向。
        ///
        /// 各线型到达方向推导：
        ///   Straight ── (to − from).normalized
        ///   Curve    ── 贝塞尔终点切线 = (to − cp2).normalized，cp2 与 DrawBezierGL 完全一致
        ///   Polyline ── 最后一段 mid2→to 的方向，mid2 与 DrawPolylineGL 完全一致
        /// </summary>
        public static void GetArrowTipAndDir(
            Vector2 from, Vector2 to,
            LineTypeData lineStyle,
            ELayoutDirection dir,
            float radius,
            out Vector2 arrowTip,
            out Vector2 arrowDir)
        {
            // 退化：两端重合
            if ((to - from).sqrMagnitude < 0.001f)
            {
                arrowDir = Vector2.up;
                arrowTip = to;
                return;
            }

            Vector2 approachDir;

            switch (lineStyle?.lineType ?? ELineType.Straight)
            {
                case ELineType.Curve:
                {
                    // cp2 与修正后的 DrawBezierGL 完全一致（屏幕空间 Y 轴向下）。
                    float dist = Vector2.Distance(from, to) * 0.5f;
                    Vector2 cp2;
                    switch (dir)
                    {
                        case ELayoutDirection.Top2Bottom: cp2 = to + Vector2.down  * dist; break;
                        case ELayoutDirection.Bottom2Top: cp2 = to + Vector2.up    * dist; break;
                        case ELayoutDirection.Left2Right: cp2 = to + Vector2.left  * dist; break;
                        default:                         cp2 = to + Vector2.right * dist; break;
                    }
                    var tangent = to - cp2;
                    approachDir = tangent.sqrMagnitude > 0.001f
                        ? tangent.normalized
                        : (to - from).normalized; // 退化回退
                    break;
                }

                case ELineType.Polyline:
                {
                    // mid2 与 DrawPolylineGL 中的定义完全相同
                    Vector2 mid2;
                    switch (dir)
                    {
                        case ELayoutDirection.Top2Bottom:
                        case ELayoutDirection.Bottom2Top:
                            mid2 = new Vector2(to.x, (from.y + to.y) * 0.5f);
                            break;
                        default:
                            mid2 = new Vector2((from.x + to.x) * 0.5f, to.y);
                            break;
                    }
                    var lastSeg = to - mid2;
                    approachDir = lastSeg.sqrMagnitude > 0.001f
                        ? lastSeg.normalized
                        : (to - from).normalized; // 退化回退（from/to 同行/同列时）
                    break;
                }

                default: // Straight
                    approachDir = (to - from).normalized;
                    break;
            }

            arrowDir = approachDir;
            arrowTip = to - approachDir * radius;
        }

        // ── 连线批量绘制（由编辑器窗口调用）──

        /// <summary>
        /// 遍历所有节点，为每条父→子关系绘制连线。
        /// GL 绘制经画布 GL 视口（见 <see cref="SetGLClip"/>）做硬件裁剪，故此处只用
        /// 包围盒做「完全在外」粗剔除、不改动端点，曲线/折线以真实端点绘制、形状不失真。
        /// 仅在 EventType.Repaint 时执行。
        /// </summary>
        public static void DrawAllLineConnections(
            Rect localBounds,
            NodeTreeData config,
            NodeTreeCanvasState canvas,
            string snapSelFrom, string snapSelTo,
            string snapHovFrom, string snapHovTo)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (!config) return;

            foreach (var node in config.nodes)
            {
                var fromScreen = canvas.CanvasToScreen(node.position);

                foreach (var childId in node.childNodeIds)
                {
                    var child = config.GetNode(childId);
                    if (child == null) continue;

                    // 连线样式归子节点类型（入边样式）：线型/线宽/颜色取子节点类型
                    var childType = config.GetNodeType(child.nodeTypeRef);
                    if (childType == null) continue;

                    var toScreen = canvas.CanvasToScreen(child.position);
                    // 不改端点：仅用包围盒做「完全在外」粗剔除，超出画布部分交由 GL 视口硬件裁剪，
                    // 曲线/折线以真实端点绘制，形状与位置不失真。
                    if (!LineBounds(fromScreen, toScreen, childType.line).Overlaps(localBounds)) continue;

                    bool isSelConn = node.nodeId == snapSelFrom && childId == snapSelTo;
                    bool isHovConn = node.nodeId == snapHovFrom && childId == snapHovTo;
                    Color lineColor = isSelConn
                        ? new Color(1f, 0.85f, 0.1f, 1f)
                        : isHovConn
                            ? childType.color
                            : childType.color * 0.8f;
                    DrawLineConnection(fromScreen, toScreen,
                        childType.line, config.layoutDirection, lineColor, canvas.Zoom);
                }
            }
        }

        /// <summary>
        /// 遍历所有节点，在每条父→子连线终点（子节点边缘）绘制方向箭头。
        /// 须在 DrawAllLineConnections 之后、节点绘制之后调用，以确保箭头始终在最上层。
        /// 仅在 EventType.Repaint 时执行。
        /// </summary>
        public static void DrawConnectionArrows(
            Rect localBounds,
            NodeTreeData config,
            NodeTreeCanvasState canvas)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (!config) return;

            foreach (var node in config.nodes)
            {
                var fromScreen = canvas.CanvasToScreen(node.position);

                foreach (var childId in node.childNodeIds)
                {
                    var child = config.GetNode(childId);
                    if (child == null) continue;

                    // 箭头随连线样式，归子节点类型（入边样式）：颜色/线宽/线型取子节点类型
                    var childType = config.GetNodeType(child.nodeTypeRef);
                    if (childType?.line == null) continue;

                    var toScreen = canvas.CanvasToScreen(child.position);
                    if ((toScreen - fromScreen).sqrMagnitude < 1f) continue;

                    var lineColor = new Color(childType.color.r * 0.8f,
                                              childType.color.g * 0.8f,
                                              childType.color.b * 0.8f, 1f);
                    float arrowSize = Mathf.Max(childType.line.lineWidth * 2.5f * canvas.Zoom, 8f);

                    Vector2 childSz   = childType.resolution;
                    float childRadius = Mathf.Min(childSz.x, childSz.y) * 0.5f * canvas.Zoom;

                    GetArrowTipAndDir(fromScreen, toScreen,
                        childType.line, config.layoutDirection, childRadius,
                        out var arrowTip, out var arrowDir);

                    var arrowBounds = new Rect(arrowTip.x - arrowSize, arrowTip.y - arrowSize,
                                               arrowSize * 2f, arrowSize * 2f);
                    if (!arrowBounds.Overlaps(localBounds)) continue;

                    DrawArrowhead(arrowTip, arrowDir, arrowSize, lineColor);
                }
            }
        }

        /// <summary>
        /// 计算一条连线（含曲线/折线）的保守屏幕包围盒，用于「完全在外」粗剔除（不改动端点）。
        /// 贝塞尔控制点在单轴上至多偏离端点 Distance*0.5、折线拐点落在端点包围盒内，
        /// 故以 pad = Distance*0.5 + lineWidth 扩张端点包围盒即可包住整条线（含线宽）；
        /// 过包含无害——真正的边缘裁切由 GL 视口（<see cref="SetGLClip"/>）完成。
        /// </summary>
        private static Rect LineBounds(Vector2 a, Vector2 b, LineTypeData line)
        {
            float pad  = Vector2.Distance(a, b) * 0.5f + (line?.lineWidth ?? 2f);
            float xmin = Mathf.Min(a.x, b.x) - pad, xmax = Mathf.Max(a.x, b.x) + pad;
            float ymin = Mathf.Min(a.y, b.y) - pad, ymax = Mathf.Max(a.y, b.y) + pad;
            return new Rect(xmin, ymin, xmax - xmin, ymax - ymin);
        }

        /// <summary>
        /// Liang-Barsky 直线段裁剪：将线段 [p0, p1] 裁剪到矩形 rect 内。
        /// 完全在矩形外时返回 false；否则原地修改 p0/p1 为裁剪后端点并返回 true。
        /// 保留为公共工具（连线绘制已改用 GL 视口硬件裁剪，不再调用此法）。
        /// </summary>
        public static bool ClipLineToRect(Rect rect, ref Vector2 p0, ref Vector2 p1)
        {
            float dx = p1.x - p0.x, dy = p1.y - p0.y;
            float t0 = 0f, t1 = 1f;
            if (!LBClip(-dx, p0.x - rect.xMin, ref t0, ref t1)) return false;
            if (!LBClip( dx, rect.xMax - p0.x, ref t0, ref t1)) return false;
            if (!LBClip(-dy, p0.y - rect.yMin, ref t0, ref t1)) return false;
            if (!LBClip( dy, rect.yMax - p0.y, ref t0, ref t1)) return false;
            if (t0 > t1) return false;
            var delta = new Vector2(dx, dy);
            if (t1 < 1f) p1 = p0 + t1 * delta;
            if (t0 > 0f) p0 = p0 + t0 * delta;
            return true;
        }

        /// <summary>Liang-Barsky 单侧裁剪步骤。</summary>
        public static bool LBClip(float num, float denom, ref float t0, ref float t1)
        {
            if (Mathf.Abs(num) < 1e-6f) return denom >= 0f;
            float t = denom / num;
            if (num < 0f) { if (t > t1) return false; if (t > t0) t0 = t; }
            else          { if (t < t0) return false; if (t < t1) t1 = t; }
            return true;
        }

        // ── 编辑器画布连线绘制 ──

        /// <summary>
        /// 在编辑器画布上绘制两节点之间的连线（直线/贝塞尔曲线/折线）。
        /// 通过 <see cref="NodeLineBuilder"/> 构建顶点 Mesh。
        /// lineStyle 为 null 或当前事件不是 Repaint 时直接跳过。
        /// colorOverride 不为 null 时覆盖默认颜色（白色）。
        /// zoom 为画布缩放比例，连线宽度随缩放等比缩放，默认 1。
        ///
        /// ⚠ 坐标系注意：
        /// NodeLineBuilder 遵循 Unity UI 坐标系（Y 轴向上）；
        /// 编辑器 GL 使用屏幕坐标系（Y 轴向下）。
        /// 贝塞尔曲线控制点的竖直分量方向相反，因此传入前需翻转 Top2Bottom ↔ Bottom2Top。
        /// 直线与折线的中点计算与 Y 轴方向无关，无需翻转。
        /// </summary>
        public static void DrawLineConnection(
            Vector2 from, Vector2 to,
            LineTypeData lineStyle,
            ELayoutDirection dir,
            Color? colorOverride = null,
            float zoom = 1f)
        {
            if (lineStyle == null) return;
            if (Event.current.type != EventType.Repaint) return;

            float halfWidth = lineStyle.lineWidth * zoom * 0.5f;

            _edVerts.Clear(); _edUvs.Clear(); _edTris.Clear();
            var verts = _edVerts;
            var uvs   = _edUvs;
            var tris  = _edTris;

            var f3 = new Vector3(from.x, from.y, 0f);
            var t3 = new Vector3(to.x,   to.y,   0f);

            switch (lineStyle.lineType)
            {
                case ELineType.Straight:
                    NodeLineBuilder.AppendStraightSegment(f3, t3, halfWidth, verts, uvs, tris);
                    break;
                case ELineType.Curve:
                    // 屏幕空间 Y 轴向下，与 NodeLineBuilder UI 空间 Y 轴向上相反；翻转竖直方向以保持控制点方向正确
                    NodeLineBuilder.AppendCurveSegment(f3, t3, halfWidth, FlipVerticalDir(dir), verts, uvs, tris);
                    break;
                case ELineType.Polyline:
                    NodeLineBuilder.AppendPolylineSegment(f3, t3, halfWidth, dir, verts, uvs, tris);
                    break;
            }

            if (verts.Count == 0) return;

            // 复用单个缓存 Mesh（Clear + 重填），避免每连线每帧 new/DestroyImmediate 的原生对象开销
            if (!_lineMesh) _lineMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            var mesh = _lineMesh;
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);

            // 颜色写入顶点色（Hidden/Internal-Colored 依据顶点色着色，无需逐实例材质）
            var color = colorOverride ?? Color.white;
            _edColors.Clear();
            for (int i = 0; i < verts.Count; i++) _edColors.Add(color);
            mesh.SetColors(_edColors);

            BeginGLClip();
            GetGLMat().SetPass(0);
            Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
            EndGLClip();
        }

        /// <summary>
        /// 翻转竖直布局方向（Top2Bottom ↔ Bottom2Top），水平方向保持不变。
        /// 用于将 UI 空间（Y 轴向上）的方向参数适配到屏幕空间（Y 轴向下）。
        /// </summary>
        private static ELayoutDirection FlipVerticalDir(ELayoutDirection dir)
        {
            switch (dir)
            {
                case ELayoutDirection.Top2Bottom: return ELayoutDirection.Bottom2Top;
                case ELayoutDirection.Bottom2Top: return ELayoutDirection.Top2Bottom;
                default: return dir;
            }
        }
    }
}
