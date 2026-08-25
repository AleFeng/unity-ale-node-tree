using Ale.Condition;

namespace Ale.NodeTree.Runtime
{
    /// <summary>
    /// 节点树条件上下文：实现 Toolkit 的 <see cref="IConditionContext"/>，
    /// 通过 <see cref="GetService{T}"/> 暴露 <see cref="INodeTreeStateSource"/> 供判定器读取节点标签状态。
    ///
    /// <para><b>本包之外的服务</b>走 fallback：节点的解锁条件常常要问别的系统
    /// （「选了哪个选项」「有没有那件道具」「第几天」），那些判定器取不到自己的数据源时一律
    /// fail-closed 返回 false，表现为条件永不成立且毫无征兆。宿主把自己的
    /// <see cref="IConditionContext"/> 注入进来即可（见
    /// <see cref="NodeTreeSaveDataManager.ExternalServices"/>）。</para>
    /// </summary>
    public sealed class NodeTreeConditionContext : IConditionContext
    {
        private readonly INodeTreeStateSource _state;
        private readonly IConditionContext _fallback;

        /// <param name="state">节点标签状态源（通常为 <see cref="NodeTreeSaveDataManager.Instance"/>）。</param>
        /// <param name="subject">被判定的主体（可选）。</param>
        /// <param name="fallback">宿主的服务上下文（可选）：本包提供不了的服务转问它。</param>
        public NodeTreeConditionContext(INodeTreeStateSource state, object subject = null,
            IConditionContext fallback = null)
        {
            _state    = state;
            Subject   = subject;
            _fallback = fallback;
        }

        /// <summary>被判定的主体（如触发条件的节点）；判定器可选用。</summary>
        public object Subject { get; }

        /// <summary>
        /// 取一个游戏状态服务：本包只提供 <see cref="INodeTreeStateSource"/>，
        /// 其余类型转问宿主注入的上下文；都没有则返回 null（判定器据此 fail-closed）。
        /// </summary>
        public T GetService<T>() where T : class => _state as T ?? _fallback?.GetService<T>();
    }
}
