using DotPixelPainter.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace DotPixelPainter;

/// <summary>
/// 落ちたときの備え。
/// - 保存していないタブを、変更が落ち着いてから数秒後・1分ごと・エラーが起きたときに控えておく（Recovery）。
///   受け止められないエラー（WinUI の内部でアプリがすぐ終了するもの）では何も実行できないので、こまめに控えておく。
/// - 想定外のエラーは記録してアプリを続け、一度だけ知らせる。
/// - 前回が正しく終了しなかったら、次の起動で知らせて、控えから復元できるようにする。
/// 開発用の起動（StartupProbe.Isolated）では何もしない。
/// </summary>
public sealed partial class MainWindow
{
    private const string IssuesUrl = "https://github.com/Yocchan1513/DotPixelPainter/issues";

    private DispatcherQueueTimer? _autosaveTimer;
    private DispatcherQueueTimer? _autosaveSoon;
    private bool _recoveryActive;
    private bool _errorNoticeShown;

    /// <summary>最初の描画のあとに呼ぶ。起動の印を置き、前回の様子を確かめ、自動の控えを始める。</summary>
    private void StartRecovery()
    {
        if (_probe.Isolated)
        {
            return;
        }

        _recoveryActive = true;
        bool crashed = Recovery.BeginSession();
        List<Recovery.Backup> backups = Recovery.List();

        _autosaveTimer = DispatcherQueue.CreateTimer();
        _autosaveTimer.Interval = TimeSpan.FromMinutes(1);
        _autosaveTimer.Tick += (_, _) => Autosave();
        _autosaveTimer.Start();

        // 変更してから少し待って控える（描いている途中に何度も書かないよう、変更のたびに待ち直す）
        _autosaveSoon = DispatcherQueue.CreateTimer();
        _autosaveSoon.Interval = TimeSpan.FromSeconds(3);
        _autosaveSoon.IsRepeating = false;
        _autosaveSoon.Tick += (_, _) => Autosave();

        if (crashed || backups.Count > 0)
        {
            ErrorLog.Run("前回の終了の確認", async () =>
            {
                await _startupOpen;
                await ShowRecoveryDialogAsync(crashed, backups);
            });
        }
    }

    /// <summary>絵が変わったときに呼ぶ。数秒たっても次の変更がなければ控える。</summary>
    private void ScheduleAutosave()
    {
        if (_autosaveSoon is not null)
        {
            _autosaveSoon.Stop();
            _autosaveSoon.Start();
        }
    }

    /// <summary>正しく終了するとき（ウィンドウを閉じたとき）。</summary>
    private void EndRecovery()
    {
        if (_recoveryActive)
        {
            _autosaveTimer?.Stop();
            _autosaveSoon?.Stop();
            Recovery.EndSession();
        }
    }

    /// <summary>保存していないタブを控える。前回控えてから変わっていないタブは飛ばす。保存済みのタブの控えは消す。</summary>
    private void Autosave()
    {
        if (!_recoveryActive)
        {
            return;
        }

        foreach (DocumentTab tab in _tabs.Values)
        {
            try
            {
                PixelDocument document = tab.Document;
                if (!document.IsDirty)
                {
                    if (tab.HasBackup)
                    {
                        Recovery.Delete(tab.RecoveryId);
                        tab.HasBackup = false;
                    }

                    continue;
                }

                // 移動中・描いている途中のタブは、中身が途中の状態なので次の機会にする
                if (document.History.Revision == tab.AutosavedRevision || tab.Floating is not null || (_stroke is not null && _strokeTab == tab))
                {
                    continue;
                }

                using var buffer = new MemoryStream();
                DotPixFile.Write(document, buffer);
                Recovery.Write(tab.RecoveryId, document.Name, document.FilePath ?? tab.ImportedFrom, buffer.ToArray());
                tab.AutosavedRevision = document.History.Revision;
                tab.HasBackup = true;
            }
            catch (Exception ex)
            {
                ErrorLog.Write("自動の控え", ex);
            }
        }
    }

    /// <summary>アプリが落ちる直前（受け止められないエラー）に、できるだけ控えを残す。どのスレッドから呼ばれてもよいよう、画面には触らない。</summary>
    internal void EmergencyBackup()
    {
        try
        {
            Autosave();
        }
        catch (Exception)
        {
            // 落ちる直前なので、できなければあきらめる
        }
    }

    /// <summary>タブを閉じたら、その控えを消す（閉じる前に保存するか確かめてある）。</summary>
    private void ForgetBackup(DocumentTab tab)
    {
        if (tab.HasBackup)
        {
            Recovery.Delete(tab.RecoveryId);
            tab.HasBackup = false;
        }
    }

    /// <summary>想定外のエラーが起きたとき（App から呼ばれる）。すぐに控えて、一度だけ知らせる。</summary>
    internal void OnUnhandledError()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            Autosave();
            if (_errorNoticeShown)
            {
                return;
            }

