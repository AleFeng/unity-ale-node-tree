# 更新日志（Changelog）

本文件记录 Node Tree System（`com.ale.nodetree`）的所有重要变更。

格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/)，版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

> 迁移说明（2026-07-28）：插件位置由 `Assets/Plugins/Ale Node Tree` 迁移至内嵌 UPM 包 `Packages/com.ale.nodetree`；程序集 `Ale.NodeTree.Runtime` / `Ale.NodeTree.Editor`、命名空间 `Ale.NodeTree.*` 保持不变。所有资产按 GUID 引用，场景 / 预制体 / 配置资产不受影响。

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
- **集成**：对象池化 UI 基于 `com.ale.toolkit`（`ToolkitPool` / `ToolkitGameObjectPool` / `IPoolable`）；节点名 / 描述本地化经 `com.unity.localization` 可选启用（`HAS_LOCALIZATION`）；连线渲染基于 URP。
- **演示 Sample `Node Tree Demo`**（`Samples~/Demo`）：可在 Package Manager 中一键导入，含配置资产（`NodeTreeData`）、运行时 UI 示例场景、节点预制体、连线材质与本地化表。
