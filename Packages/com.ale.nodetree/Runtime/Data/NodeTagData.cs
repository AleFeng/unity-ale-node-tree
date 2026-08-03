using System;
using UnityEngine;

namespace Ale.NodeTree.Runtime
{
    /// <summary>
    /// 状态标签定义。构成节点运行时状态的「词表」——节点状态以挂载标签承载而非写死 bool，便于扩展。
    /// 存储在 <see cref="NodeTreeData.tags"/>，在编辑器「标签」面板编辑；内置 Unlock / Finished，其余可自定义。
    /// </summary>
    [Serializable]
    public class NodeTagData
    {
        [Tooltip("标签名（唯一）：节点运行时状态以此标签承载，供条件系统与业务代码引用。")]
        public string tagName;

        [Tooltip("标签描述：仅用于编辑器展示。")]
        public string description;

        [Tooltip("标签颜色：仅用于编辑器展示 / 可供 UI 取用。")]
        public Color color = Color.white;

        [Tooltip("自动刷新：开启时 RefreshAllNodeStates 会按该标签的逐节点条件重算并挂上（达成即挂、单调不摘）；" +
                 "关闭则该标签只能由业务主动设置（如 Finished 阅读完成后 TrySetFinished）。")]
        public bool autoRefresh;
    }
}
