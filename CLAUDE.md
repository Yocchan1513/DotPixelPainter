# DotPixelPainter — 開発ガイド（Claude Code 向け）

Windows 11 向けの軽量なピクセルアート（ドット絵）エディタ。仮称。
企画書: https://claude.ai/code/artifact/21761432-a249-488c-a93a-052ed0df8073

## 最優先の方針: 軽量・高速起動

機能と軽さがぶつかったら軽さを取る。起動が遅くなる変更はバグと同じ扱い。

- 目標（仮）: 2回目以降の起動 0.5秒前後・遅くても1秒以内、PC起動後の初回 1.5秒以内。
- 利用者が許容できる上限は約3秒。これは「絶対に超えてはいけない線」で、目標は上の値のまま。機能が増えても上の目標に収まるよう工夫する。
- 画面を先に出す。3Dプレビュー、PSD、スキンモードなど重いものは使うときに初めて読み込む。
- 外部ライブラリは「ないと作れないもの」だけ。小さい処理は自前で書く。
- 機能を足したら `tools/measure-startup.ps1` で起動時間を測り、変化を報告する。
- Windows App SDK はまとめパッケージ（`Microsoft.WindowsAppSDK`）を使わず、必要な部品パッケージだけを参照する。まとめパッケージは AI・検索などを含み、配布サイズが約3倍になる。

## 独自実装とライセンス

- AzPainter2（Windows版）は「便利機能の考え方」だけを参考にする。コード・画面・アイコン・素材は使わない。
- Linux版 AzPainter（GPL3）のソースは「どう実装しているか」の確認程度に留める。コードのコピーや移植は絶対にしない。
- 公開ライセンス（GPL3 か MIT か）は未定。どちらも選べるよう、GPL 系ライブラリは使わない。
- 依存を追加したら `THIRD-PARTY-NOTICES.md` に名前・バージョン・ライセンスを追記する。

## 製品の方針

- 汎用のピクセルアートエディタが本体。Minecraft スキン編集（展開図・3Dプレビュー）はオプションの「スキンモード」。
- 小さい画像（〜1024px 程度）が中心。画像サイズの上限は 4096×4096。色は 8bit RGBA。
- 複数ファイルはタブで扱う。Undo・ズーム・表示位置はタブごとに持つ。
- Undo はひと筆（PixelStroke）単位で、変わった画素だけを記録する。履歴はドキュメントごとに 64MB まで。
- カスタムパレットは %LOCALAPPDATA%\DotPixelPainter\palette.txt に自動保存（AARRGGBB の16進、1行1色）。他ソフトとは .gpl でやり取りする。
- 機能は「作者（利用者）が実際に困ったか」で足す。

## 構成

```
src/DotPixelPainter.Core/   描画の中心（画像・レイヤー・合成・直線など）。UI に依存しない。AOT 対応。
src/DotPixelPainter.App/    WinUI 3 + Win2D の画面。インストール不要の単体アプリ（unpackaged, self-contained）。
tests/DotPixelPainter.Core.Tests/  Core の単体テスト（xUnit）
tools/measure-startup.ps1   起動時間の計測
```

- 描画ロジックは Core に置き、テストを書く。App は表示と入力の受け渡しに徹する。
- 色は `uint` の 0xAARRGGBB（ストレートアルファ）。表示用に乗算済み BGRA へ変換するのは App 側。
- キャンバスは物理ピクセル単位で整数倍に拡大し、最近傍補間で描く（ドットをにじませない）。

## ビルドとテスト

```powershell
dotnet test                                   # Core のテスト
dotnet build src/DotPixelPainter.App          # Debug ビルド
# 配布用（Native AOT。こちらが本番）
dotnet publish src/DotPixelPainter.App -c Release -r win-x64 -o artifacts/publish
# 比較用（ReadyToRun + トリミング。C++ ツールがない環境でも作れる）
dotnet publish src/DotPixelPainter.App -c Release -r win-x64 -o artifacts/publish-r2r -p:PublishAot=false -p:PublishReadyToRun=true -p:PublishTrimmed=true
# 起動時間の計測（-Breakdown で段階ごとの内訳も出る）
./tools/measure-startup.ps1 -Exe artifacts/publish/DotPixelPainter.exe -Runs 10 -Breakdown
```

- Release の既定は Native AOT（`PublishAot=true`）。AOT ビルドには Visual Studio の「C++ によるデスクトップ開発」ワークロードが必要。
- AOT ビルドで `'vswhere.exe' は...認識されていません` と出たら、`C:\Program Files (x86)\Microsoft Visual Studio\Installer` を PATH に足す（ワークロード追加直後で、再起動前の場合に起きる）。
- 2026-10-09 時点の実測（Ryzen 5 5500、2回目以降の中央値）: Native AOT 約0.43秒、ReadyToRun 約0.49秒。
- 公開用出力にアプリの .pri を含めるため `EnableMsixTooling=true` が必要（外すと起動直後に落ちる）。

### Native AOT の落とし穴（Debug では動くのに配布版だけ壊れる）

- `Application.Current.Resources["CaptionTextBlockStyle"]` を `(Style)` に変換すると InvalidCastException になる。コードで作る部品の文字スタイルは FontSize などを直接指定する（Brush の取り出しは問題なし）。
- 投げっぱなしの非同期処理（`_ = FooAsync()`）はエラーが消えて何も起きないように見える。必ず `ErrorLog.Run("どこ", FooAsync)` を通す。エラーは `%LOCALAPPDATA%\DotPixelPainter\error.log` に残る。
- 画面を足したら、Debug だけでなく AOT の配布版でも開いて確かめる。ダイアログは `--test-open new-document` で起動すると、入力を送らずに開いて撮影できる。

## 見た目の確認

UI の見た目や操作感は Claude Code だけでは確かめにくい。フェーズごとに利用者が実際に触って確認する。
スクリーンショットで指摘をもらったら、その画面を再現してから直す。

- 画面の確認は PrintWindow（PW_RENDERFULLCONTENT）でウィンドウだけを撮る。他のアプリの裏にあっても撮れる。
- マウスやキーボードの入力を自動で送るテストは、必ず事前に利用者へ伝えて OK をもらってから行う。利用者の別のアプリ（Illustrator など）が前面にあると、そちらに入力が届いてしまう。送る前に対象ウィンドウが前面にあることも確かめる。
- 操作の正しさは、できるだけ Core の単体テストで確かめる。
