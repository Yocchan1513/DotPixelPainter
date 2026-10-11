using Microsoft.UI.Xaml.Controls;

namespace DotPixelPainter;

/// <summary>「ヘルプ」→「言語 / Language」。どちらの言語でも読めるよう、項目名は両方で書く。切り替えは次の起動から。</summary>
public sealed partial class MainWindow
{
    private MenuFlyoutSubItem CreateLanguageMenu()
    {
        var menu = new MenuFlyoutSubItem { Text = "言語 / Language" };
        string? chosen = LanguageSetting.Chosen;
        foreach ((string text, string? value) in new (string, string?)[]
        {
            (T("自動（Windows に合わせる）", "Automatic (follow Windows)"), null),
            ("日本語", "ja"),
            ("English", "en"),
        })
        {
            var item = new RadioMenuFlyoutItem { Text = text, GroupName = "language", IsChecked = value == chosen };
            item.Click += (_, _) =>
            {
                if (LanguageSetting.Chosen == value)
                {
                    return;
                }

                LanguageSetting.Chosen = value;
                bool japanese = LanguageSetting.UseJapanese();
                ErrorLog.Run("言語の切り替え", () => ShowMessageAsync(
                    japanese ? "言語を切り替えました" : "Language changed",
                    japanese ? "次にアプリを起動したときから、日本語で表示します。" : "The app will use English from the next start."));
            };
            menu.Items.Add(item);
        }

        return menu;
    }
}
