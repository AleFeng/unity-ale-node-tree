using System.Collections.Generic;
using UnityEngine;

namespace Ale.NodeTree.Runtime
{
    /// <summary>
    /// 节点树配置资产（ScriptableObject）。
    /// 存储所有节点实例、节点类型定义、条件类型定义、状态标签词表及全局布局设置。
    /// 通过菜单 NodeTree System/Config Node Tree 创建。
    /// </summary>
    [CreateAssetMenu(fileName = "ConfigNodeTree", menuName = "NodeTree System/Config Node Tree")]
    public class NodeTreeData : ScriptableObject
    {
        [Header("节点")]
        public List<NodeData> nodes = new List<NodeData>(); // 所有节点实例列表

        [Header("节点类型定义")]
        public List<NodeTypeData> nodeTypes = new List<NodeTypeData>(); // 节点类型定义，决定外观与 UI 预制体

        [Header("条件类型定义")]
        public List<NodeConditionTypeData> conditionTypes = new List<NodeConditionTypeData>(); // 可用的条件类型元数据列表

        [Header("状态标签定义")]
        public List<NodeTagData> tags = new List<NodeTagData>(); // 节点运行时状态标签词表（内置 Unlock / Finished，可自定义）

        [Header("布局设置")]
        public ELayoutDirection layoutDirection = ELayoutDirection.Left2Right; // 编辑器画布的整体布局方向
        public float zoom = 1.0f;                                              // 编辑器画布的当前缩放比例（由编辑器写入，运行时不使用）

        // 运行时查找缓存（O(1)）：仅在 Play 模式使用；编辑期走线性查找以实时反映编辑。
        // Dictionary 不可序列化，作为普通运行时字段存在（不占资产序列化）。
        private Dictionary<string, NodeData>     _nodeLookup;
        private Dictionary<string, NodeTypeData> _typeLookup;

        /// <summary>通过 nodeId 查找节点实例，未找到返回 null。</summary>
        public NodeData GetNode(string nodeId)
        {
            if (nodeId == null) return null;
#if UNITY_EDITOR
            if (!Application.isPlaying) return FindNodeLinear(nodeId);
#endif
            if (_nodeLookup == null) BuildNodeLookup();
            return _nodeLookup.TryGetValue(nodeId, out var node) ? node : null;
        }

        /// <summary>通过 typeName 查找节点类型定义，未找到返回 null。</summary>
        public NodeTypeData GetNodeType(string typeName)
        {
            if (typeName == null) return null;
#if UNITY_EDITOR
            if (!Application.isPlaying) return FindTypeLinear(typeName);
#endif
            if (_typeLookup == null) BuildTypeLookup();
            return _typeLookup.TryGetValue(typeName, out var type) ? type : null;
        }

        /// <summary>
        /// 使运行时查找缓存失效（下次 GetNode/GetNodeType 重建）。
        /// 由 UINodeTreeWindow.InitTree 在（重新）初始化时调用，确保缓存反映最新数据。
        /// </summary>
        public void InvalidateLookup()
        {
            _nodeLookup = null;
            _typeLookup = null;
        }

        private void BuildNodeLookup()
        {
            _nodeLookup = new Dictionary<string, NodeData>(nodes.Count);
            foreach (var node in nodes)
            {
                if (node == null || node.nodeId == null) continue;
                // 重复 ID 保留第一个，与原线性查找语义一致。
                if (!_nodeLookup.ContainsKey(node.nodeId))
                    _nodeLookup.Add(node.nodeId, node);
            }
        }

        private void BuildTypeLookup()
        {
            _typeLookup = new Dictionary<string, NodeTypeData>(nodeTypes.Count);
            foreach (var type in nodeTypes)
            {
                if (type == null || type.typeName == null) continue;
                if (!_typeLookup.ContainsKey(type.typeName))
                    _typeLookup.Add(type.typeName, type);
            }
        }

        private NodeData FindNodeLinear(string nodeId)
        {
            foreach (var node in nodes)
                if (node != null && node.nodeId == nodeId) return node;
            return null;
        }

        private NodeTypeData FindTypeLinear(string typeName)
        {
            foreach (var type in nodeTypes)
                if (type != null && type.typeName == typeName) return type;
            return null;
        }

