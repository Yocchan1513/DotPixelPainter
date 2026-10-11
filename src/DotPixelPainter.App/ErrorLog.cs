using System.Globalization;
using System.Reflection;
using Microsoft.UI.Dispatching;

namespace DotPixelPainter;

/// <summary>
/// 表に出ないエラー（投げっぱなしの非同期処理など）を %LOCALAPPDATA%\DotPixelPainter\error.log に残す。
/// </summary>
public static class ErrorLog
{
    public static string FilePath => AppData.File("error.log");

    /// <summary>アプリの版（報告のときに分かるよう、記録に書く）。</summary>
    private static readonly string Version =
        typeof(ErrorLog).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "?";

    public static void Write(string where, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(FilePath, string.Create(
                CultureInfo.InvariantCulture,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] v{Version} {where}\n{ex}\n\n"));
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

    /// <summary>
    /// UI スレッドで後から実行する。WinUI では、後回しにした処理の中のエラーは Application.UnhandledException を通らず
    /// そのままアプリが落ちるので、ここで受け止めて記録し、「エラーが起きました」の流れ（控え・お知らせ）に乗せる。
    /// </summary>
    public static bool Post(this DispatcherQueue queue, string where, Action action, DispatcherQueuePriority priority = DispatcherQueuePriority.Normal) =>
        queue.TryEnqueue(priority, () =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Write(where, ex);
                App.NotifyError();
            }
        });
}
