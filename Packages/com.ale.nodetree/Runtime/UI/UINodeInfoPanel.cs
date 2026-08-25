using Ale.Toolkit.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ale.NodeTree.Runtime
{
    /// <summary>
    /// 节点信息弹窗（MonoBehaviour + IPoolable）。挂在独立的弹窗预制体上，
    /// 由 <see cref="UINodeTreeWindow"/> 通过对象池统一取用、定位与回收 —— 节点自身不再持有弹窗。
    ///
    /// <para><b>职责只有三件</b>：持有内容接线、淡入淡出、汇报自己能否被回收
    /// （<see cref="IsRecyclable"/>）。<b>定位完全不在本类</b>：弹窗摆在哪由窗口决定 ——
    /// 只有窗口知道节点在画布上的坐标，偏移量也配在窗口的 <c>infoPanelOffset</c> 上。</para>
    ///
    /// <para><b>为什么弹窗要从节点里搬出来</b>：作为节点的子物体时，弹窗的渲染顺序跟着节点在
    /// NodeContainer 里的兄弟顺序走，而节点是按视口裁剪动态 Spawn 的、顺序不可控 ——
    /// 后 Spawn 的节点会盖住先 Spawn 节点的弹窗；且弹窗会被 Viewport 的 Mask 裁掉，
    /// 靠近视口边缘时被切一半。搬到 ScrollRect 之外的专用层后这两个问题一并消失。</para>
    /// </summary>
    public class UINodeInfoPanel : MonoBehaviour, IPoolable
    {
        #region 配置
        [Header("淡入淡出")]
        [Tooltip("控制淡入淡出（alpha）的 CanvasGroup。为空时自动取本物体上的；仍取不到则弹窗不可见但逻辑正常运转。")]
        [SerializeField] protected CanvasGroup canvasGroup;
        [Tooltip("弹窗淡入时长（秒），默认 0.2。")]
        [SerializeField] protected float fadeInDuration  = 0.2f;
        [Tooltip("弹窗淡出时长（秒），默认 0.3。")]
        [SerializeField] protected float fadeOutDuration = 0.3f;

        [Header("内容（可选接线）")]
        [Tooltip("节点名称文本，绑定时用 NodeData.nodeName.ResolveText() 填充；不接线则跳过。")]
        [SerializeField] protected TMP_Text nodeNameText;
        [Tooltip("节点描述文本，绑定时用 NodeData.nodeDesc.ResolveText() 填充；不接线则跳过。")]
        [SerializeField] protected TMP_Text nodeDescText;
        [Tooltip("节点图标，绑定时取 NodeData.uiIcon 并按有无图标切换 enabled；不接线则跳过。")]
        [SerializeField] protected Image iconImage;
        #endregion

        #region 状态
        /// <summary>当前绑定的节点实例数据，未绑定时为 null。</summary>
        public NodeData BoundNode { get; private set; }

        /// <summary>当前绑定的节点类型定义，未绑定时为 null。</summary>
        public NodeTypeData BoundType { get; private set; }

        /// <summary>本弹窗的 RectTransform（Awake 缓存）。挂在非 UI 物体上时为 null。</summary>
        public RectTransform Rect { get; private set; }

        /// <summary>
        /// 当前是否处于「应当显示」的状态。这是<b>期望态</b>而非「补间是否在途」——
        /// 淡出途中它已经是 false。
        /// </summary>
        public bool IsVisible { get; private set; }

        /// <summary>
        /// 是否可以归还对象池：已请求隐藏，且淡出补间已经跑完。
        /// <para>窗口按帧轮询本属性来回收，而<b>不</b>在补间的完成回调里回收 ——
        /// 完成回调在「时长 ≤ 0」与「Kill(true)」两条路径上是<b>同步</b>触发的，
        /// 若在其中改动窗口的激活弹窗集合，就会在遍历中途改集合。轮询模型下集合只在一处被改。</para>
        /// </summary>
        public bool IsRecyclable => !IsVisible && !_fade.IsActive;

        // 淡入淡出句柄（基于 toolkit 中央 Tween），用于打断上一次动画。
        private ToolkitTweenHandle _fade;
        #endregion

        #region 生命周期
        protected virtual void Awake()
        {
            Rect = transform as RectTransform;
            if (!canvasGroup) canvasGroup = GetComponent<CanvasGroup>();

            // 恒不阻挡射线。旧实现（弹窗是节点的子物体）淡入时置 blocksRaycasts = true 是安全的：
            // uGUI 沿层级向公共祖先派发，指针落在弹窗上不会触发节点的 PointerExit。
            // 拆出去之后弹窗与节点分属两棵子树，一旦挡住光标就会 PointerExit → 隐藏 → 光标回到节点
            // → PointerEnter → 显示，形成肉眼可见的闪烁死循环。
            if (canvasGroup) canvasGroup.blocksRaycasts = false;

            ResetPanel();
        }

        /// <summary>
        /// 本物体停用时强制复位。
        /// <para>必要性：toolkit 的补间<b>不随 GameObject 停用而停止</b>，而对象池归还只是
        /// SetActive(false)、不做任何视觉复位 —— 不复位会让在途的淡入一路跑到全亮，
        /// 下次从池里取出来就是一个莫名其妙亮着的弹窗。</para>
        /// <para>子类重写务必调用 <c>base.OnDisable()</c>。声明为 virtual 是因为 Unity 只调用最派生的
        /// 同名方法，子类若自行写 <c>private void OnDisable()</c> 会静默吞掉本实现（有 virtual 至少会触发 CS0114）。</para>
        /// </summary>
        protected virtual void OnDisable() => ResetPanel();
        #endregion

        #region 内容绑定
        /// <summary>
        /// 绑定节点数据并填充内容。由 <see cref="UINodeTreeWindow"/> 在显示弹窗时调用。
        /// <para>基类实现填三个可选控件（名称 / 描述 / 图标），<b>未接线的一律跳过</b> ——
        /// Demo 的弹窗文本由 Unity Localization 的 LocalizeStringEvent 驱动，三个都不接线。</para>
        /// <para>子类可重写以填充自定义内容（属性列表、解锁条件、进度条等），
        /// 建议调用 <c>base.Bind(node, type)</c> 以保留基类的三项填充。</para>
        /// </summary>
        public virtual void Bind(NodeData node, NodeTypeData type)
        {
            BoundNode = node;
            BoundType = type;
            if (node == null) return;

            // 名称 / 描述：用 AttributeValue.Text 解析（启用 toolkit 本地化时取多语文本，否则纯文本 fallback）
            if (nodeNameText)
                nodeNameText.text = node.nodeName != null ? node.nodeName.ResolveText() : string.Empty;
            if (nodeDescText)
                nodeDescText.text = node.nodeDesc != null ? node.nodeDesc.ResolveText() : string.Empty;

            if (iconImage)
            {
                bool hasIcon = node.uiIcon;
                iconImage.enabled = hasIcon;
                iconImage.sprite  = hasIcon ? node.uiIcon : null;
            }
        }

        /// <summary>
        /// 解绑并清空引用。由 <see cref="OnDespawn"/> 自动调用。
        /// 子类可重写以释放自己的句柄（如 Addressable 图标），建议调用 <c>base.Unbind()</c>。
        /// </summary>
        public virtual void Unbind()
        {
            BoundNode = null;
            BoundType = null;
        }
        #endregion

        #region 显示与隐藏
        /// <summary>
        /// 显示弹窗（淡入）。同态重复调用直接忽略。
        /// <para>设计约定：<b>状态在本方法维护，表现交给钩子</b> ——
        /// 子类只需重写 <see cref="OnShowBegin"/> / <see cref="OnHideBegin"/>。
        /// 本方法与 <see cref="Hide"/> 刻意<b>不是</b> virtual：<see cref="IsVisible"/> 是窗口回收弹窗的
        /// 唯一依据，子类重写时漏调 base 会让弹窗永不回收（对象池泄漏）。</para>
        /// </summary>
        /// <param name="instant">true 时跳过补间、瞬间到位。</param>
        public void Show(bool instant = false)
        {
            if (IsVisible) return;
            IsVisible = true;
            OnShowBegin(instant);
        }

        /// <summary>隐藏弹窗（淡出）。同态重复调用直接忽略。语义同 <see cref="Show"/>。</summary>
        /// <param name="instant">true 时跳过补间、瞬间到位。</param>
        public void Hide(bool instant = false)
        {
            if (!IsVisible) return;
            IsVisible = false;
            OnHideBegin(instant);
        }

        /// <summary>
        /// 淡入表现。基类实现：把 <see cref="canvasGroup"/> 的 alpha 补间到 1。
        /// <para>子类可重写以实现自定义出现效果（缩放弹出、位移、描边扫光等）。</para>
        /// <para><b>务必尊重 <paramref name="instant"/></b>：为 true 时必须瞬间到位、不得留下在途补间 ——
        /// 该路径用于对象池归还与停用时的复位，而补间不随 GameObject 停用而停止，
        /// 残留的补间会渗进下一次复用。</para>
        /// </summary>
        /// <param name="instant">true = 瞬间到位，不播放补间。</param>
        protected virtual void OnShowBegin(bool instant)
        {
            if (!canvasGroup) return;

            _fade.Kill();
            _fade = ToolkitTween.FadeCanvasGroup(
                canvasGroup, 1f, instant ? 0f : fadeInDuration);
        }

        /// <summary>
        /// 淡出表现。基类实现：把 <see cref="canvasGroup"/> 的 alpha 补间到 0。
        /// 子类重写时的注意事项同 <see cref="OnShowBegin"/>。
        /// </summary>
        /// <param name="instant">true = 瞬间到位，不播放补间。</param>
        protected virtual void OnHideBegin(bool instant)
        {
            if (!canvasGroup) return;

            _fade.Kill();
            _fade = ToolkitTween.FadeCanvasGroup(
                canvasGroup, 0f, instant ? 0f : fadeOutDuration, EToolkitEase.InQuad);
        }

        /// <summary>
        /// 强制复位：打断补间并瞬间还原到隐藏态。
        /// 不经 <see cref="Hide"/> —— 首次从预制体实例化出来时 alpha 取自序列化值、
        /// 而 <see cref="IsVisible"/> 本就是 false，走幂等判断会被直接跳过。
        /// <para>补间的 Kill 收在本方法内部而不是各调用点：本方法有
        /// Awake / OnDisable / OnSpawn / OnDespawn 四个调用点，逐处手写易漏。</para>
        /// </summary>
        private void ResetPanel()
        {
            _fade.Kill();
            if (canvasGroup) canvasGroup.alpha = 0f;
            IsVisible = false;
        }
        #endregion

        #region 对象池
        /// <summary>对象从池中取出时调用（ToolkitPool 回调）：复位为隐藏态，等待窗口 Bind + Show。</summary>
        public void OnSpawn() => ResetPanel();

        /// <summary>对象归还到池中时调用（ToolkitPool 回调）：解绑数据并复位视觉状态。</summary>
        public void OnDespawn()
        {
            Unbind();
            ResetPanel();
        }
        #endregion
    }
}
