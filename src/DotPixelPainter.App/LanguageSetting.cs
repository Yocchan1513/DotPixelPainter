namespace DotPixelPainter;

/// <summary>
/// 画面の言語の設定。view.txt の language が "ja" か "en" ならそれ、なければ Windows の表示言語に合わせる
/// （日本語なら日本語、それ以外は英語）。切り替えは次の起動から。
/// </summary>
internal static class LanguageSetting
{
    private const string Key = "language";

    /// <summary>"ja"・"en"、または自動なら null。</summary>
    public static string? Chosen
    {
        get => ViewSettings.Get(Key) is "ja" or "en" ? ViewSettings.Get(Key) : null;
        set => ViewSettings.Set(Key, value ?? "auto");
    }

    public static bool UseJapanese() => Chosen switch
    {
        "ja" => true,
        "en" => false,
        _ => (NativeMethods.GetUserDefaultUILanguage() & 0x3FF) == 0x11,
    };
}
