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
    ///        鼠标悬停弹窗淡入淡出（基于 toolkit 中央 Tween）/ 鼠标点击回调。
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
            
            // 重置 信息弹窗
            ResetInfoPanel();
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

            // 归还对象池前打断弹窗淡入淡出，避免对象复用时残留动画状态
            _infoPanelFade.Kill();

            // 重置 信息弹窗
            ResetInfoPanel();
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
        /// 鼠标悬停开始时调用：弹窗淡入（带缓动，基于 toolkit 中央 Tween）。
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
        /// 鼠标悬停结束时调用：弹窗淡出（带缓动，基于 toolkit 中央 Tween）。
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
