namespace Ale.NodeTree.Runtime
{
    /// <summary>
    /// 节点树内置状态标签名常量。节点的运行时状态以「标签」承载而非写死 bool，便于扩展自定义标签；
    /// 这里仅约定两个内置标签，其余可在编辑器「标签」面板自定义。
    /// </summary>
    public static class NodeTreeTags
    {
        /// <summary>已解锁。</summary>
        public const string Unlock = "Unlock";

        /// <summary>已完成。</summary>
        public const string Finished = "Finished";
    }
}
