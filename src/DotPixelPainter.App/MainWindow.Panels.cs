using System.Globalization;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace DotPixelPainter;

/// <summary>
/// 右側のパネル（プレビュー・カラー・パレット）を別ウィンドウに切り離したり、戻したりする。
/// 普段はメイン画面の右に並べておき、必要なときだけ浮かせる（Illustrator などと同じ考え方）。
/// 浮かせた位置と大きさは %LOCALAPPDATA%\DotPixelPainter\panels.txt に保存し、次回起動時に復元する。
/// </summary>
public sealed partial class MainWindow
{
    private const string DetachGlyph = "";
    private const string DockGlyph = "";

    private readonly Dictionary<string, DockPanel> _panels = [];
    private bool _closingFloatingPanels;

    private static string PanelsFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DotPixelPainter",
        "panels.txt");

    private void InitializePanels()
    {
        AddPanel("preview", "プレビュー", PreviewSlot, PreviewSection, PreviewDetachButton);
        AddPanel("color", "カラー", ColorSlot, ColorSection, ColorDetachButton);
        AddPanel("palette", "パレット", PaletteSlot, PaletteSection, PaletteDetachButton);
        Closed += (_, _) => CloseFloatingPanels();
    }

    private void AddPanel(string key, string title, Border slot, FrameworkElement section, Button button)
    {
        var panel = new DockPanel(key, title, slot, section, button);
        _panels[key] = panel;
        UpdateDetachButton(panel);
    }

    private void PanelDetach_Click(object sender, RoutedEventArgs e)
    {
        DockPanel panel = _panels[(string)((Button)sender).Tag];
        if (panel.Floating is { } window)
        {
            window.Close(); // 閉じるとメイン画面に戻る
        }
        else
        {
            Detach(panel, null);
            SavePanelLayout();
        }
    }

    /// <summary>パネルを別ウィンドウにする。bounds が null なら、メイン画面の右上あたりに出す。</summary>
    private void Detach(DockPanel panel, RectInt32? bounds)
    {
        double scale = Root.XamlRoot?.RasterizationScale ?? 1.0;
        double widthDip = Math.Max(panel.Section.ActualWidth, 196) + 24;
        double heightDip = Math.Max(panel.Section.ActualHeight, 80) + 24;

        panel.Slot.Child = null;
        panel.Slot.Visibility = Visibility.Collapsed;

        var host = new Grid { Padding = new Thickness(12) };
        host.Children.Add(panel.Section);
        AttachShortcuts(host);

        var window = new Window
        {
            Title = $"{panel.Title} - DotPixelPainter",
            Content = host,
            SystemBackdrop = new MicaBackdrop(),
        };

        AppWindow appWindow = window.AppWindow;
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        if (bounds is { } b)
        {
            appWindow.MoveAndResize(b);
        }
        else
        {
            // タイトルバーの分（約32DIP）を足した大きさで、メイン画面の右上に重ねて出す
            int w = (int)(widthDip * scale) + 16;
            int h = (int)((heightDip + 32) * scale) + 8;
            PointInt32 mainPos = AppWindow.Position;
            SizeInt32 mainSize = AppWindow.Size;
            // すでに浮いているパネルと重ならないよう、1枚ごとに左下へずらす
            int cascade = (int)(36 * scale) * _panels.Values.Count(p => p.Floating is not null);
            appWindow.MoveAndResize(new RectInt32(
                mainPos.X + mainSize.Width - w - (int)(260 * scale) - cascade,
                mainPos.Y + (int)(120 * scale) + cascade,
                w,
                h));
        }

        nint hwnd = WindowNative.GetWindowHandle(window);
        NativeMethods.SetOwner(hwnd, WindowNative.GetWindowHandle(this));
        NativeMethods.SetDarkTitleBar(hwnd, Root.ActualTheme == ElementTheme.Dark);

        panel.LastBounds = new RectInt32(appWindow.Position.X, appWindow.Position.Y, appWindow.Size.Width, appWindow.Size.Height);
        appWindow.Changed += (s, args) =>
        {
            if (args.DidPositionChange || args.DidSizeChange)
            {
                panel.LastBounds = new RectInt32(s.Position.X, s.Position.Y, s.Size.Width, s.Size.Height);
            }
        };
        window.Closed += (_, _) =>
        {
            host.Children.Remove(panel.Section);
            Redock(panel);
            if (!_closingFloatingPanels)
            {
                SavePanelLayout(); // 利用者が閉じたときは「戻した」として覚える
            }
        };

        panel.Floating = window;
        UpdateDetachButton(panel);
        window.Activate();
    }

    private void Redock(DockPanel panel)
    {
        panel.Floating = null;
        panel.Slot.Child = panel.Section;
        panel.Slot.Visibility = Visibility.Visible;
        UpdateDetachButton(panel);
    }

    private static void UpdateDetachButton(DockPanel panel)
    {
        bool floating = panel.Floating is not null;
        ((FontIcon)panel.Button.Content).Glyph = floating ? DockGlyph : DetachGlyph;
        ToolTipService.SetToolTip(panel.Button, floating ? "メイン画面に戻す" : "別ウィンドウに切り離す");
    }

    /// <summary>メイン画面を閉じるとき。浮いているパネルの配置を保存してから閉じる。</summary>
    private void CloseFloatingPanels()
    {
        SavePanelLayout();
        _closingFloatingPanels = true;
        foreach (DockPanel panel in _panels.Values)
        {
            panel.Floating?.Close();
        }
    }

    private void SavePanelLayout()
    {
        try
        {
            var lines = _panels.Values
                .Where(p => p.Floating is not null && p.LastBounds is not null)
                .Select(p =>
                {
                    RectInt32 b = p.LastBounds!.Value;
                    return string.Create(CultureInfo.InvariantCulture, $"{p.Key}={b.X},{b.Y},{b.Width},{b.Height}");
                });
            Directory.CreateDirectory(Path.GetDirectoryName(PanelsFilePath)!);
            File.WriteAllLines(PanelsFilePath, lines);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("パネル配置の保存", ex);
        }
    }

    /// <summary>前回浮かせていたパネルを、同じ位置に浮かせ直す。起動を遅くしないよう最初の描画のあとに呼ぶ。</summary>
    private void RestorePanelLayout()
    {
        try
        {
            if (!File.Exists(PanelsFilePath))
            {
                return;
            }

            foreach (string line in File.ReadAllLines(PanelsFilePath))
            {
                string[] kv = line.Split('=', 2);
                if (kv.Length != 2 || !_panels.TryGetValue(kv[0], out DockPanel? panel) || panel.Floating is not null)
                {
                    continue;
                }

                int[] v = [.. kv[1].Split(',').Select(s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : int.MinValue)];
                if (v.Length != 4 || v.Any(n => n == int.MinValue) || v[2] < 50 || v[3] < 50)
                {
                    continue;
                }

                var bounds = new RectInt32(v[0], v[1], v[2], v[3]);
                // モニターの構成が変わって画面外になっていたら、既定の位置に出す
                bool onScreen = DisplayArea.GetFromRect(bounds, DisplayAreaFallback.None) is not null;
                Detach(panel, onScreen ? bounds : null);
            }

            Activate(); // メイン画面を手前に戻す
        }
        catch (Exception ex)
        {
            ErrorLog.Write("パネル配置の復元", ex);
        }
    }

    private sealed class DockPanel(string key, string title, Border slot, FrameworkElement section, Button button)
    {
        public string Key { get; } = key;

        public string Title { get; } = title;

        public Border Slot { get; } = slot;

        public FrameworkElement Section { get; } = section;

        public Button Button { get; } = button;

        public Window? Floating { get; set; }

        public RectInt32? LastBounds { get; set; }
    }
}
