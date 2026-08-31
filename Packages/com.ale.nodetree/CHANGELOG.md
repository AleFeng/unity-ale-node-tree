# 更新日志（Changelog）

本文件记录 Node Tree System（`com.ale.nodetree`）的所有重要变更。

格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/)，版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

> 迁移说明（2026-07-28）：插件位置由 `Assets/Plugins/Ale Node Tree` 迁移至内嵌 UPM 包 `Packages/com.ale.nodetree`；程序集 `Ale.NodeTree.Runtime` / `Ale.NodeTree.Editor`、命名空间 `Ale.NodeTree.*` 保持不变。所有资产按 GUID 引用，场景 / 预制体 / 配置资产不受影响。

## [1.7.0] - 2026-08-31

运行时节点树现在可以缩放：滚轮以光标为锚点，另可接一条滑条直接拖。既有工程**零迁移** —— 新增字段全是增量，资产 YAML 缺这些键时 Unity 反序列化保留 C# 初值；不接滑条就只有滚轮，`zoomEnabled` 关掉则完全恢复旧行为。

### 新增

- **`UINodeTreeWindow` 运行时缩放**：缩的是 `nodeTreeRoot` 的 `localScale`（它就是 ScrollRect 的 Content）。新增序列化字段 `zoomEnabled` / `minZoom`（`0.1`）/ `maxZoom`（`3`）/ `defaultZoom`（`1`）/ `zoomStepPerScroll`（`1.15`）/ `zoomInputArea` / `zoomSlider` / `zoomValueText` / `zoomValueFormat`（`"{0:0.0}x"`）/ `resetZoomOnInit`。
- **新类 `UINodeTreeZoomArea`**（`MonoBehaviour` + `IScrollHandler`）：滚轮输入区，由窗口在 `InitTree` 时**自动装配**到 `ScrollRect.viewport` 上（打了 `[AddComponentMenu("")]`，不出现在 Add Component 菜单里），宿主无需接线；窗口销毁时只拆自己装上去的那一个，宿主手工挂的原样留着。

  **为什么必须单开一个组件、而且必须装在 `Viewport` 上**：`ScrollRect` 自己就实现了 `IScrollHandler`，而 uGUI 的派发是 `ExecuteEvents.GetEventHandler<IScrollHandler>(射线命中物)` —— 从命中物**向上**找第一个实现者，找到即止。窗口通常挂在整个界面的根上、是 ScrollView 的**祖先**，永远排在 ScrollRect 后面；挂 `Content` 上也不行 —— 光标停在空白处时射线命中的是 `Viewport` 的 Image，`Content` 根本不在向上的那条路径里。只有 `Viewport` 两条路径都占先：命中节点是「节点 → Content → **Viewport** → ScrollView」，命中空白是「**Viewport** → ScrollView」。

  走 `IScrollHandler` 而不是直接读 `Input.mouseScrollDelta`，还顺带绕开了新旧输入系统的分歧 —— EventSystem 的输入模块已经把两者归一化，两种模块下每格滚轮都是 ±1。

- **Demo 窗口预制体加了缩放滑条**：`UIStoryTreeWindow.prefab` 底部居中新增 `SliderZoom`（Unity 默认 Slider 结构）与倍率文本 `TxtZoom`，并已接到窗口上。`Assets/Demo` 与 `Samples~/Demo` 两份拷贝已同步。

### 变更

- **滚轮以光标为锚点**：改完 `localScale` 后给 `nodeTreeRoot.anchoredPosition` 补一段位移，使光标下的那个点停在原处 —— 与可视化编辑器画布的滚轮缩放手感一致。滑条没有「光标位置」可言，故以**视口中心**为锚点。
- **滑条走归一化取值 + 对数映射**：窗口在 `InitTree` 时强制把滑条设成 `minValue = 0` / `maxValue = 1` / `wholeNumbers = false`，倍率映射由窗口负责（`t = ln(zoom / minZoom) / ln(maxZoom / minZoom)`）。这样换缩放范围时不必再去改每个界面上滑条的 Min / Max；对数映射也让低倍段（0.1x~0.5x）不至于被挤成滑条最左边一小截。默认范围下 1.0x 落在 68% 处。反向刷新滑条走 `SetValueWithoutNotify`，否则「写滑条 → `onValueChanged` → 缩放 → 再写滑条」会成环。
- **滚轮步进是乘性的**（`zoom *= zoomStepPerScroll ^ scrollDelta.y`，用 `Pow` 以便触控板那种小数增量也平滑生效）：配对数滑条，每格滚轮在滑条上移动的距离才是等距的，缩放手感在任何倍率下也才一致（0.2x 时不会一格就跳一大截）。默认 `1.15`，走完 0.1x~3.0x 全程约 24 格。
- **`InitTree` 会落一次倍率**：`resetZoomOnInit`（默认开）时回到 `defaultZoom`，关掉则跨越重建保留当前倍率；`zoomEnabled` 为 `false` 时把树还原成 1 倍，不让上一次的缩放状态留在场上。
- **缩放时停掉 ScrollRect 的惯性**（`StopMovement()`）：否则滚轮缩放期间残留的甩动会在随后几帧把刚对齐的视口又带跑。
- **`OnValidate` 夹住缩放参数**：`minZoom` ≥ `0.01`（对数映射要除 `ln(maxZoom / minZoom)`，`minZoom` ≤ 0 会除出 NaN）、`maxZoom` > `minZoom`、`defaultZoom` 落在两者之间、`zoomStepPerScroll` > 1。
- **既有表现天然兼容，未作改动**：ScrollRect 的边界计算走 `content.GetWorldCorners()`，`localScale` 天然被计入，夹取 / 弹性回弹 / 惯性照常工作；视口裁剪本就在 `LateUpdate` 里比较 `lossyScale`、并按 `lossyScale` 折算节点半尺寸到屏幕像素，缩放后 Spawn / Despawn 依旧正确；信息弹窗的偏移施加在弹窗层空间，**不随缩放变小**；连线 Mesh 在 Content 之下，随之缩放（线宽也一起缩）；`UIScrollingBackground` 只跟 `Content.anchoredPosition` 的**增量**走，故锚点补偿会让背景平移一下（与手动平移无异），背景本身不缩放。
- **`NodeTreeData.zoom` 仍是保留字段**：那是**编辑器画布**的缩放，与运行时缩放是两码事，本次没有复用它。

