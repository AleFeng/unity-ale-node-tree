# ノードツリーシステム（Node Tree System）

<p align="center">
  🌍
  <a href="./README.md">中文</a> |
  <a href="./README_EN.md">English</a> |
  日本語
</p>

Unity 向けの**ビジュアルなノードツリー / スキルツリー / テックツリー**プラグインです。1 つの `NodeTreeData` アセットに**ノード・ノードタイプ・状態タグ・キャンバスレイアウト**を集約し、**ビジュアルエディタ**（キャンバス上でのドラッグ / ズーム / パン / 接続）と、すぐ使える**ランタイム UI**（タイプ別オブジェクトプール、ビューポートカリング、URP 流光ライン）を同梱します。各ノードの状態は**タグ（Tag）**で表現され、セーブマネージャが管理します。解放条件は基盤パッケージ `com.ale.toolkit` の**条件システム（`Ale.Condition`）**で判定します。

- データ駆動：1 つの `NodeTreeData`（ScriptableObject）がツリー全体を保持。エディタは Undo / Redo に完全対応。
- 高性能ランタイム：ノードタイプ別のオブジェクトプール、ビューポートカリングによるオンデマンド Spawn / Despawn、ライン Mesh のバッチ化でドローコール削減。
- 拡張可能な条件：`com.ale.toolkit` の条件システム（`Ale.Condition`）に統合。`ConditionExpression` で条件を記述し、`IConditionEvaluator` を実装してカスタム判定器を追加可能。組み込みで `NodeTree.NodeFinished` / `NodeTree.NodeUnlocked` / `NodeTree.NodeHasTag` の 3 判定器。
- セーブ連携：`NodeTreeSaveDataManager` がノードごとの**タグ状態**を管理し JSON シリアライズに対応。任意のセーブシステムに組み込み可能。
- 基盤統合：ノード名 / 説明のローカライズは基盤パッケージ `com.ale.toolkit` の `AttributeValue`(Text) が担う（プロジェクトが toolkit の `ATK_LOCALIZATION` を有効化すると多言語テキスト、無効時はプレーンテキストにフォールバック。本プラグインにローカライズマクロは不要）。ホバーポップアップのフェードは `com.ale.toolkit` の中央 Tween。オブジェクトプールは `com.ale.toolkit` を利用。

---

## モジュール概要

| モジュール | 役割 | 主な型 |
|-----------|------|--------|
| **設定** | ノードツリー設定アセット | `NodeTreeData` |
| **データ** | ノード / タイプ / 状態タグ / カスタム属性 | `NodeData`、`NodeTypeData`、`LineTypeData`、`NodeTagData`、`NodeTagRule` |
| **条件** | Toolkit 条件システムへの接続 | `INodeTreeStateSource`、`NodeTreeConditionContext`、`NodeTreeTags`、判定器（`NodeFinishedEvaluator` / `NodeUnlockedEvaluator` / `NodeHasTagEvaluator`） |
| **セーブ** | ノードごとのタグ状態 | `NodeTreeSaveDataManager` |
| **ランタイム UI** | ノードツリー表示 | `UINodeTreeWindow`、`UINodeBase`、`NodeLineBuilder` |
| **エディタ** | ビジュアル編集 | `NodeTreeEditorWindow`、`NodeDrawer`、`NodeTreeCanvasState`、`NodeTreeDataEditor` |
| **シェーダー** | 流光ライン | `NodeTree/NodeLineFlow` |

ランタイムアセンブリ `Ale.NodeTree.Runtime`、エディタアセンブリ `Ale.NodeTree.Editor`（名前空間も同名）。

---

## 設定アセット `NodeTreeData`

ツリー全体の唯一のデータソース（`ScriptableObject`）。`Assets > Create > NodeTree System/Config Node Tree` で作成し、新規作成時に組み込みノードタイプ（**普通** / **エンディング**）、組み込み状態タグ（**Unlock**（`autoRefresh=true`） / **Finished**（`autoRefresh=false`））、キャンバス原点の**開始ノード**が自動で投入されます。

| フィールド | 説明 |
|-----------|------|
| `nodes` | `List<NodeData>`、全ノードインスタンス |
| `nodeTypes` | `List<NodeTypeData>`、タイプ定義（外観 + UI プレハブ + ライン様式） |
| `tags` | `List<NodeTagData>`、状態タグの語彙（タグ辞書）。組み込みで `Unlock` / `Finished`、任意に追加可能 |
| `layoutDirection` | `ELayoutDirection`、キャンバス全体のレイアウト方向 |
| `zoom` | エディタのキャンバスズーム（エディタが書き込み。ランタイム未使用） |

