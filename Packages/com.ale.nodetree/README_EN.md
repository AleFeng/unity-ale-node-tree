# Node Tree System

<p align="center">
  🌍
  <a href="./README.md">中文</a> |
  English |
  <a href="./README_JA.md">日本語</a>
</p>

A **visual node-tree / skill-tree / tech-tree** plugin for Unity. One `NodeTreeData` asset centralizes **nodes, node types, state tags, and canvas layout**; it ships with a **visual editor** (drag / zoom / pan / connect on a canvas) and a set of **ready-to-use runtime UI** (per-type object pooling, viewport culling, URP flowing-line shader). Each node's state is carried by **tags** maintained in a save manager, and the condition that attaches a tag is evaluated through `com.ale.toolkit`'s **Condition System** (`Ale.Condition`).

- Data-driven: a single `NodeTreeData` (ScriptableObject) holds the whole tree; the editor is fully Undo / Redo aware.
- High-performance runtime: object pooling per node type, on-demand Spawn / Despawn via viewport culling, batched line meshes to cut draw calls.
- Extensible conditions: powered by toolkit's `Ale.Condition`; write a custom `IConditionEvaluator` with a `[ConditionEvaluator("Key")]` attribute (auto-discovered) for any unlock rule. Ships with built-in evaluators (`NodeTree.NodeFinished` / `NodeTree.NodeUnlocked` / `NodeTree.NodeHasTag`).
- Save-friendly: `NodeTreeSaveDataManager` tracks per-node tags with JSON serialization, pluggable into any game save system.
- Base-package integration: node name / description localization is carried by `com.ale.toolkit`'s `AttributeValue`(Text) (multilingual when the project enables toolkit's `ATK_LOCALIZATION`, otherwise plain-text fallback; this plugin needs no localization macro); the hover popup fade in/out uses `com.ale.toolkit`'s central tween; object pooling is powered by `com.ale.toolkit`.

---

## Module overview

| Module | Responsibility | Key types |
|--------|----------------|-----------|
| **Config** | Node-tree config asset | `NodeTreeData` |
| **Data** | Node / type / state tags / custom attributes | `NodeData`, `NodeTypeData`, `LineTypeData`, `NodeTagData`, `NodeTagRule` |
| **Conditions** | node-tree condition wiring (into toolkit `Ale.Condition`) | `INodeTreeStateSource`, `NodeTreeConditionContext`, `NodeTreeTags`; evaluators `NodeFinished` · `NodeUnlocked` · `NodeHasTag` |
| **Save** | Per-node tag state | `NodeTreeSaveDataManager` |
| **Runtime UI** | Node-tree presentation | `UINodeTreeWindow`, `UINodeBase`, `NodeLineBuilder` |
| **Editor** | Visual editing | `NodeTreeEditorWindow`, `NodeDrawer`, `NodeTreeCanvasState`, `NodeTreeDataEditor` |
| **Shader** | Flowing line | `NodeTree/NodeLineFlow` |

Runtime assembly `Ale.NodeTree.Runtime`, editor assembly `Ale.NodeTree.Editor`, with matching namespaces.

---

## Config asset `NodeTreeData`

The single source of truth for the whole tree (`ScriptableObject`). Create it via `Assets > Create > NodeTree System/Config Node Tree`; a new asset is auto-seeded with built-in node types (**Normal** / **Ending**), built-in state tags (**Unlock** / **Finished**), and a **start node** at the canvas origin.

