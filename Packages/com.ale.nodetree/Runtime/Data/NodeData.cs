using System;
using System.Collections.Generic;
using UnityEngine;

#if HAS_LOCALIZATION
using UnityEngine.Localization;
#endif

namespace Ale.NodeTree.Runtime
{
    /// <summary>
    /// 节点实例数据。存储在 NodeTreeData.nodes 列表中，
    /// 代表节点树中的一个具体节点，包含展示信息、条件组、自定义数据及子节点引用。
    /// </summary>
    [Serializable]
    public class NodeData
    {
        [Header("基础设置")]
        [Tooltip("节点ID: 必须在同一 NodeTreeData 内唯一")]
        public string nodeId;
        [Tooltip("节点类型引用: 必须对应 NodeTreeData.nodeTypes 中的某个 NodeTypeData.typeName")]
        public string nodeTypeRef;
        [Tooltip("节点备注: 仅供编辑器使用，不影响运行时逻辑")]
        public string comment;
        
        [Header("解锁条件")]
        [Tooltip("条件组间的 满足类型: All = 全部满足（AND），Any = 任意满足（OR）。")]
        public EConditionSatisfyType conditionSatisfyType = EConditionSatisfyType.All;
        [Tooltip("条件组列表: 设置 节点的解锁条件。可以配置多个 条件组，每组内又可以配置多个条件。")]
        public List<ConditionGroupData> conditionGroups = new List<ConditionGroupData>();
        
        [Header("UI显示")]
        [Tooltip("节点图标: 在UI中显示的图标")]
        public Sprite uiIcon;
#if HAS_LOCALIZATION
        [Tooltip("本地化节点名称: 在UI中显示的 节点名称")]
        public LocalizedString localizeNodeName;
        [Tooltip("本地化节点描述: 在UI中显示的 节点描述")]
        public LocalizedString localizeNodeDesc;
#endif
        [Tooltip("节点位置: 决定节点在编辑器画布中的位置（像素坐标）")]
        public Vector2 position;
        
        [Header("自定义数据")]
        [Tooltip("自定义数据列表: 可存储任意键值对，供其他游戏系统使用。")]
        public List<NodeCustomData> customDataList = new List<NodeCustomData>();

        [Header("子节点列表")]
        public List<string> childNodeIds = new List<string>();
        
        #region 自定义数据
        /// <summary>
        /// 获取 自定义数据。
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public string GetCustomData(string key)
        {
            foreach (var d in customDataList)
                if (d.key == key) return d.value;
            return null;
        }

        /// <summary>
        /// 设置 自定义数据
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        public void SetCustomData(string key, string value)
        {
            foreach (var d in customDataList)
            {
                if (d.key == key)
                {
                    d.value = value;
                    return;
                }
            }
            customDataList.Add(new NodeCustomData { key = key, value = value });
        }
        #endregion

        #region 条件判断
        /// <summary>
        /// 判断此节点是否已解锁。
        /// conditionGroups 为空时默认返回 true（无条件解锁）；
        /// 非空时由 conditionSatisfyType 决定各组的组合逻辑（All = AND，Any = OR）。
        /// </summary>
        public bool IsUnlock(object context = null)
        {
            if (conditionGroups == null || conditionGroups.Count == 0) return true;

            if (conditionSatisfyType == EConditionSatisfyType.All)
            {
                // ALL 组都必须通过
                foreach (var group in conditionGroups)
                    if (!EvaluateGroup(group, context)) return false;
                return true;
            }
            else
            {
                // 任意一组通过即可
                foreach (var group in conditionGroups)
                    if (EvaluateGroup(group, context)) return true;
                return false;
            }
        }

        /// <summary>
        /// 评估单个条件组：由 group.satisfyType 决定组内条件的组合逻辑。
        /// 条件列表为空时视为该组无限制，恒返回 true。
        /// </summary>
        private static bool EvaluateGroup(ConditionGroupData group, object context)
        {
            if (group == null || group.conditions == null || group.conditions.Count == 0)
                return true;

            if (group.satisfyType == EConditionSatisfyType.All)
            {
                foreach (var c in group.conditions)
                {
                    if (c == null) continue;
                    if (!NodeConditionManager.Instance.Check(c.conditionType, c.conditionParam, c.comparison, context))
                        return false;
                }
                return true;
            }
            else
            {
                foreach (var c in group.conditions)
                {
                    if (c == null) continue;
                    if (NodeConditionManager.Instance.Check(c.conditionType, c.conditionParam, c.comparison, context))
                        return true;
                }
                return false;
            }
        }

        /// <summary>判断此节点是否已完成，通过 NodeTreeSaveDataManager 查询存档状态。</summary>
        public bool IsFinish() => NodeTreeSaveDataManager.Instance.IsNodeFinished(nodeId);
        #endregion
    }
}