### API

- `UINodeTreeWindow` **新增**：`public float Zoom { get; }`、`public void SetZoom(float zoom)`（以视口中心为锚点）、`public void SetZoom(float zoom, Vector2 screenPoint)`（以指定屏幕点为锚点）、`public void ResetZoom()`、`public event Action<float> ZoomChanged`；序列化字段 `zoomEnabled` / `minZoom` / `maxZoom` / `defaultZoom` / `zoomStepPerScroll` / `zoomInputArea` / `zoomSlider` / `zoomValueText` / `zoomValueFormat` / `resetZoomOnInit`。
- **新类** `UINodeTreeZoomArea`：`public UINodeTreeWindow Window { get; internal set; }`、`public void OnScroll(PointerEventData eventData)`。

## [1.6.0] - 2026-08-25

信息弹窗从节点内部拆出，改由 `UINodeTreeWindow` 用对象池统一管理。**本版含破坏性变更**（版本号按项目约定停留在次版本位），升级前请先读下方迁移步骤。

### 破坏性变更

- **`UINodeBase` 移除内置信息弹窗**：删去序列化字段 `infoPanel` / `fadeInDuration` / `fadeOutDuration` 与全部弹窗实现（`ResetInfoPanel` / `InfoPanelFadeIn` / `InfoPanelFadeOut`），**不保留兼容路径**。

  **三步迁移**：① 把节点预制体里的 `InfoPanel` 子树抽成独立预制体，根上挂 `UINodeInfoPanel`；② 接到 `UINodeTreeWindow.infoPanelPrefab`；③ 节点预制体删掉原来的 `InfoPanel` 子物体。资产 YAML 里遗留的三个键在 Unity 反序列化时自动丢弃，无需手工清理。

  **为什么要拆**：弹窗作为节点的子物体，渲染顺序跟着节点在 `NodeContainer` 里的兄弟序走，而节点是按视口裁剪**动态** Spawn 的、顺序不可控 —— 后 Spawn 的节点会盖住先 Spawn 节点的弹窗，于是「弹窗被别的节点挡住」时有时无；同时弹窗还在 ScrollView 的 `Viewport` 的 `Mask` 之内，靠近视口边缘会被切掉一半。此外每个节点预制体都要各存一份弹窗层级，改一次得改所有预制体，运行时每个激活节点也都背着一份用不上的弹窗。

  ⚠ 重写过 `OnPointerEnterNode()` / `OnPointerExitNode()` 的子类**无需改动**（基类语义不变，只是内部改成向窗口发请求）；重写过 `OnDisable()` 的子类仍须调用 `base.OnDisable()`。

### 新增

- **`UINodeInfoPanel`**（`MonoBehaviour` + `IPoolable`）：独立的信息弹窗组件，职责只有三件 —— 持有内容接线、淡入淡出、汇报自己能否被回收。序列化字段 `canvasGroup` / `fadeInDuration` / `fadeOutDuration` 与三个**可选**的内容接线 `nodeNameText` / `nodeDescText` / `iconImage`（不接线即跳过）。`Bind` / `Unbind` 为 `virtual`；`Show(bool)` / `Hide(bool)` **非虚**，表现交给 `OnShowBegin(bool)` / `OnHideBegin(bool)` 两个 `protected virtual` 钩子 —— `IsVisible` 是窗口回收弹窗的唯一依据，子类漏调 `base` 会让弹窗永不回收（对象池泄漏），故用非虚入口挡住。
- **`UINodeTreeWindow` 统一管理弹窗**：新增序列化字段 `infoPanelPrefab`（留空则整套弹窗功能静默关闭）/ `infoPanelOffset`（弹窗相对**节点中心**的偏移，默认 `(0, 120)` 即节点上方）/ `infoPanelLayer`（留空则自动创建）/ `infoPanelPreload`，以及三个公开方法 `ShowNodeInfoPanel(string)` / `HideNodeInfoPanel(string)` / `HideAllNodeInfoPanels(bool)`。弹窗按 `nodeId` 索引，**支持同时显示多个**；悬停时按节点位置定位，并在 `LateUpdate` 里跟随滚动 / 缩放。
- **`UINodeBase.OwnerWindow`**（`public get` / `internal set`，由窗口在 Spawn 时注入）与序列化开关 `showInfoPanelOnHover`（默认 `true`），以及 `ShowInfoPanel()` / `HideInfoPanel()` 两个 `protected virtual` 转调方法 —— 子类可重写以附加条件（如未解锁的节点不弹）。
- **Demo 新增 `UINodeInfoPanel.prefab`**：由原 `UINodeNomal` 的 `InfoPanel` 子树抽出（两个节点预制体的弹窗在 sprite、颜色、PPU 与本地化 key 上完全一致，合并为一份无损失），两个节点预制体的 `InfoPanel` 子物体已移除，窗口预制体已接线。`Assets/Demo` 与 `Samples~/Demo` 两份拷贝已同步。

### 变更

