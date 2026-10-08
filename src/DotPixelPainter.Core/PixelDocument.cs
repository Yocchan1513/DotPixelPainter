namespace DotPixelPainter.Core;

/// <summary>
/// タブ1枚ぶんの編集対象。開いているファイルごとに1つ持つ。
/// </summary>
public sealed class PixelDocument
{
    private readonly List<Layer> _layers = [];

    public PixelDocument(string name, int width, int height, long undoMemoryLimit = UndoHistory.DefaultMemoryLimit)
    {
        History = new UndoHistory(undoMemoryLimit);
        Name = name;
        Width = width;
        Height = height;
        _layers.Add(new Layer("レイヤー 1", width, height));
    }

    public string Name { get; set; }

    public string? FilePath { get; set; }

    public int Width { get; }

    public int Height { get; }

    public IReadOnlyList<Layer> Layers => _layers;

    public int ActiveLayerIndex { get; private set; }

    public Layer ActiveLayer => _layers[ActiveLayerIndex];

    /// <summary>元に戻す／やり直しの履歴。</summary>
    public UndoHistory History { get; }

    /// <summary>最後に保存した状態から変わっているか。元に戻して保存時の状態になれば false に戻る。</summary>
    public bool IsDirty => History.IsDirty;

    public void MarkSaved() => History.MarkSaved();

    /// <summary>アクティブなレイヤーにひと筆描き始める。描き終わったら Commit を呼ぶ。</summary>
    public PixelStroke BeginStroke() => new(this, ActiveLayer);

    public Layer AddLayer(string name)
    {
        var layer = new Layer(name, Width, Height);
        _layers.Insert(ActiveLayerIndex + 1, layer);
        ActiveLayerIndex++;
        return layer;
    }

    public void SelectLayer(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _layers.Count);
        ActiveLayerIndex = index;
    }

    /// <summary>表示中のレイヤーを下から順に通常合成した画像を作る。</summary>
    public PixelImage Composite()
    {
        var result = new PixelImage(Width, Height);
        foreach (var layer in _layers)
        {
            if (!layer.Visible || layer.Opacity <= 0)
            {
                continue;
            }

            Blend.Normal(result, layer.Image, layer.Opacity);
        }

        return result;
    }
}
