# 実装計画書

## タスク名
直線の描画機能の実装

## 実施ステップ

### ステップ1: LineObjectの実装
- **対象ファイル**: `Models/GraphicObject.cs`
- **内容**:
  - `GraphicObject`の継承クラスとして`LineObject`を追加する。
  - プロパティとして`EndX`と`EndY`を定義し、マウスの終点方向を正確に保持できるようにする。
  - `Draw(SKCanvas canvas)` メソッドをオーバーライドし、`canvas.DrawLine()` を利用して指定した線の太さと色で直線を描画する。

### ステップ2: キャンバスマウスハンドラーの改修
- **対象ファイル**: `Views/CanvasView.xaml.cs`
- **内容**:
  - **MouseLeftButtonDown**: `DrawingTool.Line` に対する `switch` リストを追加し、新しい `LineObject` を生成する。
  - **MouseMove**: 一時オブジェクトが `LineObject` の場合、`X, Y` プロパティではなく `EndX, EndY` 等に現在座標を設定するよう分岐処理を入れる。
  - **MouseLeftButtonUp**: `LineObject` の色を本番用の色 (例えば黒や指定カラー) に設定してコレクションに追加する。

### ステップ3: 動作確認
- アプリケーションをビルド・実行し、直線ツールで任意の方向に直線が引けるかどうかテストする。
- 既存の矩形や楕円の描画に影響が出ていないか、確認を行う。
