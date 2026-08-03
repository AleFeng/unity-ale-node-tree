using System.Collections.Generic;
using Ale.Condition;

namespace Ale.NodeTree.Runtime
{
    /// <summary>
    /// 判定器：目标节点是否已「完成」（挂有 Finished 标签）。键 <c>NodeTree.NodeFinished</c>。
    /// 节点状态经上下文的 <see cref="INodeTreeStateSource"/> 读取；无状态源时 fail-closed 返回 false。
    /// <para>剧情章节树常用：某章节 Node 的解锁条件 = 前置章节 Node 已完成。</para>
    /// </summary>
    [ConditionEvaluator("NodeTree.NodeFinished")]
    public sealed class NodeFinishedEvaluator : IConditionEvaluator
    {
        private static readonly ConditionParamDef[] Schema =
        {
            new ConditionParamDef("target", ConditionParamType.String, false, "目标节点ID"),
        };

        public string Key => "NodeTree.NodeFinished";
        public string DisplayName => "节点已完成";
        public string Category => "NodeTree";
        public IReadOnlyList<ConditionParamDef> ParamSchema => Schema;

        public bool Evaluate(IReadOnlyList<ConditionParam> parameters, IConditionContext ctx)
        {
            var src = ctx?.GetService<INodeTreeStateSource>();
            if (src == null) return false;

            string target = parameters.Find("target")?.GetString();
            return !string.IsNullOrEmpty(target) && src.HasTag(target, NodeTreeTags.Finished);
        }
    }
}