- **弹窗层放在 ScrollView 之外**：窗口在自己的 `RectTransform` 下自动创建 `InfoPanelLayer`（最后一个兄弟、铺满、`pivot` 居中、整层 `blocksRaycasts = false`），因此弹窗**压在所有节点之上**且**不再被 `Viewport` 的 `Mask` 裁剪**。本组件不在 `RectTransform` 上时会告警并回落 `nodeTreeRoot`，此时请手动指定 `infoPanelLayer`。
- **弹窗恒不阻挡射线**（`UINodeInfoPanel.Awake` 强制 `blocksRaycasts = false`）。旧实现在淡入时置 `true` 是安全的 —— 弹窗是节点的子物体，uGUI 沿层级向公共祖先派发，指针落在弹窗上不会触发节点的 `PointerExit`。拆出去之后二者分属两棵子树，一旦挡住光标就会 `PointerExit` → 隐藏 → 光标回到节点 → `PointerEnter` → 显示，形成肉眼可见的闪烁死循环。
- **描述为空的节点不弹窗**：`ShowNodeInfoPanel` 在取用池实例**之前**先解析 `NodeData.nodeDesc`（`ResolveText()`，启用本地化时取当前语言译文），为空或纯空白则直接返回 `null`、完全不弹 —— 一个只有内边距的空框既给不出信息，又会挡住下面的节点。若该节点此刻正显示着弹窗（描述被运行时清空后又悬停一次），会一并收起。⚠ 判据只看 `nodeDesc`：弹窗文案若不由它驱动（如挂 `LocalizeStringEvent` 显示固定文本），节点的 `nodeDesc` 仍需填写，否则一律不弹。
- **弹窗位置 = 节点中心 + `infoPanelOffset`**：`NodeData.position` 本就是节点中心，无需再按节点尺寸折算；偏移配在**窗口**上而非弹窗预制体上，一处改全树生效。
- **弹窗不随画布缩放**：偏移施加在弹窗层空间，缩放节点树画布时弹窗的尺寸与偏移保持不变。这是有意为之 —— 弹窗是给人读的，不该跟着缩到看不清。
- **回收走轮询而非补间回调**：`Hide()` 只开始淡出、不立即归还池，窗口在 `LateUpdate` 里轮询 `IsRecyclable` 才回收。好处是淡出途中重新悬停能复用同一实例淡回去、不闪；也避免了「完成回调在时长 ≤ 0 与 `Kill(true)` 两条路径上同步触发」导致的遍历中途改集合。
- **既有节点预制体无需迁移**（除上述三步）：新增的 `showInfoPanelOnHover` 是纯增量字段，资产 YAML 缺该键时 Unity 反序列化保留 C# 初始值 `true`。

### API

- `UINodeBase` **移除**：`protected CanvasGroup infoPanel`、`protected float fadeInDuration`、`protected float fadeOutDuration`。
- `UINodeBase` **新增**：`public UINodeTreeWindow OwnerWindow { get; internal set; }`、`protected bool showInfoPanelOnHover`、`protected virtual void ShowInfoPanel()`、`protected virtual void HideInfoPanel()`。
- `UINodeTreeWindow` **新增**：`public UINodeInfoPanel ShowNodeInfoPanel(string nodeId)`、`public void HideNodeInfoPanel(string nodeId)`、`public void HideAllNodeInfoPanels(bool instant = false)`；序列化字段 `infoPanelPrefab` / `infoPanelOffset` / `infoPanelLayer` / `infoPanelPreload`。
- **新类** `UINodeInfoPanel`：`public virtual void Bind(NodeData, NodeTypeData)`、`public virtual void Unbind()`、`public void Show(bool instant = false)`、`public void Hide(bool instant = false)`、`protected virtual void OnShowBegin(bool)`、`protected virtual void OnHideBegin(bool)`、`protected virtual void OnDisable()`、只读属性 `BoundNode` / `BoundType` / `Rect` / `IsVisible` / `IsRecyclable`。

## [1.5.2] - 2026-08-24

「全部解锁」测试开关此前对使用自定义标签的宿主不起作用，现在改为可配置。

### 变更

- **`UINodeTreeWindow.unlockAllForTest` 更名为 `forceUnlockForTest`**，并新增标签列表 `forceUnlockTags`
  （`List<string>`，默认**留空**）：勾选开关后，给每个节点挂上列表里的标签；**留空则挂上标签词表
  （`NodeTreeData.tags`）里的全部标签**。既有资产由 `[FormerlySerializedAs]` 承接，勾选状态不会丢。

  此前它只挂 `NodeTreeTags.Unlock`。但节点能不能进由宿主说了算，而宿主常常用自己定义的标签来判
  （如「本周目读完」「跨周目读过」）——那类宿主勾上开关会**毫无反应**，而一个测试开关最不该有的
  表现就是「点了没动静」。

  「留空即全挂」是刻意的默认：零配置就有效，宿主改了标签名也不会让这个开关静默失效。需要收窄时
  （例如某个标签另有副作用、不该被测试开关带上）再逐条填写，此时**严格按填写的来**，不再补 `Unlock`。
  留空路径下 `Unlock` 仍无条件挂上，词表被改过、没有这一项的工程行为不变。

### API

- `UINodeTreeWindow` 序列化字段：`unlockAllForTest` → `forceUnlockForTest`（`[FormerlySerializedAs]` 兼容旧资产）；
  新增 `forceUnlockTags`（`List<string>`，默认空 = 词表全部标签）。

## [1.5.1] - 2026-08-24

条件求值可以取到本插件之外的数据源了。

### 新增

