# 节点树系统（Node Tree System）

<p align="center">
  🌍
  中文 |
  <a href="./README_EN.md">English</a> |
  <a href="./README_JA.md">日本語</a>
</p>

面向 Unity 的**可视化节点树 / 技能树 / 科技树**插件。用一个 `NodeTreeData` 资产集中配置**节点、节点类型、状态标签与画布布局**；配套一个**可视化编辑器**（画布拖拽 / 缩放 / 平移 / 连线）与一套**开箱即用的运行时 UI**（按类型对象池化、视口裁剪、URP 流光连线）。节点状态以**标签（Tag）**承载（内置 **Unlock / Finished**），由存档管理器维护；每个标签的挂载门槛用 `com.ale.toolkit` 条件系统（`Ale.Condition`）的 `ConditionExpression` 描述。

- 数据驱动：一个 `NodeTreeData`（ScriptableObject）承载整棵树，编辑器全程 Undo / Redo。
- 高性能运行时：按节点类型对象池化、视口裁剪按需 Spawn / Despawn、连线 Mesh 合批降 DrawCall。
- 条件即插件：直接复用 `com.ale.toolkit` 的 `Ale.Condition` 条件系统，实现 `IConditionEvaluator` 并打 `[ConditionEvaluator("Key")]` 特性即可扩展；内置 `NodeFinished` / `NodeUnlocked` / `NodeHasTag` 三个判定器。
- 存档友好：`NodeTreeSaveDataManager` 以标签制维护节点状态并支持 JSON 序列化，可接入任意游戏存档系统。
- 底层集成：节点名 / 描述本地化经底层包 `com.ale.toolkit` 的 `AttributeValue`(Text) 承载（项目启用 toolkit 的 `ATK_LOCALIZATION` 时取多语文本，否则纯文本回退，本插件无需本地化宏）；悬停弹窗淡入淡出基于 `com.ale.toolkit` 中央 Tween；对象池基于 `com.ale.toolkit`。

---

## 模块概览

| 模块 | 职责 | 主要类型 |
|------|------|---------|
| **配置** | 节点树配置资产 | `NodeTreeData` |
| **数据** | 节点 / 类型 / 标签 / 自定义属性 | `NodeData`、`NodeTypeData`、`LineTypeData`、`NodeTagData`、`NodeTagRule` |
| **条件** | 接入 Toolkit `Ale.Condition` 与内置判定器 | `INodeTreeStateSource`、`NodeTreeConditionContext`、`NodeTreeTags`、`NodeFinishedEvaluator`、`NodeUnlockedEvaluator`、`NodeHasTagEvaluator` |
| **存档** | 节点状态标签 | `NodeTreeSaveDataManager` |
| **运行时 UI** | 节点树展示 | `UINodeTreeWindow`、`UINodeBase`、`NodeLineBuilder` |
| **编辑器** | 可视化编辑 | `NodeTreeEditorWindow`、`NodeDrawer`、`NodeTreeCanvasState`、`NodeTreeDataEditor` |
| **Shader** | 流光连线 | `NodeTree/NodeLineFlow` |

运行时程序集 `Ale.NodeTree.Runtime`、编辑器程序集 `Ale.NodeTree.Editor`，命名空间同名。

---

## 配置资产 `NodeTreeData`

整棵节点树的唯一数据源（`ScriptableObject`）。通过 `Assets > Create > NodeTree System/Config Node Tree` 创建，新建时自动填充：内置节点类型（**普通** / **结局**）、内置状态标签（**Unlock** / **Finished**）以及一个位于画布原点的**起始节点**。

| 字段 | 说明 |
|------|------|
| `nodes` | `List<NodeData>`，所有节点实例 |
| `nodeTypes` | `List<NodeTypeData>`，节点类型定义（外观 + UI 预制体 + 连线样式） |
| `tags` | `List<NodeTagData>`，标签词表（内置 `Unlock` / `Finished`，可自定义） |
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
| `tagRules` | `List<NodeTagRule>`，每个标签一条挂载规则（`tagName` + `ConditionExpression`），随 `NodeTreeData.tags` 词表自动同步 |
| `uiIcon` | 节点图标（`Sprite`） |
| `nodeName` / `nodeDesc` | 节点名称 / 描述（`com.ale.toolkit` 的 `AttributeValue`(Text)：纯文本 + 可选本地化引用；`ResolveText()` 优先取本地化、回退纯文本） |
| `position` | 画布像素坐标 |
| `attributeValues` | `List<AttributeEntry>`（`com.ale.toolkit`），自定义属性值；字段 schema 由所属 `NodeTypeData.attributes` 定义 |
| `childNodeIds` | 子节点 ID 列表（构成树 / 图结构） |

