using UnityEngine;
using UnityEngine.EventSystems;

namespace Ale.NodeTree.Runtime
{
    /// <summary>
    /// 节点树的滚轮缩放输入区。唯一职责是在<b>正确的层级上</b>接住滚轮，原样转交
    /// <see cref="UINodeTreeWindow"/>。
    ///
    /// <para><b>为什么不把 <c>IScrollHandler</c> 直接实现在窗口上</b>：<c>ScrollRect</c> 自己就实现了它，
    /// 而 uGUI 的派发是 <c>ExecuteEvents.GetEventHandler&lt;IScrollHandler&gt;(射线命中物)</c> ——
    /// 从命中物<b>向上</b>找第一个实现者，找到即止。窗口通常挂在整个界面的根上、是 ScrollView 的
    /// <b>祖先</b>，永远排在 ScrollRect 后面；挂到 <c>Content</c> 上也不行 —— 光标停在空白处时射线
    /// 命中的是 <c>Viewport</c> 的 Image，<c>Content</c> 根本不在向上的那条路径里。</para>
    ///
    /// <para>只有 <c>Viewport</c> 两条路径都占先：命中节点时是「节点 → Content → <b>Viewport</b> →
    /// ScrollView」，命中空白时是「<b>Viewport</b> → ScrollView」。所以本组件由窗口在 <c>InitTree</c>
    /// 时<b>自动装配</b>到 <c>ScrollRect.viewport</c> 上，不需要（也不该）手工挂载；想换个地方接滚轮，
    /// 改窗口的 <c>zoomInputArea</c> 即可。</para>
    ///
    /// <para>走 <c>IScrollHandler</c> 而不是直接读 <c>Input.mouseScrollDelta</c>，是为了不依赖具体输入
    /// 后端 —— 新旧输入系统都会把滚轮送到这里来。⚠ 但两者<b>并没有</b>把数值归一化：旧的
    /// <c>StandaloneInputModule</c> 每格给 ±1，新输入系统的 <c>InputSystemUIInputModule</c> 每格给
    /// <c>scrollDeltaPerTick</c>（默认 6）。折算交给窗口的 <c>ScrollUnitsPerNotch</c>，本组件只管转交。</para>
    /// </summary>
    [AddComponentMenu("")] // 由窗口自动装配，不出现在 Add Component 菜单里
    public class UINodeTreeZoomArea : MonoBehaviour, IScrollHandler
    {
        /// <summary>所属窗口。由 <see cref="UINodeTreeWindow"/> 在装配时注入，拆除时置空。</summary>
        public UINodeTreeWindow Window { get; internal set; }

        /// <summary>滚轮事件：原样转交窗口，本组件不做任何判断（含开关与步进都在窗口那边）。</summary>
        public void OnScroll(PointerEventData eventData)
        {
            if (Window) Window.ZoomByScroll(eventData);
        }
    }
}
