using UnityEditorInternal;
using UnityEngine;

namespace Ale.NodeTree.Editor
{
    /// <summary>
    /// 节点树编辑器的项目级设置。持久化到 <c>ProjectSettings/NodeTreeEditorSettings.asset</c>
    /// （文本序列化，随版本库共享），而非 EditorPrefs（后者按机器/用户存、不随版本同步）。
    /// </summary>
    public class NodeTreeEditorSettings : ScriptableObject
    {
        private const string SettingsPath = "ProjectSettings/NodeTreeEditorSettings.asset";

        [Tooltip("在画布上「添加子节点」/「添加连线」时，是否自动向子节点的 Unlock 规则写入" +
                 "「前置节点已完成（NodeTree.NodeFinished）」条件。")]
        public bool autoWriteUnlockCondition = true;

        private static NodeTreeEditorSettings _instance;

        /// <summary>获取全局单例：首次访问时从 ProjectSettings 文件加载，缺失则新建默认值。</summary>
        public static NodeTreeEditorSettings Instance
        {
            get
            {
                if (_instance) return _instance;

                var loaded = InternalEditorUtility.LoadSerializedFileAndForget(SettingsPath);
                _instance = loaded != null && loaded.Length > 0
                    ? loaded[0] as NodeTreeEditorSettings
                    : null;

                if (!_instance)
                    _instance = CreateInstance<NodeTreeEditorSettings>();

                // 非资产实例，避免被销毁 / 误入场景保存。
                _instance.hideFlags = HideFlags.HideAndDontSave;
                return _instance;
            }
        }

        /// <summary>将当前设置写回 ProjectSettings 文件（文本序列化，便于版本库 diff）。</summary>
        public void Save()
        {
            InternalEditorUtility.SaveToSerializedFileAndForget(
                new Object[] { this }, SettingsPath, true);
        }
    }
}
