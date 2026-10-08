using DotPixelPainter.Core;

namespace DotPixelPainter;

/// <summary>タブ1枚ぶんの表示状態。ドキュメント本体に加えてズームや表示位置を持つ。</summary>
public sealed class DocumentTab
{
    public static readonly int[] ZoomLevels = [1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64];

    public DocumentTab(PixelDocument document)
    {
        Document = document;
        Zoom = document.Width <= 64 && document.Height <= 64 ? 8 : 2;
    }

    public PixelDocument Document { get; }

    /// <summary>1ピクセルを何物理ピクセルで表示するか。</summary>
    public int Zoom { get; set; }

    /// <summary>中央配置からのずれ（物理ピクセル）。</summary>
    public float PanX { get; set; }

    public float PanY { get; set; }

    /// <summary>合成画像を作り直す必要があるか。</summary>
    public bool ImageChanged { get; set; } = true;

    public string Header => Document.IsDirty ? $"{Document.Name} ●" : Document.Name;

    public int NextZoom(int direction)
    {
        int i = Array.IndexOf(ZoomLevels, Zoom);
        if (i < 0)
        {
            i = 0;
        }

        return ZoomLevels[Math.Clamp(i + direction, 0, ZoomLevels.Length - 1)];
    }
}