**主な API**：`GetNode(string nodeId)`、`GetNodeType(string typeName)`（見つからない場合は `null`）。

---

## データモデル

### ノード `NodeData`

`NodeTreeData.nodes` に格納されるノードインスタンス。

| フィールド | 説明 |
|-----------|------|
| `nodeId` | ノードの一意 ID（同一 `NodeTreeData` 内で一意） |
| `nodeTypeRef` | いずれかの `NodeTypeData.typeName` を参照。外観と UI プレハブを決定 |
| `comment` | エディタ用メモ（ランタイムに影響なし） |
| `tagRules` | `List<NodeTagRule>`、タグごとに 1 件。各ルールはこのノードに当該タグを付与するための条件（`ConditionExpression`）。`tags` 語彙に追随して自動同期 |
| `uiIcon` | ノードアイコン（`Sprite`） |
| `nodeName` / `nodeDesc` | ノード名 / 説明（`com.ale.toolkit` の `AttributeValue`(Text)：プレーンテキスト + 任意のローカライズ参照。`ResolveText()` はローカライズ優先・プレーンにフォールバック） |
| `position` | キャンバスのピクセル座標 |
| `attributeValues` | `List<AttributeEntry>`（`com.ale.toolkit`）、カスタム属性値。フィールド schema は所属する `NodeTypeData.attributes` が定義 |
| `childNodeIds` | 子ノード ID リスト（ツリー / グラフ構造を構成） |

**主な API**（`NodeData` は `com.ale.toolkit` の `AttributeOwner` を継承）：

- `T GetAttributeValue<T>(string id, T fallback = default)` / `bool SetAttributeValue<T>(string id, T value)` / `AttributeEntry GetEntry(string id)` —— カスタム属性値を O(1) で読み書き。
- `void RebuildAttributes(NodeTreeData config)` —— 所属ノードタイプの `attributes` schema に合わせて `attributeValues` を調整（既定値の追加 / 削除済みの除去 / 型変化時のリセット）。
- `void RebuildTagRules(NodeTreeData config)` —— `config.tags` 語彙に合わせて `tagRules` を同期（追加されたタグのルールを追加 / 削除されたタグのルールを除去）。
- `NodeTagRule GetTagRule(string tagName)` —— 指定タグの `NodeTagRule`（＝そのタグを付与する条件）を取得。

### ノードタイプ `NodeTypeData` とライン様式 `LineTypeData`

`NodeTypeData` はノードの外観・UI・カスタム属性フィールドを記述：`typeName`、`resolution`（サイズ）、`shape`（`ENodeShape`）、`color`、`icon`、`label`、`uiPrefab`（ゲーム内 UI プレハブ、`UINodeBase` が必要）、`line`（`LineTypeData`）、`attributes`（`List<AttributeDefinition>`、`com.ale.toolkit`。本タイプのノードインスタンス用のカスタム属性フィールド schema）。

- **`ENodeShape`**（エディタキャンバスの形状）：`Circle`、`Square`、`Triangle`、`Diamond`、`HorizontalCapsule`、`Parallelogram`、`Pentagon`、`Hexagon`、`Octagon`、`Star`。
- **`LineTypeData`**：`lineType`（`ELineType`：`Straight` 直線 / `Curve` 曲線 / `Polyline` 折れ線）、`lineWidth`（ピクセル）、`material`（ライン用マテリアル。`NodeTree/NodeLineFlow` と組み合わせると流光表現）。

### 状態タグ `NodeTagData` / タグルール `NodeTagRule`

ノードの状態は**タグ（Tag）**で表現します。`NodeTreeData.tags` がタグの語彙（辞書）を、各 `NodeData.tagRules` がそのノードで各タグを付与する条件を保持します。