**主要 API**（`NodeData` 继承 `com.ale.toolkit` 的 `AttributeOwner`）：

- `T GetAttributeValue<T>(string id, T fallback = default)` / `bool SetAttributeValue<T>(string id, T value)` / `AttributeEntry GetEntry(string id)` —— O(1) 读写自定义属性值。
- `void RebuildAttributes(NodeTreeData config)` —— 按所属节点类型的 `attributes` schema 协调 `attributeValues`（补默认 / 删多余 / 类型漂移重置）。
- `void RebuildTagRules(NodeTreeData config)` —— 按 `NodeTreeData.tags` 词表协调 `tagRules`（补缺 / 删多余），保证每个标签恰有一条规则。
- `NodeTagRule GetTagRule(string tagName)` —— 取某标签的挂载规则（含其 `ConditionExpression`），未找到返回 `null`。

### 节点类型 `NodeTypeData` 与连线样式 `LineTypeData`

`NodeTypeData` 描述某一类节点的外观、UI 与自定义属性字段：`typeName`、`resolution`（尺寸）、`shape`（`ENodeShape`）、`color`、`icon`、`label`、`uiPrefab`（游戏内 UI 预制体，需挂 `UINodeBase`）、`line`（`LineTypeData`）、`attributes`（`List<AttributeDefinition>`，`com.ale.toolkit`，本类型节点实例的自定义属性字段 schema）。

- **`ENodeShape`**（编辑器画布形状）：`Circle`、`Square`、`Triangle`、`Diamond`、`HorizontalCapsule`、`Parallelogram`、`Pentagon`、`Hexagon`、`Octagon`、`Star`。
- **`LineTypeData`**：`lineType`（`ELineType`：`Straight` 直线 / `Curve` 曲线 / `Polyline` 折线）、`lineWidth`（像素）、`material`（连线材质，配合 `NodeTree/NodeLineFlow` 可做流光效果）。

### 状态标签 `NodeTagData` / 标签规则 `NodeTagRule`

- `NodeTagData`：标签词表条目（存于 `NodeTreeData.tags`）—— `tagName`（标签名）、`description`（说明）、`color`（编辑器展示色）、`autoRefresh`（是否参与自动刷新，见「存档」`RefreshAllNodeStates`）。内置 **Unlock**（`autoRefresh = true`，按前置完成情况自动挂上）与 **Finished**（`autoRefresh = false`，一般由业务主动置），可自定义新增。
- `NodeTagRule`：单个标签在某节点上的挂载规则（存于 `NodeData.tagRules`）—— `tagName`（对应词表标签）、`condition`（`ConditionExpression`，即在本节点挂上该标签的门槛；**空表达式 = 无门槛、恒通过**）。由 `NodeData.RebuildTagRules(config)` 随 `tags` 词表自动同步，`NodeData.GetTagRule(tagName)` 取用。
- `ConditionExpression`（`Ale.Condition`）：两级 AND / OR 结构（表达式 → 组 → 项 → 参数），承载一个标签的挂载条件；求值时把项路由到对应的 `IConditionEvaluator`。
- 节点自定义属性：模板端 `NodeTypeData.attributes`（`List<AttributeDefinition>`，`com.ale.toolkit`）定义字段 schema，实例端 `NodeData.attributeValues`（`List<AttributeEntry>`）承载值，二者经 `NodeData.RebuildAttributes` 同步。

---

## 条件系统：接入 Toolkit `Ale.Condition`

node-tree 不再自研条件系统，而是**直接复用 `com.ale.toolkit`（1.4.0+）的条件系统 `Ale.Condition`**：运行时依赖 `Ale.Condition.Core` / `Ale.Condition.Runtime`，编辑器依赖 `Ale.Condition.Editor`。每个标签的挂载门槛用 `ConditionExpression` 承载（两级 AND / OR：表达式 → 组 → 项 → 参数），项按其键路由到对应判定器求值。

**扩展方式**：实现 `Ale.Condition.IConditionEvaluator` 并打 `[ConditionEvaluator("Key")]` 特性（自动发现并注册），判定器经 `ctx.GetService<T>()` 读取数据源。

**node-tree 内置判定器**（命名空间 `Ale.NodeTree.Runtime`，均实现 `IConditionEvaluator`、运行时自动注册、编辑器下拉可选）：

