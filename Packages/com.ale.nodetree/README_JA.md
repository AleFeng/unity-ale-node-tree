# ノードツリーシステム（Node Tree System）

<p align="center">
  🌍
  <a href="./README.md">中文</a> |
  <a href="./README_EN.md">English</a> |
  日本語
</p>

Unity 向けの**ビジュアルなノードツリー / スキルツリー / テックツリー**プラグインです。1 つの `NodeTreeData` アセットに**ノード・ノードタイプ・解放条件・キャンバスレイアウト**を集約し、**ビジュアルエディタ**（キャンバス上でのドラッグ / ズーム / パン / 接続）と、すぐ使える**ランタイム UI**（タイプ別オブジェクトプール、ビューポートカリング、URP 流光ライン）を同梱します。各ノードの**解放済み / 完了済み**状態はセーブマネージャが管理し、解放条件は**差し替え可能な条件チェッカー**で判定します。

- データ駆動：1 つの `NodeTreeData`（ScriptableObject）がツリー全体を保持。エディタは Undo / Redo に完全対応。
- 高性能ランタイム：ノードタイプ別のオブジェクトプール、ビューポートカリングによるオンデマンド Spawn / Despawn、ライン Mesh のバッチ化でドローコール削減。
- 拡張可能な条件：`INodeConditionChecker` を実装すればカスタム解放条件を追加可能。組み込みで「解放済み / 完了済み」の 2 チェッカー。
- セーブ連携：`NodeTreeSaveDataManager` が解放 / 完了状態を管理し JSON シリアライズに対応。任意のセーブシステムに組み込み可能。
- 基盤統合：ノード名 / 説明のローカライズは基盤パッケージ `com.ale.toolkit` の `AttributeValue`(Text) が担う（プロジェクトが toolkit の `ATK_LOCALIZATION` を有効化すると多言語テキスト、無効時はプレーンテキストにフォールバック。本プラグインにローカライズマクロは不要）。ホバーポップアップのフェードは `com.ale.toolkit` の中央 Tween。オブジェクトプールは `com.ale.toolkit` を利用。

---

## モジュール概要

| モジュール | 役割 | 主な型 |
|-----------|------|--------|
| **設定** | ノードツリー設定アセット | `NodeTreeData` |
| **データ** | ノード / タイプ / 条件 / カスタム属性 | `NodeData`、`NodeTypeData`、`LineTypeData`、`ConditionData`、`ConditionGroupData`、`NodeConditionTypeData` |
| **条件** | 解放判定と拡張 | `INodeConditionChecker`、`NodeConditionManager` |
| **セーブ** | 解放済み / 完了済み状態 | `NodeTreeSaveDataManager` |
| **ランタイム UI** | ノードツリー表示 | `UINodeTreeWindow`、`UINodeBase`、`NodeLineBuilder` |
| **エディタ** | ビジュアル編集 | `NodeTreeEditorWindow`、`NodeDrawer`、`NodeTreeCanvasState`、`NodeTreeDataEditor` |
| **シェーダー** | 流光ライン | `NodeTree/NodeLineFlow` |

ランタイムアセンブリ `Ale.NodeTree.Runtime`、エディタアセンブリ `Ale.NodeTree.Editor`（名前空間も同名）。

---

## 設定アセット `NodeTreeData`

ツリー全体の唯一のデータソース（`ScriptableObject`）。`Assets > Create > NodeTree System/Config Node Tree` で作成し、新規作成時に組み込みノードタイプ（**普通** / **エンディング**）、組み込み条件タイプ（**NodeUnlocked** / **NodeFinished**）、キャンバス原点の**開始ノード**が自動で投入されます。