- `NodeTagData`：タグ辞書の 1 エントリ —— `tagName`（タグ名）、`description`（説明）、`color`（`Color`、エディタ表示色）、`autoRefresh`（`bool`：条件による自動再計算の対象か）。組み込みで `Unlock`（`autoRefresh=true`）と `Finished`（`autoRefresh=false`）。任意に追加可能。
- `NodeTagRule`：ノード×タグの 1 ルール —— `tagName`（対象タグ名）+ `condition`（`ConditionExpression`：このノードに当該タグを付与するための門）。空の `ConditionExpression` は門なし（常に通過）扱い。`NodeData.tagRules` は `NodeData.RebuildTagRules(config)` で `tags` 語彙に自動同期し、`NodeData.GetTagRule(tagName)` で個別取得。
- ノードのカスタム属性：テンプレート側 `NodeTypeData.attributes`（`List<AttributeDefinition>`、`com.ale.toolkit`）がフィールド schema を定義し、インスタンス側 `NodeData.attributeValues`（`List<AttributeEntry>`）が値を保持。両者は `NodeData.RebuildAttributes` で同期。`NodeData` は `com.ale.toolkit` の `AttributeOwner` を継承し、旧来の条件フィールドは一切持ちません。

---

## 条件システム：Toolkit `Ale.Condition` への接続

「ノードにあるタグを付与してよいか」の判定は、独自実装ではなく基盤パッケージ `com.ale.toolkit` の**条件システム（`Ale.Condition`）**が担います。node-tree のランタイムは `Ale.Condition.Core` / `Ale.Condition.Runtime` に、エディタは `Ale.Condition.Editor` に依存します。

- **`ConditionExpression`**：条件を表す構造（2 段の AND/OR：式 → グループ → 項 → パラメータ）。各 `NodeTagRule.condition` がこの型です。空の式は門なし（常に通過）。
- **拡張方式**：`Ale.Condition.IConditionEvaluator` を実装し `[ConditionEvaluator("Key")]` 特性を付けると自動発見・登録されます。判定器は `ctx.GetService<T>()` でデータソースを読みます。

### node-tree 組み込み判定器

いずれも名前空間 `Ale.NodeTree.Runtime`、`IConditionEvaluator` を実装し、ランタイムで自動登録・エディタのドロップダウンで選択可能：

- **`NodeFinishedEvaluator`**：キー `NodeTree.NodeFinished`、パラメータ `target`（対象ノード ID）→ 対象ノードに `Finished` タグが付いているか。
- **`NodeUnlockedEvaluator`**：キー `NodeTree.NodeUnlocked`、パラメータ `target` → `Unlock` タグが付いているか。
- **`NodeHasTagEvaluator`**：キー `NodeTree.NodeHasTag`、パラメータ `target` + `tag` → 指定タグが付いているか。

これら判定器はデータソースインタフェース **`INodeTreeStateSource { bool HasTag(string nodeId, string tag); }`**（セーブマネージャが実装）を通じて状態を照会します。上下文は **`NodeTreeConditionContext : IConditionContext`**、タグ名の定数は **`NodeTreeTags.Unlock` / `NodeTreeTags.Finished`**。

**カスタム判定器**（例：独自データソースを参照する条件）：

```csharp
[ConditionEvaluator("NodeTree.MyCondition")]
public sealed class MyEvaluator : IConditionEvaluator
{
    public string Key => "NodeTree.MyCondition";
    public string DisplayName => "私の条件";
    public string Category => "NodeTree";
    public IReadOnlyList<ConditionParamDef> ParamSchema => _schema; // パラメータ schema を宣言
    // ctx.GetService<INodeTreeStateSource>() や独自データソースから状態を読む
    public bool Evaluate(IReadOnlyList<ConditionParam> parameters, IConditionContext ctx) { /* ... */ return true; }
}
```

---

## セーブ `NodeTreeSaveDataManager`

各ノードの**タグ状態**を管理する静的シングルトン。`INodeTreeStateSource` を実装し、タグ制で動作します。**MonoBehaviour を継承せず、自動保存も行いません** —— 実際の永続化（ディスクへの読み書き）は宿主のゲームセーブシステムに委ねられ、プラグインはランタイム状態とそのシリアライズのみを担当します。

