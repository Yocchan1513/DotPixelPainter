namespace DotPixelPainter.Core;

/// <summary>絵の中の色を数える・近い色を探す・色数を減らすための計算。</summary>
public static class ColorTools
{
    /// <summary>
    /// 使われている色と、その画素数を多い順に返す。完全に透明な画素は数えない。
    /// area を渡すと、その範囲の中だけを数える。
    /// </summary>
    public static List<(uint Color, int Count)> CountColors(IEnumerable<PixelImage> images, PixelRect? area = null)
    {
        var counts = new Dictionary<uint, int>();
        foreach (PixelImage image in images)
        {
            if ((area ?? new PixelRect(0, 0, image.Width, image.Height)).Intersect(new PixelRect(0, 0, image.Width, image.Height)) is not { } r)
            {
                continue; // 範囲が画像の外
            }

            ReadOnlySpan<uint> pixels = image.Pixels;
            for (int y = r.Y; y < r.Bottom; y++)
            {
                for (int x = r.X; x < r.Right; x++)
                {
                    uint c = pixels[y * image.Width + x];
                    if (c >> 24 != 0)
                    {
                        counts[c] = counts.GetValueOrDefault(c) + 1;
                    }
                }
            }
        }

        return counts.Select(kv => (kv.Key, kv.Value)).OrderByDescending(c => c.Value).ThenBy(c => c.Key).ToList();
    }

    /// <summary>palette の中で color にいちばん近い色（RGB で比べる）。color の不透明度はそのまま残す。</summary>
    public static uint Nearest(uint color, IReadOnlyList<uint> palette)
    {
        if (palette.Count == 0)
        {
            return color;
        }

        uint best = palette[0];
        long bestDistance = long.MaxValue;
        foreach (uint candidate in palette)
        {
            long d = Distance(color, candidate);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = candidate;
            }
        }

        return (color & 0xFF000000) | (best & 0x00FFFFFF);
    }

    /// <summary>
    /// 色の違いの大きさ。人の目に近づけるため、赤の多さに応じて R と B の重みを変える（"redmean" という簡単な近似）。
    /// </summary>
    public static long Distance(uint a, uint b)
    {
        int r1 = (int)(a >> 16) & 0xFF, g1 = (int)(a >> 8) & 0xFF, b1 = (int)a & 0xFF;
        int r2 = (int)(b >> 16) & 0xFF, g2 = (int)(b >> 8) & 0xFF, b2 = (int)b & 0xFF;
        long rMean = (r1 + r2) / 2;
        long dr = r1 - r2, dg = g1 - g2, db = b1 - b2;
        return ((512 + rMean) * dr * dr >> 8) + 4 * dg * dg + ((767 - rMean) * db * db >> 8);
    }

    /// <summary>
    /// 色数を count 色まで減らした代表の色を求める（メディアンカット）。
    /// 色の集まりを、いちばん幅のある色の成分で半分ずつに分けていき、それぞれの平均を代表にする。
    /// 返す色はすべて不透明。
    /// </summary>
    public static List<uint> MedianCut(IReadOnlyList<(uint Color, int Count)> colors, int count)
    {
        if (count < 1 || colors.Count == 0)
        {
            return [];
        }

        if (colors.Count <= count)
        {
            return colors.Select(c => c.Color | 0xFF000000).Distinct().ToList();
        }

        var boxes = new List<List<(uint Color, int Count)>> { colors.ToList() };
        while (boxes.Count < count)
        {
            // 色の幅がいちばん大きい箱を分ける（分けられる箱がなければ終わり）
            int target = -1;
            int widest = 0;
            int channel = 0;
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].Count < 2)
                {
                    continue;
                }

                (int ch, int range) = WidestChannel(boxes[i]);
                if (range > widest)
                {
                    (widest, target, channel) = (range, i, ch);
                }
            }

            if (target < 0)
            {
                break;
            }

            List<(uint Color, int Count)> box = boxes[target];
            int shift = channel * 8;
            box.Sort((a, b) => ((int)(a.Color >> shift) & 0xFF).CompareTo((int)(b.Color >> shift) & 0xFF));

            // 画素数で半分になるところで分ける（どちらも1色以上は残す）
            long total = box.Sum(c => (long)c.Count);
            long running = 0;
            int split = 1;
            for (int i = 0; i < box.Count - 1; i++)
            {
                running += box[i].Count;
                split = i + 1;
                if (running * 2 >= total)
                {
                    break;
                }
            }

            boxes[target] = box.GetRange(0, split);
            boxes.Add(box.GetRange(split, box.Count - split));
        }

        return boxes.Select(Average).Distinct().ToList();
    }

    /// <summary>箱の中でいちばん幅のある成分（0 = B, 1 = G, 2 = R）と、その幅。</summary>
    private static (int Channel, int Range) WidestChannel(List<(uint Color, int Count)> box)
    {
        int best = 0;
        int bestRange = -1;
        for (int ch = 0; ch < 3; ch++)
        {
            int shift = ch * 8;
            int min = 255, max = 0;
            foreach ((uint c, _) in box)
            {
                int v = (int)(c >> shift) & 0xFF;
                min = Math.Min(min, v);
                max = Math.Max(max, v);
            }

            if (max - min > bestRange)
            {
                (best, bestRange) = (ch, max - min);
            }
        }

        return (best, bestRange);
    }

    private static uint Average(List<(uint Color, int Count)> box)
    {
        long r = 0, g = 0, b = 0, n = 0;
        foreach ((uint c, int count) in box)
        {
            r += ((c >> 16) & 0xFF) * count;
            g += ((c >> 8) & 0xFF) * count;
            b += (c & 0xFF) * count;
            n += count;
        }

        n = Math.Max(n, 1);
        return 0xFF000000 | (uint)((r + n / 2) / n) << 16 | (uint)((g + n / 2) / n) << 8 | (uint)((b + n / 2) / n);
    }
}
