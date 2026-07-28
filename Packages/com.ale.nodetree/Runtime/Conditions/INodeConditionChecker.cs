namespace Ale.NodeTree.Runtime
{
    /// <summary>
    /// 节点条件检查器接口。
    /// 外部系统通过实现此接口并注册到 NodeConditionManager，
    /// 来为节点提供自定义的解锁条件判断逻辑。
    /// </summary>
    public interface INodeConditionChecker
    {
        /// <summary>此检查器处理的条件类型标识，与 NodeConditionTypeData.conditionType 对应。</summary>
        string ConditionType { get; }

        /// <summary>
        /// 执行条件检查，返回 true 表示条件满足（节点可解锁）。
        /// </summary>
        /// <param name="conditionParam">节点实例中存储的条件参数字符串，格式由检查器自行约定。</param>
        /// <param name="comparison">
        /// 比较方式（等于/不等于/大于/小于）。
        /// 接口层仅传递语义，具体的比较逻辑由实现类自行决定。
        /// </param>
        /// <param name="context">调用方传入的上下文对象，可为 null。</param>
        bool Check(string conditionParam, EConditionComparison comparison, object context);
    }
}