- **外部服务注入** `NodeTreeSaveDataManager.ExternalServices`（`IConditionContext`，默认 `null`）：`NodeTreeConditionContext.GetService<T>()` 现在是「本插件只答 `INodeTreeStateSource`，其余类型转问宿主」。此前它硬编码为 `_state as T`，宿主的判定器（「在那个分支点选了第几项」「有没有那件道具」）**永远取不到数据源**，一律 fail-closed 返回 `false` —— 条件永不成立，且没有任何征兆。默认 `null` 时行为与 1.5.0 完全一致。
- `NodeTreeConditionContext` 构造函数新增可选参数 `IConditionContext fallback`。

### 注意

- ⚠️ **每次进入播放都要重新注入**：本类是静态单例，`ResetStatics`（`[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`）会把 `_instance` 整个置空以防跨播放残留，注入的上下文一并蒸发。请在场景对象的 `Awake` 里注入 —— 那时已晚于这次清空。
- setter 内部会丢弃懒建的上下文缓存并重建，因此运行中换服务表即时生效；否则新注入会**静默不生效**。

### API

- `NodeTreeSaveDataManager` 新增：`public IConditionContext ExternalServices { get; set; }`。
- `NodeTreeConditionContext` 新增构造重载：`NodeTreeConditionContext(INodeTreeStateSource state, object subject = null, IConditionContext fallback = null)`。

## [1.5.0] - 2026-08-23

节点 UI 新增鼠标悬停高亮，并把「高亮开始 / 结束」开放为子类可重写的钩子。

### 新增

- **节点悬停高亮**：`UINodeBase` 新增高亮层字段 `highlightImage`（只改其 `color.a`，不动 RGB）与 `highlightAlpha` / `highlightFadeInDuration` / `highlightFadeOutDuration`，鼠标悬停时淡入、移出时淡出（基于 toolkit 中央 Tween）。**状态与表现分离**：`SetHighlight(bool on, bool instant = false)` 是唯一写入口（悬停事件与外部调用共用，同态调用幂等），具体表现交给 `OnHighlightBegin(bool)` / `OnHighlightEnd(bool)` 两个 `protected virtual` 钩子 —— 子类只重写钩子即可实现自己的高亮表现（发光、缩放、粒子，或按解锁状态区别对待），无需也不应自行改动状态位；`protected bool IsHighlighted` 供子类组合判断。`highlightImage` 未配置时全部路径静默跳过。
- **Demo 节点预制体加提亮罩**：`UINodeNomal` / `UINodeEnding` 各新增 `ImgHighlight` 子物体（置于底板之后、图标之前，stretch 铺满、初始 alpha = 0、关闭 Raycast Target），**sprite 复用各自底板** —— 圆形节点得圆形高亮、圆角矩形节点得圆角高亮，无需新美术资源。`Assets/Demo` 与 `Samples~/Demo` 两份拷贝已同步。

### 变更

- **悬停虚方法不再因未配置弹窗而提前返回**：`OnPointerEnterNode()` / `OnPointerExitNode()` 原本开头是 `if (!infoPanel) return;`，会让没有信息弹窗的节点连高亮也一并拿不到；现改为弹窗与高亮各自独立判断。
- ⚠ **对既有子类是行为增量**：调用 `base.OnPointerEnterNode()` / `base.OnPointerExitNode()` 时，1.5.0 起会额外走一遍 `OnHighlightBegin` / `OnHighlightEnd`。既有预制体上 `highlightImage` 必为 null，故净效果为 no-op。
- 既有预制体**无需迁移**：新增的 `[SerializeField]` 字段是纯增量，资产 YAML 中缺这些键时 Unity 反序列化保留 C# 字段初始值（`highlightAlpha = 1`、两个时长各 `0.15`），`highlightImage` 为 null 走静默跳过路径。

### 修复

- **面板停用导致信息弹窗永久常亮**：节点树面板整体 `SetActive(false)` 关闭时，节点不走对象池归还，且悬停中的节点收不到 `PointerExit`（Unity 不向非激活对象派发），而 toolkit 的补间**不随 GameObject 停用而停止** —— 弹窗会一路淡入到全亮，且守卫位卡在「已淡入」，再次打开面板后该节点弹窗常亮、再悬停也毫无反应。现由新增的 `protected virtual void OnDisable()` 统一复位弹窗与高亮。声明为 `virtual` 是因为 Unity 只调用最派生的同名方法，子类若自行写 `private void OnDisable()` 会**静默**吞掉基类实现（有 `virtual` 时至少会触发 CS0114 提示）。
- **`ResetInfoPanel()` 未先打断补间就写 `alpha = 0`**：在途的淡入会继续把它推回去。现将 `Kill()` 收进该方法内部（它有绑定 / 解绑 / 停用三个调用点，逐处手写易漏）。

### API

- `UINodeBase` 新增：`public void SetHighlight(bool on, bool instant = false)`、`protected bool IsHighlighted { get; }`、`protected virtual void OnHighlightBegin(bool instant)`、`protected virtual void OnHighlightEnd(bool instant)`、`protected virtual void OnDisable()`。
- 序列化字段新增：`highlightImage`（`Image`）、`highlightAlpha`（`[Range(0,1)]`，默认 1）、`highlightFadeInDuration` / `highlightFadeOutDuration`（默认各 0.15）。

## [1.4.1] - 2026-08-23

修复方形节点在画布上相对连线与其它形状的垂直偏移。

### 修复

