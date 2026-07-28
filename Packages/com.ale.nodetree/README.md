# 节点树系统（Node Tree System）

<p align="center">
  🌍
  中文 |
  <a href="./README_EN.md">English</a> |
  <a href="./README_JA.md">日本語</a>
</p>

面向 Unity 的**可视化节点树 / 技能树 / 科技树**插件。用一个 `NodeTreeData` 资产集中配置**节点、节点类型、解锁条件与画布布局**；配套一个**可视化编辑器**（画布拖拽 / 缩放 / 平移 / 连线）与一套**开箱即用的运行时 UI**（按类型对象池化、视口裁剪、URP 流光连线）。节点的**已解锁 / 已完成**状态由存档管理器维护，解锁条件通过**可扩展的条件检查器**判定。

- 数据驱动：一个 `NodeTreeData`（ScriptableObject）承载整棵树，编辑器全程 Undo / Redo。
- 高性能运行时：按节点类型对象池化、视口裁剪按需 Spawn / Despawn、连线 Mesh 合批降 DrawCall。
- 可扩展条件：实现 `INodeConditionChecker` 即可自定义解锁条件，内置「已解锁 / 已完成」两种检查器。
- 存档友好：`NodeTreeSaveDataManager` 维护解锁 / 完成状态并支持 JSON 序列化，可接入任意游戏存档系统。
- 可选依赖：节点名 / 描述本地化经 Unity Localization 可选启用（`HAS_LOCALIZATION`）；悬停弹窗缓动经 DOTween 可选启用（`DOTWEEN`）；对象池基于底层包 `com.ale.toolkit`。

---

## 模块概览

| 模块 | 职责 | 主要类型 |
|------|------|---------|
| **配置** | 节点树配置资产 | `NodeTreeData` |
| **数据** | 节点 / 类型 / 条件 / 自定义数据 | `NodeData`、`NodeTypeData`、`LineTypeData`、`ConditionData`、`ConditionGroupData`、`NodeConditionTypeData`、`NodeCustomData` |
| **条件** | 解锁条件判定与扩展 | `INodeConditionChecker`、`NodeConditionManager` |
| **存档** | 已解锁 / 已完成状态 | `NodeTreeSaveDataManager` |
| **运行时 UI** | 节点树展示 | `UINodeTreeWindow`、`UINodeBase`、`NodeLineBuilder` |
| **编辑器** | 可视化编辑 | `NodeTreeEditorWindow`、`NodeDrawer`、`NodeTreeCanvasState`、`NodeTreeDataEditor` |
| **Shader** | 流光连线 | `NodeTree/NodeLineFlow` |

运行时程序集 `Ale.NodeTree.Runtime`、编辑器程序集 `Ale.NodeTree.Editor`，命名空间同名。

---

## 配置资产 `NodeTreeData`

整棵节点树的唯一数据源（`ScriptableObject`）。通过 `Assets > Create > NodeTree System/Config Node Tree` 创建，新建时自动填充：内置节点类型（**普通** / **结局**）、内置条件类型（**NodeUnlocked** / **NodeFinished**）以及一个位于画布原点的**起始节点**。

| 字段 | 说明 |
|------|------|
| `nodes` | `List<NodeData>`，所有节点实例 |
| `nodeTypes` | `List<NodeTypeData>`，节点类型定义（外观 + UI 预制体 + 连线样式） |
| `conditionTypes` | `List<NodeConditionTypeData>`，可用的条件类型元数据 |
| `layoutDirection` | `ELayoutDirection`，画布整体布局方向 |
| `zoom` | 编辑器画布缩放（由编辑器写入，运行时不使用） |

**主要 API**：`GetNode(string nodeId)`、`GetNodeType(string typeName)`（未找到返回 `null`）。

---

## 数据模型

### 节点 `NodeData`

节点实例，存储在 `NodeTreeData.nodes` 中。

| 字段 | 说明 |
|------|------|
| `nodeId` | 节点唯一 ID（同一 `NodeTreeData` 内唯一） |
| `nodeTypeRef` | 引用某个 `NodeTypeData.typeName`，决定外观与 UI 预制体 |
| `comment` | 编辑器备注（不影响运行时） |
| `conditionSatisfyType` | `EConditionSatisfyType`（`All` = AND / `Any` = OR），决定各条件组的组合逻辑 |
| `conditionGroups` | `List<ConditionGroupData>`，解锁条件（多组、组内多条） |
| `uiIcon` | 节点图标（`Sprite`） |
| `localizeNodeName` / `localizeNodeDesc` | 本地化名称 / 描述（`LocalizedString`，需 `HAS_LOCALIZATION`） |
| `position` | 画布像素坐标 |
| `customDataList` | `List<NodeCustomData>`，任意键值对，供其他系统挂载扩展数据 |
| `childNodeIds` | 子节点 ID 列表（构成树 / 图结构） |

**主要 API**：

- `string GetCustomData(string key)` / `void SetCustomData(string key, string value)` —— 读写自定义数据。
- `bool IsUnlock(object context = null)` —— 按 `conditionSatisfyType` + 各 `ConditionGroupData` 求值是否已解锁；`conditionGroups` 为空视为无条件解锁（`true`）。求值经 `NodeConditionManager` 路由到各条件检查器。
- `bool IsFinish()` —— 经 `NodeTreeSaveDataManager` 查询是否已完成。

