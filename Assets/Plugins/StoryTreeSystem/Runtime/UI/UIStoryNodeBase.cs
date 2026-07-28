using System;
using Ale.Toolkit.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#if HAS_LOCALIZATION
using UnityEngine.Localization.Components;
#endif

#if DOTWEEN
using DG.Tweening;
#endif

namespace StoryTreeSystem
{
    /// <summary>
    /// 故事节点UI基类（MonoBehaviour + IPoolable）。
    /// 通过 ToolkitPool 管理生命周期，子类重写各虚方法实现具体UI逻辑。
    /// 功能：节点图标显示 / 本地化文本绑定（需 HAS_LOCALIZATION）/
    ///        鼠标悬停弹窗淡入淡出（需 DOTWEEN）/ 鼠标点击回调。
    /// </summary>
    public class UIStoryNodeBase : MonoBehaviour, IPoolable,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        #region 基础设置
        [Header("节点图标")]
        [Tooltip("显示节点图标的 Image 组件，绑定时自动将 Sprite 设置为 StoryNodeData.uiIcon。")]
        [SerializeField] protected Image iconImage;

#if HAS_LOCALIZATION
        [Header("本地化文本 (需 HAS_LOCALIZATION)")]
        [Tooltip("节点名称的 LocalizeStringEvent 组件，绑定时自动关联 StoryNodeData.localizeNodeName。")]
        [SerializeField] protected LocalizeStringEvent nodeNameEvent;
        [Tooltip("节点描述的 LocalizeStringEvent 组件，绑定时自动关联 StoryNodeData.localizeNodeDesc。")]
        [SerializeField] protected LocalizeStringEvent nodeDescEvent;
#endif
        
        /// <summary>当前绑定的节点实例数据，未绑定时为 null。</summary>
        public StoryNodeData NodeData { get; private set; }
        /// <summary>当前绑定的节点类型定义，未绑定时为 null。</summary>
        public StoryNodeTypeData NodeType { get; private set; }
        
        /// <summary>
        /// 绑定节点数据，由 UIStoryTreeWindow 在生成节点UI时调用。
        /// 自动完成：节点图标赋值、本地化文本关联（需 HAS_LOCALIZATION）、弹窗初始化为隐藏状态。
        /// 子类可重写以执行额外初始化（如状态色块、完成标记、解锁动画等）。
        /// </summary>
        public virtual void OnBindData(StoryNodeData data, StoryNodeTypeData type)
        {
            NodeData = data;
            NodeType = type;

            if (data != null)
            {
                // 节点图标：将 Image.sprite 设置为 StoryNodeData.uiIcon
                if (iconImage && data.uiIcon)
                {
                    iconImage.enabled = true;
                    iconImage.sprite = data.uiIcon;
                }
                else
                {
                    iconImage.enabled = false;
                    iconImage.sprite = null;
                }

#if HAS_LOCALIZATION
                // 节点名称（本地化）：绑定 LocalizeStringEvent 的 StringReference
                if (nodeNameEvent)
                    nodeNameEvent.StringReference = data.localizeNodeName;

                // 节点描述（本地化）：绑定 LocalizeStringEvent 的 StringReference
                if (nodeDescEvent)
                    nodeDescEvent.StringReference = data.localizeNodeDesc;
#endif
            }
            
            // 重置 信息弹窗
            ResetInfoPanel();
        }

        /// <summary>
        /// 解绑节点数据，清空引用。由 OnDespawn 自动调用，也可手动调用。
        /// 子类可重写以清理UI状态（如停止协程、重置动画等），建议调用 base.OnUnbindData()。
        /// </summary>
        public virtual void OnUnbindData()
        {
            NodeData = null;
            NodeType = null;

#if DOTWEEN
            // 归还对象池前强制停止弹窗动画，避免对象复用时残留动画状态
            _infoPanelTween?.Kill();
            _infoPanelTween = null;
#endif

            // 重置 信息弹窗
            ResetInfoPanel();
        }
        #endregion
        
        #region 信息弹窗
        [Header("鼠标悬停弹窗")]
        [Tooltip("弹窗根节点的 CanvasGroup，鼠标悬停时淡入，移出时淡出。为 null 时跳过弹窗逻辑。")]
        [SerializeField] protected CanvasGroup infoPanel;
#if DOTWEEN
        [Tooltip("弹窗淡入动画时长（秒），默认 0.2。")]
        [SerializeField] protected float fadeInDuration  = 0.2f;
        [Tooltip("弹窗淡出动画时长（秒），默认 0.3。")]
        [SerializeField] protected float fadeOutDuration = 0.3f;
        
        // DOTWEEN 动画实例，用于淡入淡出动画的控制和优化，避免重复创建动画实例。
        private Tween _infoPanelTween;
#endif
        // 弹窗 是否已经淡入
        private bool _isInfoPanelFadeIn;

        /// <summary>
        /// 重置 信息弹窗
        /// </summary>
        private void ResetInfoPanel()
        {
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
            
#if DOTWEEN
            _infoPanelTween?.Kill();
            infoPanel.interactable   = true;
            infoPanel.blocksRaycasts = true;
            _infoPanelTween = DOTween
                .To(() => infoPanel.alpha, x => infoPanel.alpha = x, 1f, fadeInDuration)
                .SetEase(Ease.OutQuad);
#else
            infoPanel.alpha          = 1f;
            infoPanel.interactable   = true;
            infoPanel.blocksRaycasts = true;
#endif
        }
        
        /// <summary>
        /// 信息弹窗 淡出
        /// </summary>
        private void InfoPanelFadeOut()
        {
            if (!_isInfoPanelFadeIn) return;
            _isInfoPanelFadeIn = false;
            
#if DOTWEEN
            _infoPanelTween?.Kill();
            infoPanel.interactable   = false;
            infoPanel.blocksRaycasts = false;
            _infoPanelTween = DOTween
                .To(() => infoPanel.alpha, x => infoPanel.alpha = x, 0f, fadeOutDuration)
                .SetEase(Ease.InQuad);
#else
            infoPanel.alpha          = 0f;
            infoPanel.interactable   = false;
            infoPanel.blocksRaycasts = false;
#endif
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
        /// 鼠标悬停开始时调用：弹窗淡入（有 DOTWEEN 时带缓动动画，否则立即显示）。
        /// 子类可重写以实现自定义悬停效果（如放大、发光等），建议调用 base.OnPointerEnterNode()
        /// 以保持弹窗淡入行为，或完全自定义。
        /// </summary>
        public virtual void OnPointerEnterNode()
        {
            if (!infoPanel) return;

            // 淡入弹窗
            InfoPanelFadeIn();
        }

        /// <summary>
        /// 鼠标悬停结束时调用：弹窗淡出（有 DOTWEEN 时带缓动动画，否则立即隐藏）。
        /// 子类可重写以实现自定义淡出效果，建议调用 base.OnPointerExitNode()
        /// 以保持弹窗淡出行为，或完全自定义。
        /// </summary>
        public virtual void OnPointerExitNode()
        {
            if (!infoPanel) return;

            // 淡出弹窗
            InfoPanelFadeOut();
        }

        // ── IPointerClickHandler（鼠标点击）──

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
            => OnNodeClicked(eventData);
        
        /// <summary>
        /// 节点被点击时触发，参数为被点击的节点UI实例。
        /// 外部代码可订阅此事件；如需 Inspector 配置，可在子类中额外添加 UnityEvent。
        /// </summary>
        public event Action<UIStoryNodeBase> Clicked;
        
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
