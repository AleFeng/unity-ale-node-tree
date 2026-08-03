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
  <a href="./README_EN.md">English</a> |
  日本語
</p>

<p align="center">
  📥
  <a href="#-インストール">インストール</a> |
  <a href="#-クイックスタート">クイックスタート</a> |
  <a href="Packages/com.ale.nodetree/README_JA.md">ドキュメント</a>
</p>

# Ale Node Tree - ノードツリーシステム

Ale Node Tree は `Unity` 向けの**ビジュアルなノードツリープラグイン**で、**スキルツリー / テックツリー / レベル進行ツリー / ストーリー進行ツリー**など、あらゆる「ノード + 接続 + 解放条件」構造の構築と表示に使えます。
1 つの `NodeTreeData` アセットに**ノード・ノードタイプ・タグ語彙・キャンバスレイアウト**を集約し、**ビジュアルエディタ**（キャンバス上でのドラッグ / ズーム / パン / 接続、Undo / Redo 完全対応）と、すぐ使える**ランタイム UI**（タイプ別オブジェクトプール、ビューポートカリング、URP 流光ライン）を同梱します。各ノードの状態は**タグ（`Unlock` / `Finished` など）**で表現し、各タグを付与する条件は基盤 `com.ale.toolkit` の**条件システム（`Ale.Condition`）**の `ConditionExpression` で記述します —— 判定ロジックは設定データと分離されているため、データモデルを変えずに任意のゲームルールを組み込めます。

