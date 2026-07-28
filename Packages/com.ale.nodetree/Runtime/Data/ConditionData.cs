using System;

namespace Ale.NodeTree.Runtime
{
    // ── 条件系统枚举 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 条件满足类型：决定同级条件（或条件组）之间的逻辑关系。
    /// All = AND（全部满足）；Any = OR（满足任意一个）。
    /// </summary>
    public enum EConditionSatisfyType
    {
        /// <summary>全部满足（AND 逻辑）</summary>
        All,
        /// <summary>满足任意一个（OR 逻辑）</summary>
        Any
    }

    /// <summary>
    /// 条件比较方式。接口层仅定义语义，具体比较逻辑由各 INodeConditionChecker 实现。
    /// </summary>
    public enum EConditionComparison
    {
        /// <summary>等于</summary>
        Equal,
        /// <summary>不等于</summary>
        NotEqual,
        /// <summary>大于</summary>
        Greater,
        /// <summary>小于</summary>
        Less
    }

    // ── 数据类 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 单条条件实例数据，存储在 ConditionGroupData.conditions 列表中。
    /// conditionType 引用 NodeConditionTypeData.conditionType，
    /// comparison 决定比较方式，conditionParam 为传给检查器的参数字符串。
    /// </summary>
    [Serializable]
    public class ConditionData
    {
        /// <summary>条件类型标识符，引用 NodeConditionTypeData.conditionType。</summary>
        public string conditionType;

        /// <summary>
        /// 比较方式，由实现 INodeConditionChecker 的外部系统负责具体判断逻辑。
        /// 默认为 Equal（等于）。
        /// </summary>
        public EConditionComparison comparison = EConditionComparison.Equal;

        /// <summary>传递给条件检查器的参数字符串，具体格式由检查器约定。</summary>
        public string conditionParam;
    }
}
