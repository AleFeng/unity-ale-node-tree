using System.Collections.Generic;
using Ale.Condition;

namespace Ale.NodeTree.Runtime
{
    /// <summary>
    /// 判定器：目标节点是否已「解锁」（挂有 Unlock 标签）。键 <c>NodeTree.NodeUnlocked</c>。
    /// 节点状态经上下文的 <see cref="INodeTreeStateSource"/> 读取；无状态源时 fail-closed 返回 false。
    /// </summary>
    [ConditionEvaluator("NodeTree.NodeUnlocked")]
    public sealed class NodeUnlockedEvaluator : IConditionEvaluator
    {
        private static readonly ConditionParamDef[] Schema =
        {
            new ConditionParamDef("target", ConditionParamType.String, false, "目标节点ID"),
        };

        public string Key => "NodeTree.NodeUnlocked";
        public string DisplayName => "节点已解锁";
        public string Category => "NodeTree";
        public IReadOnlyList<ConditionParamDef> ParamSchema => Schema;

        public bool Evaluate(IReadOnlyList<ConditionParam> parameters, IConditionContext ctx)
        {
            var src = ctx?.GetService<INodeTreeStateSource>();
            if (src == null) return false;

            string target = parameters.Find("target")?.GetString();
            return !string.IsNullOrEmpty(target) && src.HasTag(target, NodeTreeTags.Unlock);
        }
    }
}