            _errorNoticeShown = true;
            ErrorLog.Run("エラーのお知らせ", ShowErrorNoticeAsync);
        });
    }

    private async Task ShowErrorNoticeAsync()
    {
        var panel = new StackPanel { Spacing = 10, MaxWidth = 420 };
        panel.Children.Add(Paragraph(T(
            "想定外のエラーが起きましたが、そのまま作業を続けられます。",
            "An unexpected error occurred, but you can keep working.")));
        if (_recoveryActive && _tabs.Values.Any(t => t.Document.IsDirty))
        {
            panel.Children.Add(Paragraph(T(
                "念のため、保存していない絵は控えてあります。早めに保存することをおすすめします。",
                "Your unsaved work has been backed up just in case. Saving soon is recommended.")));
        }

        AddReportLinks(panel);
        await ShowDialogQuietlyAsync(new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = T("エラーが起きました", "An error occurred"),
            Content = panel,
            CloseButtonText = "OK",
        });
    }

    private async Task ShowRecoveryDialogAsync(bool crashed, List<Recovery.Backup> backups)
    {
        var panel = new StackPanel { Spacing = 10, MaxWidth = 440 };
        if (crashed)
        {
            panel.Children.Add(Paragraph(T(
                "前回 DotPixelPainter は、エラーなどで途中で終了しました。",
                "DotPixelPainter stopped unexpectedly last time, for example because of an error.")));
        }

        if (backups.Count > 0)
        {
            panel.Children.Add(Paragraph(T(
                $"保存していなかった絵の控えが {backups.Count} 件あります。復元すると、まだ保存していないタブとして開きます。",
                $"Backups of {backups.Count} unsaved image(s) were found. Restoring opens them as unsaved tabs.")));
            panel.Children.Add(Paragraph(string.Join("\n", backups.Take(8).Select(b => "・" + b.Name)) + (backups.Count > 8 ? "\n…" : "")));
        }

        AddReportLinks(panel);

        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = crashed
                ? T("前回は正しく終了しませんでした", "DotPixelPainter did not close properly")
                : T("保存していない絵の控えがあります", "Unsaved work was found"),
            Content = panel,
        };
        if (backups.Count > 0)
        {
            dialog.PrimaryButtonText = T("復元する", "Restore");
            dialog.SecondaryButtonText = T("捨てる", "Discard");
            dialog.CloseButtonText = T("あとで", "Later");
            dialog.DefaultButton = ContentDialogButton.Primary;
        }
        else
        {
            dialog.CloseButtonText = "OK";
        }

        switch (await ShowDialogQuietlyAsync(dialog))
        {
            case ContentDialogResult.Primary:
                await RestoreBackupsAsync(backups);
                break;
            case ContentDialogResult.Secondary:
                Recovery.DeleteAll();
                break;
        }
    }

    /// <summary>控えをタブとして開く。元の保存先があれば、上書き保存でそこに保存できるようにする。</summary>
    private async Task RestoreBackupsAsync(List<Recovery.Backup> backups)
    {
        TabViewItem? blank = Tabs.TabItems.Count == 1
            && Tabs.TabItems[0] is TabViewItem only
            && _tabs[only].Document.FilePath is null
            && !_tabs[only].Document.History.CanUndo ? only : null;

        int restored = 0;
        foreach (Recovery.Backup backup in backups)
        {
            try
            {
                StorageFile file = await StorageFile.GetFileFromPathAsync(backup.DataPath);
                (PixelDocument document, _) = await LoadDocumentAsync(file);
                document.Name = backup.Name;
                bool savable = backup.OriginalPath is { } path && (IsDotPix(path) || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
                document.FilePath = savable ? backup.OriginalPath : null;
                document.MarkUnsaved();
                AddTab(document);
                RefreshHeader(_tabs[(TabViewItem)Tabs.SelectedItem]);
                Recovery.Delete(backup.Id);
                restored++;
            }
            catch (Exception ex)
            {
                ErrorLog.Write("控えの復元", ex);
            }
        }

        if (restored > 0 && blank is not null)
        {
            Tabs.TabItems.Remove(blank);
            _tabs.Remove(blank);
        }

        if (restored < backups.Count)
        {
            await ShowMessageAsync(T("復元できなかった控えがあります", "Some backups could not be restored"), T("エラーの記録を見てください。", "Please check the error log."));
        }
    }

    /// <summary>「問題が続くときは報告を」の案内と、エラーの記録・報告先へのリンク。</summary>
    private static void AddReportLinks(StackPanel panel)
    {
        panel.Children.Add(Paragraph(T(
            "問題が続くときは、エラーの記録（error.log）を添えて、GitHub の Issues でお知らせください。",
            "If the problem continues, please report it on GitHub Issues with the error log (error.log) attached.")));

        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var logLink = new HyperlinkButton { Content = T("エラーの記録を開く", "Open the error log"), Padding = new Thickness(0) };
        logLink.Click += (_, _) =>
        {
            string folder = Path.GetDirectoryName(ErrorLog.FilePath)!;
            Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
        };
        links.Children.Add(logLink);
        links.Children.Add(new HyperlinkButton { Content = T("GitHub で報告する", "Report on GitHub"), NavigateUri = new Uri(IssuesUrl), Padding = new Thickness(0) });
        panel.Children.Add(links);
    }

    private static TextBlock Paragraph(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };

    /// <summary>ダイアログを出す。ほかのダイアログが開いていて出せないときは、何もしない。</summary>
    private static async Task<ContentDialogResult> ShowDialogQuietlyAsync(ContentDialog dialog)
    {
        try
        {
            return await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("お知らせを出せなかった", ex);
            return ContentDialogResult.None;
        }
    }
}
