# タスクの詳細

## 概要
ユーザーからの指摘により、現在「直線（Line）」の描画機能が未実装（仕様上描けない状態）であることが確認された。
本タスクでは、キャンバス上で直線を正しく描画できるように機能を実装・拡張する。

## 目的
直線描画機能を追加し、UI上の「直線」ツールを選択した際に期待通りに図形（直線）を作成できるようにする。

## 要件
1. **LineObjectクラスの追加**:
   - `Models/GraphicObject.cs` において、`GraphicObject`を継承した`LineObject`クラスを作成する。
   - 直線には方向があるため、始点(`X`, `Y`)と終点(`EndX`, `EndY`)を保持するプロパティを持たせる必要がある。
2. **マウス操作の対応**:
   - `Views/CanvasView.xaml.cs` の `SkiaElement_MouseLeftButtonDown` メソッドにて、`DrawingTool.Line` 選択時に `LineObject` を一時オブジェクトとして生成する。
   - 同ファイルの `SkiaElement_MouseMove` メソッドにて、`LineObject` の場合のみ、始点から現在マウス座標への直接更新処理を追加する（単一方向のみの幅・高さではなく、終点座標を使用するため）。
   - `SkiaElement_MouseLeftButtonUp` にて、本番の線の色を指定してキャンバスへのオブジェクト追加を確定する。
