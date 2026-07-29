using System.Collections.Generic;
using UnityEngine;

namespace Ale.NodeTree.Runtime
{
    /// <summary>
    /// 节点条件管理器（静态单例）。
    /// 负责注册、注销条件检查器，并按 conditionType 路由执行条件检查。
    /// 初始化时自动注册内置的 NodeUnlockedChecker 和 NodeFinishedChecker。
    /// </summary>
    public class NodeConditionManager
    {
        private static NodeConditionManager _instance; // 单例实例
        /// <summary>获取全局单例，首次访问时自动创建并注册内置检查器。</summary>
        public static NodeConditionManager Instance => _instance ??= new NodeConditionManager();

        /// <summary>
        /// 关闭 Domain Reload（Enter Play Mode Options → 取消 Reload Domain）时，静态 _instance
        /// 会跨播放会话残留，使上一次运行注册的自定义检查器带入下一次运行。进入 Play 时置空，
        /// 下次访问 Instance 会重建并仅注册内置检查器（NodeUnlockedChecker / NodeFinishedChecker）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
#if UNITY_EDITOR
            _warnedMissingTypes.Clear();
#endif
        }

        // 已注册的检查器字典，key = INodeConditionChecker.ConditionType
        private readonly Dictionary<string, INodeConditionChecker> _checkers
            = new Dictionary<string, INodeConditionChecker>();

#if UNITY_EDITOR
        // 编辑器下已告警过的未注册 conditionType（每个仅告警一次，避免每帧刷屏）。
        private static readonly HashSet<string> _warnedMissingTypes = new HashSet<string>();
#endif

        /// <summary>私有构造函数，自动注册内置条件检查器。</summary>
        private NodeConditionManager()
        {
            Register(new NodeUnlockedChecker());
            Register(new NodeFinishedChecker());
        }

        /// <summary>注册条件检查器，同类型会覆盖旧的检查器。checker 为 null 或其 ConditionType 为空时忽略并告警。</summary>
        public void Register(INodeConditionChecker checker)
        {
            if (checker == null)
            {
                Debug.LogWarning("[NodeConditionManager] Register 传入 null，已忽略。");
                return;
            }
            if (string.IsNullOrEmpty(checker.ConditionType))
            {
                Debug.LogWarning($"[NodeConditionManager] {checker.GetType().Name} 的 ConditionType 为空，已忽略。");
                return;
            }
            _checkers[checker.ConditionType] = checker;
        }

        /// <summary>注销指定 conditionType 的检查器。</summary>
        public void Unregister(string conditionType)
        {
            _checkers.Remove(conditionType);
        }

        /// <summary>
        /// 执行条件检查。conditionType 为空或未注册时返回 true（不阻断流程）。
        /// </summary>
        /// <param name="conditionType">条件类型标识，对应已注册的 INodeConditionChecker.ConditionType。</param>
        /// <param name="conditionParam">条件参数字符串，由检查器自行解析。</param>
        /// <param name="comparison">比较方式，传递给检查器供其实现具体逻辑。</param>
        /// <param name="context">调用方传入的上下文对象，可为 null。</param>
        public bool Check(string conditionType, string conditionParam,
                          EConditionComparison comparison, object context)
        {
            if (string.IsNullOrEmpty(conditionType)) return true;
            if (!_checkers.TryGetValue(conditionType, out var checker))
            {
#if UNITY_EDITOR
                // 未注册（非空但找不到）通常是拼写错误或漏注册：默认视为满足，但每个类型告警一次以暴露误配。
                if (_warnedMissingTypes.Add(conditionType))
                    Debug.LogWarning($"[NodeConditionManager] 未注册的 conditionType「{conditionType}」，默认视为满足（true）。请检查是否漏注册对应的 INodeConditionChecker。");
#endif
                return true;
            }
            return checker.Check(conditionParam, comparison, context);
        }
    }
}