- **方形（Square）节点绘制偏移**：方形是唯一用 `EditorGUI.DrawRect`（IMGUI 路径）填充的节点形状，而画布上其余形状、连线与箭头全部经 `SetGLClip` 建立的 GL 视口（`GL.Viewport` + `GL.LoadPixelMatrix`）绘制。两条路径的坐标变换不同源，使方形节点相对连线与其它形状出现固定像素的垂直偏移 —— 缩小视口后节点随之变小，该固定偏移的相对占比放大，偏移更明显。现将方形改为 GL 绘制，与全部其它形状共用同一套坐标变换与硬件裁剪；描边仍为内描边，视觉语义与此前一致。
- **水平胶囊形（HorizontalCapsule）中段错位**：其中间矩形与上下两条边框同样走 IMGUI、而两端半圆走 GL，存在同源问题，一并改为 GL；并补上「宽度小于高度」时中段宽度为负的守护（此时只画两端半圆）。
- **连线箭头在节点尺寸放大时不跟随**：箭头尖端由子节点中心沿到达方向回退一段距离得到，该距离此前取 `Min(w, h) * 0.5` 的**内切圆**半径 —— 当「节点尺寸」的 X 大于 Y 时被 Y 钳住，故 X 缩小时箭头会跟随、X 放大时却停在原处。现改为按 `(w/2, h/2)` 为半轴的**内切椭圆**沿到达方向求交，X / Y 任一方向的尺寸变化都会让箭头跟随；正方形尺寸（如默认 100×100）下的结果与此前完全一致。`NodeDrawer.GetArrowTipAndDir` 相应新增 `Vector2 halfSize` 重载，原 `float radius` 重载保留并转调，既有调用无需改动。

## [1.4.0] - 2026-08-23

编辑器画布支持框选多选与右侧批量编辑面板，配套批量拖拽 / 删除与对齐分布。

### 新增

- **画布框选多选**：在画布空白处按下左键并拖拽即框选，**触碰即选**；`Shift` 加选（并集）、`Ctrl`/`Cmd` 反选（对称差），拖拽途中实时预览「松手后的选中」。框选锚点存**画布坐标**，途中滚轮缩放 / 中键平移锚点不漂移；位移不足 3 屏幕像素时退化为「点击空白取消选中」（保持既有行为）；`Esc` 取消并还原起框前的选中。节点点选同样支持 `Shift` 加选 / `Ctrl` 反选；点中**已在多选集内**的节点时，收敛为单选推迟到「未发生拖动的 MouseUp」，以免破坏批量拖拽。
- **右侧「批量编辑」面板**：选中两个及以上节点时，右侧面板由单节点属性切换为批量面板 —— 顶部选中计数、可折叠的只读节点列表（每行「定位」只移动视口、**不改动选中集**）。共有字段 **节点类型 / 备注 / 中心图标 / 画布坐标** 逐字段做变更检查后写入全部选中节点，值不一致时显示为 `—`；画布坐标拆为 X / Y 两个独立字段，填哪个轴就让全部选中节点在该轴**对齐**，另一轴各自保持原值。**节点名称 / 描述与自定义属性**以代表节点为模板，改动后经 `AttributeValue.SetRaw` 原地覆写其余选中节点（曲线深拷贝，各节点互不共享）；自定义属性按 `(id, 类型, 是否数组, 枚举类型)` 求各节点类型 schema 的**交集**，并报告因类型不同而未显示的字段数。整块编辑一次 `Undo` 收束，一次 Ctrl+Z 整体回滚。
- **状态标签条件「应用到全部选中」**：批量面板显示代表节点的标签条件，每条下方提供按钮显式复制到其余选中节点（`ConditionExpression.Clone` 深拷贝）。条件通常逐节点特定（如「前置节点 A 已完成」），故**不随编辑自动传播**，只在点按钮时应用。
- **批量拖拽移动**：拖动任一选中节点时，全部选中节点按**同一位移**整体跟随，保持相对布局。网格吸附只对主拖拽节点计算一次再统一施加 —— 逐节点各自吸附会把原本不在网格上的节点互相拉拢。
- **批量删除**：`Delete` 键与节点右键菜单删除当前**全部选中**节点 —— 单次确认、单次 Undo，一次性解开被删集合内部的父子关系（连续多层都被删时，存活后代按原顺序提升到最近的存活祖先），并清理剩余节点 `Unlock` 条件中指向被删节点的 `NodeTree.NodeFinished` 项。节点树至少保留一个节点。
- **对齐 / 分布工具栏**：工具栏新增 左 / 水平居中 / 右、上 / 垂直居中 / 下 对齐，以及水平 / 垂直等距分布，均按**节点中心**计算（`NodeData.position` 即中心）。画布 Y 轴向上，故「上对齐」取**最大** y、「下对齐」取最小 y。等距分布先按该轴坐标排序，两端节点保持不动、中间节点均分间隔。对齐需选中 ≥ 2 个、分布需 ≥ 3 个，否则按钮灰显。
- **方向键移动选中节点**：选中（含多选）后按 `↑` `↓` `←` `→` 以**网格最小单位格长度**为步长移动。开启「吸附网格」时移动到该方向上的**下一条网格线** —— 节点不在网格交叉点上时，这一次按键只做对齐，再按才走整格；关闭时单纯 ± 一格。多选时位移按**主选中节点**计算一次再整体施加，保持相对布局。画布 Y 轴向上，故 `↑` 增大 y。焦点位于文本输入框时不拦截方向键（画布输入在右侧面板之前处理，否则输入框内的光标将无法移动）。每次按键对应一次 Ctrl+Z。
- **网格尺寸可配置**：`NodeTreeData` 新增 `gridSize`（默认 20，随配置资产走、团队共享），工具栏排为「网格：[吸附网格][尺寸]」，取值 clamp 至 `[1, 500]`。背景网格间距、拖拽吸附与方向键步长共用此值。既有资产无需迁移 —— 资产 YAML 中缺该键时，Unity 反序列化保留字段初始值 20。

### 变更

