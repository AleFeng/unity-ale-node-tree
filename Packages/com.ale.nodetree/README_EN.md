# Node Tree System

<p align="center">
  🌍
  <a href="./README.md">中文</a> |
  English |
  <a href="./README_JA.md">日本語</a>
</p>

A **visual node-tree / skill-tree / tech-tree** plugin for Unity. One `NodeTreeData` asset centralizes **nodes, node types, unlock conditions, and canvas layout**; it ships with a **visual editor** (drag / zoom / pan / connect on a canvas) and a set of **ready-to-use runtime UI** (per-type object pooling, viewport culling, URP flowing-line shader). Each node's **unlocked / finished** state is maintained by a save manager, and unlock conditions are evaluated through **pluggable condition checkers**.

- Data-driven: a single `NodeTreeData` (ScriptableObject) holds the whole tree; the editor is fully Undo / Redo aware.
- High-performance runtime: object pooling per node type, on-demand Spawn / Despawn via viewport culling, batched line meshes to cut draw calls.
- Extensible conditions: implement `INodeConditionChecker` for custom unlock rules; two built-in checkers ("unlocked / finished").
- Save-friendly: `NodeTreeSaveDataManager` tracks unlocked / finished state with JSON serialization, pluggable into any game save system.
- Optional dependencies: node name / description localization via Unity Localization (`HAS_LOCALIZATION`); hover-popup easing via DOTween (`DOTWEEN`); object pooling powered by the base package `com.ale.toolkit`.

---

## Module overview

| Module | Responsibility | Key types |
|--------|----------------|-----------|
| **Config** | Node-tree config asset | `NodeTreeData` |
| **Data** | Node / type / condition / custom data | `NodeData`, `NodeTypeData`, `LineTypeData`, `ConditionData`, `ConditionGroupData`, `NodeConditionTypeData`, `NodeCustomData` |
| **Conditions** | Unlock evaluation & extension | `INodeConditionChecker`, `NodeConditionManager` |
| **Save** | Unlocked / finished state | `NodeTreeSaveDataManager` |
| **Runtime UI** | Node-tree presentation | `UINodeTreeWindow`, `UINodeBase`, `NodeLineBuilder` |
| **Editor** | Visual editing | `NodeTreeEditorWindow`, `NodeDrawer`, `NodeTreeCanvasState`, `NodeTreeDataEditor` |
| **Shader** | Flowing line | `NodeTree/NodeLineFlow` |

Runtime assembly `Ale.NodeTree.Runtime`, editor assembly `Ale.NodeTree.Editor`, with matching namespaces.

---

## Config asset `NodeTreeData`

The single source of truth for the whole tree (`ScriptableObject`). Create it via `Assets > Create > NodeTree System/Config Node Tree`; a new asset is auto-seeded with built-in node types (**Normal** / **Ending**), built-in condition types (**NodeUnlocked** / **NodeFinished**), and a **start node** at the canvas origin.

| Field | Description |
|-------|-------------|
| `nodes` | `List<NodeData>`, all node instances |
| `nodeTypes` | `List<NodeTypeData>`, type definitions (appearance + UI prefab + line style) |
| `conditionTypes` | `List<NodeConditionTypeData>`, available condition-type metadata |
| `layoutDirection` | `ELayoutDirection`, overall canvas layout direction |
| `zoom` | Editor canvas zoom (written by the editor; unused at runtime) |

**Main API**: `GetNode(string nodeId)`, `GetNodeType(string typeName)` (return `null` when not found).

---

## Data model

### Node `NodeData`

A node instance stored in `NodeTreeData.nodes`.

| Field | Description |
|-------|-------------|
| `nodeId` | Unique node ID (unique within one `NodeTreeData`) |
| `nodeTypeRef` | References a `NodeTypeData.typeName`; drives appearance & UI prefab |
| `comment` | Editor-only note (no runtime effect) |
| `conditionSatisfyType` | `EConditionSatisfyType` (`All` = AND / `Any` = OR) across condition groups |
| `conditionGroups` | `List<ConditionGroupData>`, unlock conditions (multiple groups, each with multiple conditions) |
| `uiIcon` | Node icon (`Sprite`) |
| `localizeNodeName` / `localizeNodeDesc` | Localized name / description (`LocalizedString`, requires `HAS_LOCALIZATION`) |
| `position` | Canvas pixel coordinates |
| `customDataList` | `List<NodeCustomData>`, arbitrary key/value pairs for other systems |
| `childNodeIds` | Child node IDs (forming the tree / graph) |

**Main API**:

