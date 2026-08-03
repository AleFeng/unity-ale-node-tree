using System;
using System.Collections.Generic;
using Ale.Toolkit.Runtime;
using UnityEngine;

namespace Ale.NodeTree.Runtime
{
    /// <summary>
    /// 节点实例数据。存储在 NodeTreeData.nodes 列表中，
    /// 代表节点树中的一个具体节点，包含展示信息、状态标签规则、自定义属性值及子节点引用。
    ///
    /// <para>继承 <see cref="AttributeOwner"/> 以复用带缓存的 O(1) <see cref="AttributeOwner.GetEntry"/>
    /// 与 <see cref="AttributeOwner.GetAttributeValue{T}"/> / <see cref="AttributeOwner.SetAttributeValue{T}"/>；
    /// 属性字段的 schema 由所属 <see cref="NodeTypeData.attributes"/> 定义。</para>
    /// </summary>
    [Serializable]
    public class NodeData : AttributeOwner
    {
        [Header("基础设置")]
        [Tooltip("节点ID: 必须在同一 NodeTreeData 内唯一")]
        public string nodeId;
        [Tooltip("节点类型引用: 必须对应 NodeTreeData.nodeTypes 中的某个 NodeTypeData.typeName")]
        public string nodeTypeRef;
        [Tooltip("节点备注: 仅供编辑器使用，不影响运行时逻辑")]
        public string comment;

        [Header("UI显示")]
        [Tooltip("节点图标: 在UI中显示的图标")]
        public Sprite uiIcon;
        [Tooltip("节点名称: AttributeValue(Text)，纯文本 fallback + 可选本地化引用；启用 toolkit 本地化(ATK_LOCALIZATION)时 ResolveText 优先取多语文本。")]
        public AttributeValue nodeName = new AttributeValue(EFieldType.Text);
        [Tooltip("节点描述: AttributeValue(Text)，纯文本 fallback + 可选本地化引用；启用 toolkit 本地化(ATK_LOCALIZATION)时 ResolveText 优先取多语文本。")]
        public AttributeValue nodeDesc = new AttributeValue(EFieldType.Text);
        [Tooltip("节点位置: 决定节点在编辑器画布中的位置（像素坐标）")]
        public Vector2 position;
        
        [Header("自定义属性")]
        [Tooltip("自定义属性值: 由所属 NodeTypeData.attributes 定义，可在编辑器逐节点配置；运行时经 GetAttributeValue<T>(id) 读取。")]
        public List<AttributeEntry> attributeValues = new List<AttributeEntry>();

        [Header("状态标签规则")]
        [Tooltip("逐标签的挂载条件: 每条规则对应 NodeTreeData.tags 中的一个标签，其 condition 即在本节点挂上该标签的门槛（空条件=无门槛）。由 RebuildTagRules 随标签词表自动同步。")]
        public List<NodeTagRule> tagRules = new List<NodeTagRule>();

        [Header("子节点列表")]
        public List<string> childNodeIds = new List<string>();

        // 将 attributeValues 暴露给基类 AttributeOwner 的懒加载字典缓存。
        protected override List<AttributeEntry> AttributeEntries => attributeValues;

        #region 自定义属性
        /// <summary>
        /// 按当前节点类型的属性字段定义（<see cref="NodeTypeData.attributes"/>）协调 <see cref="attributeValues"/>：
        /// 为新增字段补默认值、移除已删字段、类型漂移时重置为新类型默认值。幂等，可在编辑器中反复调用。
        /// </summary>
        /// <param name="config">节点所属的 NodeTreeData（用于按 nodeTypeRef 解析节点类型）。</param>
        public void RebuildAttributes(NodeTreeData config)
        {
            var type = config != null ? config.GetNodeType(nodeTypeRef) : null;
            AttributeSync.Sync(attributeValues, type != null ? type.attributes : null);
            InvalidateEntryCache();
        }
        #endregion

        #region 状态标签规则
        /// <summary>
        /// 按标签词表（<see cref="NodeTreeData.tags"/>）协调 <see cref="tagRules"/>：
        /// 为新增标签补一条空条件规则、移除已删标签的规则。幂等，可在编辑器中反复调用。
        /// </summary>
        /// <param name="config">节点所属的 NodeTreeData（用于取标签词表）。</param>
        public void RebuildTagRules(NodeTreeData config)
        {
            var tags = config != null ? config.tags : null;
            if (tags == null) { tagRules.Clear(); return; }

            // 移除：tagRules 中标签已从词表删除的项
            tagRules.RemoveAll(r => r == null || !TagExists(tags, r.tagName));
            // 补充：词表中存在但 tagRules 尚无的标签
            foreach (var t in tags)
            {
                if (t == null || string.IsNullOrEmpty(t.tagName)) continue;
                if (GetTagRule(t.tagName) == null)
                    tagRules.Add(new NodeTagRule(t.tagName));
            }
        }

        /// <summary>取指定标签的挂载规则，未找到返回 null。</summary>
        public NodeTagRule GetTagRule(string tagName)
        {
            if (string.IsNullOrEmpty(tagName)) return null;
            foreach (var r in tagRules)
                if (r != null && r.tagName == tagName) return r;
            return null;
        }

        private static bool TagExists(List<NodeTagData> tags, string tagName)
        {
            if (string.IsNullOrEmpty(tagName)) return false;
            foreach (var t in tags)
                if (t != null && t.tagName == tagName) return true;
            return false;
        }
        #endregion
    }
}
