using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace DotPixelPainter;

/// <summary>上下にドラッグして高さを変えるつまみ。マウスを乗せると上下矢印のカーソルになる。</summary>
public sealed partial class ResizeGrip : Grid
{
    public ResizeGrip()
    {
        // カーソルは派生クラスからしか変えられない
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);
    }
}
