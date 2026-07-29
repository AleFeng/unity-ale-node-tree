# 更新日志（Changelog）

本文件记录 Node Tree System（`com.ale.nodetree`）的所有重要变更。

格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/)，版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

> 迁移说明（2026-07-28）：插件位置由 `Assets/Plugins/Ale Node Tree` 迁移至内嵌 UPM 包 `Packages/com.ale.nodetree`；程序集 `Ale.NodeTree.Runtime` / `Ale.NodeTree.Editor`、命名空间 `Ale.NodeTree.*` 保持不变。所有资产按 GUID 引用，场景 / 预制体 / 配置资产不受影响。

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

- **配置资产 `NodeTreeData`**（ScriptableObject）：集中承载节点实例、节点类型、条件类型与画布布局；`Assets > Create > NodeTree System/Config Node Tree` 创建，新建自动填充内置节点类型（普通 / 结局）、内置条件类型（NodeUnlocked / NodeFinished）与起始节点。
- **数据模型**：`NodeData`（ID / 类型引用 / 位置 / 子节点 / 条件组 / 自定义键值）、`NodeTypeData`（形状 `ENodeShape` / 尺寸 / 颜色 / 图标 / UI 预制体 / 连线样式 `LineTypeData`：`ELineType` 直线-曲线-折线 + 线宽 + 材质）、条件数据 `ConditionData` / `ConditionGroupData`（`EConditionSatisfyType` All/Any、`EConditionComparison`）、`NodeConditionTypeData`、`NodeCustomData`。
- **条件系统**：`INodeConditionChecker` + 静态 `NodeConditionManager`（注册 / 注销 / 按 `conditionType` 路由检查），内置 `NodeUnlockedChecker` / `NodeFinishedChecker`，支持外部注册自定义解锁条件。
- **存档管理器 `NodeTreeSaveDataManager`**（静态单例）：维护节点已解锁 / 已完成状态，`GetSaveData` / `SetSaveData` + JSON 序列化，供外部存档系统接入（不自动保存）；数据结构 `NodeTreeSaveData`。
- **运行时 UI**：`UINodeTreeWindow`（按节点位置自动计算根容器尺寸、按节点类型维护对象池、视口裁剪按需 Spawn/Despawn、合批生成连线 Mesh 降 DrawCall、`InitTree` 初始化）、`UINodeBase`（`IPoolable` 节点 UI 基类，`OnBindData` 绑定 + 图标 / 本地化文本 / 悬停弹窗 / 点击回调）、连线 Mesh 构建工具 `NodeLineBuilder`（直线 / 贝塞尔曲线 / 折线）。
- **可视化编辑器 `NodeTreeEditorWindow`**（IMGUI + GL，`Tools > NodeTree > Node Tree Editor`）：三列布局（节点 / 条件类型管理｜画布拖拽-缩放-平移-连线｜节点属性面板），节点增删 / 子树切除 / 自动布局，全程 Undo / Redo；配套 `NodeDrawer`、`NodeTreeCanvasState` 与 `NodeTreeDataEditor`（自定义 Inspector 一键打开编辑器）。
- **URP 流光连线 Shader `NodeTree/NodeLineFlow`**：透明流光连线，支持主纹理 / 流动纹理 UV 滚动、边缘渐变、辉光与全局透明度。
- **集成**：对象池化 UI、悬停弹窗淡入淡出、静态单例均基于 `com.ale.toolkit`（`ToolkitPool` / `ToolkitGameObjectPool` / `IPoolable` / `ToolkitTween` / `ToolkitMonoSingleton`）；节点名 / 描述经 `com.ale.toolkit` 的 `AttributeValue`(Text) 承载本地化（项目启用 `ATK_LOCALIZATION` 时取多语文本，否则纯文本回退，插件本身无需本地化宏）；连线渲染基于 URP。
- **演示 Sample `Node Tree Demo`**（`Samples~/Demo`）：可在 Package Manager 中一键导入，含配置资产（`NodeTreeData`）、运行时 UI 示例场景、节点预制体、连线材质与本地化表。