        /// <summary>
        /// 新建资产时自动填充内置节点类型（普通 / 结局）、内置条件类型（NodeUnlocked / NodeFinished）
        /// 以及一个默认的开始节点。
        /// Unity 在通过 CreateAssetMenu 创建资产以及 Inspector Reset 时均会调用此方法。
        /// 所有 Ensure* 辅助方法均保证幂等，不会重复添加已存在的条目。
        /// </summary>
        private void Reset()
        {
            // 默认节点类型
            EnsureBuiltinNodeType("普通", ENodeShape.Circle, new Vector2(100f, 100f), new Color(0.6f, 0.8f, 0.8f));
            EnsureBuiltinNodeType("结局", ENodeShape.Hexagon, new Vector2(130f, 130f), new Color(0.6f, 0.9f, 0.3f));

            // 默认条件类型
            EnsureBuiltinConditionType("NodeUnlocked", "节点已解锁（对应 NodeTreeSaveDataManager.IsNodeUnlocked）");
            EnsureBuiltinConditionType("NodeFinished", "节点已完成（对应 NodeTreeSaveDataManager.IsNodeFinished）");

            // 默认状态标签（Unlock 自动刷新 / Finished 手动设置）
            EnsureBuiltinTag(NodeTreeTags.Unlock, "已解锁", true);
            EnsureBuiltinTag(NodeTreeTags.Finished, "已完成", false);

            // 默认开始节点（仅在 nodes 为空时添加）
            EnsureStartNode();
        }

        /// <summary>
        /// 若 nodes 列表为空，则自动添加一个"开始"节点（nodeTypeRef = "普通"，位于画布原点）。
        /// nodes 已有内容时直接返回，不做任何修改（保证幂等）。
        /// </summary>
        private void EnsureStartNode()
        {
            if (nodes.Count > 0) return;
            nodes.Add(new NodeData
            {
                nodeId      = "开始节点",
                nodeTypeRef = "普通",
                comment = "这是起始节点。请从这里开始构建你的节点树！",
                position    = Vector2.zero
            });
        }

        /// <summary>
        /// 若 nodeTypes 列表中不存在指定 typeName，则追加一条默认节点类型记录。
        /// 已存在时直接返回，不做任何修改（保证幂等）。
        /// </summary>
        private void EnsureBuiltinNodeType(string typeName, ENodeShape shape, Vector2 resolution, Color color)
        {
            foreach (var nt in nodeTypes)
                if (nt.typeName == typeName) return;
            nodeTypes.Add(new NodeTypeData
            {
                typeName   = typeName,
                shape      = shape,
                resolution = resolution,
                color      = color,
                line       = new LineTypeData()
            });
        }

        /// <summary>
        /// 若 conditionTypes 列表中不存在指定 conditionType，则追加一条新记录。
        /// 已存在时直接返回，不做任何修改（保证幂等）。
        /// </summary>
        private void EnsureBuiltinConditionType(string conditionType, string description)
        {
            foreach (var ct in conditionTypes)
                if (ct.conditionType == conditionType) return;
            conditionTypes.Add(new NodeConditionTypeData
            {
                conditionType = conditionType,
                description   = description
            });
        }

        /// <summary>
        /// 若 tags 列表中不存在指定 tagName，则追加一条新标签定义。已存在时直接返回（保证幂等）。
        /// </summary>
        private void EnsureBuiltinTag(string tagName, string description, bool autoRefresh)
        {
            foreach (var t in tags)
                if (t.tagName == tagName) return;
            tags.Add(new NodeTagData
            {
                tagName     = tagName,
                description = description,
                autoRefresh = autoRefresh
            });
        }

        /// <summary>返回所有根节点（不被任何其他节点的 childNodeIds 引用的节点）。</summary>
        public List<NodeData> GetRootNodes()
        {
            var childSet = new HashSet<string>();
            foreach (var node in nodes)
                foreach (var childId in node.childNodeIds)
                    childSet.Add(childId);

            var roots = new List<NodeData>();
            foreach (var node in nodes)
                if (!childSet.Contains(node.nodeId))
                    roots.Add(node);
            return roots;
        }

        /// <summary>返回指定节点的父节点 ID，若为根节点则返回 null。</summary>
        public string GetParentId(string nodeId)
        {
            foreach (var node in nodes)
                if (node.childNodeIds.Contains(nodeId))
                    return node.nodeId;
            return null;
        }
    }
    
    /// <summary>节点树的整体布局方向，决定父子节点的排列轴向。</summary>
    public enum ELayoutDirection
    {
        /// <summary>
        /// 从左向右展开（父节点在左，子节点在右）
        /// </summary>
        Left2Right,
        /// <summary>
        /// 从右向左展开（父节点在右，子节点在左）
        /// </summary>
        Right2Left,
        /// <summary>
        /// 从上向下展开（父节点在上，子节点在下）
        /// </summary>
        Top2Bottom,
        /// <summary>
        /// 从下向上展开（父节点在下，子节点在上）
        /// </summary>
        Bottom2Top
    }
}
