using UnityEngine;
using UnityEngine.UI;

namespace Ale.NodeTree.Runtime
{
    /// <summary>
    /// 视口尺寸来源。
    /// </summary>
    public enum EViewportMode
    {
        /// <summary>本组件 RectTransform 的 rect 尺寸。</summary>
        SelfRect = 0,

        /// <summary>屏幕尺寸（按根 Canvas.scaleFactor 换算为画布单位）。</summary>
        Screen = 1,
    }

    /// <summary>
    /// 四方连续无限滚动背景组件（RawImage uvRect UV 滚动）。
    /// 负责：
    ///  - 以 RawImage 的初始 Rect 尺寸为一块 tile，铺满视口（uvRect.size = 视口/tile）；
    ///    滚动时仅偏移 uvRect，靠纹理 Repeat 采样实现四方连续无限平铺——
    ///    单物体单 DrawCall、零 tile 实例、零逐帧分配；
    ///  - 可选绑定 ScrollRect：LateUpdate 轮询 Content.anchoredPosition 增量，
    ///    与 Content 同向滚动，speedMultiplier 控制倍速（≠1 时形成视差）；
    ///  - 未绑定时可经 ScrollBy / SetScrollOffset 等公开 API 手动驱动。
    /// 注意：
    ///  - 纹理导入的 Wrap Mode 必须为 Repeat（Awake 会检测并告警）；
    ///  - 本组件与 ScrollRect 的 LateUpdate 先后顺序未定，背景最多滞后 1 帧（不可感知）。
    /// </summary>
    public class UIScrollingBackground : MonoBehaviour
    {
        [Header("绑定")]
        [Tooltip("要跟随的 ScrollRect（可选）。\n" +
                 "为空时不自动跟随，可用 ScrollBy / SetScrollOffset 手动驱动。")]
        [SerializeField] private ScrollRect scrollRect;

        [Tooltip("平铺显示用的 RawImage。为空时自动在自身及子物体（含未激活）上查找。\n" +
                 "其初始 Rect 尺寸即为一块 tile 的尺寸（≤0 时回退纹理像素尺寸）。")]
        [SerializeField] private RawImage image;

        [Header("视口")]
        [Tooltip("视口尺寸来源：\n" +
                 "SelfRect = 本组件 RectTransform 的 rect 尺寸；\n" +
                 "Screen = 屏幕尺寸（按根 Canvas.scaleFactor 换算为画布单位）。")]
        [SerializeField] private EViewportMode viewportMode = EViewportMode.SelfRect;

        [Header("滚动")]
        [Tooltip("滚动速度倍率：1 = 与 Content 同速；<1 远景视差；>1 近景视差；0 = 静止；负值反向。\n" +
                 "仅作用于 ScrollRect 跟随路径，手动 ScrollBy 不乘倍率。")]
        [SerializeField] private float speedMultiplier = 1f;

        // tile 尺寸（画布单位）：必须在任何 Refit 改动 RawImage 尺寸之前捕获
        private Vector2 _tileSize;
        private bool    _tileSizeCaptured;

        // 累计视觉偏移 / tile（各分量回绕于 [0,1)，纯浮点精度保护，长时间运行不漂移）
        private Vector2 _offsetUv;
        // uvRect 尺寸 = 视口 / tile（Refit 时重算）
        private Vector2 _uvSize = Vector2.one;

        // ScrollRect 跟随快照：首帧 / 重绑定 / 禁用后启用时 _hasLastContentPos 为 false → 先快照不产生跳变
        private RectTransform _lastContent;
        private Vector2       _lastContentPos;
        private bool          _hasLastContentPos;

        private Canvas  _canvas;       // 根 Canvas 缓存（Screen 模式换算 scaleFactor）
        private Vector2 _lastViewport; // Screen 模式轮询用
        private bool    _inRefit;      // OnRectTransformDimensionsChange 防递归

        #region 公开接口

        /// <summary>滚动速度倍率（仅作用于 ScrollRect 跟随路径；1 同速、≠1 视差、负值反向）。</summary>
        public float SpeedMultiplier
        {
            get => speedMultiplier;
            set => speedMultiplier = value;
        }

        /// <summary>视口尺寸来源；切换后立即重新适配。</summary>
        public EViewportMode ViewportMode
        {
            get => viewportMode;
            set
            {
                if (viewportMode == value) return;
                viewportMode = value;
                if (isActiveAndEnabled) Refit();
            }
        }

