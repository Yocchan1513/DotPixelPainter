# DotPixelPainter

Windows 11 向けの、軽くてすぐ起動するピクセルアート（ドット絵）エディタです。（仮称・開発中）

- 1px グリッド線、整数倍ズーム、等倍プレビュー
- タブで複数ファイルを編集
- Minecraft スキン編集（展開図・3D プレビュー）はオプション機能として予定

## 現在の状態

フェーズ0（技術検証）。ペンでの描画、PNG の読み書き、タブ、グリッド、左右反転表示、等倍プレビューまで動きます。

## ダウンロード

[Releases](https://github.com/Yocchan1513/DotPixelPainter/releases) から zip をダウンロードし、展開して `DotPixelPainter.exe` を起動します。インストールは不要です。

| ファイル | 向いているPC |
| --- | --- |
| `DotPixelPainter-<版>-win-x64.zip` | ふつうのPC（Intel・AMD）。迷ったらこれ |
| `DotPixelPainter-<版>-win-arm64.zip` | Snapdragon などの ARM の PC（Surface Pro 11 など） |
| `…-lite.zip` | 軽量版。Windows App Runtime 2.5 以上が入っている PC 向け |

署名のないアプリなので、初めて起動するときに「Windows によって PC が保護されました」と出ることがあります。「詳細情報」→「実行」で起動できます。

## 動作環境

- Windows 11（x64 / ARM64）
- 軽量版だけ、[Windows App Runtime](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads) 2.5 以上が必要です

## ビルド

.NET 10 SDK と、Visual Studio の「WinUI アプリケーション開発」ワークロードが必要です。

```powershell
dotnet build src/DotPixelPainter.App
```

詳しくは [CLAUDE.md](CLAUDE.md) を参照してください。

## ライセンス

未定です。使用しているライブラリのライセンスは [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) にあります。
