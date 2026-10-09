# DotPixelPainter

Windows 11 向けの、軽くてすぐ起動するピクセルアート（ドット絵）エディタです。（仮称・開発中）

- 1px グリッド線、整数倍ズーム、等倍プレビュー
- タブで複数ファイルを編集
- Minecraft スキン編集（展開図・3D プレビュー）はオプション機能として予定

## 現在の状態

フェーズ0（技術検証）。ペンでの描画、PNG の読み書き、タブ、グリッド、左右反転表示、等倍プレビューまで動きます。

## 動作環境

- Windows 11（x64）

## ビルド

.NET 10 SDK と、Visual Studio の「WinUI アプリケーション開発」ワークロードが必要です。

```powershell
dotnet build src/DotPixelPainter.App
```

詳しくは [CLAUDE.md](CLAUDE.md) を参照してください。

## ライセンス

未定です。使用しているライブラリのライセンスは [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) にあります。
