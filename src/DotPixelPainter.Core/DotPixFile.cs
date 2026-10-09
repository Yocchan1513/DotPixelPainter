using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace DotPixelPainter.Core;

/// <summary>
/// DotPixelPainter の独自形式（.dotpix）。中身は zip で、
///   document.json … サイズ・レイヤーの並び（下から順）・名前・表示・不透明度
///   layers/N.png  … レイヤーごとの画像（8bit RGBA）
///   merged.png    … 表示どおりに1枚にした画像（壊れたときの取り出し・他ソフトでの確認用）
/// を入れる。JSON の version は、項目を増やして互換が崩れるときに上げる。
/// </summary>
public static class DotPixFile
{
    public const string Extension = ".dotpix";
    public const int Version = 1;

    public static void Write(PixelDocument document, Stream output)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        using (Stream json = zip.CreateEntry("document.json", CompressionLevel.Optimal).Open())
        using (var writer = new Utf8JsonWriter(json, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", "dotpix");
            writer.WriteNumber("version", Version);
            writer.WriteNumber("width", document.Width);
            writer.WriteNumber("height", document.Height);
            writer.WriteNumber("activeLayer", document.ActiveLayerIndex);
            writer.WriteStartArray("layers");
            for (int i = 0; i < document.Layers.Count; i++)
            {
                Layer layer = document.Layers[i];
                writer.WriteStartObject();
                writer.WriteString("name", layer.Name);
                writer.WriteBoolean("visible", layer.Visible);
                writer.WriteNumber("opacity", Math.Round(layer.Opacity, 4));
                writer.WriteString("file", LayerPath(i));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        for (int i = 0; i < document.Layers.Count; i++)
        {
            WriteBytes(zip, LayerPath(i), PngCodec.Encode(document.Layers[i].Image));
        }

        WriteBytes(zip, "merged.png", PngCodec.Encode(document.Composite()));
    }

    public static PixelDocument Read(Stream input, string name)
    {
        using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        ZipArchiveEntry entry = zip.GetEntry("document.json") ?? throw new FormatException("DotPixelPainter のファイルではありません（document.json がありません）。");

        using var json = JsonDocument.Parse(ReadAll(entry));
        JsonElement root = json.RootElement;
        if (root.TryGetProperty("format", out JsonElement format) && format.GetString() != "dotpix")
        {
            throw new FormatException("DotPixelPainter のファイルではありません。");
        }

        int version = root.GetProperty("version").GetInt32();
        if (version > Version)
        {
            throw new FormatException(string.Create(CultureInfo.InvariantCulture,
                $"新しいバージョン（{version}）の DotPixelPainter で作られたファイルです。アプリを更新してください。"));
        }

        int width = root.GetProperty("width").GetInt32();
        int height = root.GetProperty("height").GetInt32();

        var layers = new List<Layer>();
        foreach (JsonElement item in root.GetProperty("layers").EnumerateArray())
        {
            string file = item.GetProperty("file").GetString() ?? throw new FormatException("レイヤーの画像が指定されていません。");
            ZipArchiveEntry png = zip.GetEntry(file) ?? throw new FormatException($"レイヤーの画像 {file} が見つかりません。");
            PixelImage image = PngCodec.Decode(ReadAll(png));
            if (image.Width != width || image.Height != height)
            {
                throw new FormatException($"レイヤーの画像 {file} の大きさが合いません。");
            }

            var layer = new Layer(item.GetProperty("name").GetString() ?? "レイヤー", image)
            {
                Visible = !item.TryGetProperty("visible", out JsonElement v) || v.GetBoolean(),
                Opacity = item.TryGetProperty("opacity", out JsonElement o) ? Math.Clamp(o.GetDouble(), 0, 1) : 1.0,
            };
            layers.Add(layer);
        }

        if (layers.Count == 0)
        {
            throw new FormatException("レイヤーが1枚もありません。");
        }

        int active = root.TryGetProperty("activeLayer", out JsonElement a) ? a.GetInt32() : 0;
        return PixelDocument.FromLayers(name, width, height, layers, active);
    }

    private static string LayerPath(int index) => string.Create(CultureInfo.InvariantCulture, $"layers/{index}.png");

    private static void WriteBytes(ZipArchive zip, string path, byte[] bytes)
    {
        // PNG はすでに圧縮されているので、zip では圧縮しない（速い）
        using Stream s = zip.CreateEntry(path, CompressionLevel.NoCompression).Open();
        s.Write(bytes);
    }

    private static byte[] ReadAll(ZipArchiveEntry entry)
    {
        using Stream s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