- **节点拖拽现在可撤销**：整段拖拽手势在第一次真正移动时记一次 `Undo.RecordObject`，一次拖拽对应一次 Ctrl+Z。此前拖拽是唯一不记录 Undo 的修改路径。
- **删除改为延迟执行**：`EditorUtility.DisplayDialog` 会在 `OnGUI` 中途泵消息循环、并带着已变更的配置返回，导致同一帧内后续面板发出的控件数与本帧 Layout 不一致。现在触发时只登记待删 ID，实际删除排在**下一个 Layout 事件的开头**。
- 画布底部操作说明栏补充框选、`Shift` 加选 / `Ctrl` 反选与 `Delete` 删除选中的说明。
- 节点类型的「节点尺寸」输入下限收紧为 1：0 / 负尺寸会让节点矩形退化，令悬停、点击与框选判定同时失效。
- 背景网格在**屏幕间距不足 3px 时不再绘制**：网格尺寸可配置后，小格配合小缩放（如 1 格 × 0.2 缩放）会产生近 5000 条 `DrawRect`，而那种密度本来也糊成一片。

### 修复

- **选中状态残留**：删除节点时，若删的不是主选中项，该 ID 会残留在选中集中成为悬空引用；若删的正是主选中项，又会连带清空其余选中。切除子树同理，撤销 / 重做后也只校验了主选中项。现统一改为逐项剔除（`RemoveFromSelection` / `PruneSelection`）。
- **拖拽状态残留**：鼠标拖出编辑器窗口后松开时 `MouseUp` 不会送达，此前会让「正在拖拽节点」状态永久卡住，之后每次拖拽都会把上次选中的节点瞬移到光标处。现经 `Event.rawType` 与 `MouseLeaveWindow` 双重兜底结束（为此在 `OnEnable` 开启 `wantsMouseEnterLeaveWindow`），并在空白处重新按下时自愈；切换配置资产时也一并清理。

### API

- ⚠ **`NodeTreeCanvasState` 的选中由单个 `string` 改为有序集合**：新增 `SelectedNodeIds`（只读，末位为主选中）/ `SelectedCount` / `IsSelected` / `SelectSingle` / `AddToSelection` / `RemoveFromSelection` / `ToggleSelection` / `ClearSelection` / `SetSelection` / `PruneSelection`。`SelectedNodeId` 保留为兼容属性 —— 读取返回主选中，写入等价于「收敛为单选」（写 `null` 即清空），既有调用无需改动即可保持原语义。

## [1.3.0] - 2026-08-22

新增四方连续无限滚动背景组件 `UIScrollingBackground`（RawImage uvRect UV 滚动）。

### 新增

- **四方连续无限滚动背景 `UIScrollingBackground`**：以 RawImage **初始 Rect 尺寸**为一块 tile（≤0 时回退纹理像素尺寸），`uvRect.size = 视口/tile` 铺满视口，滚动仅偏移 `uvRect`、靠纹理 **Repeat** 采样实现四方连续无限平铺——单物体单 DrawCall、零 tile 实例、零逐帧分配（静止时零开销）。可选绑定 `ScrollRect`：`LateUpdate` 轮询 `Content.anchoredPosition` 增量、与 Content **同向**滚动（统一覆盖拖拽 / 惯性 / 回弹 / 代码驱动，首帧与重绑定只快照不回放、无跳变）；`speedMultiplier` X / Y 分轴速度倍率（`Vector2`：1 = 同速，≠1 视差，0 该轴静止，负值该轴反向）。视口模式 `SelfRect`（自身 RectTransform，image 为子物体时自动拉伸铺满）/ `Screen`（按根 `Canvas.scaleFactor` 换算画布单位并轮询分辨率变化）。纹理 Wrap 非 Repeat 时 Awake 检测并告警；未绑定 ScrollRect 时可经公开 API 手动驱动。

### 变更

- **Demo 背景改为无限滚动**：`UIStoryTreeWindow` 预制体的 `ImgBackground` 由 `Image`（Sprite）改为 `RawImage`（直接引用 Texture2D、关闭 `raycastTarget`），并挂载 `UIScrollingBackground`（绑定 Scroll View、`Screen` 视口、倍率 1）；`T_NodeTree_Background.jpg` 导入设置 Wrap Mode 由 Clamp 改为 **Repeat**（顺带修复原 1820 宽背景盖不满 1920 屏的边缘缝隙）。

### API

- `UIScrollingBackground`：属性 `SpeedMultiplier`（`Vector2` 分轴）/ `ViewportMode` / `TileSize`（只读）/ `ScrollOffset`（只读）；方法 `ScrollBy(Vector2)`（手动视觉增量，不乘倍率）、`SetScrollOffset(Vector2)`、`ResetOffset()`、`SetScrollRect(ScrollRect)`（运行时重绑定，无跳变）、`Refit()`（布局变化后重新适配视口）。

## [1.2.0] - 2026-08-03

视口裁剪修复 + 编辑器视口 UX + 全工程 Bug/优化清理。

### 新增

- **编辑器视口 UX**：画布底部常驻**操作说明栏**（黑底白字）；**滚轮改为以光标为中心缩放**（画布平移改由鼠标中键拖拽）；画布空白处**右键菜单** —— 重置视口 / 显示全部节点（缩放至框住全部）/ 定位到起始节点 / 在此处新建节点（落在光标处，可 Undo）/ 自动布局 / 吸附网格开关。

### 变更

- ⚠ **连线样式归属改为「子节点类型」**：每条连线（含箭头）现采用其**目标（子）节点类型**的 `LineTypeData`（线型 / 线宽 / 材质 / 颜色），与字段 Tooltip「从父节点连向此类型节点」一致；编辑器与运行时统一。此前按**父**节点类型绘制，依赖旧行为的项目视觉会变化，需在各节点类型上重新配置 `line`。

### 修复

