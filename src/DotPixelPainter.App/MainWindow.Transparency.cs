using System.Globalization;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace DotPixelPainter;

/// <summary>
/// 透明部分の表示（キャンバスの下地）。市松模様の明暗や、黒などの単色に切り替える。
/// 選んだ色は %LOCALAPPDATA%\DotPixelPainter\view.txt に保存する。
/// </summary>
public sealed partial class MainWindow
{
    private static string ViewSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DotPixelPainter",
        "view.txt");

    private static readonly (string Text, uint A, uint B)[] TransparencyPresets =
    [
        ("市松模様（明るい）", 0xFFFFFFFF, 0xFFE8E8E8),
        ("市松模様（暗い）", 0xFF3C3C3C, 0xFF2C2C2C),
        ("白", 0xFFFFFFFF, 0xFFFFFFFF),
        ("黒", 0xFF000000, 0xFF000000),
        ("灰色", 0xFF808080, 0xFF808080),
    ];

    /// <summary>下地の2色（同じなら単色）。</summary>
    private uint _transparentA = 0xFFFFFFFF;
    private uint _transparentB = 0xFFE8E8E8;
    private bool _transparencyLoaded;

    /// <summary>下地が暗いときは、グリッド線を明るい色にして見えるようにする。</summary>
    private Color GridColor
    {
        get
        {
            EnsureTransparencyLoaded();
            return Luminance(_transparentA) + Luminance(_transparentB) < 2 * 100
                ? Color.FromArgb(110, 200, 200, 200)
                : Color.FromArgb(110, 64, 64, 64);
        }
    }

    private static int Luminance(uint argb) =>
        (int)((((argb >> 16) & 0xFF) * 299 + ((argb >> 8) & 0xFF) * 587 + (argb & 0xFF) * 114) / 1000);

    private void EnsureTransparencyLoaded()
    {
        if (_transparencyLoaded)
        {
            return;
        }

        _transparencyLoaded = true;
        try
        {
            if (!File.Exists(ViewSettingsPath))
            {
                return;
            }

            foreach (string line in File.ReadAllLines(ViewSettingsPath))
            {
                string[] kv = line.Split('=', 2);
                if (kv.Length == 2 && kv[0] == "transparent")
                {
                    string[] colors = kv[1].Split(',');
                    if (colors.Length == 2
                        && uint.TryParse(colors[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint a)
                        && uint.TryParse(colors[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint b))
                    {
                        _transparentA = a | 0xFF000000;
                        _transparentB = b | 0xFF000000;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("透明部分の表示の読み込み", ex);
        }
    }

    private void SetTransparency(uint a, uint b)
    {
        _transparentA = a | 0xFF000000;
        _transparentB = b | 0xFF000000;
        _checker?.Dispose();
        _checker = null; // 次の描画で作り直す
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ViewSettingsPath)!);
            File.WriteAllText(ViewSettingsPath, string.Create(CultureInfo.InvariantCulture, $"transparent={_transparentA:X8},{_transparentB:X8}\n"));
        }
        catch (Exception ex)
        {
            ErrorLog.Write("透明部分の表示の保存", ex);
        }

        Canvas.Invalidate();
        PreviewCanvas.Invalidate();
    }

    /// <summary>「…」メニューに入れる、透明部分の表示の切り替え。</summary>
    private MenuFlyoutSubItem CreateTransparencyMenu()
    {
        var menu = new MenuFlyoutSubItem { Text = "透明部分の表示" };
        foreach ((string text, uint a, uint b) in TransparencyPresets)
        {
            var item = new RadioMenuFlyoutItem { Text = text, GroupName = "transparent" };
            item.Click += (_, _) => SetTransparency(a, b);
            menu.Items.Add(item);
        }

        var backColor = new RadioMenuFlyoutItem { Text = "いまの背景色", GroupName = "transparent" };
        backColor.Click += (_, _) => SetTransparency(_backColor, _backColor);
        menu.Items.Add(backColor);
        return menu;
    }

    /// <summary>メニューを開くたびに、いまの設定に印を付ける。</summary>
    private void UpdateTransparencyMenu(MenuFlyoutSubItem menu)
    {
        EnsureTransparencyLoaded();
        bool matched = false;
        for (int i = 0; i < menu.Items.Count; i++)
        {
            var item = (RadioMenuFlyoutItem)menu.Items[i];
            bool isPreset = i < TransparencyPresets.Length;
            bool on = isPreset
                ? TransparencyPresets[i].A == _transparentA && TransparencyPresets[i].B == _transparentB
                : !matched;
            item.IsChecked = on;
            matched |= on;
        }
    }
}