| フィールド | 説明 |
|-----------|------|
| `nodes` | `List<NodeData>`、全ノードインスタンス |
| `nodeTypes` | `List<NodeTypeData>`、タイプ定義（外観 + UI プレハブ + ライン様式） |
| `conditionTypes` | `List<NodeConditionTypeData>`、利用可能な条件タイプのメタデータ |
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
| `conditionSatisfyType` | 条件グループ間の `EConditionSatisfyType`（`All` = AND / `Any` = OR） |
| `conditionGroups` | `List<ConditionGroupData>`、解放条件（複数グループ・各グループ複数条件） |
| `uiIcon` | ノードアイコン（`Sprite`） |
| `nodeName` / `nodeDesc` | ノード名 / 説明（`com.ale.toolkit` の `AttributeValue`(Text)：プレーンテキスト + 任意のローカライズ参照。`ResolveText()` はローカライズ優先・プレーンにフォールバック） |
| `position` | キャンバスのピクセル座標 |
| `attributeValues` | `List<AttributeEntry>`（`com.ale.toolkit`）、カスタム属性値。フィールド schema は所属する `NodeTypeData.attributes` が定義 |
| `childNodeIds` | 子ノード ID リスト（ツリー / グラフ構造を構成） |

**主な API**（`NodeData` は `com.ale.toolkit` の `AttributeOwner` を継承）：

- `T GetAttributeValue<T>(string id, T fallback = default)` / `bool SetAttributeValue<T>(string id, T value)` / `AttributeEntry GetEntry(string id)` —— カスタム属性値を O(1) で読み書き。
- `void RebuildAttributes(NodeTreeData config)` —— 所属ノードタイプの `attributes` schema に合わせて `attributeValues` を調整（既定値の追加 / 削除済みの除去 / 型変化時のリセット）。
- `bool IsUnlock(object context = null)` —— `conditionSatisfyType` と各 `ConditionGroupData` から解放状態を評価。`conditionGroups` が空なら無条件で解放（`true`）。評価は `NodeConditionManager` 経由で各チェッカーにルーティングされます。
- `bool IsFinish()` —— `NodeTreeSaveDataManager` 経由で完了状態を照会。

### ノードタイプ `NodeTypeData` とライン様式 `LineTypeData`

`NodeTypeData` はノードの外観・UI・カスタム属性フィールドを記述：`typeName`、`resolution`（サイズ）、`shape`（`ENodeShape`）、`color`、`icon`、`label`、`uiPrefab`（ゲーム内 UI プレハブ、`UINodeBase` が必要）、`line`（`LineTypeData`）、`attributes`（`List<AttributeDefinition>`、`com.ale.toolkit`。本タイプのノードインスタンス用のカスタム属性フィールド schema）。

- **`ENodeShape`**（エディタキャンバスの形状）：`Circle`、`Square`、`Triangle`、`Diamond`、`HorizontalCapsule`、`Parallelogram`、`Pentagon`、`Hexagon`、`Octagon`、`Star`。
- **`LineTypeData`**：`lineType`（`ELineType`：`Straight` 直線 / `Curve` 曲線 / `Polyline` 折れ線）、`lineWidth`（ピクセル）、`material`（ライン用マテリアル。`NodeTree/NodeLineFlow` と組み合わせると流光表現）。

### 条件データ `ConditionData` / `ConditionGroupData`

- `ConditionData`：単一条件 —— `conditionType`（`NodeConditionTypeData.conditionType` を参照）、`comparison`（`EConditionComparison`：`Equal` / `NotEqual` / `Greater` / `Less`）、`conditionParam`（チェッカーへ渡すパラメータ文字列）。
- `ConditionGroupData`：条件グループ —— `satisfyType`（`EConditionSatisfyType` All/Any）+ `conditions`（`List<ConditionData>`）。空グループは制限なし（常に通過）扱い。
- `NodeConditionTypeData`：条件タイプのメタデータ（`conditionType` + `description`）。`NodeTreeData` に事前登録し、エディタでの表示 / 選択に使用。
- ノードのカスタム属性：テンプレート側 `NodeTypeData.attributes`（`List<AttributeDefinition>`、`com.ale.toolkit`）がフィールド schema を定義し、インスタンス側 `NodeData.attributeValues`（`List<AttributeEntry>`）が値を保持。両者は `NodeData.RebuildAttributes` で同期。

---

## 条件システム

「ノードが解放条件を満たすか」を判定します。ロジックとデータは分離されており、データ層は `conditionType` + `comparison` + `conditionParam` を記述するだけで、実際の判定は `NodeConditionManager` に登録されたチェッカーが行います。

