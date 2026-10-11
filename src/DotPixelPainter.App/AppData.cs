namespace DotPixelPainter;

/// <summary>
/// 設定や記録を置くフォルダ（ふつうは %LOCALAPPDATA%\DotPixelPainter）。
/// 開発用に、環境変数 DOTPIXELPAINTER_DATA でほかのフォルダにできる（テストで利用者の設定や記録を乱さないため）。
/// </summary>
internal static class AppData
{
    public static string Folder { get; } =
        Environment.GetEnvironmentVariable("DOTPIXELPAINTER_DATA") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DotPixelPainter");

    public static string File(string name) => Path.Combine(Folder, name);
}