        /// <summary>一块 tile 的尺寸（画布单位）。</summary>
        public Vector2 TileSize => _tileSize;

        /// <summary>当前视觉偏移（画布单位，按 tile 周期回绕于 [0, tile)）。</summary>
        public Vector2 ScrollOffset => Vector2.Scale(_offsetUv, _tileSize);

        /// <summary>
        /// 手动滚动增量（画布单位，方向语义与 Content 位移一致：正 x 向右、正 y 向上）。
        /// 不乘 speedMultiplier——手动驱动为直接控制，视差由调用方自行换算。
        /// </summary>
        public void ScrollBy(Vector2 delta)
        {
            ApplyVisualDelta(delta);
        }

        /// <summary>设置绝对视觉偏移（画布单位，内部按 tile 周期回绕）。</summary>
        public void SetScrollOffset(Vector2 offset)
        {
            CaptureTileSize();
            if (!_tileSizeCaptured) return;
            _offsetUv = new Vector2(
                Mathf.Repeat(offset.x / _tileSize.x, 1f),
                Mathf.Repeat(offset.y / _tileSize.y, 1f));
            ApplyUvRect();
        }

        /// <summary>清零滚动偏移。</summary>
        public void ResetOffset()
        {
            _offsetUv = Vector2.zero;
            ApplyUvRect();
        }

        /// <summary>
        /// 运行时重绑定 ScrollRect（传 null 即解绑）。
        /// 下一帧起从新 Content 的当前位置开始跟随，不产生跳变。
        /// </summary>
        public void SetScrollRect(ScrollRect target)
        {
            scrollRect = target;
            _hasLastContentPos = false;
        }

        /// <summary>
        /// 重新适配视口：按视口模式调整 RawImage 的锚点/尺寸，并重算 uvRect 尺寸。
        /// SelfRect 尺寸变化与 Screen 分辨率变化会自动触发；业务改动布局后也可手动调用。
        /// </summary>
        public void Refit()
        {
            if (!image) return;
            CaptureTileSize();
            if (!_tileSizeCaptured) return;

            _inRefit = true;
            try
            {
                Vector2 viewport = CalcViewportSize();
                RectTransform imgRect = image.rectTransform;

                if (viewportMode == EViewportMode.SelfRect)
                {
                    // image 为子物体：拉伸铺满本组件（此后自动跟随自身尺寸变化）；
                    // image 在本物体上：自身 rect 即视口，不写 RectTransform。
                    if (image.gameObject != gameObject)
                    {
                        if (imgRect.anchorMin != Vector2.zero) imgRect.anchorMin = Vector2.zero;
                        if (imgRect.anchorMax != Vector2.one)  imgRect.anchorMax = Vector2.one;
                        if (imgRect.offsetMin != Vector2.zero) imgRect.offsetMin = Vector2.zero;
                        if (imgRect.offsetMax != Vector2.zero) imgRect.offsetMax = Vector2.zero;
                    }
                }
                else
                {
                    // Screen：中心锚点 + sizeDelta = 视口（仅在不同才写），不动 anchoredPosition
                    Vector2 half = new Vector2(0.5f, 0.5f);
                    if (imgRect.anchorMin != half)     imgRect.anchorMin = half;
                    if (imgRect.anchorMax != half)     imgRect.anchorMax = half;
                    if (imgRect.sizeDelta != viewport) imgRect.sizeDelta = viewport;
                }

                if (viewport.x > 0f && viewport.y > 0f)
                    _uvSize = new Vector2(viewport.x / _tileSize.x, viewport.y / _tileSize.y);
                _lastViewport = viewport;
                ApplyUvRect();
            }
            finally
            {
                _inRefit = false;
            }
        }

        #endregion

        // ── Unity 生命周期 ──

        private void Awake()
        {
            if (!image) image = GetComponentInChildren<RawImage>(true);
            CaptureTileSize();
            var parentCanvas = GetComponentInParent<Canvas>();
            _canvas = parentCanvas ? parentCanvas.rootCanvas : null;

            if (image && image.texture && image.texture.wrapMode != TextureWrapMode.Repeat)
                Debug.LogWarning(
                    $"[UIScrollingBackground] 纹理 {image.texture.name} 的 Wrap Mode 不是 Repeat，" +
                    "四方连续滚动会在 tile 边缘出现拉伸/截断，请在纹理导入设置中改为 Repeat。", this);
        }

