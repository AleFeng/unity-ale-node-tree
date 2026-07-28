using UnityEditor;
using UnityEngine;

namespace StoryTreeSystem.Editor
{
    /// <summary>
    /// ConfigStoryTree 的自定义 Inspector。
    /// 在默认 Inspector 顶部添加一个快速打开 StoryTreeEditorWindow 的按钮，
    /// 方便在 Project 窗口选中配置文件后直接进入可视化编辑器。
    /// </summary>
    [CustomEditor(typeof(ConfigStoryTree))]
    public class ConfigStoryTreeEditor : UnityEditor.Editor
    {
        /// <summary>
        /// 绘制自定义 Inspector 面板：顶部显示"在 Story Tree Editor 中编辑"按钮，
        /// 按钮下方绘制水平分割线，随后绘制默认 Inspector 字段。
        /// </summary>
        public override void OnInspectorGUI()
        {
            var config = (ConfigStoryTree)target;

            // 顶部打开按钮（加粗、较大字号、固定高度）
            var btnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize    = 13,
                fontStyle   = FontStyle.Bold,
                fixedHeight = 32f
            };
            if (GUILayout.Button("在 Story Tree Editor 中编辑", btnStyle))
                StoryTreeEditorWindow.Open(config);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(string.Empty, GUI.skin.horizontalSlider); // 水平分割线
            EditorGUILayout.Space(2f);

            DrawDefaultInspector();
        }
    }
}
