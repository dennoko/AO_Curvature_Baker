# 詳細設計ドキュメント (Detailed Design)

本ドキュメントは、AO・曲率ベイク機能の内部構造、データフロー、およびインターフェースのより詳細な設計を定義します。

## 1. 状態管理 (State Management)

単一方向データフローを実現するため、全てのUI状態・設定値は不変（Immutable）なクラスとして保持されます。

### 1.1 `BakeState` クラス
アプリケーションが現在持っている全データを保持します。Setterを持たず、更新時はコピーを生成して返します（Recordパターン等の活用）。

```csharp
public class BakeState
{
    // 対象メッシュリスト
    public IReadOnlyList<BakeTarget> Targets { get; }
    
    // 共通ベイク設定 (AO)
    public AOSettings AOSettings { get; }
    
    // 共通ベイク設定 (Curvature)
    public CurvatureSettings CurvatureSettings { get; }
    
    // 現在の進行状態
    public BakeStatus Status { get; } // None, Baking, Denoising, Completed, Error
    public float Progress { get; }
    public string StatusMessage { get; }

    // コピー生成用メソッド
    public BakeState With( /* 変更したいプロパティ */ ) { ... }
}

public class AOSettings
{
    public bool UseSelfOcclusion { get; }
    public bool UseMutualOcclusion { get; }
    public int RayCount { get; }
    public float MaxDistance { get; }
}
```

### 1.2 `IAction` と `BakeStore`
ユーザーの操作を表現する `Action` と、それを受け取ってStateを更新する `Store` です。

**アクションの例:**
- `AddTargetAction(GameObject go)`
- `RemoveTargetAction(GameObject go)`
- `UpdateAOSettingsAction(AOSettings settings)`
- `StartBakeAction(BakeMode mode)`
- `UpdateProgressAction(float progress, string message)`

**BakeStore クラス:**
```csharp
public static class BakeStore
{
    public static BakeState State { get; private set; }
    public static event Action OnStateChanged;

    public static void Dispatch(IAction action)
    {
        // Reducerロジックの呼び出し
        State = BakeReducer.Reduce(State, action);
        OnStateChanged?.Invoke();
    }
}
```

## 2. 処理プロセスフロー (Data Flow Diagram)

非同期処理（実際にCompute Shaderを回すなど）は、アクションをトリガーにしてバックグラウンドで進行し、随時Storeに進行状況を書き込みます。

1. **View (Window):** ユーザーが「Apply & Save」ボタンを押す。
2. **View -> Store:** `Store.Dispatch(new StartBakeAction())` を発行。
3. **Store:** 状態が `Status = Baking` になりViewが再描画（UIロック＆プログレス表示）。
4. **Middleware/Orchestrator:** `StartBakeAction` を検知し、`BakeOrchestrator.ExecuteBakeAsync()` が裏で走る。
5. **Orchestrator -> Services:** 
   - `MeshFormatService` にデータ整形を依頼。
   - `AOBaker` にBVH構築とレイトレーシングを依頼。
   - 完了するたびに `Store.Dispatch(new UpdateProgressAction(...))` を叩く。
6. **Orchestrator -> Store:** 完了後、`Store.Dispatch(new BakeCompletedAction(Texture result))` 。
7. **View:** 再描画され、プログレスバーが消えて「完了しました」ステータスが表示。

## 3. サービスインターフェース設計 (SOLID Principles)

依存関係を逆転させるため（**D**ependency Inversion Principle）、上位モジュール（Orchestrator）は下位モジュールの具体的な実装ではなくインターフェースに依存します。

### 3.1 `IAOBaker`
レイトレーシングによるAO計算の責務を持ちます。DXR対応環境か否かで実装（`AOBakerHardware` vs `AOBakerCompute`）を差し替えることが可能です。

```csharp
public interface IAOBaker
{
    /// <summary>
    /// メッシュ情報と設定を受け取り、各テクセルのAO量を計算してバッファで返す。
    /// </summary>
    Task<ComputeBuffer> ComputeAOAsync(BakeContext context, AOSettings settings);
}
```

### 3.2 `ICurvatureBaker`
離散微分幾何学（ラプラス・ベルトラミ演算子）による曲率計算の責務を持ちます。

```csharp
public interface ICurvatureBaker
{
    Task<ComputeBuffer> ComputeCurvatureAsync(BakeContext context, CurvatureSettings settings);
}
```

### 3.3 `IDenoiseService`
SVGF（À-trousフィルタ等）を適用し、少ないサンプリング計算の結果を滑らかにする責務を持ちます。

```csharp
public interface IDenoiseService
{
    /// <summary>
    /// 入力バッファ（AO生データなど）、法線、深度情報を用いてデノイズを実行する。
    /// </summary>
    Task<ComputeBuffer> DenoiseAsync(ComputeBuffer rawBuffer, GBufferData gbuffer);
}
```

### 3.4 `IBakeOrchestrator`
各種Serviceを束ねてパイプラインを指揮します（Facadeパターン）。

```csharp
public interface IBakeOrchestrator
{
    /// <summary>
    /// Storeから受け取った状態を元に、適切なServiceを順序通りに呼び出す。
    /// </summary>
    Task ExecuteBakePipelineAsync(BakeState currentState);
}
```

## 4. UIの構成要素 (View Layer)

`AOBakerWindow` はロジックを持たず、描画とActionの発行のみに徹します。

```csharp
private void OnGUI()
{
    YourTheme.Initialize(); // dennokoカラースキーマの初期化
    var state = BakeStore.State;

    EditorGUI.DrawRect(new Rect(0,0,position.width,position.height), YourTheme.Surface0);

    // 進行状況による描画分岐
    if (state.Status == BakeStatus.Baking)
    {
        DrawProgressScreen(state); // プログレスビュー
    }
    else
    {
        DrawEditorScreen(state); // 通常の編集ビュー
    }
}

private void DrawEditorScreen(BakeState state)
{
    // メッシュ登録リスト表示
    DrawSection("TARGET MESHES", () => { ... });

    // AO基本設定
    DrawSection("AO SETTINGS", () => { ... });

    // AO詳細設定（トグル）
    DrawToggleSection("ADVANCED SETTINGS", ref showAdvanced, () => {
        // stateの内容を描画
        // 値が変わった（EditorGUI.EndChangeCheck）ら、Dispatch
    });

    //実行ボタン
    if (GUILayout.Button("Apply & Save"))
    {
        BakeStore.Dispatch(new StartBakeAction());
    }
}
```

## 今後のプロセス
この詳細設計に基づき、まずは「Store（状態管理）コア」と「空のView（Window）」を作成し、UIが正常に連動する基盤（スケルトン）を実装します。その後、Shaderと各Serviceの中身を埋めていくステップとなります。