        private void OnEnable()
        {
            // 禁用期间的 Content 位移不回放：启用后先快照再跟随，避免瞬间跳变
            _hasLastContentPos = false;
            Refit();
        }

        private void LateUpdate()
        {
            if (!image) return;

            // Screen 模式轮询视口（分辨率 / Game 视图尺寸 / scaleFactor 变化时重新适配）
            if (viewportMode == EViewportMode.Screen && CalcViewportSize() != _lastViewport)
                Refit();

            // 未绑定或绑定对象运行时被销毁：静默停止跟随，手动 API 仍可用
            if (!scrollRect) return;
            RectTransform content = scrollRect.content;
            if (!content)
            {
                _hasLastContentPos = false;
                return;
            }

            Vector2 pos = content.anchoredPosition;
            if (!_hasLastContentPos || content != _lastContent)
            {
                // 首帧 / 重绑定 / Content 实例被换：只快照，不回放差值（防跳变）
                _lastContent       = content;
                _lastContentPos    = pos;
                _hasLastContentPos = true;
                return;
            }
            if (pos != _lastContentPos)
            {
                ApplyVisualDelta((pos - _lastContentPos) * speedMultiplier);
                _lastContentPos = pos;
            }
        }

        private void OnRectTransformDimensionsChange()
        {
            // Screen 模式改自身 sizeDelta 由模式判断挡住；_inRefit 兜底防递归。
            // 实例化早期本回调可能先于 Awake 触发，image/_tileSizeCaptured 守护该情况。
            if (_inRefit || viewportMode != EViewportMode.SelfRect) return;
            if (!isActiveAndEnabled || !image || !_tileSizeCaptured) return;
            Refit();
        }

        #region 内部实现

        /// <summary>
        /// 捕获 tile 尺寸（仅一次）：取 RawImage 初始 Rect 尺寸；
        /// ≤0（未布局/零尺寸）时回退纹理像素尺寸。
        /// </summary>
        private void CaptureTileSize()
        {
            if (_tileSizeCaptured || !image) return;
            Vector2 size = image.rectTransform.rect.size;
            if (size.x <= 0f || size.y <= 0f)
            {
                var tex = image.texture;
                if (tex) size = new Vector2(tex.width, tex.height);
            }
            if (size.x > 0f && size.y > 0f)
            {
                _tileSize         = size;
                _tileSizeCaptured = true;
            }
        }

        /// <summary>按视口模式计算视口尺寸（画布单位）。</summary>
        private Vector2 CalcViewportSize()
        {
            if (viewportMode == EViewportMode.Screen)
            {
                float scale = _canvas ? _canvas.scaleFactor : 1f;
                if (scale <= 0f) scale = 1f;
                return new Vector2(Screen.width / scale, Screen.height / scale);
            }
            var self = transform as RectTransform;
            if (self) return self.rect.size;
            return image ? image.rectTransform.rect.size : Vector2.zero;
        }

        /// <summary>累加一段视觉位移（画布单位）并应用到 uvRect。</summary>
        private void ApplyVisualDelta(Vector2 deltaCanvasUnits)
        {
            CaptureTileSize();
            if (!_tileSizeCaptured || deltaCanvasUnits == Vector2.zero) return;
            _offsetUv.x = Mathf.Repeat(_offsetUv.x + deltaCanvasUnits.x / _tileSize.x, 1f);
            _offsetUv.y = Mathf.Repeat(_offsetUv.y + deltaCanvasUnits.y / _tileSize.y, 1f);
            ApplyUvRect();
        }

        /// <summary>把当前偏移与尺寸写入 RawImage.uvRect（值未变化时零开销）。</summary>
        private void ApplyUvRect()
        {
            if (!image) return;
            // 视觉偏移取负映射为 uv 偏移：uvRect.x 增大 → 采样窗口右移 → 画面左移。
            // 若引擎内实测方向相反，翻转此处两个符号即可。
            var r = new Rect(
                Mathf.Repeat(-_offsetUv.x, 1f),
                Mathf.Repeat(-_offsetUv.y, 1f),
                _uvSize.x, _uvSize.y);
            if (r != image.uvRect) image.uvRect = r;
        }

        #endregion
    }
}
