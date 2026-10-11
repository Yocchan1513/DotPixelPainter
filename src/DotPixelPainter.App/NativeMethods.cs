using System.Runtime.InteropServices;

namespace DotPixelPainter;

internal static partial class NativeMethods
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    /// <summary>Windows の表示言語（LANGID）。下位 10bit が言語の種類（0x11 が日本語）。</summary>
    [LibraryImport("kernel32.dll")]
    public static partial ushort GetUserDefaultUILanguage();

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hwnd, int command);

    private const int GwlpHwndParent = -8;

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);

    /// <summary>
    /// owner を持ち主にする。持ち主より常に手前に表示され、持ち主を最小化すると一緒に隠れる。
    /// 切り離したパネルのウィンドウに使う。
    /// </summary>
    public static void SetOwner(nint hwnd, nint owner) => SetWindowLongPtr(hwnd, GwlpHwndParent, owner);

    /// <summary>標準のタイトルバーをダーク／ライトに切り替える（Windows 11 の DWM 機能）。</summary>
    public static void SetDarkTitleBar(nint hwnd, bool dark)
    {
        int value = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
    }
}
