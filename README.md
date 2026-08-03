<p align="center">
  <img alt="GitHub Release" src="https://img.shields.io/github/v/release/AleFeng/unity-ale-node-tree?color=blue">
  <img alt="GitHub Downloads (all assets, all releases)" src="https://img.shields.io/github/downloads/AleFeng/unity-ale-node-tree/total?color=green">
  <img alt="Unity Version" src="https://img.shields.io/badge/Unity-2022.3%2B-black?logo=unity">
  <img alt="Unity Version" src="https://img.shields.io/badge/Unity-6000.3%2B-black?logo=unity">
  <img alt="Render Pipeline" src="https://img.shields.io/badge/RP-URP-blue">
  <img alt="GitHub Repo License" src="https://img.shields.io/badge/license-MIT-blueviolet">
  <img alt="GitHub Repo Issues" src="https://img.shields.io/github/issues/AleFeng/unity-ale-node-tree?color=yellow">
</p>

<p align="center">
  🌍
  中文 |
  <a href="./README_EN.md">English</a> |
  <a href="./README_JA.md">日本語</a>
</p>

<p align="center">
  📥
  <a href="#-安装">安装</a> |
  <a href="#-快速开始">快速开始</a> |
  <a href="Packages/com.ale.nodetree/README.md">详细文档</a>
</p>

# Ale Node Tree - 节点树系统

Ale Node Tree 是一款面向 `Unity` 的**可视化节点树插件**，用于搭建并展示**技能树 / 科技树 / 关卡进度树 / 剧情推进树**等一切「节点 + 连线 + 解锁条件」结构。
用一个 `NodeTreeData` 资产集中配置**节点、节点类型、标签与画布布局**；配套一个**可视化编辑器**（画布拖拽 / 缩放 / 平移 / 连线，全程 Undo / Redo）与一套**开箱即用的运行时 UI**（按节点类型对象池化、视口裁剪、URP 流光连线）。节点的**已解锁 / 已完成**等状态以**标签(Tag)**承载、由存档管理器维护；每个标签的挂载条件基于 `com.ale.toolkit` 的**条件系统**（`Ale.Condition`）用 `ConditionExpression` 描述，判定交给**可扩展的判定器**（`IConditionEvaluator`）——判断逻辑与配置数据解耦，无需改数据结构即可接入任意游戏规则。

