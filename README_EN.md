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
  <a href="./README.md">中文</a> |
  English |
  <a href="./README_JA.md">日本語</a>
</p>

<p align="center">
  📥
  <a href="#-installation">Installation</a> |
  <a href="#-quick-start">Quick Start</a> |
  <a href="Packages/com.ale.nodetree/README_EN.md">Documentation</a>
</p>

# Ale Node Tree

Ale Node Tree is a **visual node-tree plugin** for `Unity`, for building and displaying any "nodes + connections + unlock conditions" structure — **skill trees / tech trees / level-progression trees / story-progression trees**, and more.
One `NodeTreeData` asset centralizes **nodes, node types, unlock conditions, and canvas layout**; it ships with a **visual editor** (drag / zoom / pan / connect on a canvas, fully Undo / Redo aware) and a set of **ready-to-use runtime UI** (per-type object pooling, viewport culling, URP flowing-line shader). Each node's **unlocked / finished** state is maintained by a save manager, and unlock conditions are evaluated through **pluggable condition checkers** — the judgement logic is decoupled from the config data, so you can plug in any game rule without changing the data model.

## 📜 Table of Contents
- [Ale Node Tree](#ale-node-tree)
  - [📜 Table of Contents](#-table-of-contents)
  - [Introduction](#introduction)
    - [Features](#features)
    - [Modules](#modules)
  - [💻 Requirements](#-requirements)
  - [📦 Installation](#-installation)
    - [Via UPM (recommended)](#via-upm-recommended)
    - [Other ways](#other-ways)
  - [🚀 Quick Start](#-quick-start)
  - [📖 Documentation](#-documentation)
  - [📁 Directory Structure](#-directory-structure)
  - [📋 To-Do](#-to-do)
  - [📄 License](#-license)

## Introduction
Many games need a "nodes + connections + unlock conditions" tree — skill trees, tech trees, level progression, story progression… but rebuilding editor drawing, connection rendering, viewport performance, and unlock evaluation from scratch every time is costly. Ale Node Tree gathers them into **one data asset** and **one toolchain**:

1. **Visual editing** — one `NodeTreeData` holds the whole tree; the editor is a three-column layout (left type management + center canvas with drag / zoom / pan / connect + right property panel), drawing node shapes and connections in real time with IMGUI + GL, fully Undo / Redo aware.
2. **Data-driven unlocking** — each node can hold multiple condition groups (AND / OR across groups, AND / OR within a group); evaluation is delegated to checkers registered with `NodeConditionManager`; built-in "unlocked / finished" checkers, and a custom rule is just one interface away.
3. **High-performance runtime** — runtime UI is pooled per node type, Spawned / Despawned on demand via viewport culling, and line meshes are batched per type to cut draw calls; a URP flowing-line shader is included.
4. **Save-friendly** — `NodeTreeSaveDataManager` tracks unlocked / finished state with JSON serialization; `GetSaveData` / `SetSaveData` plug into any game save system.
5. **Built on the base package, not hard-bound** — node name / description localization is carried by `com.ale.toolkit`'s `AttributeValue`(Text) (localized when the project enables toolkit's `ATK_LOCALIZATION`, otherwise plain-text fallback; the plugin itself needs no localization macro); the hover popup fade in/out is built in (via `com.ale.toolkit`'s central tween); object pooling reuses `com.ale.toolkit`.

### Features
| Feature | Description |
| --- | --- |
| Single-asset config | One `NodeTreeData` holds all nodes, node types, condition types, and canvas layout; the editor works only on the ScriptableObject, fully Undo / Redo. |
| Visual editor | Three-column layout + canvas drag / zoom / pan / connect; add/delete nodes, cut subtrees, auto-layout; IMGUI + GL draws 10 node shapes and straight / curve / polyline connections. |
| Extensible conditions | Data only describes `conditionType` + comparison + parameter; judgement is done by `INodeConditionChecker`; built-in "unlocked / finished", custom rules via a single interface. |
| High-performance runtime UI | Object pooling per node type (via `com.ale.toolkit`), on-demand Spawn / Despawn via viewport culling, batched line meshes to cut draw calls. |
| URP flowing lines | `NodeTree/NodeLineFlow` transparent flow shader (flow texture / edge fade / glow / HDR color); line style is configured per node type. |
| Save-friendly | `NodeTreeSaveDataManager` tracks unlocked / finished state, JSON serialization, `GetSaveData` / `SetSaveData` for external saves. |
| Base-package integration | Node name / description localization via `com.ale.toolkit`'s `AttributeValue`(Text) (multilingual when `ATK_LOCALIZATION` is on, else plain text); hover popup fade via `com.ale.toolkit`'s central tween; object pooling via `com.ale.toolkit`. The plugin itself has no localization / DOTween macro. |

### Modules
| Module | Responsibility | Key types |
| --- | --- | --- |
| **Config** | Node-tree config asset | `NodeTreeData` |
| **Data** | Node / type / condition / custom attributes | `NodeData`, `NodeTypeData`, `LineTypeData`, `ConditionData`, `ConditionGroupData`, `NodeConditionTypeData` |
| **Conditions** | Unlock evaluation & extension | `INodeConditionChecker`, `NodeConditionManager` |
| **Save** | Unlocked / finished state | `NodeTreeSaveDataManager` |
| **Runtime UI** | Node-tree presentation | `UINodeTreeWindow`, `UINodeBase`, `NodeLineBuilder` |
| **Editor** | Visual editing | `NodeTreeEditorWindow`, `NodeDrawer`, `NodeTreeCanvasState`, `NodeTreeDataEditor` |

> See the [documentation](#-documentation) for each module's fields, API, and usage.

## 💻 Requirements
- `Unity 2022.3` or newer (the minimum declared in `package.json`; this repo is developed and maintained on `Unity 6000.3`).
- **Universal Render Pipeline (URP)**: the flowing-line shader `NodeTree/NodeLineFlow` targets URP.
- **Required dependency [`com.ale.toolkit`](https://github.com/AleFeng/unity-ale-toolkit)**: runtime UI pooling builds on its `ToolkitPool` / `ToolkitGameObjectPool` / `IPoolable`.
- Base-package integration: node name / description localization is carried by **`com.ale.toolkit`**'s `AttributeValue`(Text) — multilingual when the project enables toolkit's `ATK_LOCALIZATION`, otherwise plain-text fallback (the plugin itself needs no localization macro); the hover popup fade in/out is built in (via `com.ale.toolkit`'s central tween, no DOTween needed).

## 📦 Installation

> ⚠️ **This plugin depends on the base package [`com.ale.toolkit`](https://github.com/AleFeng/unity-ale-toolkit); install it first, then this plugin.** The Unity Package Manager cannot pull git URLs declared in `package.json` `dependencies`, so **the order must not be reversed**. Install toolkit the same way as below first: `https://github.com/AleFeng/unity-ale-toolkit.git?path=/Packages/com.ale.toolkit`. Missing it or reversing the order causes `Ale.Toolkit.* not found`-style compile errors — just add toolkit and wait for recompilation; no need to reinstall this plugin.

### Via UPM (recommended)
`Window > Package Manager` → top-left `+` → `Install package from git URL...` → paste:

```
https://github.com/AleFeng/unity-ale-node-tree.git?path=/Packages/com.ale.nodetree
```

This installs the latest commit on `main`. **To pin a version, append `#<tag>` at the very end of the whole URL** (after `?path=`):

```
https://github.com/AleFeng/unity-ale-node-tree.git?path=/Packages/com.ale.nodetree#1.1.0
```

Available tags are listed under [Releases](https://github.com/AleFeng/unity-ale-node-tree/releases).

### Other ways
You can also download the repo and copy the whole `Packages/com.ale.nodetree` folder into your project's **`Packages/` directory** (not `Assets/`) — Unity recognizes it as a local package automatically.

After installation, the menu **`Tools > NodeTree > Node Tree Editor`** appears.

### Import the demo Sample (optional)
Once installed, in `Window > Package Manager` select this package → `Samples` → import **Node Tree Demo** (config asset `NodeTreeData` + runtime UI sample scene + node prefabs + line material + localization tables); enter Play to try it out.

## 🚀 Quick Start
The shortest path below; **see the [documentation](#-documentation) for full module and API details**.

**1. Create the config asset**
```
Project panel right-click > Create > NodeTree System/Config Node Tree
```
A new `NodeTreeData` is auto-seeded with built-in node types (Normal / Ending), built-in condition types (NodeUnlocked / NodeFinished), and a start node.

**2. Edit visually**
Select the `.asset` and click "Edit in Node Tree Editor" at the top of the Inspector, or use `Tools > NodeTree > Node Tree Editor`. Manage **node types** (shape / color / size / UI prefab / line style) and **condition types** on the left; drag nodes and connect them to build parent–child links on the center canvas; configure a node's ID, icon, unlock condition groups, and custom data on the right property panel.

**3. Mount at runtime**
Add a `UINodeTreeWindow` component to a UI root, drag the `NodeTreeData` into `config`, assign the content root container, and call `InitTree()` at runtime to spawn pooled node UI and batched connections from the node data.

```csharp
using Ale.NodeTree.Runtime;

// Initialize / rebuild the whole tree (or InitTree(otherConfig) to switch configs)
nodeTreeWindow.InitTree();

// Subscribe to node clicks (UINodeBase.Clicked)
someNodeUI.Clicked += ui => Debug.Log($"Clicked node {ui.nodeData.nodeId}");
```

**4. Conditions and saving**
```csharp
using Ale.NodeTree.Runtime;

// Custom unlock condition (register once at game startup)
NodeConditionManager.Instance.Register(new MyLevelChecker());

// Query whether a node is unlocked (context is forwarded to every checker)
bool unlocked = node.IsUnlock(context: player.Level);

// Unlocked / finished state + saving
var save = NodeTreeSaveDataManager.Instance;
save.SetNodeUnlocked("node_02", true);
string json = save.SerializeToJson();   // persist via your save system
save.DeserializeFromJson(json);          // overwrite on load
```

**5. Try the Demo**
In Package Manager, select this package → `Samples` → import **Node Tree Demo**, open the demo scene and enter Play to see pooled node spawning and flowing connections.

## 📖 Documentation
This README is an overview and quick start. For **each module's fields, API, usage, and code samples**, see the in-package documentation:

👉 **[Packages/com.ale.nodetree/README_EN.md](Packages/com.ale.nodetree/README_EN.md)** ([中文](Packages/com.ale.nodetree/README.md) · [日本語](Packages/com.ale.nodetree/README_JA.md))

## 📁 Directory Structure
```
Packages/com.ale.nodetree/           ← package root
├── package.json  CHANGELOG.md  LICENSE.md  README.md   ← in-package docs (trilingual)
├── Runtime/
│   ├── Config/      node-tree config asset NodeTreeData
│   ├── Data/        data model (NodeData / NodeTypeData / LineTypeData / Condition*)
│   ├── Conditions/  condition system (INodeConditionChecker / NodeConditionManager / built-in checkers)
│   ├── Save/        save manager NodeTreeSaveDataManager
│   ├── UI/          runtime UI (UINodeTreeWindow / UINodeBase)
│   └── Utility/     line mesh builder NodeLineBuilder
├── Editor/          visual editor (NodeTreeEditorWindow / NodeDrawer / NodeTreeCanvasState / NodeTreeDataEditor)
├── Shaders/         URP flowing-line shader (NodeTree/NodeLineFlow)
└── Samples~/Demo/   demo Sample (scene + config asset + node prefabs + line material + localization tables; import via Package Manager)
```

## 📋 To-Do
- More built-in node shapes and line-style presets.
- More sample scenes and runtime use cases.

## 📄 License
Released under the [MIT License](LICENSE); free for commercial and non-commercial use.
