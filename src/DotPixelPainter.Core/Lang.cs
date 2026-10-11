namespace DotPixelPainter.Core;

/// <summary>
/// 画面の言語（日本語・英語）。文字は使う場所に両方を並べて書き、T("日本語", "English") で選ぶ。
/// アプリの起動の最初に決めて、途中では変えない（変えたら次の起動から）。
/// </summary>
public static class Lang
{
    /// <summary>日本語で表示するか。false なら英語。</summary>
    public static bool Japanese { get; set; } = true;

    public static string T(string japanese, string english) => Japanese ? japanese : english;
}
