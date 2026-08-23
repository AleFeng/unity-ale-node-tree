using System;
using Ale.Toolkit.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ale.NodeTree.Runtime
{
    /// <summary>
    /// 节点UI基类（MonoBehaviour + IPoolable）。
    /// 通过 ToolkitPool 管理生命周期，子类重写各虚方法实现具体UI逻辑。
    /// 功能：节点图标显示 / 节点名称·描述文本（toolkit AttributeValue.Text，本地化优先+纯文本回退）/
    ///        鼠标悬停弹窗淡入淡出 + 悬停高亮（均基于 toolkit 中央 Tween）/ 鼠标点击回调。
    /// </summary>
    public class UINodeBase : MonoBehaviour, IPoolable,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        #region 基础设置
        [Header("节点图标")]
        [Tooltip("显示节点图标的 Image 组件，绑定时自动将 Sprite 设置为 NodeData.uiIcon。")]
        [SerializeField] protected Image iconImage;

        [Header("节点文本")]
        [Tooltip("节点名称文本组件，绑定时用 NodeData.nodeName.ResolveText() 填充；为空则跳过。")]
        [SerializeField] protected TMP_Text nodeNameText;
        [Tooltip("节点描述文本组件，绑定时用 NodeData.nodeDesc.ResolveText() 填充；为空则跳过。")]
        [SerializeField] protected TMP_Text nodeDescText;

        /// <summary>当前绑定的节点实例数据，未绑定时为 null。</summary>
        public NodeData nodeData;
        /// <summary>当前绑定的节点类型定义，未绑定时为 null。</summary>
        public NodeTypeData nodeType;
        
        /// <summary>
        /// 绑定节点数据，由 UINodeTreeWindow 在生成节点UI时调用。
        /// 自动完成：节点图标赋值、名称/描述文本解析（AttributeValue.ResolveText）、弹窗初始化为隐藏状态。
        /// 子类可重写以执行额外初始化（如状态色块、完成标记、解锁动画等）。
        /// </summary>
        public virtual void OnBindData(NodeData data, NodeTypeData type)
        {
            // 绑定节点数据和类型
            nodeData = data;
            nodeType = type;

            if (data != null)
            {
                // 节点图标：将 Image.sprite 设置为 NodeData.uiIcon
                // iconImage 未在预制体上赋值时整段跳过，避免原 else 分支解引用 null 抛 NRE。
                if (iconImage)
                {
                    bool hasIcon = data.uiIcon;
                    iconImage.enabled = hasIcon;
                    iconImage.sprite  = hasIcon ? data.uiIcon : null;
                }

                // 节点名称 / 描述：用 AttributeValue.Text 解析（启用 toolkit 本地化时取多语文本，否则纯文本 fallback）
                if (nodeNameText)
                    nodeNameText.text = data.nodeName != null ? data.nodeName.ResolveText() : string.Empty;
                if (nodeDescText)
                    nodeDescText.text = data.nodeDesc != null ? data.nodeDesc.ResolveText() : string.Empty;
            }
            
            // 重置 信息弹窗与高亮：首次从预制体实例化出来时，二者的视觉状态取自序列化值，
            // 没有任何其它代码会把它们归零。
            ResetInfoPanel();
            ResetHighlight();
        }

        /// <summary>
        /// 解绑节点数据，清空引用。由 OnDespawn 自动调用，也可手动调用。
        /// 子类可重写以清理UI状态（如停止协程、重置动画等），建议调用 base.OnUnbindData()。
        /// </summary>
        public virtual void OnUnbindData()
        {
            nodeData = null;
            nodeType = null;

            // 清空外部订阅：对象池复用同一实例，若不清空，旧订阅者会在该实例复用为其它节点后继续被触发，
            // 且其捕获的引用会被一直保活（内存泄漏）。
            Clicked = null;

            // 复位选中态：节点可能在被选中时 despawn，归还前调用一次还原钩子，避免复用后残留高亮。
            OnNodeDeselected();

            // 重置 信息弹窗与高亮（二者内部各自打断在途补间）。
            // 补间不随 GameObject 停用而停止，对象池又不做任何视觉复位，故必须在此显式还原。
            ResetInfoPanel();
            ResetHighlight();
        }

        /// <summary>
        /// 本物体停用时复位弹窗与高亮。
        /// <para>必要性：面板整体 SetActive(false) 关闭时节点<b>不走</b>对象池归还，且悬停中的节点
        /// 收不到 PointerExit（Unity 不向非激活对象派发），而 toolkit 的补间<b>不随 GameObject 停用而停止</b>
        /// —— 不复位会让弹窗 / 高亮一路跑到全亮并把守卫位卡住，再次打开面板时常亮且不再响应悬停。</para>
        /// <para>子类重写务必调用 <c>base.OnDisable()</c>。声明为 virtual 是因为 Unity 只调用最派生的
        /// 同名方法，子类若自行写 <c>private void OnDisable()</c> 会静默吞掉本实现（有了 virtual 至少会触发 CS0114）。</para>
        /// </summary>
        protected virtual void OnDisable()
        {
            ResetInfoPanel();
            ResetHighlight();
        }
        #endregion
        
        #region 信息弹窗
        [Header("鼠标悬停弹窗")]
        [Tooltip("弹窗根节点的 CanvasGroup，鼠标悬停时淡入，移出时淡出。为 null 时跳过弹窗逻辑。")]
        [SerializeField] protected CanvasGroup infoPanel;
        [Tooltip("弹窗淡入动画时长（秒），默认 0.2。")]
        [SerializeField] protected float fadeInDuration  = 0.2f;
        [Tooltip("弹窗淡出动画时长（秒），默认 0.3。")]
        [SerializeField] protected float fadeOutDuration = 0.3f;

        // 悬停弹窗淡入淡出句柄（基于 toolkit 中央 Tween），用于打断上一次动画。
        private ToolkitTweenHandle _infoPanelFade;
        // 弹窗 是否已经淡入
        private bool _isInfoPanelFadeIn;

        /// <summary>
        /// 重置 信息弹窗
        /// </summary>
        private void ResetInfoPanel()
        {
            // 先打断在途补间：直接写 alpha 而不 Kill 的话，正在跑的淡入会把它推回去。
            // 收在方法内部而不是各调用点，是因为本方法有三个调用点（绑定 / 解绑 / 停用）。
            _infoPanelFade.Kill();

            if (infoPanel)
            {
                infoPanel.alpha          = 0f;
                infoPanel.interactable   = false;
                infoPanel.blocksRaycasts = false;
            }

            _isInfoPanelFadeIn = false;
        }

        /// <summary>
        /// 信息弹窗 淡入
        /// </summary>
        private void InfoPanelFadeIn()
        {
            if (_isInfoPanelFadeIn) return;
            _isInfoPanelFadeIn = true;

            _infoPanelFade.Kill();
            infoPanel.interactable   = true;
            infoPanel.blocksRaycasts = true;
            _infoPanelFade = ToolkitTween.FadeCanvasGroup(
                infoPanel, 1f, fadeInDuration, EToolkitEase.OutQuad);
        }
        
        /// <summary>
        /// 信息弹窗 淡出
        /// </summary>
        private void InfoPanelFadeOut()
        {
            if (!_isInfoPanelFadeIn) return;
            _isInfoPanelFadeIn = false;

            _infoPanelFade.Kill();
            infoPanel.interactable   = false;
            infoPanel.blocksRaycasts = false;
            _infoPanelFade = ToolkitTween.FadeCanvasGroup(
                infoPanel, 0f, fadeOutDuration, EToolkitEase.InQuad);
        }
        #endregion

        #region 悬停高亮
        [Header("悬停高亮")]
        [Tooltip("悬停时淡入的高亮层 Image（仅改其 color.a，不动 RGB）。为 null 时跳过高亮逻辑。\n" +
                 "建议置于节点底板之上、图标与文字之下，并关闭其 Raycast Target。")]
        [SerializeField] protected Image highlightImage;
        [Tooltip("高亮完全显示时的不透明度，默认 1。")]
        [Range(0f, 1f)]
        [SerializeField] protected float highlightAlpha = 1f;
        [Tooltip("高亮淡入时长（秒），默认 0.15。")]
        [SerializeField] protected float highlightFadeInDuration  = 0.15f;
        [Tooltip("高亮淡出时长（秒），默认 0.15。")]
        [SerializeField] protected float highlightFadeOutDuration = 0.15f;

        // 高亮淡入淡出句柄（基于 toolkit 中央 Tween），用于打断上一次动画。
        private ToolkitTweenHandle _highlightFade;

        /// <summary>
        /// 当前是否处于高亮态。这是「期望状态」而非「补间是否在途」，供子类组合判断。
        /// </summary>
        protected bool IsHighlighted { get; private set; }

        /// <summary>
        /// 设置高亮态。悬停事件与外部调用（如「高亮某条路径上的全部节点」）共用这一个入口。
        /// <para>设计约定：<b>状态在本方法维护，表现交给钩子</b> ——
        /// 子类只需重写 <see cref="OnHighlightBegin"/> / <see cref="OnHighlightEnd"/>，
        /// 不需要（也不应该）自行改动状态位。</para>
        /// <para>同态重复调用直接忽略：对象池复用同一实例时，EventSystem 可能朝「已被复用为其它节点」
        /// 的旧对象补发一次游离的 PointerExit，幂等判断可挡住这类误灭。</para>
        /// </summary>
        /// <param name="on">true = 进入高亮，false = 取消高亮。</param>
        /// <param name="instant">true 时跳过补间、瞬间到位（复位路径使用）。</param>
        public void SetHighlight(bool on, bool instant = false)
        {
            if (IsHighlighted == on) return;
            IsHighlighted = on;

            if (on) OnHighlightBegin(instant);
            else    OnHighlightEnd(instant);
        }

        /// <summary>
        /// 高亮开始。基类实现：把 <see cref="highlightImage"/> 的 alpha 淡入到 <see cref="highlightAlpha"/>。
        /// <para>子类可重写以实现自定义表现（发光、缩放、粒子，或按解锁状态区别对待）。</para>
        /// <para><b>务必尊重 <paramref name="instant"/></b>：为 true 时必须瞬间到位、不得留下在途补间 ——
        /// 该路径用于对象池归还与停用时的复位，而补间不随 GameObject 停用而停止，
        /// 残留的补间会渗进下一次复用（表现为「另一个节点莫名其妙亮着」）。</para>
        /// </summary>
        /// <param name="instant">true = 瞬间到位，不播放补间。</param>
        protected virtual void OnHighlightBegin(bool instant)
        {
            if (!highlightImage) return;

            _highlightFade.Kill();
            _highlightFade = ToolkitTween.FadeGraphic(
                highlightImage, highlightAlpha,
                instant ? 0f : highlightFadeInDuration, EToolkitEase.OutQuad);
        }

        /// <summary>
        /// 高亮结束。基类实现：把 <see cref="highlightImage"/> 的 alpha 淡出到 0。
        /// 子类重写时的注意事项同 <see cref="OnHighlightBegin"/>。
        /// </summary>
        /// <param name="instant">true = 瞬间到位，不播放补间。</param>
        protected virtual void OnHighlightEnd(bool instant)
        {
            if (!highlightImage) return;

            _highlightFade.Kill();
            _highlightFade = ToolkitTween.FadeGraphic(
                highlightImage, 0f,
                instant ? 0f : highlightFadeOutDuration, EToolkitEase.InQuad);
        }

        /// <summary>
        /// 强制复位高亮：打断补间并瞬间还原到常态。
        /// 不经 <see cref="SetHighlight"/> —— 首次从预制体实例化出来时 alpha 取自序列化值、
        /// 而状态位本就是 false，走幂等判断会被直接跳过。
        /// </summary>
        private void ResetHighlight()
        {
            _highlightFade.Kill();
            IsHighlighted = false;
            OnHighlightEnd(instant: true);
        }
        #endregion

        #region 交互操作
        /// <summary>节点被选中时调用，子类可重写以实现高亮、缩放等视觉反馈。</summary>
        public virtual void OnNodeSelected() { }

        /// <summary>节点取消选中时调用，子类可重写以还原高亮等视觉状态。</summary>
        public virtual void OnNodeDeselected() { }

        // ── IPointerEnterHandler / IPointerExitHandler（鼠标悬停）──

        void IPointerEnterHandler.OnPointerEnter(PointerEventData eventData)
            => OnPointerEnterNode();

        void IPointerExitHandler.OnPointerExit(PointerEventData eventData)
            => OnPointerExitNode();

        /// <summary>
        /// 鼠标悬停开始时调用：弹窗淡入 + 进入高亮态（均带缓动，基于 toolkit 中央 Tween）。
        /// 子类若只想改高亮表现，重写 <see cref="OnHighlightBegin"/> 即可，无需重写本方法；
        /// 确需重写本方法时建议调用 base.OnPointerEnterNode() 以保留弹窗与高亮，或完全自定义。
        /// </summary>
        public virtual void OnPointerEnterNode()
        {
            // 弹窗与高亮相互独立：未配置弹窗的节点同样应当获得高亮，故不在此提前 return
            if (infoPanel) InfoPanelFadeIn();

            SetHighlight(true);
        }

        /// <summary>
        /// 鼠标悬停结束时调用：弹窗淡出 + 退出高亮态（均带缓动，基于 toolkit 中央 Tween）。
        /// 子类若只想改高亮表现，重写 <see cref="OnHighlightEnd"/> 即可，无需重写本方法；
        /// 确需重写本方法时建议调用 base.OnPointerExitNode() 以保留弹窗与高亮，或完全自定义。
        /// </summary>
        public virtual void OnPointerExitNode()
        {
            // 与 OnPointerEnterNode 对称：不因未配置弹窗而跳过高亮
            if (infoPanel) InfoPanelFadeOut();

            SetHighlight(false);
        }

        // ── IPointerClickHandler（鼠标点击）──

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
            => OnNodeClicked(eventData);
        
        /// <summary>
        /// 节点被点击时触发，参数为被点击的节点UI实例。
        /// 外部代码可订阅此事件；如需 Inspector 配置，可在子类中额外添加 UnityEvent。
        /// </summary>
        public event Action<UINodeBase> Clicked;
        
        /// <summary>
        /// 鼠标点击节点时调用，并触发 Clicked 事件。
        /// 子类可重写以实现自定义点击效果（如播放音效、弹出对话、触发剧情等）；
        /// 建议调用 base.OnNodeClicked(eventData) 以保留 Clicked 事件通知，或完全自定义。
        /// </summary>
        public virtual void OnNodeClicked(PointerEventData eventData = null)
        {
            Clicked?.Invoke(this);
        }
        #endregion

        #region 对象池
        /// <summary>对象从池中取出时调用（ToolkitPool 回调），当前无需额外初始化。</summary>
        public void OnSpawn() { }
        /// <summary>对象归还到池中时调用（ToolkitPool 回调），自动执行解绑并重置状态。</summary>
        public void OnDespawn() => OnUnbindData();
        #endregion
    }
}
