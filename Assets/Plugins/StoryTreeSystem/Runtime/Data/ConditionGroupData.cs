using System;
using System.Collections.Generic;

namespace StoryTreeSystem
{
    /// <summary>
    /// 条件组实例数据，存储在 StoryNodeData.conditionGroups 列表中。
    /// 每个条件组包含若干 ConditionData，组内条件以 satisfyType 指定的逻辑（AND/OR）进行组合。
    /// 各条件组之间的关系由 StoryNodeData.conditionSatisfyType 统一决定。
    /// </summary>
    [Serializable]
    public class ConditionGroupData
    {
        /// <summary>
        /// 组内条件的组合逻辑：All = 全部条件满足（AND），Any = 任意条件满足（OR）。
        /// </summary>
        public EConditionSatisfyType satisfyType = EConditionSatisfyType.All;

        /// <summary>本组包含的条件列表，为空时视为该组无条件限制（恒为通过）。</summary>
        public List<ConditionData> conditions = new List<ConditionData>();
    }
}
