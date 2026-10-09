using DotPixelPainter.Core;

namespace DotPixelPainter;

/// <summary>タブ1枚ぶんの表示状態。ドキュメント本体に加えてズームや表示位置を持つ。</summary>
public sealed class DocumentTab
{
    public DocumentTab(PixelDocument document)
    {
        Document = document;
        ShownSize = (document.Width, document.Height);
    }

    /// <summary>最後に表示を合わせたときの画像の大きさ。元に戻すなどで大きさが変わったことに気づくために使う。</summary>
    public (int Width, int Height) ShownSize { get; set; }

    public PixelDocument Document { get; }

    /// <summary>
    /// 読み込んだ元のファイル（PSD のように、保存先にならないファイルから作ったときだけ）。
    /// 同じファイルをもう一度開こうとしたときに、このタブへ切り替えるために使う。
    /// </summary>
    public string? ImportedFrom { get; set; }

    /// <summary>
    /// 1ピクセルを何物理ピクセルで表示するか。
    /// 0 は「まだ決めていない」で、最初に描くときに画面に収まる倍率を選ぶ。
    /// </summary>
    public int Zoom { get; set; }

    /// <summary>
    /// 倍率を一度も手で変えていない（表示位置も動かしていない）か。
    /// true の間は、ウィンドウの大きさが変わったら倍率を選び直す。
    /// </summary>
    public bool AutoZoom { get; set; } = true;

    /// <summary>中央配置からのずれ（物理ピクセル）。</summary>
    public float PanX { get; set; }

    public float PanY { get; set; }

    /// <summary>選択範囲（なければ null）。</summary>
    public PixelRect? Selection { get; set; }

    /// <summary>選択範囲から持ち上げて移動中の中身と、その記録（確定するまで履歴には積まない）。</summary>
    public FloatingSelection? Floating { get; set; }

    public PixelStroke? FloatStroke { get; set; }

    /// <summary>合成画像を作り直す必要があるか。</summary>
    public bool ImageChanged { get; set; } = true;

    public string Header => Document.IsDirty ? $"{Document.Name} ●" : Document.Name;

    public int NextZoom(int direction) => ZoomLevels.Next(Zoom, direction);
}
