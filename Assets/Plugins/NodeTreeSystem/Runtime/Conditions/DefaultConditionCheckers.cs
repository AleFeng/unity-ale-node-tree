namespace Ale.NodeTree.Runtime
{
    /// <summary>
    /// 内置条件检查器：检查目标节点是否已解锁。
    /// conditionParam 为目标节点的 nodeId，由 NodeTreeSaveDataManager 维护解锁状态。
    /// 在 NodeConditionManager 初始化时自动注册，conditionType = "NodeUnlocked"。
    ///
    /// 比较方式语义：
    ///   Equal / Greater → 节点已解锁时返回 true
    ///   NotEqual / Less → 节点未解锁时返回 true
    /// </summary>
    public class NodeUnlockedChecker : INodeConditionChecker
    {
        /// <summary>此检查器处理的条件类型标识，固定为 "NodeUnlocked"。</summary>
        public string ConditionType => "NodeUnlocked";

        /// <summary>
        /// 根据 comparison 判断 conditionParam 指定节点的解锁状态。
        /// Equal/Greater：已解锁返回 true；NotEqual/Less：未解锁返回 true。
        /// </summary>
        public bool Check(string conditionParam, EConditionComparison comparison, object context)
        {
            bool isUnlocked = NodeTreeSaveDataManager.Instance.IsNodeUnlocked(conditionParam);
            switch (comparison)
            {
                case EConditionComparison.Equal:
                case EConditionComparison.Greater:
                    return isUnlocked;
                case EConditionComparison.NotEqual:
                case EConditionComparison.Less:
                    return !isUnlocked;
                default:
                    return isUnlocked;
            }
        }
    }

    /// <summary>
    /// 内置条件检查器：检查目标节点是否已完成。
    /// conditionParam 为目标节点的 nodeId，由 NodeTreeSaveDataManager 维护完成状态。
    /// 在 NodeConditionManager 初始化时自动注册，conditionType = "NodeFinished"。
    ///
    /// 比较方式语义：
    ///   Equal / Greater → 节点已完成时返回 true
    ///   NotEqual / Less → 节点未完成时返回 true
    /// </summary>
    public class NodeFinishedChecker : INodeConditionChecker
    {
        /// <summary>此检查器处理的条件类型标识，固定为 "NodeFinished"。</summary>
        public string ConditionType => "NodeFinished";

        /// <summary>
        /// 根据 comparison 判断 conditionParam 指定节点的完成状态。
        /// Equal/Greater：已完成返回 true；NotEqual/Less：未完成返回 true。
        /// </summary>
        public bool Check(string conditionParam, EConditionComparison comparison, object context)
        {
            bool isFinished = NodeTreeSaveDataManager.Instance.IsNodeFinished(conditionParam);
            switch (comparison)
            {
                case EConditionComparison.Equal:
                case EConditionComparison.Greater:
                    return isFinished;
                case EConditionComparison.NotEqual:
                case EConditionComparison.Less:
                    return !isFinished;
                default:
                    return isFinished;
            }
        }
    }
}
