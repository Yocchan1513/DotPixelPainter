namespace DotPixelPainter.Core;

/// <summary>レイヤーを足す（複製も含む）。</summary>
internal sealed class LayerInsertEdit(PixelDocument document, Layer layer, int index, int previousActive) : HistoryEntry
{
    public override long ByteSize => layer.Image.Width * layer.Image.Height * 4L + 64;

    internal override void Apply(bool undo)
    {
        if (undo)
        {
            document.RemoveLayerAt(index);
            document.SetActive(previousActive);
        }
        else
        {
            document.InsertLayerAt(index, layer);
            document.SetActive(index);
        }
    }
}

/// <summary>レイヤーを消す。</summary>
internal sealed class LayerRemoveEdit(PixelDocument document, Layer layer, int index) : HistoryEntry
{
    public override long ByteSize => layer.Image.Width * layer.Image.Height * 4L + 64;

    internal override void Apply(bool undo)
    {
        if (undo)
        {
            document.InsertLayerAt(index, layer);
            document.SetActive(index);
        }
        else
        {
            document.RemoveLayerAt(index);
            document.SetActive(index - 1);
        }
    }
}

/// <summary>レイヤーの並びを変える。</summary>
internal sealed class LayerMoveEdit(PixelDocument document, int from, int to) : HistoryEntry
{
    public override long ByteSize => 64;

    internal override void Apply(bool undo)
    {
        (int a, int b) = undo ? (to, from) : (from, to);
        Layer layer = document.Layers[a];
        document.RemoveLayerAt(a);
        document.InsertLayerAt(b, layer);
        document.SetActive(b);
    }
}

/// <summary>下のレイヤーと結合する。下のレイヤーの結合前後の画素と、上のレイヤー自体を持つ。</summary>
internal sealed class LayerMergeEdit(PixelDocument document, Layer upper, int upperIndex, Layer lower, uint[] lowerBefore, uint[] lowerAfter) : HistoryEntry
{
    public override long ByteSize => (lowerBefore.Length + lowerAfter.Length) * 4L + 64;

    internal override void Apply(bool undo)
    {
        if (undo)
        {
            lower.Image.RestorePixels(lowerBefore);
            document.InsertLayerAt(upperIndex, upper);
            document.SetActive(upperIndex);
        }
        else
        {
            lower.Image.RestorePixels(lowerAfter);
            document.RemoveLayerAt(upperIndex);
            document.SetActive(upperIndex - 1);
        }
    }
}

internal enum LayerProperty
{
    Visible,
    Opacity,
    Name,
}

/// <summary>レイヤーの表示・不透明度・名前の変更。不透明度はスライダーを動かし続けた分を1件にまとめる。</summary>
internal sealed class LayerPropertyEdit(Layer layer, LayerProperty property, object before, object after) : HistoryEntry
{
    private object _after = after;

    public override long ByteSize => 64;

    public static object Get(Layer layer, LayerProperty property) => property switch
    {
        LayerProperty.Visible => layer.Visible,
        LayerProperty.Opacity => layer.Opacity,
        _ => layer.Name,
    };

    internal override void Apply(bool undo)
    {
        object value = undo ? before : _after;
        switch (property)
        {
            case LayerProperty.Visible:
                layer.Visible = (bool)value;
                break;
            case LayerProperty.Opacity:
                layer.Opacity = (double)value;
                break;
            default:
                layer.Name = (string)value;
                break;
        }
    }

    internal override bool TryMerge(HistoryEntry next)
    {
        if (property == LayerProperty.Opacity
            && next is LayerPropertyEdit other
            && other.Layer == layer
            && other.Property == LayerProperty.Opacity)
        {
            _after = other._after;
            return true;
        }

        return false;
    }

    private Layer Layer => layer;

    private LayerProperty Property => property;
}

/// <summary>
/// 画像全体の大きさ・向きの変更。すべてのレイヤーの前後の画像と、前後の大きさを持つ。
/// レイヤーの画像そのものを差し替えるので、前後の履歴（ひと筆の記録など）はそれぞれの大きさのまま正しく戻せる。
/// </summary>
internal sealed class ImageTransformEdit(
    PixelDocument document,
    Layer[] layers,
    PixelImage[] before,
    PixelImage[] after,
    (int Width, int Height) sizeBefore,
    (int Width, int Height) sizeAfter) : HistoryEntry
{
    public override long ByteSize =>
        before.Sum(i => i.Width * i.Height * 4L) + after.Sum(i => i.Width * i.Height * 4L) + 64;

    internal override void Apply(bool undo)
    {
        PixelImage[] images = undo ? before : after;
        for (int i = 0; i < layers.Length; i++)
        {
            layers[i].Image = images[i];
        }

        (int w, int h) = undo ? sizeBefore : sizeAfter;
        document.SetSize(w, h);
    }
}

/// <summary>いくつかの操作を1回分としてまとめる（すべてのレイヤーの色を変えるときなど）。</summary>
internal sealed class GroupEdit(HistoryEntry[] entries) : HistoryEntry
{
    public override long ByteSize => entries.Sum(e => e.ByteSize);

    internal override void Apply(bool undo)
    {
        if (undo)
        {
            for (int i = entries.Length - 1; i >= 0; i--)
            {
                entries[i].Apply(undo: true);
            }
        }
        else
        {
            foreach (HistoryEntry entry in entries)
            {
                entry.Apply(undo: false);
            }
        }
    }
}
