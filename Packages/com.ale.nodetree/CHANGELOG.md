# 更新日志（Changelog）

本文件记录 Node Tree System（`com.ale.nodetree`）的所有重要变更。

格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/)，版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

> 迁移说明（2026-07-28）：插件位置由 `Assets/Plugins/Ale Node Tree` 迁移至内嵌 UPM 包 `Packages/com.ale.nodetree`；程序集 `Ale.NodeTree.Runtime` / `Ale.NodeTree.Editor`、命名空间 `Ale.NodeTree.*` 保持不变。所有资产按 GUID 引用，场景 / 预制体 / 配置资产不受影响。

## [1.3.0] - 2026-08-22

新增四方连续无限滚动背景组件 `UIScrollingBackground`（RawImage uvRect UV 滚动）。

### 新增

- **四方连续无限滚动背景 `UIScrollingBackground`**：以 RawImage **初始 Rect 尺寸**为一块 tile（≤0 时回退纹理像素尺寸），`uvRect.size = 视口/tile` 铺满视口，滚动仅偏移 `uvRect`、靠纹理 **Repeat** 采样实现四方连续无限平铺——单物体单 DrawCall、零 tile 实例、零逐帧分配（静止时零开销）。可选绑定 `ScrollRect`：`LateUpdate` 轮询 `Content.anchoredPosition` 增量、与 Content **同向**滚动（统一覆盖拖拽 / 惯性 / 回弹 / 代码驱动，首帧与重绑定只快照不回放、无跳变）；`speedMultiplier` 速度倍率（1 = 同速，≠1 视差，负值反向）。视口模式 `SelfRect`（自身 RectTransform，image 为子物体时自动拉伸铺满）/ `Screen`（按根 `Canvas.scaleFactor` 换算画布单位并轮询分辨率变化）。纹理 Wrap 非 Repeat 时 Awake 检测并告警；未绑定 ScrollRect 时可经公开 API 手动驱动。

### 变更

- **Demo 背景改为无限滚动**：`UIStoryTreeWindow` 预制体的 `ImgBackground` 由 `Image`（Sprite）改为 `RawImage`（直接引用 Texture2D、关闭 `raycastTarget`），并挂载 `UIScrollingBackground`（绑定 Scroll View、`Screen` 视口、倍率 1）；`T_NodeTree_Background.jpg` 导入设置 Wrap Mode 由 Clamp 改为 **Repeat**（顺带修复原 1820 宽背景盖不满 1920 屏的边缘缝隙）。

### API

- `UIScrollingBackground`：属性 `SpeedMultiplier` / `ViewportMode` / `TileSize`（只读）/ `ScrollOffset`（只读）；方法 `ScrollBy(Vector2)`（手动视觉增量，不乘倍率）、`SetScrollOffset(Vector2)`、`ResetOffset()`、`SetScrollRect(ScrollRect)`（运行时重绑定，无跳变）、`Refit()`（布局变化后重新适配视口）。

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
