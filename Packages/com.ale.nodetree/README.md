# 节点树系统（Node Tree System）

<p align="center">
  🌍
  中文 |
  <a href="./README_EN.md">English</a> |
  <a href="./README_JA.md">日本語</a>
</p>

面向 Unity 的**可视化节点树 / 技能树 / 科技树**插件。用一个 `NodeTreeData` 资产集中配置**节点、节点类型、状态标签与画布布局**；配套一个**可视化编辑器**（画布拖拽 / 缩放 / 平移 / 连线）与一套**开箱即用的运行时 UI**（按类型对象池化、视口裁剪、滚轮缩放、URP 流光连线）。节点状态以**标签（Tag）**承载（内置 **Unlock / Finished**），由存档管理器维护；每个标签的挂载门槛用 `com.ale.toolkit` 条件系统（`Ale.Condition`）的 `ConditionExpression` 描述。

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
| **运行时 UI** | 节点树展示、悬停高亮、信息弹窗、滚轮缩放与无限滚动背景 | `UINodeTreeWindow`、`UINodeTreeZoomArea`、`UINodeBase`、`UINodeInfoPanel`、`UIScrollingBackground`、`NodeLineBuilder` |
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
| `zoom` | 保留字段，当前未使用；编辑器画布缩放经 `EditorPrefs` 持久化 |
| `gridSize` | 编辑器画布网格的最小单位格长度（画布单位，默认 20）。背景网格间距、拖拽吸附与方向键步长共用；经只读属性 `GridSize` 读取，下限 1。运行时不使用 |

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
- **`LineTypeData`**：`lineType`（`ELineType`：`Straight` 直线 / `Curve` 曲线 / `Polyline` 折线）、`lineWidth`（像素）、`material`（连线材质，配合 `NodeTree/NodeLineFlow` 可做流光效果）、连线颜色。
  - **样式归属（1.2.0 起）**：每条连线（含箭头）采用其**目标（子）节点类型**的 `LineTypeData` 绘制（线型 / 线宽 / 材质 / 颜色）——即「从父节点连向此类型（子）节点」的默认线样式。此前是按**父**节点类型绘制。

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

### 让条件读到本插件之外的数据（1.5.1+）

解锁条件常常要问别的系统：「在那个分支点选了第几项」「有没有那件道具」「第几天」。这类判定器取不到自己的数据源时一律 fail-closed 返回 `false`，表现为**条件永不成立且毫无征兆**。把宿主自己的 `IConditionContext` 交给 `NodeTreeSaveDataManager.ExternalServices` 即可打通——本插件只提供 `INodeTreeStateSource`，其余类型转问它：

```csharp
var context = new ConditionContext();                       // Ale.Condition，toolkit 提供
context.RegisterService<IMyChoiceSource>(myChoiceSource);    // 一律按接口登记
NodeTreeSaveDataManager.Instance.ExternalServices = context; // 默认 null，即与注入前完全一致
```

