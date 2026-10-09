using System.Globalization;

namespace DotPixelPainter;

/// <summary>
/// 表に出ないエラー（投げっぱなしの非同期処理など）を %LOCALAPPDATA%\DotPixelPainter\error.log に残す。
/// </summary>
public static class ErrorLog
{
    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DotPixelPainter",
        "error.log");

    public static void Write(string where, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(FilePath, string.Create(
                CultureInfo.InvariantCulture,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {where}\n{ex}\n\n"));
        }
        catch (Exception)
        {
            // 記録できなくても続ける
        }
    }

    /// <summary>投げっぱなしにする非同期処理を、エラーを記録したうえで実行する。</summary>
    public static async void Run(string where, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Write(where, ex);
        }
    }
}
