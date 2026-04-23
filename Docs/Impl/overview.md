AO/曲率ベイク機能 要件定義 (Implementation Plan)
本ドキュメントは、Unity上でSubstance Painter級の高速かつ高品質なAOマップ・曲率マップのベイクを可能にする拡張機能の機能要件と非機能要件を定義するものです。

ユーザーUXの方針
ワンクリック体感 (Simple by Default): 必要最低限の設定（対象メッシュと解像度の指定など）のみで、直感的に高品質なベイクが実行できるシンプルで洗練された基本UIを提供します。
柔軟な拡張性 (Advanced Options): 「Advanced」や「詳細設定」の折りたたみ（フォールドアウト）メニューを用意し、プロフェッショナルな用途や特殊なケースに対応できるよう、柔軟なレイ制御や相互オクルージョン設定を開放します。
機能要件 (Functional Requirements)
1. 入出力とターゲット設定
複数メッシュのバッチ処理:
複数のメッシュ（GameObject）を登録し、一まとめにベイクする機能。
マテリアル単位のきめ細かい制御:
メッシュにアタッチされたマテリアルごとに、ベイク対象のオン/オフや使用するUVチャネルを設定可能。
テクスチャ出力とアーティファクト防止:
各種解像度（256～4096等）でのAOマップ・曲率マップの出力。
ダイレーション機能（エッジパディング）: UVアイランドの境界での黒ずみ（ミップマップによるブロッキング）を防ぐための外側へのピクセル拡張。
2. アンビエントオクルージョン (AO) ベイク機能
オクルージョンの分離制御 (基本機能):
自己オクルージョン (Self-Occlusion): メッシュ自身の形状による遮蔽の有効/無効化。
相互オクルージョン (Mutual-Occlusion): 環境全体（他のメッシュ）からの遮蔽の有効/無効化。
レイトレーシングパラメータ (Advanced設定):
サンプリング数（レイ数）の設定。
影響の最大距離 (Max Distance) の制限。
ブルーノイズサンプリングシードのオン/オフ等。
3. 曲率 (Curvature) ベイク機能
離散ラ পরিস্থিতির高速計算: メッシュの曲率をジオメトリから直接算出。
曲率タイプの選択 (Advanced設定):
平均曲率 (Mean Curvature): エッジの摩耗マスク等。
ガウス曲率 (Gaussian Curvature): 特定の特徴抽出等。
4. 実行管理とプレビュー
実行時のプログレスバー表示と、安全なキャンセル処理。
非機能要件 (Non-Functional Requirements)
1. パフォーマンス (速度)
Substance Painterに匹敵する高速化:
Compute Shaderを用いたGPU完全オフロード。
可能な環境での動作として、DXR API (Hardware BVH, TraceRayInline) によるハードウェアアクセラレーションを活用。
スタックレス探索や非条件分岐型3D-DDAなど、GPUアーキテクチャに最適化された探索アルゴリズムの実装。
テクスチャ空間ラスタライズ:
計算の起点をUV空間に置くことで、複雑な判定処理を省略しスループットを向上。
2. 視覚的品質
強力なデノイズアルゴリズム:
極小サンプル数（ブルーノイズ）での計算結果を、エッジ回避型À-trousフィルタ等のSVGFベースの手法で処理し、ノイズのない滑らかなテクスチャを瞬時に生成。
連続最小二乗法による高精度化:
頂点付近等でのサンプリングアーティファクトを抑え、滑らかな結果を保証。
3. 拡張性と保守性
モジュール化されたアーキテクチャ:
今後、ノーマルマップやポジションマップといった追加ベイク機能の実装が容易になるよう、パイプラインを疎結合に設計。
アーキテクチャ設計 (Architecture Design)
保守性と拡張性を高めるため、SOLID原則および単一方向データフロー (Unidirectional Data Flow) に基づいた設計を行います。

単一方向データフロー (Unidirectional Data Flow)
ReactやReduxのような状態管理アーキテクチャをUnity Editor上で実現します。

