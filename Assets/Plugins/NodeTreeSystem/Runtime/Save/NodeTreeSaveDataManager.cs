using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ale.NodeTree.Runtime
{
    /// <summary>
    /// 节点树存档管理器（静态单例）。
    /// 维护节点的已解锁/已完成状态，不继承 MonoBehaviour，不自动保存/加载。
    /// 外部游戏存档系统通过 GetSaveData/SetSaveData 读写数据，
    /// 并可使用 SerializeToJson/DeserializeFromJson 进行 JSON 序列化。
    /// </summary>
    public class NodeTreeSaveDataManager
    {
        private static NodeTreeSaveDataManager _instance; // 单例实例
        /// <summary>获取全局单例，首次访问时自动创建。</summary>
        public static NodeTreeSaveDataManager Instance => _instance ??= new NodeTreeSaveDataManager();

        /// <summary>
        /// 节点树存档数据结构（可序列化）。
        /// 供外部游戏存档系统通过 GetSaveData/SetSaveData 进行整体读写。
        /// </summary>
        [Serializable]
        public class NodeTreeSaveData
        {
            public List<string> unlockedNodeIds = new List<string>(); // 已解锁的节点ID列表
            public List<string> finishedNodeIds = new List<string>(); // 已完成的节点ID列表
        }

        private NodeTreeSaveData _data = new NodeTreeSaveData(); // 当前运行时存档数据

        // ── 状态查询 ──

        /// <summary>查询指定节点是否已解锁。</summary>
        public bool IsNodeUnlocked(string nodeId) => _data.unlockedNodeIds.Contains(nodeId);

        /// <summary>查询指定节点是否已完成。</summary>
        public bool IsNodeFinished(string nodeId) => _data.finishedNodeIds.Contains(nodeId);

        // ── 状态修改 ──

        /// <summary>设置指定节点的解锁状态。unlocked=true 时添加到列表，false 时从列表移除。</summary>
        public void SetNodeUnlocked(string nodeId, bool unlocked)
        {
            if (unlocked)
            {
                if (!_data.unlockedNodeIds.Contains(nodeId))
                    _data.unlockedNodeIds.Add(nodeId);
            }
            else
            {
                _data.unlockedNodeIds.Remove(nodeId);
            }
        }

        /// <summary>设置指定节点的完成状态。finished=true 时添加到列表，false 时从列表移除。</summary>
        public void SetNodeFinished(string nodeId, bool finished)
        {
            if (finished)
            {
                if (!_data.finishedNodeIds.Contains(nodeId))
                    _data.finishedNodeIds.Add(nodeId);
            }
            else
            {
                _data.finishedNodeIds.Remove(nodeId);
            }
        }

        // ── Get/Set 供外部游戏存档系统集成（不自动保存/加载） ──

        /// <summary>
        /// 返回当前存档数据的深拷贝，供外部游戏存档系统读取并持久化。
        /// 返回的是副本，修改不会影响内部状态。
        /// </summary>
        public NodeTreeSaveData GetSaveData()
        {
            // 返回深拷贝，防止外部直接修改内部状态
            var copy = new NodeTreeSaveData();
            copy.unlockedNodeIds.AddRange(_data.unlockedNodeIds);
            copy.finishedNodeIds.AddRange(_data.finishedNodeIds);
            return copy;
        }

        /// <summary>
        /// 用外部传入的存档数据覆盖当前状态。通常在游戏加载存档时调用。
        /// data 为 null 时直接返回，不清空现有数据。
        /// </summary>
        public void SetSaveData(NodeTreeSaveData data)
        {
            if (data == null) return;
            _data = new NodeTreeSaveData();
            if (data.unlockedNodeIds != null)
                _data.unlockedNodeIds.AddRange(data.unlockedNodeIds);
            if (data.finishedNodeIds != null)
                _data.finishedNodeIds.AddRange(data.finishedNodeIds);
        }

        // ── JSON 序列化辅助 ──

        /// <summary>将当前存档数据序列化为 JSON 字符串（使用 JsonUtility，零外部依赖）。</summary>
        public string SerializeToJson() => JsonUtility.ToJson(_data);

        /// <summary>
        /// 从 JSON 字符串反序列化并覆盖当前存档数据。
        /// json 为空时直接返回，不清空现有数据。
        /// </summary>
        public void DeserializeFromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            var loaded = JsonUtility.FromJson<NodeTreeSaveData>(json);
            SetSaveData(loaded);
        }

        /// <summary>清空所有解锁/完成记录，重置为初始状态。</summary>
        public void Reset()
        {
            _data = new NodeTreeSaveData();
        }
    }
}