- `string GetCustomData(string key)` / `void SetCustomData(string key, string value)` — read/write custom data.
- `bool IsUnlock(object context = null)` — evaluates unlock state from `conditionSatisfyType` + `ConditionGroupData`s; an empty `conditionGroups` means unconditionally unlocked (`true`). Evaluation is routed through `NodeConditionManager` to the registered checkers.
- `bool IsFinish()` — queries finished state via `NodeTreeSaveDataManager`.

### Node type `NodeTypeData` and line style `LineTypeData`

`NodeTypeData` describes a category of node: `typeName`, `resolution` (size), `shape` (`ENodeShape`), `color`, `icon`, `label`, `uiPrefab` (in-game UI prefab, must have `UINodeBase`), `line` (`LineTypeData`).

- **`ENodeShape`** (editor canvas shapes): `Circle`, `Square`, `Triangle`, `Diamond`, `HorizontalCapsule`, `Parallelogram`, `Pentagon`, `Hexagon`, `Octagon`, `Star`.
- **`LineTypeData`**: `lineType` (`ELineType`: `Straight` / `Curve` / `Polyline`), `lineWidth` (pixels), `material` (line material; pair it with `NodeTree/NodeLineFlow` for a flow effect).

### Condition data `ConditionData` / `ConditionGroupData`

- `ConditionData`: a single condition — `conditionType` (references `NodeConditionTypeData.conditionType`), `comparison` (`EConditionComparison`: `Equal` / `NotEqual` / `Greater` / `Less`), `conditionParam` (parameter string passed to the checker).
- `ConditionGroupData`: a group — `satisfyType` (`EConditionSatisfyType` All/Any) + `conditions` (`List<ConditionData>`). An empty group is treated as no restriction (always passes).
- `NodeConditionTypeData`: condition-type metadata (`conditionType` + `description`), pre-registered in `NodeTreeData` for editor display / selection.
- `NodeCustomData`: a `key` / `value` pair.

---

## Condition system

Decides whether a node meets its unlock conditions. Logic is decoupled from data: the data layer only describes `conditionType` + `comparison` + `conditionParam`, while the actual judgement is performed by checkers registered with `NodeConditionManager`.

- **`INodeConditionChecker`**: `string ConditionType { get; }` + `bool Check(string conditionParam, EConditionComparison comparison, object context)`.
- **`NodeConditionManager`** (static singleton): `Register(INodeConditionChecker)` / `Unregister(string conditionType)` / `Check(conditionType, conditionParam, comparison, context)` (returns `true` when `conditionType` is empty or unregistered, i.e. non-blocking). Built-in checkers are auto-registered on first access.
- **Built-in checkers**: `NodeUnlockedChecker` (`conditionType = "NodeUnlocked"`, reads `NodeTreeSaveDataManager.IsNodeUnlocked`) and `NodeFinishedChecker` (`"NodeFinished"`, reads `IsNodeFinished`); `conditionParam` is the target node's `nodeId`.

**Custom condition** (e.g. unlock only when level is high enough):

```csharp
public class LevelChecker : INodeConditionChecker
{
    public string ConditionType => "PlayerLevel";

    // conditionParam = required level; context is passed in by the caller (here, the player level)
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

// Register once at game startup
NodeConditionManager.Instance.Register(new LevelChecker());

// Evaluate (context is forwarded to every checker)
bool unlocked = node.IsUnlock(context: player.Level);
```

---

## Save `NodeTreeSaveDataManager`

A static singleton that tracks each node's **unlocked / finished** state. It **does not derive from MonoBehaviour and does not auto-save** — an external game save system reads/writes it; the plugin only owns the runtime state and its serialization.

- **Query**: `bool IsNodeUnlocked(string nodeId)`, `bool IsNodeFinished(string nodeId)`.
- **Mutate**: `void SetNodeUnlocked(string nodeId, bool)`, `void SetNodeFinished(string nodeId, bool)`.
- **Save integration**: `NodeTreeSaveData GetSaveData()` (deep copy), `void SetSaveData(NodeTreeSaveData)` (overwrite; `null` is ignored). Data shape `NodeTreeSaveData { List<string> unlockedNodeIds; List<string> finishedNodeIds; }`.
- **JSON**: `string SerializeToJson()`, `void DeserializeFromJson(string)` (via `JsonUtility`, zero external dependency).
- **Reset**: `void Reset()` (clears all records, for "new game").

```csharp
var mgr = NodeTreeSaveDataManager.Instance;
mgr.SetNodeUnlocked("node_02", true);
mgr.SetNodeFinished("node_01", true);

string json = mgr.SerializeToJson();   // hand off to your save system
// ...on load:
mgr.DeserializeFromJson(json);
```

---

## Runtime UI