## 📜 目录
- [Ale Node Tree - 节点树系统](#ale-node-tree---节点树系统)
  - [📜 目录](#-目录)
  - [简介](#简介)
    - [项目特性](#项目特性)
    - [模块一览](#模块一览)
  - [💻 环境要求](#-环境要求)
  - [📦 安装](#-安装)
    - [使用 UPM（推荐）](#使用-upm推荐)
    - [其他方式](#其他方式)
  - [🚀 快速开始](#-快速开始)
  - [📖 详细文档](#-详细文档)
  - [📁 目录结构](#-目录结构)
  - [📋 待办事项](#-待办事项)
  - [📄 许可](#-许可)

## 简介
很多游戏都需要一套「节点 + 连线 + 解锁条件」的树状结构——技能树、科技树、关卡进度、剧情推进……但每次都要从零处理编辑器绘制、连线渲染、视口性能与解锁判定，成本很高。Ale Node Tree 把它们收拢到**同一份数据资产**与**同一套工具链**下：

1. **可视化编辑** —— 一个 `NodeTreeData` 承载整棵树，编辑器为「左侧类型管理 + 中央画布（拖拽 / 缩放 / 平移 / 连线）+ 右侧属性面板」的三列布局，IMGUI + GL 实时绘制节点形状与连线，全程 Undo / Redo。
2. **数据驱动的解锁** —— 每个节点为其每个标签配置一条挂载条件，用 `com.ale.toolkit` 条件系统的 `ConditionExpression`（两级 AND / OR：表达式 → 组 → 项）描述；判定交给自动注册的判定器 `IConditionEvaluator`，内置「已完成 / 已解锁 / 是否挂某标签」，自定义规则只需实现一个接口并打特性。
3. **高性能运行时** —— 运行时 UI 按节点类型对象池化，视口裁剪按需 Spawn / Despawn，连线 Mesh 按类型合批以降低 DrawCall；配套 URP 流光连线 Shader。
4. **易接入存档** —— `NodeTreeSaveDataManager` 以标签制维护各节点状态并支持 JSON 序列化，`Save()` / `Load()` / `Get()` / `Set()` 对接任意游戏存档系统（落盘交宿主）。
5. **底层集成，非硬绑定** —— 节点名 / 描述本地化由底层 `com.ale.toolkit` 的 `AttributeValue`(Text) 承载（项目启用 toolkit 的 `ATK_LOCALIZATION` 时取多语文本，否则纯文本回退，插件本身无需本地化宏）；悬停弹窗淡入淡出内置（基于 `com.ale.toolkit` 中央 Tween）；对象池复用 `com.ale.toolkit`。

### 项目特性
| 特性 | 描述 |
| --- | --- |
| 单资产集中配置 | 一个 `NodeTreeData` 承载全部节点、节点类型、标签与画布布局；编辑器仅在 ScriptableObject 上工作，全程 Undo / Redo。 |
| 可视化编辑器 | 三列布局 + 画布拖拽 / 连线；滚轮以光标为中心缩放、鼠标中键平移，底部常驻操作说明栏与空白处右键菜单（重置视口 / 显示全部节点 / 定位起始节点 / 就地新建节点 / 自动布局 / 吸附网格）；节点增删、子树切除、自动布局；IMGUI + GL 绘制 10 种节点形状与直线 / 曲线 / 折线连线。 |
| 可扩展条件系统 | 挂载条件用 `com.ale.toolkit` 的 `ConditionExpression` 描述，判定由 `IConditionEvaluator` 完成（打 `[ConditionEvaluator("Key")]` 自动注册）；内置「已完成 / 已解锁 / 是否挂某标签」，自定义规则一接口即可接入。 |
| 高性能运行时 UI | 按节点类型对象池化（基于 `com.ale.toolkit`），视口裁剪按需 Spawn / Despawn，连线 Mesh 合批降 DrawCall。 |
| URP 流光连线 | `NodeTree/NodeLineFlow` 透明流光 Shader（流动纹理 / 边缘渐变 / 辉光 / HDR 颜色），每条连线的样式取自其**子（目标）节点类型**。 |
| 存档友好 | `NodeTreeSaveDataManager` 以标签制维护各节点状态，JSON 序列化，`Save()` / `Load()` / `Get()` / `Set()` 对接外部存档。 |
| 底层集成 | 节点名 / 描述本地化经 `com.ale.toolkit` 的 `AttributeValue`(Text)（启用 `ATK_LOCALIZATION` 取多语、否则纯文本）；悬停弹窗淡入淡出基于 `com.ale.toolkit` 中央 Tween；对象池复用 `com.ale.toolkit`。插件本身无本地化 / DOTween 宏。 |

### 模块一览
| 模块 | 职责 | 主要类型 |
| --- | --- | --- |
| **配置** | 节点树配置资产 | `NodeTreeData` |
| **数据** | 节点 / 类型 / 标签 / 自定义属性 | `NodeData`、`NodeTypeData`、`LineTypeData`、`NodeTagData`、`NodeTagRule` |
| **条件与状态** | 挂载条件与判定 | `ConditionExpression`(Toolkit)、`INodeTreeStateSource`、`NodeTreeConditionContext`、内置判定器 `NodeFinished` · `NodeUnlocked` · `NodeHasTag` |
| **存档** | 节点标签状态 | `NodeTreeSaveDataManager` |
| **运行时 UI** | 节点树展示 | `UINodeTreeWindow`、`UINodeBase`、`NodeLineBuilder` |
| **编辑器** | 可视化编辑 | `NodeTreeEditorWindow`、`NodeDrawer`、`NodeTreeCanvasState`、`NodeTreeDataEditor` |

> 每个模块的字段、API 与用法见 [详细文档](#-详细文档)。

## 💻 环境要求
- `Unity 2022.3` 或更新版本（`package.json` 声明的最低版本；本仓库基于 `Unity 6000.3` 开发与维护）。
- **通用渲染管线（URP）**：连线流光 Shader `NodeTree/NodeLineFlow` 基于 URP。
- **必需依赖 [`com.ale.toolkit`](https://github.com/AleFeng/unity-ale-toolkit)**：运行时 UI 的对象池化基于其 `ToolkitPool` / `ToolkitGameObjectPool` / `IPoolable`。
- 底层集成：节点名 / 描述本地化经 **`com.ale.toolkit`** 的 `AttributeValue`(Text) 承载——项目启用 toolkit 的 `ATK_LOCALIZATION` 时取多语文本，否则纯文本回退（插件本身无需本地化宏）；悬停弹窗淡入淡出内置（基于 `com.ale.toolkit` 中央 Tween，无需 DOTween）。

## 📦 安装

> ⚠️ **本插件依赖通用底层包 [`com.ale.toolkit`](https://github.com/AleFeng/unity-ale-toolkit)，必须先装它、再装本插件。** Unity Package Manager 不支持在 `package.json` 的 `dependencies` 里写 git URL、无法自动拉取，故**顺序不能颠倒**。用与下方相同的方式先安装 toolkit：`https://github.com/AleFeng/unity-ale-toolkit.git?path=/Packages/com.ale.toolkit`。漏装或颠倒会报 `找不到 Ale.Toolkit.*` 一类编译错——补装 toolkit 并等重新编译即可，无需重装本插件。

### 使用 UPM（推荐）
`Window > Package Manager` → 左上角 `+` → `Install package from git URL...` → 粘贴：

```
https://github.com/AleFeng/unity-ale-node-tree.git?path=/Packages/com.ale.nodetree
```

这样装的是 `main` 的最新提交。**要固定版本，把 `#<tag>` 加在整条 URL 的最末尾**（必须在 `?path=` 之后）：

```
https://github.com/AleFeng/unity-ale-node-tree.git?path=/Packages/com.ale.nodetree#1.1.0
```

可用的 tag 见 [Releases](https://github.com/AleFeng/unity-ale-node-tree/releases)。

### 其他方式
也可以下载仓库，把 `Packages/com.ale.nodetree` 整个文件夹拷进你项目的 **`Packages/` 目录**（不是 `Assets/`）—— Unity 会自动把它识别为本地包。

安装成功后，菜单栏会出现 **`Tools > NodeTree > Node Tree Editor`**。

### 导入演示 Sample（可选）
装好后在 `Window > Package Manager` 里选中本包 → `Samples` → 导入 **Node Tree Demo**（配置资产 `NodeTreeData` + 运行时 UI 示例场景 + 节点预制体 + 连线材质 + 本地化表），可直接进 Play 体验。

## 🚀 快速开始
下面是最短路径的使用流程，**完整的模块说明与 API 见 [详细文档](#-详细文档)**。

**1. 创建配置资产**
```
Project 面板右键 > Create > NodeTree System/Config Node Tree
```
新建的 `NodeTreeData` 会自动带上内置节点类型（普通 / 结局）、内置状态标签（`Unlock` / `Finished`）与一个起始节点。

**2. 可视化编辑**
选中该 `.asset`，在 Inspector 顶部点「在 Node Tree Editor 中编辑」，或菜单 `Tools > NodeTree > Node Tree Editor`。在左侧管理**节点类型**（形状 / 颜色 / 尺寸 / UI 预制体 / 连线样式）与**标签**；在中央画布拖拽节点、连线构建父子关系；在右侧属性面板为节点配置 ID、图标、各标签的挂载条件与自定义属性（字段 schema 在节点类型上定义）。

**3. 运行时挂载**
给 UI 根节点添加 `UINodeTreeWindow` 组件，把 `NodeTreeData` 拖到 `config`、指定内容根容器，运行时调用 `InitTree()` 即按节点数据生成对象池化的节点 UI 与合批连线。

```csharp
using Ale.NodeTree.Runtime;

// 初始化 / 重建整棵树（也可 InitTree(otherConfig) 切换配置）
nodeTreeWindow.InitTree();

// 订阅节点点击（UINodeBase.Clicked）
someNodeUI.Clicked += ui => Debug.Log($"点击了节点 {ui.nodeData.nodeId}");
```

**4. 解锁条件与存档**
```csharp
using Ale.NodeTree.Runtime;

var save = NodeTreeSaveDataManager.Instance;

// 业务时机主动置状态：内部自判该标签的条件，返回是否设置成功
save.TrySetFinished(config, "chapter_01");   // 读完本章→置完成（Finished 条件通常为空=直接通过）

// 打开面板 / 加载存档后刷新所有自动标签（Unlock 按前置完成情况链式解锁）
// 提醒：autoRefresh 标签「空条件视为通过」（fail-open）——起始 / 根节点据此自动解锁属预期；非根节点须显式配置 Unlock 条件，否则会被自动挂上标签。
save.RefreshAllNodeStates(config);
bool unlocked = save.HasTag("chapter_02", NodeTreeTags.Unlock);

// 存档往返（落盘交宿主）
string json = save.Save();
save.Load(json);
```

**5. 体验 Demo**
在 Package Manager 选中本包 → `Samples` → 导入 **Node Tree Demo**，打开演示场景直接进 Play，即可查看节点树的对象池化生成与流光连线。

## 📖 详细文档
本 README 面向整体介绍与快速上手。**每个模块的字段、API、用法与代码示例**请见插件内文档：

👉 **[Packages/com.ale.nodetree/README.md](Packages/com.ale.nodetree/README.md)**（[English](Packages/com.ale.nodetree/README_EN.md) · [日本語](Packages/com.ale.nodetree/README_JA.md)）

## 📁 目录结构
```
Packages/com.ale.nodetree/           ← 包根
├── package.json  CHANGELOG.md  LICENSE.md  README.md   ← 详细使用文档（三语）
├── Runtime/
│   ├── Config/      节点树配置资产 NodeTreeData
│   ├── Data/        数据模型（NodeData / NodeTypeData / LineTypeData / NodeTagData / NodeTagRule）
│   ├── Conditions/  条件接入（INodeTreeStateSource / NodeTreeConditionContext / NodeTreeTags / Evaluators：NodeFinished · NodeUnlocked · NodeHasTag）
│   ├── Save/        存档管理器 NodeTreeSaveDataManager（标签制）
│   ├── UI/          运行时 UI（UINodeTreeWindow / UINodeBase）
│   └── Utility/     连线 Mesh 构建 NodeLineBuilder
├── Editor/          可视化编辑器（NodeTreeEditorWindow / NodeDrawer / NodeTreeCanvasState / NodeTreeDataEditor）
├── Shaders/         URP 流光连线 Shader（NodeTree/NodeLineFlow）
└── Samples~/Demo/   演示 Sample（场景 + 配置资产 + 节点预制体 + 连线材质 + 本地化表；Package Manager 一键导入）
```

## 📋 待办事项
- 更多内置节点形状与连线样式预设。
- 更多示例场景与运行时用例。

## 📄 许可
本项目基于 [MIT License](LICENSE) 开源，可自由用于商业与非商业项目。