- **汎用タグ**：`bool HasTag(string nodeId, string tag)`、`void AddTag(string nodeId, string tag)`、`void RemoveTag(string nodeId, string tag)`、`IReadOnlyCollection<string> GetTags(string nodeId)`、`void ClearNode(string nodeId)`。
- **一括入出力 / シリアライズ**：`NodeTreeSaveData Get()`、`void Set(NodeTreeSaveData)`、`string Save()`（JSON を返す）、`void Load(string json)`、`void Reset()`（全記録をクリア、「ニューゲーム」用）。実際の落とし込みは宿主に委ねます。データ構造 `NodeTreeSaveData { List<NodeTagState> nodes }`、`NodeTagState { string nodeId; List<string> tags; }`。
- **便利ファサード**（内部で条件を自己判定し、設定できたかを返す）：`bool TrySetTag(NodeTreeData config, string nodeId, string tag)`、`bool TrySetUnlock(NodeTreeData config, string nodeId)`、`bool TrySetFinished(NodeTreeData config, string nodeId)`。
- **一括再計算**：`void RefreshAllNodeStates(NodeTreeData config)` —— `autoRefresh` タグを条件で再計算して付与（達成で付与・単調で外さない・不動点反復で連鎖解放に対応）。

```csharp
var save = NodeTreeSaveDataManager.Instance;

// 業務タイミングで能動的に状態を設定：内部で当該タグの条件を自己判定し、設定できたかを返す
save.TrySetFinished(config, "chapter_01");   // 本章を読了→完了を付与（Finished の条件は通常空＝直接通過）

// パネルを開いた / セーブをロードした後、全 auto タグを再計算（Unlock は前提の完了状況で連鎖解放）
save.RefreshAllNodeStates(config);
bool unlocked = save.HasTag("chapter_02", NodeTreeTags.Unlock);

// セーブの往復（落とし込みは宿主）
string json = save.Save();
save.Load(json);
```

---

## ランタイム UI

### `UINodeTreeWindow`

ランタイムのノードツリー UI ウィンドウ（単体の `MonoBehaviour`）。UI ルートにアタッチし、`config`（`NodeTreeData`）とコンテンツルートコンテナを指定すればツリー全体を表示します。

- 各ノードの位置 / サイズから**ルートコンテナの Size を自動計算・設定**；
- **ノードタイプ別にオブジェクトプール**（`com.ale.toolkit` の `ToolkitGameObjectPool`）を維持し、**ビューポートカリングでオンデマンドに Spawn / Despawn**；
- **ノードタイプごとにライン Mesh を統合生成**（ドローコール削減）。UV は `NodeTree/NodeLineFlow` と連携して流光表現；
- `LateUpdate` でダーティフラグに応じてラインを再構築。
- `refreshStatesOnInit`：オンにすると `InitTree` 時に `NodeTreeSaveDataManager.RefreshAllNodeStates(config)` を自動実行し、全 `autoRefresh` タグを再計算。

**主な API**：`InitTree(NodeTreeData configOverride = null)`（ツリー全体を初期化 / 再構築）、`RefreshAllNodeStates()`（`NodeTreeSaveDataManager` 経由で全 auto タグを再計算）、`SelectNode(string nodeId)`、`RefreshVisibility()`（ビューポートカリングを再計算）、`MarkLineDirty()`（ラインを再構築対象としてマーク）。

### `UINodeBase`

ノード UI 基底クラス（`MonoBehaviour` + `IPoolable` + ポインタイベント）。ノード UI プレハブにアタッチし、`UINodeTreeWindow` がプール経由で生成・バインドします。機能：ノードアイコン表示、名前 / 説明テキスト（`AttributeValue.ResolveText()` で解決し `TMP_Text` に反映）、マウスホバー時の情報ポップアップのフェード（`com.ale.toolkit` の中央 Tween ベース）、クリックコールバック。

**オーバーライド可能な仮想メソッド / イベント**：

- `OnBindData(NodeData data, NodeTypeData type)` / `OnUnbindData()` —— データのバインド / アンバインド（`OnDespawn` で自動アンバインド）。
- `OnNodeSelected()` / `OnNodeDeselected()` —— 選択 / 選択解除の視覚フィードバック。
- `OnPointerEnterNode()` / `OnPointerExitNode()` —— ホバーポップアップのフェードイン / アウト。
- `OnNodeClicked(PointerEventData)` + `event Action<UINodeBase> Clicked` —— クリックコールバック（イベント購読、またはサブクラスでオーバーライドして SE / ダイアログ / ストーリーなどを発火）。
- `OnSpawn()` / `OnDespawn()` —— `IPoolable` プールコールバック（`OnDespawn` → 自動 `OnUnbindData`。再利用時に古い状態が残らない）。

### `NodeLineBuilder`