| Field | Description |
|-------|-------------|
| `nodes` | `List<NodeData>`, all node instances |
| `nodeTypes` | `List<NodeTypeData>`, type definitions (appearance + UI prefab + line style) |
| `tags` | `List<NodeTagData>`, the tag vocabulary (state tags a node can carry); seeded with `Unlock` (auto-refresh) and `Finished` |
| `layoutDirection` | `ELayoutDirection`, overall canvas layout direction |
| `zoom` | Reserved field, currently unused; editor canvas zoom is persisted via `EditorPrefs` |

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
| `tagRules` | `List<NodeTagRule>`, one rule per tag in the vocabulary; each holds the `ConditionExpression` that gates attaching that tag on this node (auto-synced with `NodeTreeData.tags`) |
| `uiIcon` | Node icon (`Sprite`) |
| `nodeName` / `nodeDesc` | Node name / description (`com.ale.toolkit`'s `AttributeValue`(Text): plain text + optional localization reference; `ResolveText()` prefers localized, falls back to plain) |
| `position` | Canvas pixel coordinates |
| `attributeValues` | `List<AttributeEntry>` (`com.ale.toolkit`), custom attribute values; the field schema is defined by the owning `NodeTypeData.attributes` |
| `childNodeIds` | Child node IDs (forming the tree / graph) |

**Main API** (`NodeData` inherits `com.ale.toolkit`'s `AttributeOwner`):

- `T GetAttributeValue<T>(string id, T fallback = default)` / `bool SetAttributeValue<T>(string id, T value)` / `AttributeEntry GetEntry(string id)` — O(1) read/write of custom attribute values.
- `void RebuildAttributes(NodeTreeData config)` — reconciles `attributeValues` against the owning node type's `attributes` schema (add defaults / drop removed / reset on type drift).
- `void RebuildTagRules(NodeTreeData config)` — reconciles `tagRules` against `NodeTreeData.tags` (add a rule per new tag / drop rules whose tag was removed), so every node keeps exactly one rule per tag in the vocabulary.
- `NodeTagRule GetTagRule(string tagName)` — returns the rule (and thus the gating `ConditionExpression`) for a given tag, or `null` when absent.

### Node type `NodeTypeData` and line style `LineTypeData`

`NodeTypeData` describes a category of node's appearance, UI and custom attribute fields: `typeName`, `resolution` (size), `shape` (`ENodeShape`), `color`, `icon`, `label`, `uiPrefab` (in-game UI prefab, must have `UINodeBase`), `line` (`LineTypeData`), `attributes` (`List<AttributeDefinition>`, `com.ale.toolkit`; the custom attribute-field schema for node instances of this type).

- **`ENodeShape`** (editor canvas shapes): `Circle`, `Square`, `Triangle`, `Diamond`, `HorizontalCapsule`, `Parallelogram`, `Pentagon`, `Hexagon`, `Octagon`, `Star`.
- **`LineTypeData`**: `lineType` (`ELineType`: `Straight` / `Curve` / `Polyline`), `lineWidth` (pixels), `material` (line material; pair it with `NodeTree/NodeLineFlow` for a flow effect). Each connection (including its arrow) is drawn with the **target (child) node type's** `LineTypeData` — i.e. the default line style from the parent to **this (child)** type node.

### State tags `NodeTagData` / tag rules `NodeTagRule`

- `NodeTagData`: one entry in the tree-wide tag vocabulary — `tagName`, `description`, `color` (editor display tint), `autoRefresh` (whether `RefreshAllNodeStates` recomputes this tag from its condition). Built-in `Unlock` (`autoRefresh = true`) and `Finished` (`autoRefresh = false`); add your own freely. Note that for an `autoRefresh` tag an **empty condition passes (fail-open)**, so give every non-root node an explicit `Unlock` condition or it will be auto-tagged.
- `NodeTagRule`: a per-node, per-tag rule — `tagName` + `condition` (`ConditionExpression`, `com.ale.toolkit`). The `condition` is the gate for attaching that tag **on this node**; an empty expression means no gate (always passes). Rules are kept in sync with the vocabulary via `NodeData.RebuildTagRules`, and fetched via `NodeData.GetTagRule(tagName)`.
- Node custom attributes: the template side `NodeTypeData.attributes` (`List<AttributeDefinition>`, `com.ale.toolkit`) defines the field schema; the instance side `NodeData.attributeValues` (`List<AttributeEntry>`) holds the values, kept in sync via `NodeData.RebuildAttributes`.

---

## Condition system: `Ale.Condition` (toolkit) + built-in evaluators

Conditions are **not** a bespoke system anymore — node-tree wires directly into `com.ale.toolkit` **1.4.0**'s Condition System (`Ale.Condition`). The runtime depends on `Ale.Condition.Core` / `Ale.Condition.Runtime`, the editor on `Ale.Condition.Editor`.

- **`ConditionExpression`** carries a condition as a two-level AND/OR tree (expression → groups → items → params). It is what every `NodeTagRule.condition` stores, and what the node inspector edits inline.
- **Extension point** is toolkit's `Ale.Condition.IConditionEvaluator`: implement it and tag the class with `[ConditionEvaluator("Key")]` — evaluators are **auto-discovered and registered**, and appear in the editor's condition dropdown. An evaluator reads its data source through `ctx.GetService<T>()`.

**node-tree built-in evaluators** (namespace `Ale.NodeTree.Runtime`; all implement `IConditionEvaluator`, auto-register at runtime, selectable in the editor):

- **`NodeFinishedEvaluator`** — key `NodeTree.NodeFinished`, param `target` (a node ID) → whether the target node carries the `Finished` tag.
- **`NodeUnlockedEvaluator`** — key `NodeTree.NodeUnlocked`, param `target` → whether the target node carries the `Unlock` tag.
- **`NodeHasTagEvaluator`** — key `NodeTree.NodeHasTag`, params `target` + `tag` → whether the target node carries the named tag.

These evaluators read state through **`INodeTreeStateSource`** (`bool HasTag(string nodeId, string tag)`), implemented by the save manager and exposed via **`NodeTreeConditionContext : IConditionContext`**. Tag-name constants live in **`NodeTreeTags`** (`NodeTreeTags.Unlock` / `NodeTreeTags.Finished`).

**Custom evaluator** (e.g. unlock only when level is high enough):

```csharp
[ConditionEvaluator("NodeTree.PlayerLevel")]
public sealed class PlayerLevelEvaluator : IConditionEvaluator
{
    public string Key => "NodeTree.PlayerLevel";
    public string DisplayName => "Player Level";
    public string Category => "NodeTree";
    public IReadOnlyList<ConditionParamDef> ParamSchema => _schema; // declare the param schema

    // Read state via ctx.GetService<T>() (e.g. INodeTreeStateSource, or your own service)
    public bool Evaluate(IReadOnlyList<ConditionParam> parameters, IConditionContext ctx)
    {
        int need   = parameters[0].AsInt();
        int level  = ctx.GetService<IPlayerStats>().Level;
        return level >= need;
    }
}
// Discovered automatically via [ConditionEvaluator]; no manual registration needed.
```

---

## Save `NodeTreeSaveDataManager`

A static singleton that tracks each node's **tags**. It implements `INodeTreeStateSource`, **does not derive from MonoBehaviour and does not auto-save** — an external game save system reads/writes it; the plugin only owns the runtime state and its serialization.

- **Generic tag ops**: `bool HasTag(string nodeId, string tag)`, `void AddTag(string nodeId, string tag)`, `void RemoveTag(string nodeId, string tag)`, `IReadOnlyCollection<string> GetTags(string nodeId)`, `void ClearNode(string nodeId)`.
- **Whole-state & serialization**: `NodeTreeSaveData Get()` / `void Set(NodeTreeSaveData)`, `string Save()` (JSON) / `void Load(string json)`, `void Reset()` (clears all records, for "new game"). Actual persistence is left to the host. Data shape `NodeTreeSaveData { List<NodeTagState> nodes }`, where `NodeTagState { string nodeId; List<string> tags; }`.
- **Convenience façade** (each self-evaluates the tag's condition and returns whether the tag was set): `bool TrySetTag(NodeTreeData config, string nodeId, string tag)`, `bool TrySetUnlock(NodeTreeData config, string nodeId)`, `bool TrySetFinished(NodeTreeData config, string nodeId)`.
- **Auto-refresh**: `void RefreshAllNodeStates(NodeTreeData config)` recomputes every `autoRefresh` tag from its condition and attaches it — set once achieved, monotonic (never removed), and iterated to a fixed point so chained unlocks resolve in one call. **Note (fail-open)**: an `autoRefresh` tag with an **empty condition is treated as passing**, so the start / root node unlocking automatically here is expected; every non-root node must **explicitly configure its `Unlock` condition**, otherwise it will be auto-tagged as well.

```csharp
var save = NodeTreeSaveDataManager.Instance;

// Push a state at the right gameplay moment: it self-evaluates that tag's
// condition and returns whether it was set (a Finished condition is usually empty = passes directly).
save.TrySetFinished(config, "chapter_01");   // finished reading this chapter → mark Finished

// After opening the panel / loading a save, refresh every auto tag
// (Unlock cascades from the finished state of prerequisites).
save.RefreshAllNodeStates(config);
bool unlocked = save.HasTag("chapter_02", NodeTreeTags.Unlock);

// Save round-trip (persistence is the host's job)
string json = save.Save();
save.Load(json);
```

---

## Runtime UI

### `UINodeTreeWindow`

The runtime node-tree UI window (a standalone `MonoBehaviour`). Attach it to a UI root, assign `config` (`NodeTreeData`) and the content root container to display the whole tree.

- Automatically **computes and sets the root container Size** from node positions / sizes;
- **Pools node UI per node type** (via `ToolkitGameObjectPool` from `com.ale.toolkit`) and **Spawns / Despawns on demand through viewport culling**;
- **Merges a line mesh per node type** (fewer draw calls); UVs pair with `NodeTree/NodeLineFlow` for the flow effect;
- Rebuilds lines on demand in `LateUpdate` via a dirty flag;
- When `refreshStatesOnInit` is on, `InitTree` calls `RefreshAllNodeStates` so auto tags (e.g. `Unlock`) resolve on open.

**Main API**: `InitTree(NodeTreeData configOverride = null)` (initialize / rebuild the whole tree), `SelectNode(string nodeId)`, `RefreshVisibility()` (recompute viewport culling), `MarkLineDirty()` (flag lines for rebuild), `RefreshAllNodeStates()` (recompute auto tags via `NodeTreeSaveDataManager`).

### `UINodeBase`

The node UI base class (`MonoBehaviour` + `IPoolable` + pointer events). Attach it to node UI prefabs; `UINodeTreeWindow` spawns and binds them through the pool. Features: node icon display, name / description text (resolved via `AttributeValue.ResolveText()` into `TMP_Text`), hover info-popup fade in/out (via `com.ale.toolkit`'s central tween), and a click callback.

**Overridable virtuals & events**:

- `OnBindData(NodeData data, NodeTypeData type)` / `OnUnbindData()` — bind / unbind data (`OnDespawn` unbinds automatically).
- `OnNodeSelected()` / `OnNodeDeselected()` — visual feedback for select / deselect.
- `OnPointerEnterNode()` / `OnPointerExitNode()` — hover popup fade in / out.
- `OnNodeClicked(PointerEventData)` + `event Action<UINodeBase> Clicked` — click callback (subscribe to the event, or override in a subclass to trigger SFX / dialogue / story, etc.).
- `OnSpawn()` / `OnDespawn()` — `IPoolable` pool callbacks (`OnDespawn` → auto `OnUnbindData`, so reused instances carry no stale state).

### `NodeLineBuilder`

A static line-mesh builder (usable at runtime): `BuildCombinedLineMesh(segments, LineTypeData, ELayoutDirection)` **merges a batch of parent→child segments into a single mesh** (fewer draw calls) by line type (straight / bezier curve / polyline), with built-in bezier geometry helpers. Each segment uses the **target (child) node type's** `LineTypeData`, so segments are grouped by their child type's line style. UV convention: `UV.x` maps to the two sides of the line width (edge fade), `UV.y` is accumulated arc length / 100 (texture density independent of line length).

---

## Visual editor

Open via `Tools > NodeTree > Node Tree Editor` (or the "Edit in Node Tree Editor" button on a `NodeTreeData` asset's Inspector).

- **`NodeTreeEditorWindow`** (`EditorWindow`, IMGUI + GL): three-column layout — left **node-type / tag management**, center **canvas** (drag / zoom / pan / connect nodes), right **node property panel**; supports adding/deleting nodes, cutting subtrees, and auto-layout. All edits go through `Undo.RecordObject` + `EditorUtility.SetDirty`, so the window is **fully Undo / Redo aware and persists the asset**. Canvas pan / zoom / last-opened config are persisted via `EditorPrefs`.
  - The left **Tags** tab manages the `NodeTreeData.tags` vocabulary and has a **Tag settings** block on top with an **"auto-write Unlock condition"** toggle. This is a project-level setting stored in `ProjectSettings/NodeTreeEditorSettings.asset` (shared through version control): when on, adding a child node / drawing a connection on the canvas automatically writes a `NodeTree.NodeFinished(target = parentId)` condition into the child's `Unlock` rule.
  - The right **node property panel** edits each tag's gating condition inline, drawing the `NodeTagRule.condition` with toolkit's `ConditionExpression` inline drawer.
  - **Canvas viewport UX**: the mouse **wheel zooms centered on the cursor**, and **panning is done by dragging with the middle mouse button**. A **persistent operation-hint bar** sits at the bottom of the canvas (semi-transparent black background, white text). **Right-clicking on empty canvas** opens a context menu: **Reset viewport** / **Show all nodes** (zoom to fit every node) / **Focus start node** / **Create node here** (spawns at the cursor position, Undo-able) / **Auto-layout** / **Snap-to-grid** toggle.
- **`NodeDrawer`** (static): draws node shapes (circle / square / polygon, etc.) and connections (straight / bezier / polyline) with IMGUI + GL, during `Repaint` only.
- **`NodeTreeCanvasState`**: canvas interaction state (pan / zoom / selection / drag) and canvas↔screen coordinate conversion.
- **`NodeTreeDataEditor`**: a custom Inspector for `NodeTreeData` that adds an "Edit in Node Tree Editor" button on top.

---

## Line shader `NodeTree/NodeLineFlow`

A URP transparent **flowing-line** shader: main texture + flow texture UV scrolling, edge fade (`_EdgeFade`), glow (`_Glow`), global alpha (`_Alpha`), flow color (HDR). Assign a material using this shader to a node type's `LineTypeData.material` to make the connections **into** nodes of that type (the target/child type) show an animated flow.

---

## Integration & dependencies

- **`com.ale.toolkit`** (required): runtime UI pooling builds on `ToolkitPool` / `ToolkitGameObjectPool` / `IPoolable`.
- **Localization** (via `com.ale.toolkit`): node name / description are carried by `AttributeValue`(Text) (plain text + optional localization table/entry reference). When the project enables toolkit's `ATK_LOCALIZATION` macro, `ResolveText()` prefers the localized string, otherwise it falls back to plain text; this plugin needs no localization macro and does not reference `Unity.Localization` directly.
- **Hover popup fade** (built in): via `com.ale.toolkit`'s central tween (`ToolkitTween.FadeCanvasGroup`), always available, no DOTween required.
- **URP**: the flowing-line shader targets the Universal Render Pipeline.

> See the [project root README](../../README_EN.md) for installation and requirements.

---

## License

[MIT](LICENSE.md) © 2026 Ale
