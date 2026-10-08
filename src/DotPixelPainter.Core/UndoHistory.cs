namespace DotPixelPainter.Core;

/// <summary>
/// 1回の操作（ペンのひと筆など）で変わったピクセルの記録。変わった画素だけを持つので小さい。
/// </summary>
public sealed class PixelEdit
{
    private readonly int[] _indices;
    private readonly uint[] _before;
    private readonly uint[] _after;

    internal PixelEdit(Layer layer, int[] indices, uint[] before, uint[] after)
    {
        Layer = layer;
        _indices = indices;
        _before = before;
        _after = after;
    }

    /// <summary>レイヤーの並びが変わっても正しく戻せるよう、番号ではなくレイヤー自体を持つ。</summary>
    public Layer Layer { get; }

    public int PixelCount => _indices.Length;

    /// <summary>おおよそのメモリ使用量（バイト）。</summary>
    public long ByteSize => _indices.Length * 12L + 64;

    internal void Apply(bool undo)
    {
        PixelImage image = Layer.Image;
        uint[] values = undo ? _before : _after;
        for (int i = 0; i < _indices.Length; i++)
        {
            int index = _indices[i];
            image.SetPixel(index % image.Width, index / image.Width, values[i]);
        }
    }
}

/// <summary>
/// 元に戻す／やり直しの履歴。ドキュメント（タブ）ごとに1つ持つ。
/// メモリ上限を超えたら古いものから捨てる。
/// </summary>
public sealed class UndoHistory
{
    public const long DefaultMemoryLimit = 64L * 1024 * 1024;

    private readonly List<PixelEdit> _edits = [];
    private int _position;
    private int _savedPosition;
    private long _bytes;

    public UndoHistory(long memoryLimit = DefaultMemoryLimit)
    {
        MemoryLimit = memoryLimit;
    }

    public long MemoryLimit { get; }

    public bool CanUndo => _position > 0;

    public bool CanRedo => _position < _edits.Count;

    /// <summary>最後に保存した状態から変わっているか。</summary>
    public bool IsDirty => _position != _savedPosition;

    public int UndoCount => _position;

    public int RedoCount => _edits.Count - _position;

    public void MarkSaved() => _savedPosition = _position;

    internal void Push(PixelEdit edit)
    {
        // やり直し待ちの操作は捨てる
        if (_position < _edits.Count)
        {
            for (int i = _position; i < _edits.Count; i++)
            {
                _bytes -= _edits[i].ByteSize;
            }

            _edits.RemoveRange(_position, _edits.Count - _position);
            if (_savedPosition > _position)
            {
                _savedPosition = -1; // 保存した状態にはもう戻れない
            }
        }

        _edits.Add(edit);
        _bytes += edit.ByteSize;
        _position++;

        while (_bytes > MemoryLimit && _edits.Count > 1)
        {
            _bytes -= _edits[0].ByteSize;
            _edits.RemoveAt(0);
            _position--;
            _savedPosition = _savedPosition > 0 ? _savedPosition - 1 : -1;
        }
    }

    public bool Undo()
    {
        if (!CanUndo)
        {
            return false;
        }

        _position--;
        _edits[_position].Apply(undo: true);
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo)
        {
            return false;
        }

        _edits[_position].Apply(undo: false);
        _position++;
        return true;
    }
}

/// <summary>
/// ひと筆ぶんの描画。描いた画素の「描く前の色」を覚えておき、Commit で履歴に1件として積む。
/// </summary>
public sealed class PixelStroke
{
    private readonly PixelDocument _document;
    private readonly Layer _layer;
    private readonly PixelImage _image;
    private readonly Dictionary<int, uint> _before = [];
    private bool _committed;

    internal PixelStroke(PixelDocument document, Layer layer)
    {
        _document = document;
        _layer = layer;
        _image = layer.Image;
    }

    /// <summary>1画素を塗る。色が変わったら true。</summary>
    public bool Plot(int x, int y, uint argb)
    {
        if (_committed || !_image.Contains(x, y))
        {
            return false;
        }

        uint old = _image.GetPixel(x, y);
        if (old == argb)
        {
            return false;
        }

        _before.TryAdd(y * _image.Width + x, old);
        _image.SetPixel(x, y, argb);
        return true;
    }

    /// <summary>区間の集まり（図形）を塗る。1画素でも変わったら true。</summary>
    public bool PlotSpans(IEnumerable<PixelSpan> spans, uint argb)
    {
        bool changed = false;
        foreach (PixelSpan s in spans)
        {
            int x0 = Math.Max(s.X0, 0);
            int x1 = Math.Min(s.X1, _image.Width - 1);
            for (int x = x0; x <= x1; x++)
            {
                changed |= Plot(x, s.Y, argb);
            }
        }

        return changed;
    }

    /// <summary>
    /// 塗りつぶし。(x, y) と同じ色で上下左右につながった範囲を塗る（斜めにはつながない）。
    /// 塗った画素数を返す。
    /// </summary>
    public int FloodFill(int x, int y, uint argb)
    {
        if (_committed || !_image.Contains(x, y))
        {
            return 0;
        }

        uint target = _image.GetPixel(x, y);
        if (target == argb)
        {
            return 0;
        }

        int count = 0;
        var stack = new Stack<(int X, int Y)>();
        stack.Push((x, y));
        while (stack.Count > 0)
        {
            (int sx, int sy) = stack.Pop();
            if (_image.GetPixel(sx, sy) != target || !_image.Contains(sx, sy))
            {
                continue;
            }

            // 左右に伸ばして1行ぶん塗る
            int left = sx;
            while (left > 0 && _image.GetPixel(left - 1, sy) == target)
            {
                left--;
            }

            int right = sx;
            while (right < _image.Width - 1 && _image.GetPixel(right + 1, sy) == target)
            {
                right++;
            }

            for (int px = left; px <= right; px++)
            {
                Plot(px, sy, argb);
                count++;
            }

            // 上下の行で、まだ塗っていない区間の先頭を積む
            foreach (int ny in (ReadOnlySpan<int>)[sy - 1, sy + 1])
            {
                if (ny < 0 || ny >= _image.Height)
                {
                    continue;
                }

                bool inRun = false;
                for (int px = left; px <= right; px++)
                {
                    bool match = _image.GetPixel(px, ny) == target;
                    if (match && !inRun)
                    {
                        stack.Push((px, ny));
                    }

                    inRun = match;
                }
            }
        }

        return count;
    }

    /// <summary>履歴に積む。実際に色が変わった画素がなければ何も積まない。</summary>
    public void Commit()
    {
        if (_committed)
        {
            return;
        }

        _committed = true;
        var indices = new List<int>(_before.Count);
        var before = new List<uint>(_before.Count);
        var after = new List<uint>(_before.Count);
        foreach (var (index, old) in _before)
        {
            uint now = _image.Pixels[index];
            if (now != old)
            {
                indices.Add(index);
                before.Add(old);
                after.Add(now);
            }
        }

        if (indices.Count > 0)
        {
            _document.History.Push(new PixelEdit(_layer, [.. indices], [.. before], [.. after]));
        }
    }
}
