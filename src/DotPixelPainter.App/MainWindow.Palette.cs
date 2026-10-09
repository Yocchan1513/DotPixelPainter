using DotPixelPainter.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DotPixelPainter;

/// <summary>カスタムパレットと描画色。</summary>
public sealed partial class MainWindow
{
    private readonly List<Border> _swatchButtons = [];
    private MenuFlyout? _swatchMenu;
    private int _swatchMenuIndex;
    private Palette _palette = Palette.CreateDefault();
    private uint _color = 0xFF000000;
    private Flyout? _colorFlyout;
    private ColorPicker? _colorPicker;
    private bool _syncingPicker;

    /// <summary>次回起動時も同じパレットを使えるよう、ここに自動保存する。透明度も保てる独自の16進リスト形式。</summary>
    private static string PaletteFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DotPixelPainter",
        "palette.txt");

    private void InitializePalette()
    {
        _palette = LoadSavedPalette();
        RebuildPaletteGrid();
        SetCurrentColor(_palette.Colors.Count > 0 ? _palette.Colors[0] : 0xFF000000);
    }

    private static Palette LoadSavedPalette()
    {
        try
        {
            if (File.Exists(PaletteFilePath))
            {
                Palette saved = Palette.FromHexList(File.ReadAllText(PaletteFilePath));
                if (saved.Colors.Count > 0)
                {
                    return saved;
                }
            }
        }
        catch (Exception)
        {
            // 読めなければ初期パレットで始める
        }

        return Palette.CreateDefault();
    }

    private void SavePalette()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PaletteFilePath)!);
            File.WriteAllText(PaletteFilePath, _palette.ToHexList());
        }
        catch (Exception)
        {
            // 保存に失敗しても編集は続けられるようにする
        }
    }

    private void OnPaletteChanged()
    {
        RebuildPaletteGrid();
        SavePalette();
    }

    private void RebuildPaletteGrid()
    {
        PaletteGrid.Children.Clear();
        _swatchButtons.Clear();
        for (int i = 0; i < _palette.Colors.Count; i++)
        {
            // 起動を軽くするため、ボタンではなく枠（Border）で描き、右クリックメニューは1つを使い回す
            int index = i;
            uint argb = _palette.Colors[i];
            var swatch = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(ToColor(argb)),
                Tag = argb,
            };
            ToolTipService.SetToolTip(swatch, $"#{argb:X8}（右クリックで編集）");
            swatch.Tapped += (_, _) => SetCurrentColor(_palette.Colors[index]);
            swatch.RightTapped += (s, e) =>
            {
                _swatchMenuIndex = index;
                GetSwatchMenu().ShowAt((FrameworkElement)s, e.GetPosition((UIElement)s));
                e.Handled = true;
            };

            _swatchButtons.Add(swatch);
            PaletteGrid.Children.Add(swatch);
        }

        UpdateSwatchSelection();
    }

    /// <summary>パレットの色の右クリックメニュー。初めて右クリックしたときに作る。</summary>
    private MenuFlyout GetSwatchMenu()
    {
        if (_swatchMenu is not null)
        {
            return _swatchMenu;
        }

        var replace = new MenuFlyoutItem { Text = "描画色で置き換え" };
        replace.Click += (_, _) =>
        {
            _palette.Replace(_swatchMenuIndex, _color);
            OnPaletteChanged();
        };
        var remove = new MenuFlyoutItem { Text = "削除" };
        remove.Click += (_, _) =>
        {
            _palette.RemoveAt(_swatchMenuIndex);
            OnPaletteChanged();
        };
        _swatchMenu = new MenuFlyout();
        _swatchMenu.Items.Add(replace);
        _swatchMenu.Items.Add(remove);
        return _swatchMenu;
    }

    private void UpdateSwatchSelection()
    {
        var accent = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        var normal = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"];
        foreach (Border b in _swatchButtons)
        {
            bool selected = (uint)b.Tag == _color;
            b.BorderThickness = new Thickness(selected ? 3 : 1);
            b.BorderBrush = selected ? accent : normal;
        }
    }

    /// <summary>描画色の四角をクリックしたら、カラーピッカーを開く。</summary>
    private void ForeColor_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        // カラーピッカーは重いので、初めて使うときに作る
        if (_colorPicker is null)
        {
            _colorPicker = new ColorPicker
            {
                IsAlphaEnabled = true,
                IsMoreButtonVisible = true,
                ColorSpectrumShape = ColorSpectrumShape.Box,
            };
            _colorPicker.ColorChanged += (_, args) =>
            {
                if (_syncingPicker)
                {
                    return;
                }

                _syncingPicker = true;
                var c = args.NewColor;
                SetCurrentColor((uint)c.A << 24 | (uint)c.R << 16 | (uint)c.G << 8 | c.B);
                _syncingPicker = false;
            };
            _colorFlyout = new Flyout { Content = _colorPicker };
        }

        _syncingPicker = true;
        _colorPicker.Color = ToColor(_color);
        _syncingPicker = false;
        _colorFlyout!.ShowAt(ForeColorSwatch);
    }

    private async void AddColor_Click(object sender, RoutedEventArgs e)
    {
        if (!_palette.Add(_color))
        {
            await ShowMessageAsync("追加できません", $"パレットには {Palette.MaxColors} 色まで登録できます。");
            return;
        }

        OnPaletteChanged();
    }

    private async void ImportPalette_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add(".gpl");
        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            Palette loaded = Palette.FromGpl(await FileIO.ReadTextAsync(file));
            if (loaded.Colors.Count == 0)
            {
                await ShowMessageAsync("読み込めませんでした", "色が1つも入っていません。");
                return;
            }

            _palette = loaded;
            OnPaletteChanged();
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("読み込めませんでした", ex.Message);
        }
    }

    private async void ExportPalette_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker { SuggestedFileName = "palette" };
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeChoices.Add("GIMP パレット", [".gpl"]);
        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            await FileIO.WriteTextAsync(file, _palette.ToGpl());
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("書き出せませんでした", ex.Message);
        }
    }

    private void ResetPalette_Click(object sender, RoutedEventArgs e)
    {
        _palette = Palette.CreateDefault();
        OnPaletteChanged();
    }
}
