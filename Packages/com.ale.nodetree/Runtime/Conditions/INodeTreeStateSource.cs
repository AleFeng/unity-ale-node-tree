namespace Ale.NodeTree.Runtime
{
    /// <summary>
    /// 节点树状态源：条件判定器借此读取节点的运行时标签状态。
    /// 由 <see cref="NodeTreeSaveDataManager"/> 实现，经 <see cref="NodeTreeConditionContext"/>
    /// 以 <c>ctx.GetService&lt;INodeTreeStateSource&gt;()</c> 提供给各判定器。
    /// </summary>
    public interface INodeTreeStateSource
    {
        /// <summary>查询指定节点是否挂载某标签。</summary>
        bool HasTag(string nodeId, string tag);
    }
}
