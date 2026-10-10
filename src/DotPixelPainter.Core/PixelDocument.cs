namespace DotPixelPainter.Core;

/// <summary>
/// タブ1枚ぶんの編集対象。開いているファイルごとに1つ持つ。
/// レイヤーは下から順に並ぶ（Layers[0] がいちばん下）。
/// レイヤーの操作はすべて履歴に積まれ、元に戻せる。
/// </summary>
public sealed class PixelDocument
{
    private readonly List<Layer> _layers = [];
    private int _nextLayerNumber = 2;

    public PixelDocument(string name, int width, int height, long undoMemoryLimit = UndoHistory.DefaultMemoryLimit)
    {
        History = new UndoHistory(undoMemoryLimit);
        Name = name;
        Width = width;
        Height = height;
        _layers.Add(new Layer("レイヤー 1", width, height));
    }

    /// <summary>ファイルから読んだレイヤーで作る（.dotpix の読み込み用）。読み込み直後は「保存済み」。</summary>
    internal static PixelDocument FromLayers(string name, int width, int height, List<Layer> layers, int activeLayer)
    {
        var document = new PixelDocument(name, width, height);
        document._layers.Clear();
        document._layers.AddRange(layers);
        document._nextLayerNumber = layers.Count + 1;
        document.SetActive(activeLayer);
        return document;
    }

    public string Name { get; set; }

    public string? FilePath { get; set; }

    /// <summary>Minecraft スキンとして編集しているか（部位ガイドと 3D プレビューを出す）。</summary>
    public bool SkinMode { get; set; }

    /// <summary>スキンの腕がスリム（3px）か。false ならクラシック（4px）。</summary>
    public bool SlimArms { get; set; }

    public bool IsLegacySkin => Height == 32;

    public int Width { get; private set; }

    public int Height { get; private set; }

    public IReadOnlyList<Layer> Layers => _layers;

    public int ActiveLayerIndex { get; private set; }

    public Layer ActiveLayer => _layers[ActiveLayerIndex];

    /// <summary>元に戻す／やり直しの履歴。</summary>
    public UndoHistory History { get; }

    /// <summary>最後に保存した状態から変わっているか。元に戻して保存時の状態になれば false に戻る。</summary>
    public bool IsDirty => History.IsDirty;

    public void MarkSaved() => History.MarkSaved();

    /// <summary>アクティブなレイヤーにひと筆描き始める。描き終わったら Commit を呼ぶ。</summary>
    /// <remarks>mask を渡すと、カラーマスクで禁止された画素には描かない（描く道具のときだけ渡す）。</remarks>
    public PixelStroke BeginStroke(ColorMask? mask = null) => new(this, ActiveLayer, mask);

