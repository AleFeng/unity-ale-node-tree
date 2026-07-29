using System;
using UnityEngine;

namespace Ale.NodeTree.Runtime
{
    /// <summary>
    /// 条件类型定义。在 NodeTreeData 中预先注册，供节点实例的 conditionType 字段引用。
    /// 仅描述元数据，实际检查逻辑由外部系统注册到 NodeConditionManager 中。
    /// </summary>
    [Serializable]
    public class NodeConditionTypeData
    {
        [Tooltip("条件类型唯一标识，与 INodeConditionChecker.ConditionType 对应")]
        public string conditionType;
        [Tooltip("条件的描述说明，仅用于编辑器展示")]
        public string description;
    }
}
