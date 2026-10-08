<#
.SYNOPSIS
  DotPixelPainter の起動時間を測る。

.DESCRIPTION
  アプリを --startup-log 付きで繰り返し起動し、
  「プロセス起動の直前」から「キャンバスが最初に描画されたあと」までの時間を測る。
  1回目は2回目以降より遅くなりやすいので別に表示する。

.EXAMPLE
  ./tools/measure-startup.ps1 -Exe ./artifacts/publish/DotPixelPainter.exe -Runs 10
#>
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [int]$Runs = 10
)

$ErrorActionPreference = 'Stop'
$exePath = (Resolve-Path $Exe).Path
$logPath = Join-Path ([IO.Path]::GetTempPath()) 'dotpixelpainter-startup.txt'
$results = @()

for ($i = 1; $i -le $Runs; $i++) {
    if (Test-Path $logPath) { [IO.File]::Delete($logPath) }

    $startTicks = [DateTime]::UtcNow.Ticks
    $process = Start-Process -FilePath $exePath -ArgumentList '--startup-log', "`"$logPath`"", '--exit-after-startup' -PassThru
    if (-not $process.WaitForExit(30000)) {
        $process.Kill()
        throw "$i 回目: 30秒以内に終了しませんでした。"
    }

    if (-not (Test-Path $logPath)) {
        throw "$i 回目: 計測ログが書かれていません（終了コード $($process.ExitCode)）。"
    }

    $lines = Get-Content $logPath
    $ms = ([long]$lines[0] - $startTicks) / 10000.0
    $results += [pscustomobject]@{ Run = $i; StartupMs = [math]::Round($ms, 1) }
    Start-Sleep -Milliseconds 300
}

$results | Format-Table -AutoSize

$warm = @($results | Select-Object -Skip 1 | ForEach-Object StartupMs | Sort-Object)
if ($warm.Count -gt 0) {
    $median = $warm[[int][math]::Floor(($warm.Count - 1) / 2)]
    Write-Output ("1回目: {0} ms / 2回目以降の中央値: {1} ms / 最速: {2} ms" -f $results[0].StartupMs, $median, $warm[0])
}