### `UINodeTreeWindow`

The runtime node-tree UI window (a standalone `MonoBehaviour`). Attach it to a UI root, assign `config` (`NodeTreeData`) and the content root container to display the whole tree.

- Automatically **computes and sets the root container Size** from node positions / sizes;
- **Pools node UI per node type** (via `ToolkitGameObjectPool` from `com.ale.toolkit`) and **Spawns / Despawns on demand through viewport culling**;
- **Merges a line mesh per node type** (fewer draw calls); UVs pair with `NodeTree/NodeLineFlow` for the flow effect;
- Rebuilds lines on demand in `LateUpdate` via a dirty flag.

**Main API**: `InitTree(NodeTreeData configOverride = null)` (initialize / rebuild the whole tree), `SelectNode(string nodeId)`, `RefreshVisibility()` (recompute viewport culling), `MarkLineDirty()` (flag lines for rebuild).

### `UINodeBase`

The node UI base class (`MonoBehaviour` + `IPoolable` + pointer events). Attach it to node UI prefabs; `UINodeTreeWindow` spawns and binds them through the pool. Features: node icon display, localized name / description binding (`HAS_LOCALIZATION`), hover info-popup fade in/out (eased when `DOTWEEN`), and a click callback.

**Overridable virtuals & events**:

- `OnBindData(NodeData data, NodeTypeData type)` / `OnUnbindData()` — bind / unbind data (`OnDespawn` unbinds automatically).
- `OnNodeSelected()` / `OnNodeDeselected()` — visual feedback for select / deselect.
- `OnPointerEnterNode()` / `OnPointerExitNode()` — hover popup fade in / out.
- `OnNodeClicked(PointerEventData)` + `event Action<UINodeBase> Clicked` — click callback (subscribe to the event, or override in a subclass to trigger SFX / dialogue / story, etc.).
- `OnSpawn()` / `OnDespawn()` — `IPoolable` pool callbacks (`OnDespawn` → auto `OnUnbindData`, so reused instances carry no stale state).

### `NodeLineBuilder`

A static line-mesh builder (usable at runtime): `BuildCombinedLineMesh(segments, LineTypeData, ELayoutDirection)` **merges a batch of parent→child segments into a single mesh** (fewer draw calls) by line type (straight / bezier curve / polyline), with built-in bezier geometry helpers. UV convention: `UV.x` maps to the two sides of the line width (edge fade), `UV.y` is accumulated arc length / 100 (texture density independent of line length).

---

## Visual editor

Open via `Tools > NodeTree > Node Tree Editor` (or the "Edit in Node Tree Editor" button on a `NodeTreeData` asset's Inspector).

- **`NodeTreeEditorWindow`** (`EditorWindow`, IMGUI + GL): three-column layout — left **node-type / condition-type management**, center **canvas** (drag / zoom / pan / connect nodes), right **node property panel**; supports adding/deleting nodes, cutting subtrees, and auto-layout. All edits go through `Undo.RecordObject` + `EditorUtility.SetDirty`, so the window is **fully Undo / Redo aware and persists the asset**. Canvas pan / zoom / last-opened config are persisted via `EditorPrefs`.
- **`NodeDrawer`** (static): draws node shapes (circle / square / polygon, etc.) and connections (straight / bezier / polyline) with IMGUI + GL, during `Repaint` only.
- **`NodeTreeCanvasState`**: canvas interaction state (pan / zoom / selection / drag) and canvas↔screen coordinate conversion.
- **`NodeTreeDataEditor`**: a custom Inspector for `NodeTreeData` that adds an "Edit in Node Tree Editor" button on top.

---

## Line shader `NodeTree/NodeLineFlow`

A URP transparent **flowing-line** shader: main texture + flow texture UV scrolling, edge fade (`_EdgeFade`), glow (`_Glow`), global alpha (`_Alpha`), flow color (HDR). Assign a material using this shader to a node type's `LineTypeData.material` to make that type's connections show an animated flow.

---

## Integration & dependencies

- **`com.ale.toolkit`** (required): runtime UI pooling builds on `ToolkitPool` / `ToolkitGameObjectPool` / `IPoolable`.
- **Unity Localization** (optional, `HAS_LOCALIZATION`): when enabled, node name / description use `LocalizedString` + `LocalizeStringEvent`; otherwise the related fields and logic are skipped.
- **DOTween** (optional, `DOTWEEN`): when enabled, the hover info popup fades in/out; otherwise it toggles instantly.
- **URP**: the flowing-line shader targets the Universal Render Pipeline.

> See the [project root README](../../README.md) for installation and requirements.

---

## License

[MIT](LICENSE.md) © 2026 Ale
