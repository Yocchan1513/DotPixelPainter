using System.Globalization;
using System.Text;

namespace DotPixelPainter.Core;

/// <summary>カスタムパレット。色は 0xAARRGGBB。</summary>
public sealed class Palette
{
    public const int MaxColors = 256;

    private readonly List<uint> _colors = [];

    public Palette(string name = "カスタム")
    {
        Name = name;
    }

    public string Name { get; set; }

    public IReadOnlyList<uint> Colors => _colors;

    public static Palette CreateDefault()
    {
        var palette = new Palette();
        foreach (uint c in DefaultColors)
        {
            palette.Add(c);
        }

        return palette;
    }

    private static readonly uint[] DefaultColors =
    [
        0xFF000000, 0xFF404040, 0xFF808080, 0xFFC0C0C0, 0xFFFFFFFF,
        0xFF7F1F1F, 0xFFE53935, 0xFFFB8C00, 0xFFFDD835, 0xFF43A047,
        0xFF1B5E20, 0xFF1E88E5, 0xFF0D47A1, 0xFF8E24AA, 0xFFF48FB1, 0xFF6D4C41,
    ];

    public bool Add(uint argb)
    {
        if (_colors.Count >= MaxColors)
        {
            return false;
        }

        _colors.Add(argb);
        return true;
    }

    public void RemoveAt(int index) => _colors.RemoveAt(index);

    public void Replace(int index, uint argb) => _colors[index] = argb;

    public void Clear() => _colors.Clear();

    // ---- アプリ内の保存形式（1行に1色、AARRGGBB の16進） ----

    public string ToHexList()
    {
        var sb = new StringBuilder();
        foreach (uint c in _colors)
        {
            sb.Append(c.ToString("X8", CultureInfo.InvariantCulture)).Append('\n');
        }

        return sb.ToString();
    }

    public static Palette FromHexList(string text, string name = "カスタム")
    {
        var palette = new Palette(name);
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim().TrimStart('#');
            if (line.Length == 8 && uint.TryParse(line, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb))
            {
                palette.Add(argb);
            }
            else if (line.Length == 6 && uint.TryParse(line, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
            {
                palette.Add(0xFF000000 | rgb);
            }
        }

        return palette;
    }

    // ---- GIMP パレット（.gpl）。他ソフトとのやり取り用。透明度は持てない ----

    public string ToGpl()
    {
        var sb = new StringBuilder();
        sb.Append("GIMP Palette\n");
        sb.Append("Name: ").Append(Name).Append('\n');
        sb.Append("Columns: 8\n#\n");
        foreach (uint c in _colors)
        {
            sb.Append(string.Create(CultureInfo.InvariantCulture, $"{(c >> 16) & 0xFF,3} {(c >> 8) & 0xFF,3} {c & 0xFF,3}\t#{c & 0xFFFFFF:X6}\n"));
        }

        return sb.ToString();
    }

    public static Palette FromGpl(string text)
    {
        string[] lines = text.Replace("\r", string.Empty).Split('\n');
        if (lines.Length == 0 || !lines[0].Trim().Equals("GIMP Palette", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("GIMP パレット（.gpl）ではありません。");
        }

        var palette = new Palette();
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("Name:", StringComparison.OrdinalIgnoreCase))
            {
                palette.Name = line[5..].Trim();
                continue;
            }

            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("Columns:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string[] parts = line.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3
                && byte.TryParse(parts[0], CultureInfo.InvariantCulture, out byte r)
                && byte.TryParse(parts[1], CultureInfo.InvariantCulture, out byte g)
                && byte.TryParse(parts[2], CultureInfo.InvariantCulture, out byte b))
            {
                palette.Add(0xFF000000 | (uint)r << 16 | (uint)g << 8 | b);
            }
        }

        return palette;
    }
}
