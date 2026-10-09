using DotPixelPainter.Core;

namespace DotPixelPainter;

/// <summary>タブ1枚ぶんの表示状態。ドキュメント本体に加えてズームや表示位置を持つ。</summary>
public sealed class DocumentTab
{
    public DocumentTab(PixelDocument document)
    {
        Document = document;
    }

    public PixelDocument Document { get; }

    /// <summary>
    /// 1ピクセルを何物理ピクセルで表示するか。
    /// 0 は「まだ決めていない」で、最初に描くときに画面に収まる倍率を選ぶ。
    /// </summary>
    public int Zoom { get; set; }

    /// <summary>中央配置からのずれ（物理ピクセル）。</summary>
    public float PanX { get; set; }

    public float PanY { get; set; }

    /// <summary>合成画像を作り直す必要があるか。</summary>
    public bool ImageChanged { get; set; } = true;

    public string Header => Document.IsDirty ? $"{Document.Name} ●" : Document.Name;

    public int NextZoom(int direction) => ZoomLevels.Next(Zoom, direction);
}
