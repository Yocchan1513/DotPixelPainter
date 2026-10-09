<#
.SYNOPSIS
  DotPixelPainter の起動時間を測る。

.DESCRIPTION
  アプリを --startup-log 付きで繰り返し起動し、
  「プロセス起動の直前」から「キャンバスが最初に描画されたあと」までの時間を測る。
  1回目は2回目以降より遅くなりやすいので別に表示する。
  -Breakdown を付けると、起動の各段階（Mark）が何ミリ秒の時点だったかの中央値も表示する。

.EXAMPLE
  ./tools/measure-startup.ps1 -Exe ./artifacts/publish/DotPixelPainter.exe -Runs 10 -Breakdown
#>
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [int]$Runs = 10,
    [switch]$Breakdown
)

$ErrorActionPreference = 'Stop'
$exePath = (Resolve-Path $Exe).Path
$logPath = Join-Path ([IO.Path]::GetTempPath()) 'dotpixelpainter-startup.txt'
$results = @()
$marks = [ordered]@{}

function Median($values) {
    $sorted = @($values | Sort-Object)
    return $sorted[[int][math]::Floor(($sorted.Count - 1) / 2)]
}

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

    # 2回目以降の区切りを集める
    if ($i -gt 1) {
        foreach ($line in ($lines | Select-Object -Skip 2)) {
            $name, $ticks = $line -split '=', 2
            if (-not $marks.Contains($name)) { $marks[$name] = @() }
            $marks[$name] += ([long]$ticks - $startTicks) / 10000.0
        }
    }

    Start-Sleep -Milliseconds 300
}

$results | Format-Table -AutoSize

if ($Breakdown -and $marks.Count -gt 0) {
    Write-Output '段階ごとの到達時刻（2回目以降の中央値）:'
    $previous = 0.0
    foreach ($name in $marks.Keys) {
        $at = Median $marks[$name]
        Write-Output ('  {0,-14} {1,7:F1} ms  (+{2,6:F1})' -f $name, $at, ($at - $previous))
        $previous = $at
    }
}

$warm = @($results | Select-Object -Skip 1 | ForEach-Object StartupMs)
if ($warm.Count -gt 0) {
    $sorted = @($warm | Sort-Object)
    Write-Output ("1回目: {0} ms / 2回目以降の中央値: {1} ms / 最速: {2} ms" -f $results[0].StartupMs, (Median $warm), $sorted[0])
}
