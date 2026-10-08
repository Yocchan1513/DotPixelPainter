using System.Diagnostics;
using System.Globalization;

namespace DotPixelPainter;

/// <summary>
/// 起動時間の計測用。`--startup-log &lt;path&gt;` 付きで起動すると、
/// キャンバスが最初に描画された時刻をファイルに書き、`--exit-after-startup` があればそのまま終了する。
/// tools/measure-startup.ps1 から使う。
/// </summary>
public sealed class StartupProbe
{
    private bool _reported;

    private StartupProbe(string? logPath, bool exitAfterStartup)
    {
        LogPath = logPath;
        ExitAfterStartup = exitAfterStartup;
    }

    public string? LogPath { get; }

    public bool ExitAfterStartup { get; }

    public static StartupProbe FromCommandLine()
    {
        string[] args = Environment.GetCommandLineArgs();
        string? logPath = null;
        bool exit = false;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--startup-log" && i + 1 < args.Length)
            {
                logPath = args[++i];
            }
            else if (args[i] == "--exit-after-startup")
            {
                exit = true;
            }
        }

        return new StartupProbe(logPath, exit);
    }

    /// <summary>最初の描画が終わったときに1回だけ呼ぶ。終了すべきなら true。</summary>
    public bool ReportFirstFrame()
    {
        if (_reported || LogPath is null)
        {
            return false;
        }

        _reported = true;
        long nowTicks = DateTime.UtcNow.Ticks;
        double sinceProcessStart = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
        File.WriteAllText(LogPath, string.Create(CultureInfo.InvariantCulture, $"{nowTicks}\n{sinceProcessStart:F1}\n"));
        return ExitAfterStartup;
    }
}
