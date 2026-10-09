using System.Globalization;
using Microsoft.UI.Xaml.Controls;

namespace DotPixelPainter;

/// <summary>
/// 前回のタブの復元。終了時に、保存先のあるファイルと選んでいたタブを session.txt に書き、
/// 次の起動の最初の描画のあとに開き直す。保存していない「無題」は復元しない（終了時に確認している）。
/// </summary>
public sealed partial class MainWindow
{
    private static string SessionFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DotPixelPainter",
        "session.txt");

    /// <summary>前回のタブを開き直すか（「…」メニューで切り替える。初めはオン）。</summary>
    private bool _restoreSession = true;

    private sealed record Session(bool Restore, int Active, List<string> Files);

    private static Session LoadSession()
    {
        var session = new Session(true, 0, []);
        try
        {
            if (!File.Exists(SessionFilePath))
            {
                return session;
            }

            bool restore = true;
            int active = 0;
            var files = new List<string>();
            foreach (string line in File.ReadAllLines(SessionFilePath))
            {
                string[] kv = line.Split('=', 2);
                if (kv.Length != 2)
                {
                    continue;
                }

                switch (kv[0])
                {
                    case "restore":
                        restore = kv[1] != "false";
                        break;
                    case "active":
                        _ = int.TryParse(kv[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out active);
                        break;
                    case "file":
                        files.Add(kv[1]);
                        break;
                }
            }

            return new Session(restore, active, files);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("前回のタブの読み込み", ex);
            return session;
        }
    }

    /// <summary>終了時に、開いていたファイルを書いておく。</summary>
    private void SaveSession()
    {
        try
        {
            var lines = new List<string> { $"restore={(_restoreSession ? "true" : "false")}" };
            int active = 0;
            int index = 0;
            foreach (object item in Tabs.TabItems)
            {
                if (item is TabViewItem tabItem && _tabs.TryGetValue(tabItem, out DocumentTab? tab) && tab.Document.FilePath is { } path)
                {
                    if (ReferenceEquals(item, Tabs.SelectedItem))
                    {
                        active = index;
                    }

                    lines.Add($"file={path}");
                    index++;
                }
            }

            lines.Insert(1, string.Create(CultureInfo.InvariantCulture, $"active={active}"));
            Directory.CreateDirectory(Path.GetDirectoryName(SessionFilePath)!);
            File.WriteAllLines(SessionFilePath, lines);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("前回のタブの保存", ex);
        }
    }

    /// <summary>起動時（最初の描画のあと）。ファイルを渡されて起動したときや計測・テスト中は復元しない。</summary>
    private async Task RestoreSessionAsync()
    {
        Session session = LoadSession();
        _restoreSession = session.Restore;
        if (!session.Restore || session.Files.Count == 0)
        {
            return;
        }

        List<string> existing = session.Files.Where(File.Exists).ToList();
        int opened = await OpenFilesReplacingBlankAsync(existing, reportErrors: false);
        if (opened > 0)
        {
            Tabs.SelectedIndex = Math.Clamp(session.Active, 0, Tabs.TabItems.Count - 1);
        }
    }

    private bool ShouldRestoreSession() =>
        _probe.FilesToOpen.Count == 0 && _probe.TestOpen is null && _probe.LogPath is null;
}