| 判定器 | 键 | 参数 | 判定 |
|--------|----|----|------|
| `NodeFinishedEvaluator` | `NodeTree.NodeFinished` | `target`（目标节点 ID） | 目标节点是否挂 `Finished` 标签 |
| `NodeUnlockedEvaluator` | `NodeTree.NodeUnlocked` | `target` | 目标节点是否挂 `Unlock` 标签 |
| `NodeHasTagEvaluator` | `NodeTree.NodeHasTag` | `target` + `tag` | 目标节点是否挂指定标签 |

- **数据源接口** `INodeTreeStateSource { bool HasTag(string nodeId, string tag); }` —— 判定器读取节点标签的抽象来源，由 `NodeTreeSaveDataManager` 实现。
- **条件上下文** `NodeTreeConditionContext : IConditionContext` —— 承载求值所需服务，判定器经 `ctx.GetService<INodeTreeStateSource>()` 取数据源。
- **标签名常量** `NodeTreeTags.Unlock` / `NodeTreeTags.Finished`。

**自定义判定器**（实现 `IConditionEvaluator` + 打特性即自动注册；经 `ctx.GetService<T>()` 读状态）：

```csharp
[ConditionEvaluator("NodeTree.MyCondition")]
public sealed class MyEvaluator : IConditionEvaluator
{
    public string Key => "NodeTree.MyCondition";
    public string DisplayName => "我的条件";
    public string Category => "NodeTree";
    public IReadOnlyList<ConditionParamDef> ParamSchema => _schema; // 声明参数 schema
    // 经 ctx.GetService<INodeTreeStateSource>() 或自定义数据源读状态
    public bool Evaluate(IReadOnlyList<ConditionParam> parameters, IConditionContext ctx) { /* ... */ return true; }
}
```

---

## 存档 `NodeTreeSaveDataManager`

静态单例，以**标签制**维护节点状态，并实现 `INodeTreeStateSource` 供条件判定器读取。**不继承 MonoBehaviour、不自动落盘**——由外部游戏存档系统读写，插件只负责运行时状态与序列化。

- **通用标签**：`bool HasTag(string nodeId, string tag)`、`void AddTag(string nodeId, string tag)`、`void RemoveTag(string nodeId, string tag)`、`IReadOnlyCollection<string> GetTags(string nodeId)`、`void ClearNode(string nodeId)`。
- **整份存取 / 序列化**：`NodeTreeSaveData Get()`（返回当前存档）、`void Set(NodeTreeSaveData)`（覆盖式）、`string Save()`（序列化为 JSON）、`void Load(string json)`（从 JSON 载入）、`void Reset()`（清空所有记录，用于「开新游戏」）。实际落盘交宿主。数据结构 `NodeTreeSaveData { List<NodeTagState> nodes }`，`NodeTagState { string nodeId; List<string> tags; }`。
- **便捷门面**（内部自判该标签的 `ConditionExpression`，返回是否设置成功）：`bool TrySetTag(NodeTreeData config, string nodeId, string tag)`、`bool TrySetUnlock(config, nodeId)`、`bool TrySetFinished(config, nodeId)`。
- **批量刷新**：`void RefreshAllNodeStates(NodeTreeData config)` —— 对 `autoRefresh` 标签（如 `Unlock`）按条件重算并挂上：**达成即挂、单调不摘、定点迭代**（支持链式解锁）。

```csharp
var save = NodeTreeSaveDataManager.Instance;

// 业务时机主动置状态：内部自判该标签的条件，返回是否设置成功
save.TrySetFinished(config, "chapter_01");   // 读完本章→置完成（Finished 条件通常为空=直接通过）

// 打开面板 / 加载存档后刷新所有自动标签（Unlock 按前置完成情况链式解锁）
save.RefreshAllNodeStates(config);
bool unlocked = save.HasTag("chapter_02", NodeTreeTags.Unlock);

// 存档往返（落盘交宿主）
string json = save.Save();
save.Load(json);
```

---

## 运行时 UI

### `UINodeTreeWindow`

运行时节点树 UI 主窗口（独立 `MonoBehaviour`）。挂到一个 UI 根节点上，指定 `config`（`NodeTreeData`）与内容根容器即可展示整棵树。

