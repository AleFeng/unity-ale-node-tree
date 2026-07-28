using System;

namespace Ale.NodeTree.Runtime
{
    /// <summary>
    /// 节点自定义键值对数据。存储在 NodeData.customDataList 中，
    /// 用于挂载任意扩展数据（如 Dialogue System 的 ConversationID 等）。
    /// </summary>
    [Serializable]
    public class NodeCustomData
    {
        public string key;   // 数据键名，通过 NodeData.GetCustomData(key) 查询
        public string value; // 数据值（字符串格式，由业务层自行解析）
    }
}
