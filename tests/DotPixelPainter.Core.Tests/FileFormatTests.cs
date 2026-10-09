using System.IO.Compression;
using System.Text;
using DotPixelPainter.Core;

namespace DotPixelPainter.Core.Tests;

public class FileFormatTests
{
    private static PixelImage Sample(int w = 7, int h = 5)
    {
        var image = new PixelImage(w, h);
        var random = new Random(7);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // 透明・半透明・不透明が混ざるように
                uint a = (uint)random.Next(0, 4) switch { 0 => 0, 1 => 128, _ => 255 };
                image.SetPixel(x, y, a << 24 | (uint)random.Next(0, 0x1000000));
            }
        }

        return image;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Png_RoundTrip_AllFilters(byte filter)
    {
        PixelImage image = Sample();

        PixelImage back = PngCodec.Decode(PngCodec.Encode(image, filter));

        Assert.Equal(image.Pixels.ToArray(), back.Pixels.ToArray());
    }

    [Fact]
    public void Png_RejectsOtherData()
    {
        Assert.Throws<FormatException>(() => PngCodec.Decode(Encoding.ASCII.GetBytes("not a png file")));
    }

    private static PixelDocument SampleDocument()
    {
        var doc = new PixelDocument("sample", 7, 5);
        doc.ActiveLayer.Image.SetPixel(0, 0, 0xFF112233);
        doc.RenameLayer(0, "背景");
        Layer top = doc.AddLayer("線画");
        top.Image.SetPixel(6, 4, 0x80FFFFFF);
        doc.SetLayerOpacity(1, 0.75);
        doc.AddLayer("下書き");
        doc.SetLayerVisible(2, false);
        doc.SelectLayer(1);
        return doc;
    }

    [Fact]
    public void DotPix_RoundTrip_KeepsLayers()
    {
        PixelDocument doc = SampleDocument();
        using var stream = new MemoryStream();
        DotPixFile.Write(doc, stream);
        stream.Position = 0;

        PixelDocument back = DotPixFile.Read(stream, "sample.dotpix");

        Assert.Equal(7, back.Width);
        Assert.Equal(5, back.Height);
        Assert.Equal(["背景", "線画", "下書き"], back.Layers.Select(l => l.Name));
        Assert.Equal(0.75, back.Layers[1].Opacity);
        Assert.False(back.Layers[2].Visible);
        Assert.Equal(1, back.ActiveLayerIndex);
        Assert.Equal(0xFF112233u, back.Layers[0].Image.GetPixel(0, 0));
        Assert.Equal(0x80FFFFFFu, back.Layers[1].Image.GetPixel(6, 4));
        Assert.False(back.IsDirty);
        Assert.False(back.History.CanUndo);

        // 読み込んだあとに足したレイヤーの名前が、既存の番号とぶつからない
        Assert.Equal("レイヤー 4", back.AddLayer().Name);
    }

    [Fact]
    public void DotPix_KeepsSkinSettings()
    {
        var doc = new PixelDocument("skin", 64, 64) { SkinMode = true, SlimArms = true };
        using var stream = new MemoryStream();
        DotPixFile.Write(doc, stream);
        stream.Position = 0;

        PixelDocument back = DotPixFile.Read(stream, "skin.dotpix");

        Assert.True(back.SkinMode);
        Assert.True(back.SlimArms);
    }

    [Fact]
    public void DotPix_NonSkinDocument_StaysNormal()
    {
        PixelDocument doc = SampleDocument();
        using var stream = new MemoryStream();
        DotPixFile.Write(doc, stream);
        stream.Position = 0;

        Assert.False(DotPixFile.Read(stream, "x").SkinMode);
    }

    [Fact]
    public void DotPix_ContainsMergedPreview()
    {
        PixelDocument doc = SampleDocument();
        using var stream = new MemoryStream();
        DotPixFile.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var merged = new MemoryStream();
        zip.GetEntry("merged.png")!.Open().CopyTo(merged);

        Assert.Equal(doc.Composite().Pixels.ToArray(), PngCodec.Decode(merged.ToArray()).Pixels.ToArray());
    }

    private static MemoryStream ZipWith(string json)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(zip.CreateEntry("document.json").Open()))
        {
            writer.Write(json);
        }

        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void DotPix_NewerVersion_IsRejectedWithMessage()
    {
        using var stream = ZipWith("""{"format":"dotpix","version":99,"width":1,"height":1,"layers":[]}""");

        var ex = Assert.Throws<FormatException>(() => DotPixFile.Read(stream, "x"));
        Assert.Contains("新しいバージョン", ex.Message);
    }

    [Fact]
    public void DotPix_MissingLayerImage_IsRejected()
    {
        using var stream = ZipWith("""{"format":"dotpix","version":1,"width":1,"height":1,"layers":[{"name":"a","file":"layers/0.png"}]}""");

        Assert.Throws<FormatException>(() => DotPixFile.Read(stream, "x"));
    }

    [Fact]
    public void DotPix_PlainZip_IsRejected()
    {
        var stream = new MemoryStream();
        using (new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
        }

        stream.Position = 0;
        Assert.Throws<FormatException>(() => DotPixFile.Read(stream, "x"));
    }
}