    /// <summary>選択するだけで、履歴には積まない。</summary>
    public void SelectLayer(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _layers.Count);
        ActiveLayerIndex = index;
    }

    // ---- レイヤーの操作（すべて元に戻せる） ----

    /// <summary>アクティブなレイヤーのすぐ上に新しいレイヤーを足し、それを選ぶ。</summary>
    public Layer AddLayer(string? name = null)
    {
        var layer = new Layer(name ?? $"レイヤー {_nextLayerNumber++}", Width, Height);
        Push(new LayerInsertEdit(this, layer, ActiveLayerIndex + 1, ActiveLayerIndex));
        return layer;
    }

    /// <summary>レイヤーを複製して、すぐ上に置く。</summary>
    public Layer DuplicateLayer(int index)
    {
        Layer source = _layers[index];
        var copy = new Layer($"{source.Name} のコピー", source.Image.Clone())
        {
            Visible = source.Visible,
            Opacity = source.Opacity,
        };
        Push(new LayerInsertEdit(this, copy, index + 1, ActiveLayerIndex));
        return copy;
    }

    /// <summary>レイヤーを消す。最後の1枚は消せない（false を返す）。</summary>
    public bool DeleteLayer(int index)
    {
        if (_layers.Count <= 1)
        {
            return false;
        }

        Push(new LayerRemoveEdit(this, _layers[index], index));
        return true;
    }

    /// <summary>レイヤーの位置を from から to へ動かす（どちらも下からの番号）。</summary>
    public bool MoveLayer(int from, int to)
    {
        to = Math.Clamp(to, 0, _layers.Count - 1);
        if (from == to)
        {
            return false;
        }

        Push(new LayerMoveEdit(this, from, to));
        return true;
    }

    /// <summary>レイヤーをすぐ下のレイヤーに、表示どおり（不透明度込み）に合成して1枚にする。</summary>
    public bool MergeDown(int index)
    {
        if (index <= 0)
        {
            return false;
        }

        Layer upper = _layers[index];
        Layer lower = _layers[index - 1];
        uint[] before = lower.Image.CopyPixels();
        if (upper.Visible)
        {
            Blend.Normal(lower.Image, upper.Image, upper.Opacity);
        }

        uint[] after = lower.Image.CopyPixels();
        lower.Image.RestorePixels(before);
        Push(new LayerMergeEdit(this, upper, index, lower, before, after));
        return true;
    }

    public void SetLayerVisible(int index, bool visible) => SetProperty(index, LayerProperty.Visible, visible);

    public void SetLayerOpacity(int index, double opacity) => SetProperty(index, LayerProperty.Opacity, Math.Clamp(opacity, 0, 1));

    public void RenameLayer(int index, string name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            SetProperty(index, LayerProperty.Name, name.Trim());
        }
    }

    // ---- 画像全体の操作（大きさ・向き。すべて元に戻せる） ----

    /// <summary>キャンバスの大きさを変える。元の絵を (offsetX, offsetY) に置く。</summary>
    public bool ResizeCanvas(int width, int height, int offsetX, int offsetY) =>
        TransformImage(width, height, image => ImageTransform.ResizeCanvas(image, width, height, offsetX, offsetY));

    /// <summary>選んだ範囲だけを残して切り抜く。</summary>
    public bool Crop(PixelRect rect) => ResizeCanvas(rect.Width, rect.Height, -rect.X, -rect.Y);

    /// <summary>絵を拡大・縮小する（最近傍。ドットがにじまない）。</summary>
    public bool ScaleImage(int width, int height) =>
        TransformImage(width, height, image => ImageTransform.ScaleNearest(image, width, height));

    public bool FlipImage(bool horizontal) =>
        TransformImage(Width, Height, image => ImageTransform.Flip(image, horizontal), force: true);

    public bool RotateImage(bool clockwise) =>
        TransformImage(Height, Width, image => ImageTransform.Rotate90(image, clockwise), force: true);

    /// <summary>
    /// すべてのレイヤーの画像を作り直す。大きさが変わらず force でもなければ何もしない（false）。
    /// </summary>
    private bool TransformImage(int width, int height, Func<PixelImage, PixelImage> transform, bool force = false)
    {
        if (width < 1 || height < 1 || width > PixelImage.MaxSize || height > PixelImage.MaxSize)
        {
            throw new ArgumentOutOfRangeException(nameof(width), $"画像サイズは 1〜{PixelImage.MaxSize} の範囲で指定してください。");
        }

        if (!force && width == Width && height == Height)
        {
            return false;
        }

        var before = _layers.Select(l => l.Image).ToArray();
        var after = before.Select(transform).ToArray();
        Push(new ImageTransformEdit(this, [.. _layers], before, after, (Width, Height), (width, height)));
        return true;
    }

    internal void SetSize(int width, int height)
    {
        Width = width;
        Height = height;
    }

    private void SetProperty(int index, LayerProperty property, object value)
    {
        Layer layer = _layers[index];
        object before = LayerPropertyEdit.Get(layer, property);
        if (!Equals(before, value))
        {
            Push(new LayerPropertyEdit(layer, property, before, value));
        }
    }

    /// <summary>操作を実行して履歴に積む。</summary>
    private void Push(HistoryEntry entry)
    {
        entry.Apply(undo: false);
        History.Push(entry);
    }

    // 履歴の各操作から呼ぶ、レイヤー一覧の直接操作
    internal void InsertLayerAt(int index, Layer layer) => _layers.Insert(index, layer);

    internal void RemoveLayerAt(int index) => _layers.RemoveAt(index);

    internal void SetActive(int index) => ActiveLayerIndex = Math.Clamp(index, 0, _layers.Count - 1);

    /// <summary>
    /// 表示中のレイヤーを下から順に通常合成した画像を作る。
    /// floating を渡すと、アクティブなレイヤーにそれを重ねた状態で合成する（移動中の表示用）。
    /// </summary>
    public PixelImage Composite(FloatingSelection? floating = null)
    {
        var result = new PixelImage(Width, Height);
        for (int i = 0; i < _layers.Count; i++)
        {
            Layer layer = _layers[i];
            if (!layer.Visible || layer.Opacity <= 0)
            {
                continue;
            }

            PixelImage image = layer.Image;
            if (floating is not null && i == ActiveLayerIndex)
            {
                image = image.Clone();
                floating.DrawOnto(image);
            }

            Blend.Normal(result, image, layer.Opacity);
        }

        return result;
    }
}