State: UIの描画に必要な全ての状態（ベイク設定、対象メッシュ、進行状況など）を保持するイミュータブル（不変）なデータクラス。
View: Stateを読み取ってUI（EditorWindow）を描画するのみ。ユーザー操作が発生した場合はActionを発行（Dispatch）する。
Action: ユーザーからの操作内容（例：「メッシュを追加」「ベイク開始」）を定義する。
Store / Reducer: Actionを受け取り、現在のStateとActionの内容から新しいStateを生成し、Viewに変更を通知する。非同期処理（実際のベイク処理）はミドルウェアやService層に移譲する。
ディレクトリ構成 (Directory Structure)
text
AO_Curvature_Baker/
├── Docs/
├── Editor/
│   ├── Window/
│   │   ├── AOBakerWindow.cs       (View: EditorWindowの本体)
│   │   ├── UniTexTheme.cs         (dennokoworks_color_schema準拠のテーマ定義)
│   ├── Store/
│   │   ├── BakeState.cs           (状態データ)
│   │   ├── BakeStore.cs           (状態管理、ActionのDispatcher)
│   │   ├── BakeActions.cs         (Action定数/クラス群)
│   ├── Services/                  (ビジネスロジック: SOLIDの単一責任原則)
│   │   ├── BakeOrchestrator.cs    (ベイク全体の進行管理)
│   │   ├── IAOBaker.cs / AOBaker.cs           (AOベイク実行インターフェース/実装)
│   │   ├── ICurvatureBaker.cs / CurvatureBaker.cs (曲率ベイク実行インターフェース/実装)
│   │   ├── IDenoiseService.cs / SVGFDenoiseService.cs (デノイズ処理)
│   │   ├── MeshFormatService.cs   (SDFやBVH構築用のメッシュ前処理)
├── Shaders/
│   ├── Compute/
│   │   ├── AOBake.compute
│   │   ├── Curvature.compute
│   │   ├── SVGFDenoise.compute
│   │   ├── Dilation.compute
クラス・関数レベルの設計 (Class Design)
1. Store & State (状態管理)
BakeState クラス

IReadOnlyList<GameObject> TargetMeshes
bool UseSelfOcclusion, bool UseMutualOcclusion
int RayCount, float MaxDistance
BakeStatus CurrentStatus (None, Baking, Denoising, Completed, Error)
BakeStore クラス

static BakeState CurrentState { get; private set; }
public static event Action OnStateChanged;
public static void Dispatch(IAction action)
2. View (UI層)
AOBakerWindow クラス (EditorWindow 継承)

OnEnable() にて BakeStore.OnStateChanged += Repaint; を登録。
OnGUI() は BakeStore.CurrentState を参照して描画。
dennokoworks_color_schemaのガイド（window_structure_template.md等）に完全準拠し、DrawSection や DrawToggleSection を利用したデザインパターンを導入。
Color Schema: 背景 #121212, カード #1e1e1e, PrimaryText等を利用しフローティングデザインを適用。
構造: 基本機能は常時表示 (DrawSection)、レイのサンプリング数等のAdvancedオプションは DrawToggleSection で折りたたみ/グレーアウト表示。
3. Services (ビジネスロジック層)
BakeOrchestrator クラス

ベイク開始のアクションを受け取り、Taskベースでパイプライン（メッシュ解析 → 計算 → デノイズ → テクスチャ保存）をタスクフローとして管理。完了時に結果をStoreへDispatch。 AOBaker クラス (依存性注入で IAOBaker として扱う)
public ComputeBuffer ExecuteBake(Mesh mesh, BakeSettings settings) : DXR/Compute Shaderを呼び出してレイの交差判定を実行。 CurvatureBaker クラス
public ComputeBuffer ExecuteBake(Mesh mesh, CurvatureType type) : 頂点のラプラス・ベルトラミ演算子の計算を呼び出す。