- **`INodeConditionChecker`**：`string ConditionType { get; }` + `bool Check(string conditionParam, EConditionComparison comparison, object context)`。
- **`NodeConditionManager`**（静的シングルトン）：`Register(INodeConditionChecker)` / `Unregister(string conditionType)` / `Check(conditionType, conditionParam, comparison, context)`（`conditionType` が空または未登録なら `true` を返し、進行を妨げない）。初回アクセス時に組み込みチェッカーを自動登録。
- **組み込みチェッカー**：`NodeUnlockedChecker`（`conditionType = "NodeUnlocked"`、`NodeTreeSaveDataManager.IsNodeUnlocked` を参照）、`NodeFinishedChecker`（`"NodeFinished"`、`IsNodeFinished` を参照）。`conditionParam` は対象ノードの `nodeId`。

**カスタム条件**（例：レベルが十分なら解放）：

```csharp
public class LevelChecker : INodeConditionChecker
{
    public string ConditionType => "PlayerLevel";

    // conditionParam = 必要レベル；context は呼び出し側から渡される（ここではプレイヤーレベル）
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

// ゲーム起動時に一度だけ登録
NodeConditionManager.Instance.Register(new LevelChecker());

// 評価（context は各チェッカーへ透過的に渡される）
bool unlocked = node.IsUnlock(context: player.Level);
```

---

## セーブ `NodeTreeSaveDataManager`

各ノードの**解放済み / 完了済み**状態を管理する静的シングルトン。**MonoBehaviour を継承せず、自動保存も行いません** —— 外部のゲームセーブシステムが読み書きし、プラグインはランタイム状態とそのシリアライズのみを担当します。

- **照会**：`bool IsNodeUnlocked(string nodeId)`、`bool IsNodeFinished(string nodeId)`。
- **変更**：`void SetNodeUnlocked(string nodeId, bool)`、`void SetNodeFinished(string nodeId, bool)`。
- **セーブ連携**：`NodeTreeSaveData GetSaveData()`（ディープコピーを返す）、`void SetSaveData(NodeTreeSaveData)`（上書き。`null` は無視）。データ構造 `NodeTreeSaveData { List<string> unlockedNodeIds; List<string> finishedNodeIds; }`。
- **JSON**：`string SerializeToJson()`、`void DeserializeFromJson(string)`（`JsonUtility` ベース、外部依存なし）。
- **リセット**：`void Reset()`（全記録をクリア、「ニューゲーム」用）。

```csharp
var mgr = NodeTreeSaveDataManager.Instance;
mgr.SetNodeUnlocked("node_02", true);
mgr.SetNodeFinished("node_01", true);

string json = mgr.SerializeToJson();   // あなたのセーブシステムで永続化
// ……ロード時：
mgr.DeserializeFromJson(json);
```

---

## ランタイム UI

### `UINodeTreeWindow`

ランタイムのノードツリー UI ウィンドウ（単体の `MonoBehaviour`）。UI ルートにアタッチし、`config`（`NodeTreeData`）とコンテンツルートコンテナを指定すればツリー全体を表示します。

- 各ノードの位置 / サイズから**ルートコンテナの Size を自動計算・設定**；
- **ノードタイプ別にオブジェクトプール**（`com.ale.toolkit` の `ToolkitGameObjectPool`）を維持し、**ビューポートカリングでオンデマンドに Spawn / Despawn**；
- **ノードタイプごとにライン Mesh を統合生成**（ドローコール削減）。UV は `NodeTree/NodeLineFlow` と連携して流光表現；
- `LateUpdate` でダーティフラグに応じてラインを再構築。

**主な API**：`InitTree(NodeTreeData configOverride = null)`（ツリー全体を初期化 / 再構築）、`SelectNode(string nodeId)`、`RefreshVisibility()`（ビューポートカリングを再計算）、`MarkLineDirty()`（ラインを再構築対象としてマーク）。

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

- **`NodeTreeEditorWindow`**（`EditorWindow`、IMGUI + GL）：3 カラムレイアウト —— 左：**ノードタイプ / 条件タイプ管理**、中央：**キャンバス**（ノードのドラッグ / ズーム / パン / 接続）、右：**ノードプロパティパネル**。ノードの追加 / 削除、サブツリーの切り離し、自動レイアウトに対応。すべての変更は `Undo.RecordObject` + `EditorUtility.SetDirty` を通し、**Undo / Redo に完全対応しアセットを保存**します。キャンバスのパン / ズーム / 最後に開いた設定は `EditorPrefs` に永続化。
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
