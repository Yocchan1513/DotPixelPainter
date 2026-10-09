using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DotPixelPainter;

/// <summary>
/// .dotpix をこのアプリで開くように、Windows に登録する（今のユーザーだけ。管理者の権限はいらない）。
/// インストール不要のアプリなので、利用者がメニューから選んだときだけ登録する。
/// </summary>
internal static partial class FileAssociation
{
    private const string Extension = ".dotpix";
    private const string ProgId = "DotPixelPainter.dotpix";
    private const string ClassesRoot = @"Software\Classes";

    public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "DotPixelPainter.exe");

    /// <summary>今のこの exe に登録されているか。</summary>
    public static bool IsRegistered()
    {
        using RegistryKey? command = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\{ProgId}\shell\open\command");
        return command?.GetValue(null) is string value && value.Contains(ExePath, StringComparison.OrdinalIgnoreCase);
    }

    public static void Register()
    {
        string exe = ExePath;
        using (RegistryKey prog = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{ProgId}"))
        {
            prog.SetValue(null, "DotPixelPainter ドキュメント");
            using (RegistryKey icon = prog.CreateSubKey("DefaultIcon"))
            {
                icon.SetValue(null, $"\"{exe}\",0");
            }

            using RegistryKey command = prog.CreateSubKey(@"shell\open\command");
            command.SetValue(null, $"\"{exe}\" \"%1\"");
        }

        using (RegistryKey ext = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{Extension}"))
        {
            ext.SetValue(null, ProgId);
            using RegistryKey openWith = ext.CreateSubKey("OpenWithProgids");
            openWith.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        NotifyShell();
    }

    public static void Unregister()
    {
        Registry.CurrentUser.DeleteSubKeyTree($@"{ClassesRoot}\{ProgId}", throwOnMissingSubKey: false);
        using (RegistryKey? ext = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\{Extension}", writable: true))
        {
            if (ext is not null)
            {
                if (ext.GetValue(null) as string == ProgId)
                {
                    ext.DeleteValue(string.Empty, throwOnMissingValue: false);
                }

                using RegistryKey? openWith = ext.OpenSubKey("OpenWithProgids", writable: true);
                openWith?.DeleteValue(ProgId, throwOnMissingValue: false);
            }
        }

        NotifyShell();
    }

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);

    /// <summary>エクスプローラーにアイコンと関連付けの変更を知らせる。</summary>
    private static void NotifyShell() => SHChangeNotify(0x08000000, 0, 0, 0); // SHCNE_ASSOCCHANGED
}
