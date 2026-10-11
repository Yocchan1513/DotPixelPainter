using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotPixelPainter.Thumbnail;

/// <summary>COM の DLL として外に見せる関数（エクスプローラーが呼ぶ）。</summary>
internal static unsafe class Exports
{
    /// <summary>この部品の番号（CLSID）。アプリの FileAssociation.ThumbnailClsid と同じにする。</summary>
    public static readonly Guid Clsid = new("b4c6ee2d-e80b-482e-b254-20cd1ffb67be");

    private const int ClassNotAvailable = unchecked((int)0x80040111);
    private const int Unexpected = unchecked((int)0x8000FFFF);

    public static readonly StrategyBasedComWrappers ComWrappers = new();

    [UnmanagedCallersOnly(EntryPoint = "DllGetClassObject")]
    private static int DllGetClassObject(Guid* clsid, Guid* iid, nint* ppv)
    {
        *ppv = 0;
        if (*clsid != Clsid)
        {
            return ClassNotAvailable;
        }

        try
        {
            nint unknown = ComWrappers.GetOrCreateComInterfaceForObject(new ClassFactory(), CreateComInterfaceFlags.None);
            int hr = Marshal.QueryInterface(unknown, in *iid, out nint result);
            Marshal.Release(unknown);
            *ppv = result;
            return hr;
        }
        catch (Exception ex)
        {
            return ex.HResult != 0 ? ex.HResult : Unexpected;
        }
    }

    /// <summary>Native AOT の部品は読み込んだら外せないので、いつも「まだ使っている」と答える（S_FALSE）。</summary>
    [UnmanagedCallersOnly(EntryPoint = "DllCanUnloadNow")]
    private static int DllCanUnloadNow() => 1;
}
