<#
.SYNOPSIS
  配布用の zip を作る（GitHub の Releases に置くもの）。

.DESCRIPTION
  x64 と ARM64 のそれぞれについて、次の2種類を作る。
    完全版: Windows App SDK を同梱する。展開してすぐ動く。
    軽量版: Windows App SDK を同梱しない。PC に Windows App Runtime 2.5 が必要。
  デバッグ用の .pdb は外し、THIRD-PARTY-NOTICES.md と使い方の説明を入れる。
  版の番号は DotPixelPainter.App.csproj の <Version> から取る。
  できたものは artifacts/package/ に置く。

.EXAMPLE
  ./tools/package.ps1
#>
param(
    [string[]]$Runtimes = @('win-x64', 'win-arm64')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/DotPixelPainter.App'
$csproj = Join-Path $project 'DotPixelPainter.App.csproj'
$version = ([xml](Get-Content $csproj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'csproj に <Version> がありません。' }

$packageDir = Join-Path $root 'artifacts/package'
if (Test-Path $packageDir) { Remove-Item $packageDir -Recurse -Force }
New-Item -ItemType Directory $packageDir | Out-Null

# Native AOT のリンカーが vswhere.exe を探すので、PATH に入っていなければ足す
$installer = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer'
if ((Test-Path $installer) -and ($env:PATH -notlike "*$installer*")) { $env:PATH += ";$installer" }

$readmeFull = @"
DotPixelPainter $version

使い方
  1. このフォルダを好きな場所に置く
  2. DotPixelPainter.exe を起動する

  署名のないアプリなので、初めて起動するときに「Windows によって PC が保護されました」と出ることがあります。
  そのときは「詳細情報」→「実行」を押してください。

  .dotpix ファイルをダブルクリックで開きたいときは、アプリの「ヘルプ」メニューから登録できます。
  アンインストールはフォルダを消すだけです（設定は %LOCALAPPDATA%\DotPixelPainter にあります）。

動作環境
  Windows 11

使用しているソフトウェアのライセンスは THIRD-PARTY-NOTICES.md を見てください。
https://github.com/Yocchan1513/DotPixelPainter
"@

$readmeLite = $readmeFull -replace '動作環境\r?\n  Windows 11', @"
動作環境
  Windows 11
  Windows App Runtime 2.5 以上（この軽量版には含まれていません）
    入っていないときは起動時に案内が出ます。次のページの「Windows App Runtime」の 2.5 以降を入れてください。
    https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads
"@

$results = @()
foreach ($rid in $Runtimes) {
    $platform = if ($rid -eq 'win-arm64') { 'ARM64' } else { 'x64' }
    foreach ($lite in @($false, $true)) {
        $name = "DotPixelPainter-$version-$rid" + $(if ($lite) { '-lite' } else { '' })
        $out = Join-Path $packageDir $name
        Write-Output "== $name"

        $publishArgs = @('publish', $project, '-c', 'Release', '-r', $rid, "-p:Platform=$platform", '-o', $out, '-nologo', '-v:q')
        if ($lite) { $publishArgs += '-p:WindowsAppSDKSelfContained=false' }
        & dotnet @publishArgs
        if ($LASTEXITCODE -ne 0) { throw "$name の発行に失敗しました。" }

        # 取り違えていないか確かめる（完全版には WinUI 本体があり、軽量版にはない）
        $hasWinUI = Test-Path (Join-Path $out 'Microsoft.ui.xaml.dll')
        if ($hasWinUI -eq $lite) { throw "$name の中身が想定と違います（WinUI 同梱: $hasWinUI）。" }

        Get-ChildItem $out -Filter *.pdb -Recurse | Remove-Item -Force
        Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') $out
        Set-Content -Path (Join-Path $out 'はじめにお読みください.txt') -Value $(if ($lite) { $readmeLite } else { $readmeFull }) -Encoding utf8BOM

        $zip = "$out.zip"
        Compress-Archive -Path $out -DestinationPath $zip -CompressionLevel Optimal
        $folderMB = (Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
        $results += [pscustomobject]@{
            Zip      = Split-Path $zip -Leaf
            FolderMB = [math]::Round($folderMB, 1)
            ZipMB    = [math]::Round((Get-Item $zip).Length / 1MB, 1)
        }
    }
}

# 次の普段のビルドが完全版の設定で復元し直すようにする
& dotnet restore $project -nologo -v:q | Out-Null

$results | Format-Table -AutoSize
Write-Output "出力先: $packageDir"