静的なライン Mesh 構築ユーティリティ（ランタイム利用可）：`BuildCombinedLineMesh(segments, LineTypeData, ELayoutDirection)` は親→子のセグメント群をライン種別（直線 / ベジェ曲線 / 折れ線）ごとに**単一 Mesh へ統合**（ドローコール削減）し、ベジェ幾何ヘルパーも内蔵。UV 規約：`UV.x` はライン幅の両側（エッジフェード）、`UV.y` は累積弧長 / 100（テクスチャ密度はライン長に依存しない）。

---

## ビジュアルエディタ

`Tools > NodeTree > Node Tree Editor`（または `NodeTreeData` アセットの Inspector の「Node Tree Editor で編集」ボタン）で開きます。

- **`NodeTreeEditorWindow`**（`EditorWindow`、IMGUI + GL）：3 カラムレイアウト —— 左：**ノードタイプ / タグ管理**（左側の切替タブは「ノードタイプ / タグ」。旧「条件タイプ」タブは削除済み）、中央：**キャンバス**（ノードのドラッグ / ズーム / パン / 接続）、右：**ノードプロパティパネル**。ノードの追加 / 削除、サブツリーの切り離し、自動レイアウトに対応。すべての変更は `Undo.RecordObject` + `EditorUtility.SetDirty` を通し、**Undo / Redo に完全対応しアセットを保存**します。キャンバスのパン / ズーム / 最後に開いた設定は `EditorPrefs` に永続化。
  - **「タグ」タブ**：上部の「タグ設定」区に**「Unlock 条件を自動書き込み」**トグルがあります（プロジェクト単位の設定。`ProjectSettings/NodeTreeEditorSettings.asset` に保存され、リポジトリ経由で共有）。オンのとき、キャンバスで「子ノードを追加 / 接続」すると、子ノードの `Unlock` ルールへ `NodeTree.NodeFinished(target = 親 ID)` 条件が自動で書き込まれます。
  - **右のノードプロパティパネル**：各タグごとに、Toolkit の `ConditionExpression` インライン描画器でそのタグの付与条件を編集できます（`NodeData.tagRules` の各ルールを `NodeTreeData.tags` 語彙に沿って表示）。
- **`NodeDrawer`**（静的）：IMGUI + GL でノード形状（円 / 四角 / 多角形など）と接続線（直線 / ベジェ / 折れ線）を描画。`Repaint` 時のみ。
- **`NodeTreeCanvasState`**：キャンバスの操作状態（パン / ズーム / 選択 / ドラッグ）とキャンバス↔スクリーン座標の相互変換。
- **`NodeTreeDataEditor`**：`NodeTreeData` のカスタム Inspector。上部に「Node Tree Editor で編集」ボタンを追加。

---

## ラインシェーダー `NodeTree/NodeLineFlow`

URP 透明の**流光ライン**シェーダー：メインテクスチャ + フローテクスチャの UV スクロール、エッジフェード（`_EdgeFade`）、グロー（`_Glow`）、全体アルファ（`_Alpha`）、流光カラー（HDR）。このシェーダーを使うマテリアルをノードタイプの `LineTypeData.material` に設定すると、そのタイプの接続線が動的な流光を示します。

---

## 連携と依存

- **`com.ale.toolkit`**（必須）：ランタイム UI のプール化は `ToolkitPool` / `ToolkitGameObjectPool` / `IPoolable` に基づきます。
- **ローカライズ**（`com.ale.toolkit` 経由）：ノード名 / 説明は `AttributeValue`(Text) が担う（プレーンテキスト + 任意のローカライズ表/エントリ参照）。プロジェクトが toolkit の `ATK_LOCALIZATION` マクロを有効化すると `ResolveText()` は多言語テキストを優先、無効時はプレーンにフォールバック。本プラグイン自体にローカライズマクロは不要で、`Unity.Localization` に直接依存しません。
- **ホバーポップアップのフェード**（内蔵）：`com.ale.toolkit` の中央 Tween（`ToolkitTween.FadeCanvasGroup`）ベース。常に利用可能で、DOTween は不要。
- **URP**：流光ラインシェーダーは Universal Render Pipeline 対応。

> インストールと動作環境は[プロジェクトルート README](../../README_JA.md) を参照してください。

---

## ライセンス

[MIT](LICENSE.md) © 2026 Ale
