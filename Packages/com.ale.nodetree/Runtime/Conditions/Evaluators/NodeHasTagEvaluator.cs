using System.Collections.Generic;
using Ale.Condition;

namespace Ale.NodeTree.Runtime
{
    /// <summary>
    /// 判定器：目标节点是否挂有指定标签（通用原语）。键 <c>NodeTree.NodeHasTag</c>。
    /// 覆盖任意自定义标签（含 Unlock / Finished）；无状态源时 fail-closed 返回 false。
    /// </summary>
    [ConditionEvaluator("NodeTree.NodeHasTag")]
    public sealed class NodeHasTagEvaluator : IConditionEvaluator
    {
        private static readonly ConditionParamDef[] Schema =
        {
            new ConditionParamDef("target", ConditionParamType.String, false, "目标节点ID"),
            new ConditionParamDef("tag",    ConditionParamType.String, false, "标签名"),
        };

        public string Key => "NodeTree.NodeHasTag";
        public string DisplayName => "节点拥有标签";
        public string Category => "NodeTree";
        public IReadOnlyList<ConditionParamDef> ParamSchema => Schema;

        public bool Evaluate(IReadOnlyList<ConditionParam> parameters, IConditionContext ctx)
        {
            var src = ctx?.GetService<INodeTreeStateSource>();
            if (src == null) return false;

            string target = parameters.Find("target")?.GetString();
            string tag    = parameters.Find("tag")?.GetString();
            return !string.IsNullOrEmpty(target) && !string.IsNullOrEmpty(tag) && src.HasTag(target, tag);
        }
    }
}
