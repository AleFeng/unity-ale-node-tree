using System;

namespace StoryTreeSystem
{
    /// <summary>
    /// 节点自定义键值对数据。存储在 StoryNodeData.customDataList 中，
    /// 用于挂载任意扩展数据（如 Dialogue System 的 ConversationID 等）。
    /// </summary>
    [Serializable]
    public class StoryNodeCustomData
    {
        public string key;   // 数据键名，通过 StoryNodeData.GetCustomData(key) 查询
        public string value; // 数据值（字符串格式，由业务层自行解析）
    }
}