- 根据所有节点的位置 / 尺寸**自动计算并设置根容器 Size**；
- **按节点类型维护对象池**（基于 `com.ale.toolkit` 的 `ToolkitGameObjectPool`），通过**视口裁剪按需 Spawn / Despawn** 节点 UI；
- **为每种节点类型合并生成连线 Mesh**（减少 DrawCall），UV 配合 `NodeTree/NodeLineFlow` 做流光；
- 在 `LateUpdate` 中按脏标记按需重建连线；
- `refreshStatesOnInit`：勾选后在 `InitTree` 时自动调用 `RefreshAllNodeStates`（按存档与条件刷新所有自动标签）。

**主要 API**：`InitTree(NodeTreeData configOverride = null)`（初始化 / 重建整棵树）、`RefreshAllNodeStates()`（对当前 `config` 调 `NodeTreeSaveDataManager.RefreshAllNodeStates`）、`SelectNode(string nodeId)`、`RefreshVisibility()`（重算视口裁剪）、`MarkLineDirty()`（标记连线待重建）。

### `UINodeBase`

节点 UI 基类（`MonoBehaviour` + `IPoolable` + 指针事件）。挂到节点 UI 预制体上，由 `UINodeTreeWindow` 通过对象池生成并绑定数据。功能：节点图标显示、名称 / 描述文本（`AttributeValue.ResolveText()` 解析，填充 `TMP_Text`）、鼠标悬停信息弹窗淡入淡出（基于 `com.ale.toolkit` 中央 Tween）、点击回调。

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

- **`NodeTreeEditorWindow`**（`EditorWindow`，IMGUI + GL）：三列布局 —— 左侧**节点类型 / 标签管理**（原「条件类型」页签已删）、中央**画布**（节点拖拽 / 缩放 / 平移 / 连线）、右侧**节点属性面板**；支持节点增删、子树切除、自动布局，所有修改经 `Undo.RecordObject` + `EditorUtility.SetDirty`，**全程 Undo / Redo 并触发资产保存**。画布平移 / 缩放 / 上次打开的配置经 `EditorPrefs` 持久化。
  - **「标签」页签**：维护 `NodeTreeData.tags` 词表（增删标签、改名 / 说明 / 颜色 / `autoRefresh`）。顶部「标签设置」区含**「自动写入 Unlock 条件」**开关——项目级设置（存 `ProjectSettings/NodeTreeEditorSettings.asset`，随版本库共享）：开启时在画布「添加子节点 / 连线」会自动向子节点的 `Unlock` 规则写入 `NodeTree.NodeFinished(target = 父节点 ID)` 条件。
  - **右侧节点属性面板**：对每个标签用 Toolkit 的 `ConditionExpression` 内联绘制器（`ConditionExpressionDrawer`）编辑其在本节点的挂载条件，内置判定器可从下拉直接选择。
- **`NodeDrawer`**（静态）：用 IMGUI + GL 绘制节点形状（圆 / 方 / 多边形等）与连线（直线 / 贝塞尔 / 折线），仅在 `Repaint` 期绘制。
- **`NodeTreeCanvasState`**：画布交互状态（平移 / 缩放 / 选中 / 拖拽）与画布↔屏幕坐标互转。
- **`NodeTreeDataEditor`**：`NodeTreeData` 的自定义 Inspector，顶部加「在 Node Tree Editor 中编辑」按钮。

---

## 连线 Shader `NodeTree/NodeLineFlow`

URP 透明**流光连线** Shader：主纹理 + 流动纹理 UV 滚动、边缘渐变（`_EdgeFade`）、辉光（`_Glow`）、全局透明度（`_Alpha`）、流光颜色（HDR）。把使用此 Shader 的材质配到某节点类型的 `LineTypeData.material`，即可让该类型的连线呈现动态流光。

---

## 集成与依赖

- **`com.ale.toolkit`**（必需）：运行时 UI 的对象池化基于 `ToolkitPool` / `ToolkitGameObjectPool` / `IPoolable`。
- **本地化**（经 `com.ale.toolkit`）：节点名 / 描述用 `AttributeValue`(Text) 承载（纯文本 + 可选本地化表/条目引用）。项目启用 toolkit 的 `ATK_LOCALIZATION` 宏时 `ResolveText()` 优先取多语文本，否则回退纯文本；本插件自身无需本地化宏、也不直接依赖 `Unity.Localization`。
- **悬停弹窗淡入淡出**（内置）：基于 `com.ale.toolkit` 的中央 Tween（`ToolkitTween.FadeCanvasGroup`），始终可用，无需 DOTween。
- **URP**：连线流光 Shader 基于通用渲染管线。

> 安装与环境要求见[项目根目录 README](../../README.md)。

---

## 许可

[MIT](LICENSE.md) © 2026 Ale