### 节点类型 `NodeTypeData` 与连线样式 `LineTypeData`

`NodeTypeData` 描述某一类节点的外观与 UI：`typeName`、`resolution`（尺寸）、`shape`（`ENodeShape`）、`color`、`icon`、`label`、`uiPrefab`（游戏内 UI 预制体，需挂 `UINodeBase`）、`line`（`LineTypeData`）。

- **`ENodeShape`**（编辑器画布形状）：`Circle`、`Square`、`Triangle`、`Diamond`、`HorizontalCapsule`、`Parallelogram`、`Pentagon`、`Hexagon`、`Octagon`、`Star`。
- **`LineTypeData`**：`lineType`（`ELineType`：`Straight` 直线 / `Curve` 曲线 / `Polyline` 折线）、`lineWidth`（像素）、`material`（连线材质，配合 `NodeTree/NodeLineFlow` 可做流光效果）。

### 条件数据 `ConditionData` / `ConditionGroupData`

- `ConditionData`：单条条件 —— `conditionType`（引用 `NodeConditionTypeData.conditionType`）、`comparison`（`EConditionComparison`：`Equal` / `NotEqual` / `Greater` / `Less`）、`conditionParam`（传给检查器的参数字符串）。
- `ConditionGroupData`：条件组 —— `satisfyType`（`EConditionSatisfyType` All/Any）+ `conditions`（`List<ConditionData>`）。空组视为无限制（恒通过）。
- `NodeConditionTypeData`：条件类型元数据（`conditionType` + `description`），在 `NodeTreeData` 中预注册，仅供编辑器展示与选择。
- `NodeCustomData`：`key` / `value` 键值对。

---

## 条件系统

判定「节点是否满足解锁条件」。检查逻辑与数据解耦：数据层只描述 `conditionType` + `comparison` + `conditionParam`，实际判断由注册到 `NodeConditionManager` 的检查器完成。

- **`INodeConditionChecker`**：`string ConditionType { get; }` + `bool Check(string conditionParam, EConditionComparison comparison, object context)`。
- **`NodeConditionManager`**（静态单例）：`Register(INodeConditionChecker)` / `Unregister(string conditionType)` / `Check(conditionType, conditionParam, comparison, context)`（`conditionType` 为空或未注册时返回 `true`，不阻断）。首次访问自动注册内置检查器。
- **内置检查器**：`NodeUnlockedChecker`（`conditionType = "NodeUnlocked"`，读 `NodeTreeSaveDataManager.IsNodeUnlocked`）、`NodeFinishedChecker`（`"NodeFinished"`，读 `IsNodeFinished`）；`conditionParam` 为目标节点 `nodeId`。

**自定义条件**（例：等级达标才解锁）：

```csharp
public class LevelChecker : INodeConditionChecker
{
    public string ConditionType => "PlayerLevel";

    // conditionParam = 需求等级；context 由调用方传入（此处为玩家等级）
    public bool Check(string conditionParam, EConditionComparison comparison, object context)
    {
        int need = int.Parse(conditionParam);
        int level = (int)context;
        return comparison switch
        {
            EConditionComparison.Greater  => level >  need,
            EConditionComparison.Less     => level <  need,
            EConditionComparison.NotEqual => level != need,
            _                             => level >= need,
        };
    }
}

// 注册（游戏启动时一次）
NodeConditionManager.Instance.Register(new LevelChecker());

// 求值（context 会透传到每个检查器）
bool unlocked = node.IsUnlock(context: player.Level);
```

---

## 存档 `NodeTreeSaveDataManager`

静态单例，维护节点的**已解锁 / 已完成**状态。**不继承 MonoBehaviour、不自动保存**——由外部游戏存档系统读写，插件只负责运行时状态与序列化。

- **查询**：`bool IsNodeUnlocked(string nodeId)`、`bool IsNodeFinished(string nodeId)`。
- **修改**：`void SetNodeUnlocked(string nodeId, bool)`、`void SetNodeFinished(string nodeId, bool)`。
- **存档集成**：`NodeTreeSaveData GetSaveData()`（返回深拷贝）、`void SetSaveData(NodeTreeSaveData)`（覆盖式，`null` 忽略）。数据结构 `NodeTreeSaveData { List<string> unlockedNodeIds; List<string> finishedNodeIds; }`。
- **JSON**：`string SerializeToJson()`、`void DeserializeFromJson(string)`（基于 `JsonUtility`，零外部依赖）。
- **重置**：`void Reset()`（清空所有记录，用于「开新游戏」）。

```csharp
var mgr = NodeTreeSaveDataManager.Instance;
mgr.SetNodeUnlocked("node_02", true);
mgr.SetNodeFinished("node_01", true);

string json = mgr.SerializeToJson();   // 交给你的存档系统持久化
// ……读档时：
mgr.DeserializeFromJson(json);
```

---

## 运行时 UI

### `UINodeTreeWindow`

