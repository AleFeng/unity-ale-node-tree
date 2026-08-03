using Ale.Condition;

namespace Ale.NodeTree.Runtime
{
    /// <summary>
    /// 节点树条件上下文：实现 Toolkit 的 <see cref="IConditionContext"/>，
    /// 通过 <see cref="GetService{T}"/> 暴露 <see cref="INodeTreeStateSource"/> 供判定器读取节点标签状态。
    /// </summary>
    public sealed class NodeTreeConditionContext : IConditionContext
    {
        private readonly INodeTreeStateSource _state;

        /// <param name="state">节点标签状态源（通常为 <see cref="NodeTreeSaveDataManager.Instance"/>）。</param>
        /// <param name="subject">被判定的主体（可选）。</param>
        public NodeTreeConditionContext(INodeTreeStateSource state, object subject = null)
        {
            _state  = state;
            Subject = subject;
        }

        /// <summary>被判定的主体（如触发条件的节点）；判定器可选用。</summary>
        public object Subject { get; }

        /// <summary>取一个游戏状态服务；当前仅提供 <see cref="INodeTreeStateSource"/>，其它类型返回 null。</summary>
        public T GetService<T>() where T : class => _state as T;
    }
}
