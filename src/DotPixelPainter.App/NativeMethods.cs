using System.Runtime.InteropServices;

namespace DotPixelPainter;

internal static partial class NativeMethods
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    /// <summary>標準のタイトルバーをダーク／ライトに切り替える（Windows 11 の DWM 機能）。</summary>
    public static void SetDarkTitleBar(nint hwnd, bool dark)
    {
        int value = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
    }
}