> ⚠️ **每次进入播放都要重新注入**：`NodeTreeSaveDataManager` 是静态单例，它在 `SubsystemRegistration` 时把自己整个置空（防止关闭 Domain Reload 时上一轮状态残留），注入的上下文一并蒸发。请在场景对象的 `Awake` 里注入——那时已晚于这次清空。

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
  - ⚠️ **提醒**：`autoRefresh` 标签的**空条件视为通过（fail-open）**——起始 / 根节点据此自动解锁属预期；非根节点须**显式配置 `Unlock` 条件**，否则空条件恒通过会被自动挂上标签。

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
- **统一管理信息弹窗**（1.6.0 起）：`infoPanelPrefab` 指定弹窗预制体，窗口自动在自己下面建 `InfoPanelLayer`（ScrollView 之外、最后一个兄弟）与对象池；悬停时定位到**节点中心 + `infoPanelOffset`**（默认 `(0, 120)`，即节点上方），并随滚动 / 缩放跟随。详见 [`UINodeInfoPanel`](#uinodeinfopanel)。
- **运行时缩放**（1.7.0 起）：滚轮**以光标为锚点**、滑条以视口中心为锚点，缩的是 `nodeTreeRoot` 的 `localScale`。范围 `minZoom`~`maxZoom`（默认 `0.1`~`3`）、默认倍率 `defaultZoom`（`1`）、滚轮每格加减 `zoomStepPerScroll`（`0.1`，全程约 29 格）。接一根 `zoomSlider` 即可拖动缩放（窗口会强制它走归一化 `0~1` 取值，与倍率之间是**对数映射**，默认范围下 1.0x 落在 68% 处），再接一个 `zoomValueText` 就能按 `zoomValueFormat`（`"{0:0.0}x"`）显示当前倍率。滚轮输入区由窗口自动装配，见 [`UINodeTreeZoomArea`](#uinodetreezoomarea)。
- `refreshStatesOnInit`：勾选后在 `InitTree` 时自动调用 `RefreshAllNodeStates`（按存档与条件刷新所有自动标签）。
- `forceUnlockForTest` + `forceUnlockTags`【测试用】（1.5.2 起，原 `unlockAllForTest`）：勾选开关后，`InitTree` 给每个节点挂上 `forceUnlockTags` 里的标签，绕过解锁条件与存档状态，便于查看整棵树的完整结构。**列表留空则挂上词表（`NodeTreeData.tags`）里的全部标签**——零配置即生效，宿主改了标签名也不会让开关静默失效（节点能不能进由宿主说了算，只挂 `Unlock` 对用自定义标签判定的宿主毫无作用）。需要收窄时再逐条填写。只往内存态里加标签，不改存档文件；发布前应保持关闭。

**主要 API**：`InitTree(NodeTreeData configOverride = null)`（初始化 / 重建整棵树）、`RefreshAllNodeStates()`（对当前 `config` 调 `NodeTreeSaveDataManager.RefreshAllNodeStates`）、`SelectNode(string nodeId)`、`RefreshVisibility()`（重算视口裁剪）、`MarkLineDirty()`（标记连线待重建）、`ShowNodeInfoPanel(string nodeId)` / `HideNodeInfoPanel(string nodeId)` / `HideAllNodeInfoPanels(bool instant = false)`（信息弹窗；悬停会自动调用，也可由外部直接调用来「钉住某几个节点的弹窗」）、`Zoom`（当前倍率）/ `SetZoom(float)`（以视口中心为锚点）/ `SetZoom(float, Vector2)`（以指定屏幕点为锚点）/ `ResetZoom()` 与事件 `ZoomChanged`（1.7.0 起）。

### `UINodeTreeZoomArea`

滚轮缩放的输入区（`MonoBehaviour` + `IScrollHandler`）。唯一职责是在**正确的层级上**接住滚轮，原样转交 `UINodeTreeWindow`（开关与步进都在窗口那边）。由窗口在 `InitTree` 时**自动装配**到 `ScrollRect.viewport` 上（打了 `[AddComponentMenu("")]`，不出现在 Add Component 菜单里），**宿主不需要手工挂载**；想换个地方接滚轮，填窗口的 `zoomInputArea` 即可。

**为什么不把 `IScrollHandler` 直接实现在窗口上**：`ScrollRect` 自己就实现了它，而 uGUI 的派发是 `ExecuteEvents.GetEventHandler<IScrollHandler>(射线命中物)` —— 从命中物**向上**找第一个实现者，找到即止。窗口通常挂在整个界面的根上、是 ScrollView 的**祖先**，永远排在 ScrollRect 后面；挂 `Content` 上也不行 —— 光标停在空白处时射线命中的是 `Viewport` 的 Image，`Content` 根本不在向上的那条路径里。只有 `Viewport` 两条路径都占先：命中节点是「节点 → Content → **Viewport** → ScrollView」，命中空白是「**Viewport** → ScrollView」。

⚠ 输入区上必须有一个开着 `Raycast Target` 的 `Graphic`（标准 ScrollView 模板的 `Viewport` 自带 `Image`，天然满足）。没有的话窗口会告警 —— 收不到射线就是滚轮静默失灵，是这套东西最难查的一种坏法。

走 `IScrollHandler` 而不是直接读 `Input.mouseScrollDelta`，还顺带绕开了新旧输入系统的分歧 —— EventSystem 的输入模块已经把两者归一化，两种模块下每格滚轮都是 ±1。

### `UINodeBase`

节点 UI 基类（`MonoBehaviour` + `IPoolable` + 指针事件）。挂到节点 UI 预制体上，由 `UINodeTreeWindow` 通过对象池生成并绑定数据。功能：节点图标显示、名称 / 描述文本（`AttributeValue.ResolveText()` 解析，填充 `TMP_Text`）、**悬停高亮**（基于 `com.ale.toolkit` 中央 Tween）、悬停时向所属窗口请求**信息弹窗**、点击回调。

> 1.6.0 起信息弹窗**不再由节点持有**，改由 `UINodeTreeWindow` 统一管理，见 [`UINodeInfoPanel`](#uinodeinfopanel)。

**可重写虚方法与事件**：

- `OnBindData(NodeData data, NodeTypeData type)` / `OnUnbindData()` —— 绑定 / 解绑数据（`OnDespawn` 自动解绑）。
- `OnNodeSelected()` / `OnNodeDeselected()` —— 选中 / 取消选中的视觉反馈。
- `OnPointerEnterNode()` / `OnPointerExitNode()` —— 悬停：请求窗口显示 / 收起信息弹窗，并进入 / 退出高亮态。
- `ShowInfoPanel()` / `HideInfoPanel()`（`protected virtual`）+ 序列化开关 `showInfoPanelOnHover` + `OwnerWindow` —— **信息弹窗**。基类实现是转调 `OwnerWindow.ShowNodeInfoPanel(nodeData.nodeId)` / `HideNodeInfoPanel(...)`；子类可重写以附加条件（如未解锁的节点不弹）。`HideInfoPanel` 刻意**不看**该开关：开关在弹窗已弹出后才被关掉时，那一个仍要能收回去。`OwnerWindow` 由窗口在 Spawn 时注入（不走 `GetComponentInParent` —— `nodeTreeRoot` 允许挂在窗口组件之外，向上找不保证能找到）。
- `SetHighlight(bool on, bool instant = false)`（非虚，唯一写入口）+ `OnHighlightBegin(bool instant)` / `OnHighlightEnd(bool instant)`（`protected virtual` 钩子）+ `IsHighlighted` —— **悬停高亮**。基类实现是把 `highlightImage` 的 alpha 在 `0` 与 `highlightAlpha` 之间淡入淡出；**状态在 `SetHighlight` 维护、表现在钩子里**，子类只重写钩子即可。`SetHighlight` 也可由外部调用（如「高亮某条路径上的全部节点」）。
- `OnNodeClicked(PointerEventData)` + `event Action<UINodeBase> Clicked` —— 点击回调（可订阅事件，或子类重写触发音效 / 对话 / 剧情等）。
- `OnSpawn()` / `OnDespawn()` —— `IPoolable` 池回调（`OnDespawn` → 自动 `OnUnbindData`，复用不残留旧状态）。
- `OnDisable()`（`protected virtual`）—— 停用时收起弹窗并复位高亮；子类重写务必调用 `base.OnDisable()`。

**高亮层预制体配置**：`highlightImage` 建议置于节点底板之上、图标与文字之下，stretch 铺满，初始 `alpha = 0`，并**关闭 Raycast Target** —— alpha 为 0 的 `Image` 依然阻挡射线（`Image.IsRaycastLocationValid` 默认不看 `color.a`），高亮层一旦外扩就会挤占相邻节点的悬停与点击。Demo 的两个节点预制体直接复用了各自底板的 sprite，高亮形状天然与节点吻合。

**子类实现注意**：

- **务必尊重 `instant` 参数**：为 `true` 时必须瞬间到位、不得留下在途补间 —— 该路径用于对象池归还与停用时的复位，而 toolkit 的补间**不随 GameObject 停用而停止**，残留的补间会渗进下一次复用（表现为「另一个节点莫名其妙亮着」）。
- 补间默认 `unscaled = true`（`Time.timeScale = 0` 时仍推进）。节点树面板通常在暂停界面打开，不要改成 `false`。
- 典型用法 —— 未解锁的节点不高亮：

```csharp
protected override void OnHighlightBegin(bool instant)
{
    if (nodeData == null) return;
    if (!NodeTreeSaveDataManager.HasTag(nodeData.nodeId, NodeTreeTags.Unlock)) return;
    base.OnHighlightBegin(instant);   // 已解锁才走基类的淡入
}
```

### `UINodeInfoPanel`

信息弹窗组件（`MonoBehaviour` + `IPoolable`，1.6.0 起）。挂在**独立的弹窗预制体**上，配到 `UINodeTreeWindow.infoPanelPrefab`，由窗口用对象池统一取用、定位与回收 —— 节点自身不再持有弹窗。

| 字段 | 默认 | 说明 |
|------|------|------|
| `canvasGroup` | — | 控制淡入淡出的 `CanvasGroup`；为空时自动取本物体上的。 |
| `fadeInDuration` / `fadeOutDuration` | 0.2 / 0.3 | 淡入 / 淡出时长（秒）。 |
| `nodeNameText` / `nodeDescText` / `iconImage` | — | **可选**接线：绑定时分别用 `NodeData.nodeName.ResolveText()` / `nodeDesc.ResolveText()` / `uiIcon` 填充；不接线即跳过（Demo 的弹窗文本由 Unity Localization 驱动，三个都不接）。 |

**主要 API**：`Bind(NodeData, NodeTypeData)` / `Unbind()`（`virtual`，重写务必调 `base`）、`Show(bool instant = false)` / `Hide(bool instant = false)`（**非虚**）、只读属性 `BoundNode` / `BoundType` / `Rect` / `IsVisible` / `IsRecyclable`。

**可重写钩子**：`OnShowBegin(bool instant)` / `OnHideBegin(bool instant)`（`protected virtual`）—— 基类实现是把 `canvasGroup` 的 alpha 补间到 1 / 0；子类可重写以实现缩放弹出、位移、描边扫光等自定义出现效果。`instant` 的注意事项同 `UINodeBase` 的高亮钩子。

`Show` / `Hide` 刻意**不是** `virtual`：`IsVisible` 是窗口回收弹窗的唯一依据，子类重写时漏调 `base` 会让弹窗永不回收（对象池泄漏），因此状态由非虚入口维护、表现交给钩子。

**弹窗预制体配置**：

- 根 `RectTransform` 的 anchor 取 **(0.5, 0.5)**（与弹窗层的 `pivot` 居中配合，窗口写 `localPosition` 即可定位）。
- **不必也不要开 `blocksRaycasts`**：组件在 `Awake` 里会强制关掉它。弹窗与节点分属两棵子树，一旦挡住光标就会 `PointerExit` → 隐藏 → 光标回到节点 → `PointerEnter` → 显示，形成肉眼可见的闪烁死循环。
- 弹窗层由窗口自动创建在其 `RectTransform` 下（最后一个兄弟），**必须在 ScrollView 的 `Viewport` 之外** —— 否则会被其 `Mask` 裁剪，且回到「被别的节点盖住」的老问题。需要放在别处时用 `infoPanelLayer` 指定。
- **位置配在窗口上**：弹窗落点 = 节点中心 + `UINodeTreeWindow.infoPanelOffset`（默认 `(0, 120)`，即节点上方）。`NodeData.position` 本就是节点中心，无需按节点尺寸折算。
- 弹窗**不随画布缩放**：偏移施加在弹窗层空间，缩放节点树画布时弹窗的尺寸与偏移保持不变。这是有意为之 —— 弹窗是给人读的，不该跟着缩到看不清。

**描述为空即不弹**：窗口在取用池实例**之前**会先解析节点的 `nodeDesc`（`ResolveText()`），为空或纯空白就直接不弹、连实例都不建 —— 空框既给不出信息又挡视线。⚠ 判据**只看 `nodeDesc`**：弹窗文案若不由它驱动（例如像 Demo 那样挂 `LocalizeStringEvent` 显示固定文本），节点的 `nodeDesc` 仍需填写，否则一律不弹。

**同时显示多个**：弹窗按 `nodeId` 索引，可对多个节点同时 `ShowNodeInfoPanel`。`Hide` 只是开始淡出、不立即归还池，窗口在 `LateUpdate` 里轮询 `IsRecyclable` 才回收 —— 因此淡出途中重新悬停会复用同一实例淡回去，不会闪。

### `UIScrollingBackground`

四方连续无限滚动背景组件（`RawImage` uvRect UV 滚动）。挂到背景物体上，以 `RawImage` 的**初始 Rect 尺寸**为一块 tile 铺满视口（`uvRect.size = 视口 / tile`），滚动时仅偏移 `uvRect`、靠纹理 **Repeat** 采样实现四方连续无限平铺——单物体单 DrawCall、零 tile 实例、零逐帧分配（静止时零开销）。

| 字段 | 说明 |
|------|------|
| `scrollRect` | 要跟随的 `ScrollRect`（可选）。绑定后在 `LateUpdate` 轮询 `Content.anchoredPosition` 增量、与 Content **同向**滚动（统一覆盖拖拽 / 惯性 / 回弹 / 代码驱动；首帧与重绑定只快照不回放、无跳变）。为空时仅由 API 手动驱动。 |
| `image` | 平铺显示用的 `RawImage`。为空时自动在自身及子物体（含未激活）上查找；其**初始 Rect 尺寸即 tile 尺寸**（≤0 时回退纹理像素尺寸）。 |
| `viewportMode` | 视口尺寸来源：`SelfRect` = 本组件 RectTransform（image 为子物体时自动拉伸铺满）；`Screen` = 屏幕尺寸（按根 `Canvas.scaleFactor` 换算画布单位，并轮询分辨率变化自动重适配）。 |
| `speedMultiplier` | X / Y 分轴滚动速度倍率（`Vector2`）：1 = 与 Content 同速；<1 远景视差；>1 近景视差；0 = 该轴静止；负值 = 该轴反向。仅作用于 ScrollRect 跟随路径。 |

**主要 API**：`ScrollBy(Vector2)`（手动视觉增量，不乘倍率）、`SetScrollOffset(Vector2)` / `ResetOffset()`、`SetScrollRect(ScrollRect)`（运行时重绑定，无跳变）、`Refit()`（布局变化后重新适配视口）；属性 `SpeedMultiplier`（`Vector2` 分轴）、`ViewportMode`、`TileSize`（只读）、`ScrollOffset`（只读）。

> ⚠ 纹理导入设置的 **Wrap Mode 必须为 Repeat**（否则 tile 边缘拉伸 / 截断，组件会在 Awake 告警）；使用 `RawImage` 直接引用 `Texture2D`，不支持图集内 Sprite。演示预制体的 `ImgBackground` 即此用法（绑定 Scroll View、`Screen` 视口、倍率 1）。

### `NodeLineBuilder`

静态连线 Mesh 构建工具（Runtime 可用）：`BuildCombinedLineMesh(segments, LineTypeData, ELayoutDirection)` 将一批父→子线段按线型（直线 / 贝塞尔曲线 / 折线）**合并为单个 Mesh**（降 DrawCall），并内置贝塞尔几何辅助。UV 约定：`UV.x` 对应线宽两侧（边缘渐变），`UV.y` 为累积弧长 / 100（纹理密度与线长无关）。

---

## 可视化编辑器

`Tools > NodeTree > Node Tree Editor`（或在 `NodeTreeData` 资产的 Inspector 点「在 Node Tree Editor 中编辑」）打开。

- **`NodeTreeEditorWindow`**（`EditorWindow`，IMGUI + GL）：三列布局 —— 左侧**节点类型 / 标签管理**（原「条件类型」页签已删）、中央**画布**（节点拖拽 / 缩放 / 平移 / 连线）、右侧**节点属性面板**；支持节点增删、子树切除、自动布局，所有修改经 `Undo.RecordObject` + `EditorUtility.SetDirty`，**全程 Undo / Redo 并触发资产保存**。画布平移 / 缩放 / 上次打开的配置经 `EditorPrefs` 持久化。
  - **画布视口交互（1.2.0 起）**：
    - **滚轮以光标为中心缩放**；画布平移改由**鼠标中键拖拽**。
    - 画布**底部常驻操作说明栏**（黑底半透明、白字），提示当前可用的鼠标 / 快捷操作。
    - 画布**空白处右键菜单**：重置视口 / 显示全部节点（缩放至框住全部）/ 定位到起始节点 / 在此处新建节点（落在光标处，可 Undo）/ 自动布局 / 吸附网格开关。
  - **多选与批量编辑（1.4.0 起）**：
    - **框选**：空白处按下左键拖拽即框选（触碰即选）；`Shift` 加选、`Ctrl` 反选，拖拽途中实时预览「松手后的选中」，`Esc` 取消。节点点选同样支持 `Shift` / `Ctrl` 修饰。
    - **批量编辑面板**：选中 ≥ 2 个节点时右侧切换为批量面板 —— 节点类型 / 备注 / 中心图标 / 画布坐标（X、Y 分轴即分轴对齐）逐字段写入全部选中节点，多值显示 `—`；节点名称 / 描述与**共有**自定义属性（按 `(id, 类型, 数组, 枚举)` 求节点类型 schema 交集）以代表节点为模板同步；状态标签条件显示代表节点的条件并提供「应用到全部选中」按钮，**不自动传播**。整块一次 Ctrl+Z 回滚。
    - **批量拖拽**：拖动任一选中节点，全部选中节点按同一位移整体跟随（吸附只对主拖拽节点算一次，保持相对布局）。
    - **批量删除**：`Delete` 键或右键菜单删除全部选中 —— 单次确认、单次 Undo，存活后代按原顺序提升到最近的存活祖先。
    - **对齐 / 分布**：工具栏提供 左 / 水平居中 / 右、上 / 垂直居中 / 下 对齐与水平 / 垂直等距分布，按**节点中心**计算（画布 Y 轴向上，「上对齐」取最大 y）；等距分布两端不动、中间均分。
    - **方向键移动**：选中（含多选）后按方向键以网格步长移动。开启吸附时移动到该方向的**下一条网格线**（不在网格上时本次按键只做对齐）；多选按主选中节点算一次位移整体施加，保持相对布局。焦点在文本输入框时不拦截。
  - **网格设置（1.4.0 起）**：工具栏排为 `网格：[吸附网格][尺寸]`。尺寸即网格最小单位格长度，存于 `NodeTreeData.gridSize`（默认 20，取值 1–500，随配置资产共享），背景网格、拖拽吸附与方向键步长共用。
  - **「标签」页签**：维护 `NodeTreeData.tags` 词表（增删标签、改名 / 说明 / 颜色 / `autoRefresh`）。顶部「标签设置」区含**「自动写入 Unlock 条件」**开关——项目级设置（存 `ProjectSettings/NodeTreeEditorSettings.asset`，随版本库共享）：开启时在画布「添加子节点 / 连线」会自动向子节点的 `Unlock` 规则写入 `NodeTree.NodeFinished(target = 父节点 ID)` 条件。
  - **右侧节点属性面板**：对每个标签用 Toolkit 的 `ConditionExpression` 内联绘制器（`ConditionExpressionDrawer`）编辑其在本节点的挂载条件，内置判定器可从下拉直接选择。
- **`NodeDrawer`**（静态）：用 IMGUI + GL 绘制节点形状（圆 / 方 / 多边形等）与连线（直线 / 贝塞尔 / 折线），仅在 `Repaint` 期绘制。
- **`NodeTreeCanvasState`**：画布交互状态（平移 / 缩放 / 选中 / 拖拽）与画布↔屏幕坐标互转。
- **`NodeTreeDataEditor`**：`NodeTreeData` 的自定义 Inspector，顶部加「在 Node Tree Editor 中编辑」按钮。

---

## 连线 Shader `NodeTree/NodeLineFlow`

URP 透明**流光连线** Shader：主纹理 + 流动纹理 UV 滚动、边缘渐变（`_EdgeFade`）、辉光（`_Glow`）、全局透明度（`_Alpha`）、流光颜色（HDR）。把使用此 Shader 的材质配到某节点类型的 `LineTypeData.material`，即可让**连向该类型（子）节点**的连线呈现动态流光。

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