## 📜 目次
- [Ale Node Tree - ノードツリーシステム](#ale-node-tree---ノードツリーシステム)
  - [📜 目次](#-目次)
  - [概要](#概要)
    - [特徴](#特徴)
    - [モジュール一覧](#モジュール一覧)
  - [💻 動作環境](#-動作環境)
  - [📦 インストール](#-インストール)
    - [UPM を使う（推奨）](#upm-を使う推奨)
    - [その他の方法](#その他の方法)
  - [🚀 クイックスタート](#-クイックスタート)
  - [📖 ドキュメント](#-ドキュメント)
  - [📁 ディレクトリ構成](#-ディレクトリ構成)
  - [📋 TODO](#-todo)
  - [📄 ライセンス](#-ライセンス)

## 概要
多くのゲームは「ノード + 接続 + 解放条件」のツリー構造 —— スキルツリー、テックツリー、レベル進行、ストーリー進行…… を必要としますが、エディタ描画・接続レンダリング・ビューポート性能・解放判定を毎回ゼロから作るのは高コストです。Ale Node Tree はそれらを**1 つのデータアセット**と**1 つのツールチェーン**にまとめます：

1. **ビジュアル編集** —— 1 つの `NodeTreeData` がツリー全体を保持。エディタは 3 カラムレイアウト（左：タイプ管理／中央：キャンバス（ドラッグ / ズーム / パン / 接続）／右：プロパティパネル）で、IMGUI + GL によりノード形状と接続線をリアルタイム描画。Undo / Redo に完全対応。
2. **タグと条件で駆動** —— 各ノードは**タグごとの規則（`NodeTagRule`）**を持ち、そのタグを付与する条件を基盤 `com.ale.toolkit` の `ConditionExpression`（2 段 AND / OR：式 → グループ → 項 → パラメータ）で記述。判定器は `IConditionEvaluator` を実装し `[ConditionEvaluator("Key")]` を付けるだけで自動登録。組み込みで `NodeTree.NodeFinished` / `NodeTree.NodeUnlocked` / `NodeTree.NodeHasTag` 判定器を備え、カスタムルールも同じ流儀で追加可能。
3. **高性能ランタイム** —— ランタイム UI はノードタイプ別にプール化し、ビューポートカリングでオンデマンドに Spawn / Despawn。接続線 Mesh はタイプごとにバッチ化してドローコールを削減。URP 流光ラインシェーダーを同梱。
4. **セーブ連携が容易** —— `NodeTreeSaveDataManager` がノードのタグ状態を管理し JSON シリアライズに対応。`Get()` / `Set()` / `Save()` / `Load()` で任意のゲームセーブシステムに接続（実際の永続化はホストに委譲）。
5. **基盤パッケージに統合、ハード依存なし** —— ノード名 / 説明のローカライズは基盤 `com.ale.toolkit` の `AttributeValue`(Text) が担う（プロジェクトが toolkit の `ATK_LOCALIZATION` を有効化すると多言語テキスト、無効時はプレーンテキストにフォールバック。プラグイン自体にローカライズマクロは不要）。ホバーポップアップのフェードは内蔵（`com.ale.toolkit` の中央 Tween ベース）。オブジェクトプールは `com.ale.toolkit` を再利用。

### 特徴
| 特徴 | 説明 |
| --- | --- |
| 単一アセット集約 | 1 つの `NodeTreeData` が全ノード・ノードタイプ・タグ語彙・キャンバスレイアウトを保持。エディタは ScriptableObject のみで動作、Undo / Redo 完全対応。 |
| ビジュアルエディタ | 3 カラムレイアウト + キャンバスのドラッグ / ズーム / パン / 接続；ノード追加・削除、サブツリー切り離し、自動レイアウト；IMGUI + GL で 10 種のノード形状と直線 / 曲線 / 折れ線を描画。ズームはカーソル中心、パンはマウス中ボタン；下部に操作説明バー、空白右クリックメニュー（ビューポートリセット / 全ノード表示 / 開始ノードへ移動 / ここに新規ノード / 自動レイアウト / グリッドスナップ切替）。 |
| 拡張可能な条件システム | 条件は基盤 `com.ale.toolkit` の `ConditionExpression`（2 段 AND / OR）で記述、判定は `IConditionEvaluator`（`[ConditionEvaluator]` で自動登録）が実施。組み込み判定器 `NodeTree.NodeFinished` / `NodeUnlocked` / `NodeHasTag`、カスタムルールは判定器 1 つ。 |
| 高性能ランタイム UI | ノードタイプ別オブジェクトプール（`com.ale.toolkit` ベース）、ビューポートカリングでオンデマンド Spawn / Despawn、ライン Mesh バッチ化でドローコール削減。 |
| URP 流光ライン | `NodeTree/NodeLineFlow` 透明流光シェーダー（フローテクスチャ / エッジフェード / グロー / HDR カラー）。各接続線（矢印含む）は子（対象）ノードタイプのライン様式で描画（親からこの子タイプへ向かう線に適用）。 |
| セーブ連携 | `NodeTreeSaveDataManager` がノードのタグ状態を管理、JSON シリアライズ、`Get()` / `Set()` / `Save()` / `Load()` で外部セーブに接続。 |
| 基盤統合 | ノード名 / 説明のローカライズは `com.ale.toolkit` の `AttributeValue`(Text)（`ATK_LOCALIZATION` 有効時は多言語、無効時はプレーンテキスト）。ホバーポップアップのフェードは `com.ale.toolkit` の中央 Tween。オブジェクトプールは `com.ale.toolkit`。プラグイン自体にローカライズ / DOTween マクロは不要。 |

### モジュール一覧
| モジュール | 役割 | 主な型 |
| --- | --- | --- |
| **設定** | ノードツリー設定アセット | `NodeTreeData` |
| **データ** | ノード / タイプ / タグ | `NodeData`、`NodeTypeData`、`LineTypeData`、`NodeTagData`、`NodeTagRule` |
| **条件と状態** | 条件判定と状態接続 | `ConditionExpression`(Toolkit)、`INodeTreeStateSource`、`NodeTreeSaveDataManager`、判定器 `NodeFinished` / `NodeUnlocked` / `NodeHasTag` |
| **セーブ** | ノードのタグ状態 | `NodeTreeSaveDataManager` |
| **ランタイム UI** | ノードツリー表示 | `UINodeTreeWindow`、`UINodeBase`、`NodeLineBuilder` |
| **エディタ** | ビジュアル編集 | `NodeTreeEditorWindow`、`NodeDrawer`、`NodeTreeCanvasState`、`NodeTreeDataEditor` |

> 各モジュールのフィールド・API・使い方は[ドキュメント](#-ドキュメント)を参照。

## 💻 動作環境
- `Unity 2022.3` 以降（`package.json` が宣言する最低バージョン。本リポジトリは `Unity 6000.3` で開発・保守）。
- **Universal Render Pipeline（URP）**：流光ラインシェーダー `NodeTree/NodeLineFlow` は URP 対応。
- **必須依存 [`com.ale.toolkit`](https://github.com/AleFeng/unity-ale-toolkit)**：ランタイム UI のプール化は `ToolkitPool` / `ToolkitGameObjectPool` / `IPoolable` に基づきます。
- 基盤統合：ノード名 / 説明のローカライズは **`com.ale.toolkit`** の `AttributeValue`(Text) が担う —— プロジェクトが toolkit の `ATK_LOCALIZATION` を有効化すると多言語テキスト、無効時はプレーンテキストにフォールバック（プラグイン自体にローカライズマクロは不要）。ホバーポップアップのフェードは内蔵（`com.ale.toolkit` の中央 Tween ベース、DOTween 不要）。

## 📦 インストール

> ⚠️ **本プラグインは基盤パッケージ [`com.ale.toolkit`](https://github.com/AleFeng/unity-ale-toolkit) に依存します。先に toolkit、次に本プラグインの順で導入してください。** Unity Package Manager は `package.json` の `dependencies` に書いた git URL を自動取得できないため、**順序を逆にできません**。下記と同じ方法で先に toolkit を導入します：`https://github.com/AleFeng/unity-ale-toolkit.git?path=/Packages/com.ale.toolkit`。未導入や順序の誤りは `Ale.Toolkit.* が見つからない` 系のコンパイルエラーになります —— toolkit を追加して再コンパイルを待てば解決し、本プラグインの再インストールは不要です。

### UPM を使う（推奨）
`Window > Package Manager` → 左上の `+` → `Install package from git URL...` → 次を貼り付け：

```
https://github.com/AleFeng/unity-ale-node-tree.git?path=/Packages/com.ale.nodetree
```

これで `main` の最新コミットが入ります。**バージョンを固定するには、URL 全体の末尾に `#<tag>` を付けます**（`?path=` の後）：

```
https://github.com/AleFeng/unity-ale-node-tree.git?path=/Packages/com.ale.nodetree#1.1.0
```

利用可能な tag は [Releases](https://github.com/AleFeng/unity-ale-node-tree/releases) を参照。

### その他の方法
リポジトリをダウンロードし、`Packages/com.ale.nodetree` フォルダごとプロジェクトの **`Packages/` ディレクトリ**（`Assets/` ではない）へコピーしても、Unity が自動でローカルパッケージとして認識します。

インストール後、メニューに **`Tools > NodeTree > Node Tree Editor`** が現れます。

### デモ Sample のインポート（任意）
導入後、`Window > Package Manager` で本パッケージを選択 → `Samples` → **Node Tree Demo**（設定アセット `NodeTreeData` + ランタイム UI サンプルシーン + ノードプレハブ + ラインマテリアル + ローカライズテーブル）をインポート。Play に入ってそのまま試せます。

## 🚀 クイックスタート
最短の流れは以下のとおりです。**モジュールと API の詳細は[ドキュメント](#-ドキュメント)を参照してください。**

**1. 設定アセットを作成**
```
Project パネル右クリック > Create > NodeTree System/Config Node Tree
```
新規の `NodeTreeData` には組み込みノードタイプ（普通 / エンディング）、組み込み状態タグ（`Unlock` / `Finished`）、開始ノードが自動で投入されます。

**2. ビジュアル編集**
`.asset` を選択し、Inspector 上部の「Node Tree Editor で編集」、またはメニュー `Tools > NodeTree > Node Tree Editor` を使用。左で**ノードタイプ**（形状 / 色 / サイズ / UI プレハブ / ライン様式）と**タグ**を管理し、中央キャンバスでノードをドラッグ・接続して親子関係を構築、右のプロパティパネルでノードの ID・アイコン・各タグの付与条件（`ConditionExpression`）・カスタム属性を設定します。

**3. ランタイムに組み込む**
UI ルートに `UINodeTreeWindow` コンポーネントを追加し、`NodeTreeData` を `config` にドラッグ、コンテンツルートコンテナを指定して、ランタイムで `InitTree()` を呼ぶと、ノードデータからプール化されたノード UI とバッチ化された接続線が生成されます。

```csharp
using Ale.NodeTree.Runtime;

// ツリー全体を初期化 / 再構築（InitTree(otherConfig) で設定切り替えも可）
nodeTreeWindow.InitTree();

// ノードクリックを購読（UINodeBase.Clicked）
someNodeUI.Clicked += ui => Debug.Log($"ノード {ui.nodeData.nodeId} をクリック");
```

**4. タグ状態とセーブ**
```csharp
using Ale.NodeTree.Runtime;

var save = NodeTreeSaveDataManager.Instance;

// ゲーム進行の任意のタイミングで状態を付与：内部でそのタグの条件を自己判定し、設定できたかを返す
save.TrySetFinished(config, "chapter_01");   // 本章を読了 → 完了を付与（Finished 条件は通常空 = 無条件で通過）

// パネルを開いた / セーブをロードした後に自動タグを再計算（Unlock は前提の完了状況に応じて連鎖的に解放）
save.RefreshAllNodeStates(config);
bool unlocked = save.HasTag("chapter_02", NodeTreeTags.Unlock);

// セーブの往復（実際の永続化はホストに委譲）
string json = save.Save();
save.Load(json);
```

> ⚠️ `autoRefresh` タグは条件が空の場合「通過（fail-open）」扱いです —— 開始 / ルートノードがこれで自動解放されるのは想定動作。非ルートノードには `Unlock` 条件を明示的に設定してください（未設定だと `RefreshAllNodeStates` が自動でタグを付与します）。

**5. Demo を試す**
Package Manager で本パッケージを選択 → `Samples` → **Node Tree Demo** をインポートし、デモシーンを開いて Play に入ると、ノードのプール生成と流光接続を確認できます。

## 📖 ドキュメント
本 README は概要とクイックスタート向けです。**各モジュールのフィールド・API・使い方・コード例**はパッケージ内ドキュメントを参照してください：

👉 **[Packages/com.ale.nodetree/README_JA.md](Packages/com.ale.nodetree/README_JA.md)**（[中文](Packages/com.ale.nodetree/README.md) · [English](Packages/com.ale.nodetree/README_EN.md)）

## 📁 ディレクトリ構成
```
Packages/com.ale.nodetree/           ← パッケージルート
├── package.json  CHANGELOG.md  LICENSE.md  README.md   ← パッケージ内ドキュメント（3 言語）
├── Runtime/
│   ├── Config/      ノードツリー設定アセット NodeTreeData
│   ├── Data/        データモデル（NodeData / NodeTypeData / LineTypeData / NodeTagData / NodeTagRule）
│   ├── Conditions/  条件接続（INodeTreeStateSource / NodeTreeConditionContext / NodeTreeTags / 判定器：NodeFinished・NodeUnlocked・NodeHasTag）
│   ├── Save/        セーブマネージャ NodeTreeSaveDataManager
│   ├── UI/          ランタイム UI（UINodeTreeWindow / UINodeBase）
│   └── Utility/     ライン Mesh 構築 NodeLineBuilder
├── Editor/          ビジュアルエディタ（NodeTreeEditorWindow / NodeDrawer / NodeTreeCanvasState / NodeTreeDataEditor）
├── Shaders/         URP 流光ラインシェーダー（NodeTree/NodeLineFlow）
└── Samples~/Demo/   デモ Sample（シーン + 設定アセット + ノードプレハブ + ラインマテリアル + ローカライズテーブル；Package Manager からインポート）
```

## 📋 TODO
- 組み込みノード形状とライン様式プリセットの拡充。
- サンプルシーンとランタイムユースケースの追加。

## 📄 ライセンス
本プロジェクトは [MIT License](LICENSE) で公開されており、商用・非商用を問わず自由に利用できます。
