using System.Collections.Generic;
using UnityEngine;

namespace Ale.NodeTree.Runtime
{
    /// <summary>
    /// 节点树连线 Mesh 构建工具（静态类，Runtime 可用）。
    /// 负责：
    ///  - 将一批父→子线段合并为单个 Mesh（减少 DrawCall）；
    ///  - 提供贝塞尔曲线几何辅助方法（可被 Runtime 和 Editor 共用）。
    ///
    /// UV 约定：
    ///  UV.x = 0/1 对应线宽两侧（用于边缘渐变）；
    ///  UV.y = 累积弧长 / 100f（100 像素 = 1 UV 单位，纹理密度与线长无关）。
    /// </summary>
    public static class NodeLineBuilder
    {
        /// <summary>
        /// 将一批连线段合并为一个 Mesh。
        /// halfWidth 以 UI 像素为单位，与顶点坐标系一致，无需额外换算。
        /// </summary>
        public static Mesh BuildCombinedLineMesh(
            List<(Vector3 from, Vector3 to)> segments,
            LineTypeData lineData,
            ELayoutDirection dir)
        {
            if (lineData == null || segments == null || segments.Count == 0) return null;

            float halfWidth = lineData.lineWidth * 0.5f;

            var vertices  = new List<Vector3>();
            var uvs       = new List<Vector2>();
            var triangles = new List<int>();

            foreach (var (from, to) in segments)
            {
                switch (lineData.lineType)
                {
                    case ELineType.Straight:
                        AppendStraightSegment(from, to, halfWidth, vertices, uvs, triangles);
                        break;
                    case ELineType.Curve:
                        AppendCurveSegment(from, to, halfWidth, dir, vertices, uvs, triangles);
                        break;
                    case ELineType.Polyline:
                        AppendPolylineSegment(from, to, halfWidth, dir, vertices, uvs, triangles);
                        break;
                }
            }

            var mesh = new Mesh();
            mesh.name = "NodeLineMesh";
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ── 各线型追加 ──

        /// <summary>
        /// 追加直线。
        /// UV.x=0/1 对应线宽两侧，UV.y 按像素距离映射（100 像素 = 1 UV 单位）。
        /// </summary>
        public static void AppendStraightSegment(
            Vector3 from, Vector3 to, float halfWidth,
            List<Vector3> verts, List<Vector2> uvs, List<int> tris)
        {
            var dir  = (to - from).normalized;
            var perp = new Vector3(-dir.y, dir.x, 0f) * halfWidth;

            int baseIdx = verts.Count;
            verts.Add(from + perp); // 0: 起点左
            verts.Add(from - perp); // 1: 起点右
            verts.Add(to   - perp); // 2: 终点右
            verts.Add(to   + perp); // 3: 终点左

            float uvLen = Vector3.Distance(from, to) / 100f;
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(1f, 0f));
            uvs.Add(new Vector2(1f, uvLen));
            uvs.Add(new Vector2(0f, uvLen));

            tris.Add(baseIdx + 0); tris.Add(baseIdx + 3); tris.Add(baseIdx + 1);
            tris.Add(baseIdx + 1); tris.Add(baseIdx + 3); tris.Add(baseIdx + 2);
        }

        /// <summary>
        /// 追加曲线（三次贝塞尔 Ribbon Strip）。
        /// 相邻段共享顶点，消除缝隙。UV.y 按弧长映射（100 像素 = 1 UV 单位）。
        /// </summary>
        public static void AppendCurveSegment(
            Vector3 from, Vector3 to, float halfWidth, ELayoutDirection dir,
            List<Vector3> verts, List<Vector2> uvs, List<int> tris)
        {
            var (cp1, cp2) = GetBezierControlPoints(from, to, dir);

            // 根据曲线近似长度动态计算分段数：每 10 像素一段，最少 1 段
            int segments   = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(from, to) / 10f));
            int pointCount = segments + 1;

            // ── 采样曲线上的点 ──
            var points = new Vector3[pointCount];
            for (int i = 0; i <= segments; i++)
                points[i] = EvaluateBezier(from, cp1, cp2, to, i / (float)segments);

            // ── 计算各点累积弧长 ──
            var arcLengths = new float[pointCount];
            arcLengths[0] = 0f;
            for (int i = 1; i < pointCount; i++)
                arcLengths[i] = arcLengths[i - 1] + Vector3.Distance(points[i - 1], points[i]);

            // UV.y：100 像素 = 1 UV 单位，纹理密度与线长无关
            const float kUVPixelScale = 100f;

            // ── 构建 Ribbon Strip ──
            int baseIdx = verts.Count;
            for (int i = 0; i < pointCount; i++)
            {
                Vector3 tangent;
                if (i == 0)
                    tangent = (points[1] - points[0]).normalized;
                else if (i == pointCount - 1)
                    tangent = (points[i] - points[i - 1]).normalized;
                else
                    tangent = (points[i + 1] - points[i - 1]).normalized;

                var perp = new Vector3(-tangent.y, tangent.x, 0f) * halfWidth;
                float v  = arcLengths[i] / kUVPixelScale;

                verts.Add(points[i] + perp);
                verts.Add(points[i] - perp);
                uvs.Add(new Vector2(0f, v));
                uvs.Add(new Vector2(1f, v));
            }