- **编辑器画布视口裁剪**：GL 绘制经 `GL.Viewport` 建立硬件裁剪（按 DPI 换算、绘制后复原），节点形状 / 连线 / 箭头 / 预览线在视口边缘**像素级裁切**；移除会扭曲曲线 / 折线的端点预裁剪，改为不改端点的包围盒粗剔除。修复此前「节点整块显隐、连线被裁时位置与形状改变」。
- **运行时空安全**：`InitPools` / `InitLineMeshRenderers` / `SpawnNode` / `RefreshVisibility` 跳过 null 节点类型 / 空 `typeName` / 空 `nodeTypeRef` / 空 `nodeId` 与重复 `nodeId`，坏配置只被跳过、不再中断 `InitTree`；`NodeTreeData.GetRootNodes` / `GetParentId` / `EnsureBuiltin*` 补 null 守护。
- **运行时视口裁剪坐标与缩放**：改用容器 `TransformPoint` → 按 Canvas 渲染模式投影屏幕，兼容 `CanvasScaler` `scaleFactor ≠ 1` 与 Screen Space-Camera（旧法误差随距离放大）；以节点**包围盒**判定而非仅中心；spawn / despawn 加**滞回**边距消除边界抖动；缩放画布（`localScale`）也触发重算。
- **运行时销毁泄漏**：`OnDestroy` 归还全部激活节点克隆（清除 `ToolkitPool.Links` 静态引用）、销毁 `NodeContainer` / `LineContainer` 与各对象池 GameObject，覆盖 `nodeTreeRoot` 挂在组件之外（ScrollView-Content 用法）时的孤儿克隆与残留层级。
- **编辑器接线生命周期**：删除 / 切除节点时清理所有父节点 `childNodeIds` 中的悬空引用，并清除所有节点 `Unlock` 条件中指向已删节点的 `NodeTree.NodeFinished` 项（避免被提升子节点永久锁死），提升的子节点重接到祖父保持解锁链；断线时的条件删除改由「自动写入 Unlock 条件」开关门控，避免误删手工条件。
- **编辑器自动布局与连线环检测**：`LayoutSubtree` 增 visited 集合，修复回边导致的 StackOverflow，并令多父钻石节点只布局一次；`AddConnection` 增环检测（目标可达起点则拒绝并提示）。
- **编辑器状态与脏标记健壮性**：切换 / 加载配置时清理残留选中；节点类型 / 标签 `ReorderableList` 删除按钮加 `index` 越界守护；删除节点类型时置空引用它的节点 `nodeTypeRef` 并告警；撤销 / 重做保留条件折叠态并清理已失效选中，一帧内的编辑合并为一次 Ctrl+Z；标签重命名等**计数中性**变化也正确落盘。

### 优化

- **运行时连线 Mesh 分配**：`NodeLineBuilder` 改用静态复用 scratch 缓冲（顶点 / UV / 索引、曲线采样与折线路径 grow-only 数组），退化段（重合 / 自环）跳过；`RebuildAllLineMeshes` 复用分组字典与列表。
- **编辑器每帧分配**：连线绘制复用顶点 / UV / 索引 / 颜色缓冲；节点拖拽跳过重名重扫（免每帧 `Dictionary` / `HashSet`）；标签条件行复用 `GUIContent`。

### API

- `NodeData.RebuildTagRules(NodeTreeData)` 由 `void` 改为返回 `bool`（本次同步是否改变了 `tagRules`）；旧调用忽略返回值即可，向后兼容。

> 说明：`autoRefresh` 标签的**空条件视为通过**（fail-open）—— 起始 / 根节点据此自动解锁属预期；非根节点若漏配 `Unlock` 条件会自动挂标签，属配置错误的可接受折衷，请为非根节点显式配置解锁条件。

## [1.1.0] - 2026-07-29

节点类型「自定义属性字段」：以 `com.ale.toolkit` 的 `AttributeValue` 属性体系取代原有的节点自定义键值数据。

### 新增

- **节点类型自定义属性字段（schema）**：`NodeTypeData.attributes`（`List<AttributeDefinition>`，来自 `com.ale.toolkit`）—— 在节点类型模板上定义带类型的自定义字段（`Bool` / `Int` / `Float` / `String` / `Text` / `Enum` / `Vector*` / `Color` / `Sprite` 等对象引用及数组形态）。编辑器右侧「节点类型」面板接入 `AttributeDefinitionListDrawer`，支持可视化增删、拖拽排序、设置默认值，全程 Undo。
- **节点实例自定义属性值**：`NodeData.attributeValues`（`List<AttributeEntry>`），按所属节点类型的 schema 逐节点配置。编辑器右侧节点面板以 `AttributeFieldDrawer` 按类型绘制，支持 Undo 与右键 / `Ctrl+C·V` 复制粘贴。
- **强类型取值 API**：`NodeData` 继承 `com.ale.toolkit` 的 `AttributeOwner`，提供 O(1) 懒加载字典缓存与 `GetAttributeValue<T>(id, fallback)` / `SetAttributeValue<T>(id, value)` / `GetEntry(id)`；新增 `NodeData.RebuildAttributes(NodeTreeData)` 按节点类型 schema 协调属性值集合（补默认 / 删多余 / 类型漂移重置，幂等）。

### 变更

- 节点自定义数据由「纯字符串键值对」升级为「节点类型定义 schema + 节点实例强类型填值」，与 `com.ale.inventory` 的属性字段模式同构。node-tree 自身无枚举类型来源，`Enum` / `EnumIntPair` 字段的枚举类型解析依赖项目侧向 toolkit 全局 `EnumTypeResolver` 注入（本包保持透明）。

### 移除

- ⚠️ **不兼容变更**：删除 `NodeCustomData` 类型、`NodeData.customDataList` 字段，以及 `NodeData.GetCustomData(string)` / `SetCustomData(string, string)` 方法。既有资产中旧的键值自定义数据在升级后会被 Unity 丢弃、**不自动迁移**；如需保留，请在升级前手工转存为对应节点类型的属性字段与实例值。

