using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace DotPixelPainter;

/// <summary>
/// 起動時間の計測用。`--startup-log &lt;path&gt;` 付きで起動すると、
/// キャンバスが最初に描画された時刻をファイルに書き、`--exit-after-startup` があればそのまま終了する。
/// 途中の区切り（Mark）も書くので、起動のどこに時間がかかっているかが分かる。
/// tools/measure-startup.ps1 から使う。
/// </summary>
public sealed class StartupProbe
{
    private readonly List<(string Name, long Ticks)> _marks = [];
    private bool _reported;

    private StartupProbe(string? logPath, bool exitAfterStartup)
    {
        LogPath = logPath;
        ExitAfterStartup = exitAfterStartup;
    }

    public static StartupProbe Current { get; } = FromCommandLine();

    public string? LogPath { get; }

    public bool ExitAfterStartup { get; }

    private static StartupProbe FromCommandLine()
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

    /// <summary>起動途中の区切りを記録する。計測していないときは何もしない。</summary>
    public void Mark(string name)
    {
        if (LogPath is not null && !_reported)
        {
            _marks.Add((name, DateTime.UtcNow.Ticks));
        }
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
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{nowTicks}\n{sinceProcessStart:F1}\n");
        foreach (var (name, ticks) in _marks)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{name}={ticks}\n");
        }

        File.WriteAllText(LogPath, sb.ToString());
        return ExitAfterStartup;
    }
}
