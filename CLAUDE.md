# CLAUDE.md

このファイルは、このリポジトリで作業する際の Claude Code へのガイドラインを提供します。

## プロジェクト概要

**Fast AO Baker** は、3Dモデルのアンビエントオクルージョン（AO）を高速に計算し、テクスチャとして出力するための Unity エディタ拡張ツールです。GPU（Compute Shader）と加速構造（BVH）を利用することで、高精細な影を短時間で作成できます。

## 開発環境

- **Unity**: 2019.4 以降推奨（Compute Shader 必須）。
- **ビルド/テスト**: Unity エディタ拡張のため、標準的なビルドコマンドはありません。`.cs` ファイルの変更は保存時に Unity によって自動的にコンパイルされます。
- **ウィンドウの開き方**: Unity メニューの `dennokoworks/Fast AO Baker` から開きます。

## アーキテクチャ

### 状態管理 (Redux-like)
`BakeStore` がプロジェクト全体の状態 (`BakeState`) を保持し、`BakeStore.Dispatch(action)` を介してのみ変更されます。UI (`AOBakerWindow`) は `OnStateChanged` イベントを購読して再描画を行います。

### 実行パイプライン
`BakeOrchestrator` がベイク工程の全ライフサイクルを管理します。
1. **ジオメトリ構築**: `MeshFormatService` と `OcclusionGeometryBuilder` を使用してメッシュデータを準備。
2. **BVH 構築**: `BVHBuilder` による加速構造（Bounding Volume Hierarchy）の作成。
3. **GPU 計算**: `AOBaker` が Compute Shader をディスパッチして影を計算。
4. **ポストプロセス**: ノイズ除去（SVGF風）、ダイレーション（穴埋め）、ガウスぼかしの適用。

### フォルダ構成
- `Editor/dennokoworks.FastAOBaker.Editor.asmdef`: エディタ専用アセンブリ定義（`includePlatforms: ["Editor"]`）。
- `Editor/Services/`: コアロジック。AO計算、BVH構築、メッシュ変換、オーケストレーション、パス解決 (`FastAOBakerPaths`)。
- `Editor/Shaders/`: UVラスタライズ用シェーダーおよび `Compute/` 内の GPU 計算用 `.compute` ファイル（AO、曲率、ノイズ除去、ポストプロセス）。
- `Editor/Store/`: `BakeStore` と、状態 (`BakeState`)、アクション、ステータスの定義。
- `Editor/Window/`: UIの実装 (`AOBakerWindow`) とテーマ設定 (`UniTexTheme`)。
- `Editor/Source/Language/`: 多言語対応（日本語/英語）用の JSON リソース。
- `Docs/`: マニュアルおよび設計ドキュメント。

## 主要なパターン

- **Async/Await**: ベイク処理は `ExecuteBakePipelineAsync` により非同期で実行され、エディタのメインスレッドをブロックしません。
- **Compute Shader ディスパッチ**: GPU へのデータ転送には `ComputeBuffer` を使用します。低リソースモードでは、タイムアウトを監視しながら分割実行する制御が行われます。
- **ローカライゼーション**: `LocalizationManager.Get(key)` を使用して文字列を取得します。リソースは `Editor/Source/Language/` 内の JSON に定義します。
- **アセットパス解決**: シェーダーや翻訳ファイルは `FastAOBakerPaths.LoadAsset<T>(relativePath)` 経由で動的に解決するため、フォルダ移動後も動作します。
- **テクスチャ生成**: 生成されたテクスチャは `AssetDatabase` を通じて保存され、自動的にインポート設定が調整されます。

## デザインシステム
`Editor/Window/UniTexTheme.cs` に独自のカラートークンと `GUIStyle` が定義されています。
- `Surface0/1/2`: 背景のレイヤー（濃い色から順に重なりを表現）。
- `CardStyle`: 各セクションを囲む 1px 境界線付きのコンテナ。
- `ActionButtonStyle`: 下部の「ベイク実行」ボタンに使用される強調スタイル。
- `TitleStyle`: ヘッダー用の大きなフォント。