## [1.0.0] - 2026-07-28

首个正式版本：可视化节点树编辑器 + 运行时节点树展示。

### 新增

- **配置资产 `NodeTreeData`**（ScriptableObject）：集中承载节点实例、节点类型、状态标签词表与画布布局；`Assets > Create > NodeTree System/Config Node Tree` 创建，新建自动填充内置节点类型（普通 / 结局）、内置状态标签（`Unlock` / `Finished`）与起始节点。
- **数据模型**：`NodeData`（ID / 类型引用 / 位置 / 子节点 / 状态标签规则 `tagRules` / 自定义属性值 `attributeValues`，继承 `com.ale.toolkit` 的 `AttributeOwner`）、`NodeTypeData`（形状 `ENodeShape` / 尺寸 / 颜色 / 图标 / UI 预制体 / 连线样式 `LineTypeData`：`ELineType` 直线-曲线-折线 + 线宽 + 材质）、状态标签词表 `NodeTagData`（`tagName` / `description` / `color` / `autoRefresh`，内置 `Unlock`（自动刷新）与 `Finished`）、逐节点标签规则 `NodeTagRule`（`tagName` + 承载挂载门槛的 `ConditionExpression`，空表达式即无门槛；由 `NodeData.RebuildTagRules` 随词表同步、`GetTagRule` 取用）。
- **条件系统**：接入 `com.ale.toolkit` 的条件系统 `Ale.Condition`（条件以 `ConditionExpression` 承载，表达式 → 组 → 项 → 参数两级 AND/OR；扩展方式为实现 `IConditionEvaluator` 并打 `[ConditionEvaluator("Key")]` 特性自动发现注册，判定器经 `ctx.GetService<T>()` 读数据源）；node-tree 运行时内置判定器 `NodeFinishedEvaluator`（键 `NodeTree.NodeFinished`）/ `NodeUnlockedEvaluator`（键 `NodeTree.NodeUnlocked`）/ `NodeHasTagEvaluator`（键 `NodeTree.NodeHasTag`），经数据源接口 `INodeTreeStateSource` 与上下文 `NodeTreeConditionContext` 读取节点标签状态，标签名常量见 `NodeTreeTags`（`Unlock` / `Finished`）。
- **存档管理器 `NodeTreeSaveDataManager`**（静态单例，实现 `INodeTreeStateSource`）：以标签制维护每个节点的状态标签，提供通用标签读写 `HasTag` / `AddTag` / `RemoveTag` / `GetTags` / `ClearNode`，整份存取与序列化 `Get` / `Set` / `Save`（JSON）/ `Load` / `Reset`（不自动落盘，交宿主保存），以及内部自判条件的便捷门面 `TrySetTag` / `TrySetUnlock` / `TrySetFinished` 与自动标签重算 `RefreshAllNodeStates`（对 `autoRefresh` 标签按条件定点迭代、达成即挂、单调不摘，支持链式解锁）；数据结构 `NodeTreeSaveData` / `NodeTagState`。
- **运行时 UI**：`UINodeTreeWindow`（按节点位置自动计算根容器尺寸、按节点类型维护对象池、视口裁剪按需 Spawn/Despawn、合批生成连线 Mesh 降 DrawCall、`InitTree` 初始化、`refreshStatesOnInit` 开启时于 `InitTree` 自动调用 `RefreshAllNodeStates()` 刷新自动标签，并公开 `RefreshAllNodeStates()` 方法）、`UINodeBase`（`IPoolable` 节点 UI 基类，`OnBindData` 绑定 + 图标 / 本地化文本 / 悬停弹窗 / 点击回调）、连线 Mesh 构建工具 `NodeLineBuilder`（直线 / 贝塞尔曲线 / 折线）。
- **可视化编辑器 `NodeTreeEditorWindow`**（IMGUI + GL，`Tools > NodeTree > Node Tree Editor`）：三列布局（节点类型 / 标签管理｜画布拖拽-缩放-平移-连线｜节点属性面板），节点增删 / 子树切除 / 自动布局，全程 Undo / Redo；「标签」页签内含「标签设置」区（「自动写入 Unlock 条件」开关为项目级设置，存 `ProjectSettings/NodeTreeEditorSettings.asset` 随版本库共享：开启时画布「添加子节点 / 连线」会自动向子节点 `Unlock` 规则写入 `NodeTree.NodeFinished(target=父ID)` 条件），右侧节点面板对每个标签用 Toolkit 的 `ConditionExpression` 内联绘制器编辑其挂载条件；配套 `NodeDrawer`、`NodeTreeCanvasState` 与 `NodeTreeDataEditor`（自定义 Inspector 一键打开编辑器）。
- **URP 流光连线 Shader `NodeTree/NodeLineFlow`**：透明流光连线，支持主纹理 / 流动纹理 UV 滚动、边缘渐变、辉光与全局透明度。
- **集成**：对象池化 UI、悬停弹窗淡入淡出、静态单例均基于 `com.ale.toolkit`（`ToolkitPool` / `ToolkitGameObjectPool` / `IPoolable` / `ToolkitTween` / `ToolkitMonoSingleton`）；节点名 / 描述经 `com.ale.toolkit` 的 `AttributeValue`(Text) 承载本地化（项目启用 `ATK_LOCALIZATION` 时取多语文本，否则纯文本回退，插件本身无需本地化宏）；连线渲染基于 URP。
- **演示 Sample `Node Tree Demo`**（`Samples~/Demo`）：可在 Package Manager 中一键导入，含配置资产（`NodeTreeData`）、运行时 UI 示例场景、节点预制体、连线材质与本地化表。
