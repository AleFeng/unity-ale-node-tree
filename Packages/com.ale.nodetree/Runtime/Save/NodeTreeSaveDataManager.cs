using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ale.NodeTree.Runtime
{
    /// <summary>
    /// 节点树实时存档管理器（静态单例）。
    /// 以「标签（Tag）」承载每个节点的运行时状态（解锁 / 完成 / 自定义），不写死 bool 便于扩展；
    /// 不继承 MonoBehaviour、不自动落盘。宿主游戏存档系统通过 Get/Set 读写整份数据，
    /// 或用 Save/Load 进行 JSON 序列化；实际持久化（磁盘 / PlayerPrefs）由宿主负责。
    /// <para>实现 <see cref="INodeTreeStateSource"/>，供条件系统（<c>NodeTree.NodeFinished</c> /
    /// <c>NodeTree.NodeUnlocked</c> 等判定器）读取节点标签。</para>
    /// </summary>
    public class NodeTreeSaveDataManager : INodeTreeStateSource
    {
        private static NodeTreeSaveDataManager _instance; // 单例实例
        /// <summary>获取全局单例，首次访问时自动创建。</summary>
        public static NodeTreeSaveDataManager Instance => _instance ??= new NodeTreeSaveDataManager();

        /// <summary>
        /// 关闭 Domain Reload（Enter Play Mode Options → 取消 Reload Domain）时，静态 _instance
        /// 会跨播放会话残留，使上一次运行的标签状态带入下一次运行。进入 Play 时置空，
        /// 保证每次运行都从干净状态开始（下次访问 Instance 会重建）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        /// <summary>单个节点的标签状态（可序列化）。</summary>
        [Serializable]
        public class NodeTagState
        {
            public string nodeId;                              // 节点ID（键）
            public List<string> tags = new List<string>();     // 该节点当前挂载的标签集合
        }

        /// <summary>
        /// 节点树存档数据结构（可序列化）。
        /// 供宿主游戏存档系统通过 Get/Set 整体读写、Save/Load 进行 JSON 序列化。
        /// </summary>
        [Serializable]
        public class NodeTreeSaveData
        {
            public List<NodeTagState> nodes = new List<NodeTagState>(); // 各节点的标签状态
        }

        private NodeTreeSaveData _data = new NodeTreeSaveData(); // 当前运行时存档数据

        // 与 _data 并行的 O(1) 查询镜像（HashSet 不可序列化，仅运行时加速；随 _data 同步维护）。
        private readonly Dictionary<string, HashSet<string>> _tagSets = new Dictionary<string, HashSet<string>>();

        // ── 通用标签查询 / 修改 ──────────────────────────────────────────────────

        /// <summary>查询指定节点是否挂载某标签。</summary>
        public bool HasTag(string nodeId, string tag)
            => !string.IsNullOrEmpty(nodeId) && _tagSets.TryGetValue(nodeId, out var set) && set.Contains(tag);

        /// <summary>为指定节点挂上某标签（已存在则忽略，List 与镜像同步）。</summary>
        public void AddTag(string nodeId, string tag)
        {
            if (string.IsNullOrEmpty(nodeId) || string.IsNullOrEmpty(tag)) return;
            if (!_tagSets.TryGetValue(nodeId, out var set))
            {
                set = new HashSet<string>();
                _tagSets[nodeId] = set;
            }
            if (set.Add(tag)) GetOrCreateState(nodeId).tags.Add(tag); // 新增才追加，避免重复
        }

        /// <summary>移除指定节点的某标签（List 与镜像同步）。</summary>
        public void RemoveTag(string nodeId, string tag)
        {
            if (string.IsNullOrEmpty(nodeId) || string.IsNullOrEmpty(tag)) return;
            if (_tagSets.TryGetValue(nodeId, out var set) && set.Remove(tag))
                FindState(nodeId)?.tags.Remove(tag);
        }

        /// <summary>返回指定节点当前的标签集合（只读；无记录时为空集合）。</summary>
        public IReadOnlyCollection<string> GetTags(string nodeId)
            => !string.IsNullOrEmpty(nodeId) && _tagSets.TryGetValue(nodeId, out var set)
                ? (IReadOnlyCollection<string>)set
                : Array.Empty<string>();

        /// <summary>清空指定节点的全部标签记录。</summary>
        public void ClearNode(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return;
            if (_tagSets.Remove(nodeId))
            {
                int idx = _data.nodes.FindIndex(n => n != null && n.nodeId == nodeId);
                if (idx >= 0) _data.nodes.RemoveAt(idx);
            }
        }

        // ── Get / Set：供宿主存档系统整体读写（不自动保存 / 加载） ──────────────────

        /// <summary>返回当前存档数据的深拷贝，供宿主读取并持久化。修改副本不影响内部状态。</summary>
        public NodeTreeSaveData Get()
        {
            var copy = new NodeTreeSaveData();
            foreach (var n in _data.nodes)
            {
                if (n == null || string.IsNullOrEmpty(n.nodeId)) continue;
                var ns = new NodeTagState { nodeId = n.nodeId };
                if (n.tags != null) ns.tags.AddRange(n.tags);
                copy.nodes.Add(ns);
            }
            return copy;
        }

        /// <summary>用外部传入的存档数据覆盖当前状态（通常在加载存档时调用）。data 为 null 时直接返回。</summary>
        public void Set(NodeTreeSaveData data)
        {
            if (data == null) return;
            _data = new NodeTreeSaveData();
            if (data.nodes != null)
            {
                foreach (var n in data.nodes)
                {
                    if (n == null || string.IsNullOrEmpty(n.nodeId)) continue;
                    var ns = new NodeTagState { nodeId = n.nodeId };
                    if (n.tags != null) ns.tags.AddRange(n.tags);
                    _data.nodes.Add(ns);
                }
            }
            RebuildSets();
        }

        // ── Save / Load：JSON 序列化辅助（零外部依赖，落盘交宿主） ──────────────────

        /// <summary>将当前存档数据序列化为 JSON 字符串（使用 JsonUtility）。</summary>
        public string Save() => JsonUtility.ToJson(_data);

        /// <summary>从 JSON 字符串反序列化并覆盖当前存档数据。json 为空时直接返回，不清空现有数据。</summary>
        public void Load(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            var loaded = JsonUtility.FromJson<NodeTreeSaveData>(json);
            Set(loaded);
        }

        /// <summary>清空所有标签记录，重置为初始状态。</summary>
        public void Reset()
        {
            _data = new NodeTreeSaveData();
            _tagSets.Clear();
        }

        // ── 兼容包装：旧「解锁 / 完成」bool API（映射到 Unlock / Finished 标签） ─────────
        // 说明：为使旧条件判定器在过渡期仍可编译；随自研条件系统一并移除。

        /// <summary>查询指定节点是否已解锁（等价于 HasTag(nodeId, "Unlock")）。</summary>
        public bool IsNodeUnlocked(string nodeId) => HasTag(nodeId, NodeTreeTags.Unlock);

        /// <summary>查询指定节点是否已完成（等价于 HasTag(nodeId, "Finished")）。</summary>
        public bool IsNodeFinished(string nodeId) => HasTag(nodeId, NodeTreeTags.Finished);

        /// <summary>设置指定节点的解锁状态（true 挂 / false 摘 Unlock 标签）。</summary>
        public void SetNodeUnlocked(string nodeId, bool unlocked)
        {
            if (unlocked) AddTag(nodeId, NodeTreeTags.Unlock);
            else RemoveTag(nodeId, NodeTreeTags.Unlock);
        }

        /// <summary>设置指定节点的完成状态（true 挂 / false 摘 Finished 标签）。</summary>
        public void SetNodeFinished(string nodeId, bool finished)
        {
            if (finished) AddTag(nodeId, NodeTreeTags.Finished);
            else RemoveTag(nodeId, NodeTreeTags.Finished);
        }

        // ── 内部辅助 ────────────────────────────────────────────────────────────

        private NodeTagState FindState(string nodeId) => _data.nodes.Find(n => n != null && n.nodeId == nodeId);

        private NodeTagState GetOrCreateState(string nodeId)
        {
            var s = FindState(nodeId);
            if (s == null)
            {
                s = new NodeTagState { nodeId = nodeId };
                _data.nodes.Add(s);
            }
            return s;
        }

        // 从 _data 重建查询镜像（Set / Load 后调用，使镜像与 _data 一致）。
        private void RebuildSets()
        {
            _tagSets.Clear();
            foreach (var n in _data.nodes)
            {
                if (n == null || string.IsNullOrEmpty(n.nodeId)) continue;
                if (!_tagSets.TryGetValue(n.nodeId, out var set))
                {
                    set = new HashSet<string>();
                    _tagSets[n.nodeId] = set;
                }
                if (n.tags != null)
                    foreach (var t in n.tags)
                        if (!string.IsNullOrEmpty(t)) set.Add(t);
            }
        }
    }
}
