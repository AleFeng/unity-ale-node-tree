using System;
using UnityEngine;

namespace Ale.NodeTree.Runtime
{
    /// <summary>
    /// 故事节点类型定义。描述某一类节点的外观、尺寸、UI预制体及连线样式。
    /// 存储在 ConfigStoryTree.nodeTypes 列表中，节点实例通过 nodeTypeRef 引用。
    /// </summary>
    [Serializable]
    public class NodeTypeData
    {
        [Tooltip("类型唯一名称，节点实例通过此名引用")]
        public string typeName;                 
        [Tooltip("节点的默认显示尺寸（像素）")]
        public Vector2 resolution = new Vector2(100f, 100f);
        [Tooltip("节点形状")]
        public ENodeShape shape = ENodeShape.Circle;
        [Tooltip("节点颜色")]
        public Color color = Color.cyan;
        [Tooltip("节点图标：用于自定义节点的视觉标识，显示在节点中心")]
        public Sprite icon;
        [Tooltip("节点标签文字，显示在节点底部")]
        public string label;
        [Tooltip("游戏内 UI 预制体，需挂载 UIStoryNodeBase")]
        public GameObject uiPrefab;
        [Tooltip("从父节点连向此类型节点的默认连线样式")]
        public StoryLineTypeData line = new StoryLineTypeData();
    }
    
    /// <summary>故事节点的形状类型。</summary>
    public enum ENodeShape
    {
        /// <summary>
        /// 圆形
        /// </summary>
        Circle,
        /// <summary>
        /// 方形
        /// </summary>
        Square,
        /// <summary>
        /// 三角形
        /// </summary>
        Triangle,
        /// <summary>
        /// 菱形
        /// </summary>
        Diamond,
        /// <summary>
        /// 胶囊形
        /// </summary>
        HorizontalCapsule,
        /// <summary>
        /// 平行四边形
        /// </summary>
        Parallelogram,
        /// <summary>
        /// 五边形
        /// </summary>
        Pentagon,
        /// <summary>
        /// 六边形
        /// </summary>
        Hexagon,
        /// <summary>
        /// 八边形
        /// </summary>
        Octagon,
        /// <summary>
        /// 五角星形
        /// </summary>
        Star
    }
    
    /// <summary>连线的外观样式配置，嵌套在 StoryNodeTypeData 中使用。</summary>
    [Serializable]
    public class StoryLineTypeData
    {
        /// <summary>
        /// 线型配置：决定节点之间连线的形状（直线、曲线或折线），
        /// </summary>
        public ELineType lineType = ELineType.Straight;
        /// <summary>
        /// 线宽配置：以像素为单位，决定连线的粗细程度，数值越大线条越粗。建议在 1-20 之间调整以获得最佳视觉效果。
        /// </summary>
        public float lineWidth = 10f;
        /// <summary>
        /// 渲染材质配置：决定连线的视觉风格和颜色，可以使用 Unity 的 Material 资源进行设置。建议使用支持颜色属性的材质，以便在编辑器中调整连线颜色。
        /// </summary>
        public Material material;
    }
    
    /// <summary>节点之间连线的线型。</summary>
    public enum ELineType
    {
        /// <summary>
        /// 直线
        /// </summary>
        Straight,
        /// <summary>
        /// 曲线
        /// </summary>
        Curve,
        /// <summary>
        /// 直角折线
        /// </summary>
        Polyline
    }
}