运行时节点树 UI 主窗口（独立 `MonoBehaviour`）。挂到一个 UI 根节点上，指定 `config`（`NodeTreeData`）与内容根容器即可展示整棵树。

- 根据所有节点的位置 / 尺寸**自动计算并设置根容器 Size**；
- **按节点类型维护对象池**（基于 `com.ale.toolkit` 的 `ToolkitGameObjectPool`），通过**视口裁剪按需 Spawn / Despawn** 节点 UI；
- **为每种节点类型合并生成连线 Mesh**（减少 DrawCall），UV 配合 `NodeTree/NodeLineFlow` 做流光；
- 在 `LateUpdate` 中按脏标记按需重建连线。

**主要 API**：`InitTree(NodeTreeData configOverride = null)`（初始化 / 重建整棵树）、`SelectNode(string nodeId)`、`RefreshVisibility()`（重算视口裁剪）、`MarkLineDirty()`（标记连线待重建）。

### `UINodeBase`

节点 UI 基类（`MonoBehaviour` + `IPoolable` + 指针事件）。挂到节点 UI 预制体上，由 `UINodeTreeWindow` 通过对象池生成并绑定数据。功能：节点图标显示、本地化名称 / 描述绑定（`HAS_LOCALIZATION`）、鼠标悬停信息弹窗淡入淡出（`DOTWEEN` 时带缓动）、点击回调。

**可重写虚方法与事件**：

- `OnBindData(NodeData data, NodeTypeData type)` / `OnUnbindData()` —— 绑定 / 解绑数据（`OnDespawn` 自动解绑）。
- `OnNodeSelected()` / `OnNodeDeselected()` —— 选中 / 取消选中的视觉反馈。
- `OnPointerEnterNode()` / `OnPointerExitNode()` —— 悬停弹窗淡入 / 淡出。
- `OnNodeClicked(PointerEventData)` + `event Action<UINodeBase> Clicked` —— 点击回调（可订阅事件，或子类重写触发音效 / 对话 / 剧情等）。
- `OnSpawn()` / `OnDespawn()` —— `IPoolable` 池回调（`OnDespawn` → 自动 `OnUnbindData`，复用不残留旧状态）。

### `NodeLineBuilder`

静态连线 Mesh 构建工具（Runtime 可用）：`BuildCombinedLineMesh(segments, LineTypeData, ELayoutDirection)` 将一批父→子线段按线型（直线 / 贝塞尔曲线 / 折线）**合并为单个 Mesh**（降 DrawCall），并内置贝塞尔几何辅助。UV 约定：`UV.x` 对应线宽两侧（边缘渐变），`UV.y` 为累积弧长 / 100（纹理密度与线长无关）。

---

## 可视化编辑器

`Tools > NodeTree > Node Tree Editor`（或在 `NodeTreeData` 资产的 Inspector 点「在 Node Tree Editor 中编辑」）打开。

- **`NodeTreeEditorWindow`**（`EditorWindow`，IMGUI + GL）：三列布局 —— 左侧**节点类型 / 条件类型管理**、中央**画布**（节点拖拽 / 缩放 / 平移 / 连线）、右侧**节点属性面板**；支持节点增删、子树切除、自动布局，所有修改经 `Undo.RecordObject` + `EditorUtility.SetDirty`，**全程 Undo / Redo 并触发资产保存**。画布平移 / 缩放 / 上次打开的配置经 `EditorPrefs` 持久化。
- **`NodeDrawer`**（静态）：用 IMGUI + GL 绘制节点形状（圆 / 方 / 多边形等）与连线（直线 / 贝塞尔 / 折线），仅在 `Repaint` 期绘制。
- **`NodeTreeCanvasState`**：画布交互状态（平移 / 缩放 / 选中 / 拖拽）与画布↔屏幕坐标互转。
- **`NodeTreeDataEditor`**：`NodeTreeData` 的自定义 Inspector，顶部加「在 Node Tree Editor 中编辑」按钮。

---

## 连线 Shader `NodeTree/NodeLineFlow`

URP 透明**流光连线** Shader：主纹理 + 流动纹理 UV 滚动、边缘渐变（`_EdgeFade`）、辉光（`_Glow`）、全局透明度（`_Alpha`）、流光颜色（HDR）。把使用此 Shader 的材质配到某节点类型的 `LineTypeData.material`，即可让该类型的连线呈现动态流光。

---

## 集成与依赖

- **`com.ale.toolkit`**（必需）：运行时 UI 的对象池化基于 `ToolkitPool` / `ToolkitGameObjectPool` / `IPoolable`。
- **Unity Localization**（可选，`HAS_LOCALIZATION`）：启用后节点名 / 描述使用 `LocalizedString` + `LocalizeStringEvent`；未启用时相关字段与逻辑自动跳过。
- **DOTween**（可选，`DOTWEEN`）：启用后悬停信息弹窗带淡入淡出缓动；未启用时即时显隐。
- **URP**：连线流光 Shader 基于通用渲染管线。

> 安装与环境要求见[项目根目录 README](../../README.md)。

---

## 许可

[MIT](LICENSE.md) © 2026 Ale