            for (int i = 0; i < segments; i++)
            {
                int b = baseIdx + i * 2;
                tris.Add(b + 0); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
            }
        }

        /// <summary>
        /// 追加折线（三段折线 Ribbon Strip）。
        /// 折角处生成到达行 + 离开行双排顶点，彻底消除缝隙。
        /// UV.y 按弧长映射（100 像素 = 1 UV 单位）。
        /// </summary>
        public static void AppendPolylineSegment(
            Vector3 from, Vector3 to, float halfWidth, ELayoutDirection dir,
            List<Vector3> verts, List<Vector2> uvs, List<int> tris)
        {
            // ── 1. 计算折线路径点 ──
            Vector3 mid1, mid2;
            switch (dir)
            {
                case ELayoutDirection.Top2Bottom:
                case ELayoutDirection.Bottom2Top:
                    float midY = (from.y + to.y) * 0.5f;
                    mid1 = new Vector3(from.x, midY, from.z);
                    mid2 = new Vector3(to.x,   midY, to.z);
                    break;
                default:
                    float midX = (from.x + to.x) * 0.5f;
                    mid1 = new Vector3(midX, from.y, from.z);
                    mid2 = new Vector3(midX, to.y,   to.z);
                    break;
            }

            // ── 2. 去除连续重合点 ──
            const float kMinSegLen = 0.01f;
            var rawPath = new[] { from, mid1, mid2, to };
            var path = new List<Vector3>(4);
            foreach (var pt in rawPath)
            {
                if (path.Count == 0 ||
                    Vector3.SqrMagnitude(pt - path[path.Count - 1]) > kMinSegLen * kMinSegLen)
                    path.Add(pt);
            }
            if (path.Count < 2) return;
            if (path.Count == 2)
            {
                AppendStraightSegment(path[0], path[1], halfWidth, verts, uvs, tris);
                return;
            }

            // ── 3. 计算各路径点的累积弧长 ──
            int n = path.Count;
            var arcLen = new float[n];
            arcLen[0] = 0f;
            for (int i = 1; i < n; i++)
                arcLen[i] = arcLen[i - 1] + Vector3.Distance(path[i - 1], path[i]);

            // UV.y：100 像素 = 1 UV 单位
            const float kUVPixelScale = 100f;

            // ── 4. 预计算每段法线偏移 ──
            var segPerp = new Vector3[n - 1];
            for (int i = 0; i < n - 1; i++)
            {
                var d = (path[i + 1] - path[i]).normalized;
                segPerp[i] = new Vector3(-d.y, d.x, 0f) * halfWidth;
            }

            // ── 5. 构建完整 Ribbon Strip ──
            int baseIdx = verts.Count;

            // 首行
            {
                float v = arcLen[0] / kUVPixelScale;
                var   p = segPerp[0];
                verts.Add(path[0] + p); verts.Add(path[0] - p);
                uvs.Add(new Vector2(0f, v)); uvs.Add(new Vector2(1f, v));
            }
            // 中间各点：到达行 + 离开行
            for (int i = 1; i < n - 1; i++)
            {
                float v  = arcLen[i] / kUVPixelScale;
                var   p0 = segPerp[i - 1];
                var   p1 = segPerp[i];

                verts.Add(path[i] + p0); verts.Add(path[i] - p0);
                uvs.Add(new Vector2(0f, v)); uvs.Add(new Vector2(1f, v));

                verts.Add(path[i] + p1); verts.Add(path[i] - p1);
                uvs.Add(new Vector2(0f, v)); uvs.Add(new Vector2(1f, v));
            }
            // 尾行
            {
                float v = arcLen[n - 1] / kUVPixelScale;
                var   p = segPerp[n - 2];
                verts.Add(path[n - 1] + p); verts.Add(path[n - 1] - p);
                uvs.Add(new Vector2(0f, v)); uvs.Add(new Vector2(1f, v));
            }

            // ── 6. 生成三角形 ──
            int rowCount = 2 * (n - 1);
            for (int r = 0; r < rowCount - 1; r++)
            {
                int b = baseIdx + r * 2;
                tris.Add(b + 0); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
            }
        }

        // ── 贝塞尔几何辅助 ──

        /// <summary>
        /// 根据布局方向计算三次贝塞尔控制点。
        /// 坐标系使用 Unity UI 局部空间（Y 轴向上）。
        /// 控制点偏移距离为 from→to 距离的一半。
        /// </summary>
        public static (Vector3 cp1, Vector3 cp2) GetBezierControlPoints(
            Vector3 from, Vector3 to, ELayoutDirection dir)
        {
            float dist = Vector3.Distance(from, to) * 0.5f;
            switch (dir)
            {
                case ELayoutDirection.Top2Bottom:
                    return (from + Vector3.down  * dist, to + Vector3.up    * dist);
                case ELayoutDirection.Bottom2Top:
                    return (from + Vector3.up    * dist, to + Vector3.down  * dist);
                case ELayoutDirection.Left2Right:
                    return (from + Vector3.right * dist, to + Vector3.left  * dist);
                default: // Right2Left
                    return (from + Vector3.left  * dist, to + Vector3.right * dist);
            }
        }

        /// <summary>求三次贝塞尔曲线在参数 t 处的点。</summary>
        public static Vector3 EvaluateBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1f - t;
            return u * u * u * p0
                 + 3f * u * u * t * p1
                 + 3f * u * t * t * p2
                 + t  * t  * t     * p3;
        }
    }
}

