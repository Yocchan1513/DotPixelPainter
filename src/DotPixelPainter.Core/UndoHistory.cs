namespace DotPixelPainter.Core;

/// <summary>元に戻す／やり直しの履歴の1件。ひと筆、レイヤーの追加、不透明度の変更など。</summary>
public abstract class HistoryEntry
{
    /// <summary>おおよそのメモリ使用量（バイト）。</summary>
    public abstract long ByteSize { get; }

    internal abstract void Apply(bool undo);

    /// <summary>直後の操作 next を自分にまとめられるなら、まとめて true を返す（スライダーを動かし続けたときなど）。</summary>
    internal virtual bool TryMerge(HistoryEntry next) => false;
}

/// <summary>
/// 1回の操作（ペンのひと筆など）で変わったピクセルの記録。変わった画素だけを持つので小さい。
/// </summary>
public sealed class PixelEdit : HistoryEntry
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

    public override long ByteSize => _indices.Length * 12L + 64;

    internal override void Apply(bool undo)
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

    private readonly List<HistoryEntry> _edits = [];
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

    internal void Push(HistoryEntry edit)
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

        // 直前の操作にまとめられるならまとめる（保存した直後の操作はまとめない）
        if (_position > 0 && _position == _edits.Count && _savedPosition != _position && _edits[_position - 1].TryMerge(edit))
        {
            return;
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

    private readonly ColorMask? _mask;
    private readonly DrawPattern? _pattern;

    internal PixelStroke(PixelDocument document, Layer layer, ColorMask? mask = null, DrawPattern? pattern = null)
    {
        _document = document;
        _layer = layer;
        _image = layer.Image;
        _mask = mask is { IsActive: true } ? mask : null;
        _pattern = pattern;
    }

    /// <summary>
    /// 1画素を塗る。色が変わったら true。カラーマスクで禁止された画素には塗らない。
    /// パターンがあるときは、模様の外の画素は塗らない（すき間の色があればその色で塗る）。
    /// </summary>
    public bool Plot(int x, int y, uint argb)
    {
        if (_committed || !_image.Contains(x, y))
        {
            return false;
        }

        if (_pattern is not null)
        {
            if (_pattern.ColorAt(x, y, argb) is not { } patterned)
            {
                return false;
            }

            argb = patterned;
        }

        int index = y * _image.Width + x;
        uint old = _image.GetPixel(x, y);

        // マスクは「このひと筆で描く前の色」で判断する（同じ筆で重ねて塗っても判断が変わらないように）
        if (_mask is not null && !_mask.Allows(_before.TryGetValue(index, out uint original) ? original : old))
        {
            return false;
        }

        if (old == argb)
        {
            return false;
        }

        _before.TryAdd(index, old);
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
    /// 先に範囲を決めてから塗るので、マスクやパターンで塗らない画素があっても止まる。範囲の画素数を返す。
    /// </summary>
    public int FloodFill(int x, int y, uint argb)
    {
        if (_committed || !_image.Contains(x, y))
        {
            return 0;
        }

        uint target = _image.GetPixel(x, y);
        if (target == argb && _pattern is null)
        {
            return 0;
        }

        int width = _image.Width;
        int height = _image.Height;
        var inRegion = new bool[width * height];
        var region = new List<int>();
        var stack = new Stack<(int X, int Y)>();
        stack.Push((x, y));
        while (stack.Count > 0)
        {
            (int sx, int sy) = stack.Pop();
            if (inRegion[sy * width + sx] || _image.GetPixel(sx, sy) != target)
            {
                continue;
            }

            // 左右に伸ばして1行ぶんを範囲に入れる
            int left = sx;
            while (left > 0 && !inRegion[sy * width + left - 1] && _image.GetPixel(left - 1, sy) == target)
            {
                left--;
            }

            int right = sx;
            while (right < width - 1 && !inRegion[sy * width + right + 1] && _image.GetPixel(right + 1, sy) == target)
            {
                right++;
            }

            for (int px = left; px <= right; px++)
            {
                inRegion[sy * width + px] = true;
                region.Add(sy * width + px);
            }

            // 上下の行で、まだ範囲に入れていない区間の先頭を積む
            foreach (int ny in (ReadOnlySpan<int>)[sy - 1, sy + 1])
            {
                if (ny < 0 || ny >= height)
                {
                    continue;
                }

                bool inRun = false;
                for (int px = left; px <= right; px++)
                {
                    bool match = !inRegion[ny * width + px] && _image.GetPixel(px, ny) == target;
                    if (match && !inRun)
                    {
                        stack.Push((px, ny));
                    }

                    inRun = match;
                }
            }
        }

        foreach (int index in region)
        {
            Plot(index % width, index / width, argb);
        }

        return region.Count;
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
