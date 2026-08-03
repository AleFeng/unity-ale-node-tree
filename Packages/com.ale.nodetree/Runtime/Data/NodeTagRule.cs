using System;
using Ale.Condition;
using UnityEngine;

namespace Ale.NodeTree.Runtime
{
    /// <summary>
    /// 逐节点的标签挂载规则：某标签在本节点挂上的条件门槛。
    /// 存储在 <see cref="NodeData.tagRules"/>，由 <see cref="NodeData.RebuildTagRules"/> 随
    /// <see cref="NodeTreeData.tags"/> 词表自动同步（每个标签一条）。
    /// <para><see cref="condition"/> 用 Toolkit 的 <see cref="ConditionExpression"/> 承载，空表达式=无门槛（恒通过）。</para>
    /// </summary>
    [Serializable]
    public class NodeTagRule
    {
        [Tooltip("标签名：对应 NodeTreeData.tags 中的某个 NodeTagData.tagName。")]
        public string tagName;

        [Tooltip("挂载该标签的条件（两级 AND/OR 表达式）。空表达式=无条件通过。")]
        public ConditionExpression condition = new ConditionExpression();

        public NodeTagRule() { }

        public NodeTagRule(string tagName)
        {
            this.tagName = tagName;
        }
    }
}
