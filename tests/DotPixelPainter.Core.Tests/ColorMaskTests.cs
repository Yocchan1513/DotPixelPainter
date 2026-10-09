using DotPixelPainter.Core;

namespace DotPixelPainter.Core.Tests;

public class ColorMaskTests
{
    private const uint Line = 0xFF000000;
    private const uint Skin = 0xFFF0C8A0;
    private const uint Red = 0xFFFF0000;

    /// <summary>1行: [線][肌][透明][肌]</summary>
    private static PixelDocument Sample()
    {
        var doc = new PixelDocument("t", 4, 1);
        doc.ActiveLayer.Image.SetPixel(0, 0, Line);
        doc.ActiveLayer.Image.SetPixel(1, 0, Skin);
        doc.ActiveLayer.Image.SetPixel(3, 0, Skin);
        return doc;
    }

    private static void PaintRow(PixelDocument doc, ColorMask mask, uint color)
    {
        PixelStroke stroke = doc.BeginStroke(mask);
        for (int x = 0; x < 4; x++)
        {
            stroke.Plot(x, 0, color);
        }

        stroke.Commit();
    }

    [Fact]
    public void Protect_KeepsListedColors()
    {
        PixelDocument doc = Sample();
        var mask = new ColorMask { Mode = ColorMaskMode.Protect };
        mask.Add(Line);

        PaintRow(doc, mask, Red);

        Assert.Equal([Line, Red, Red, Red], doc.ActiveLayer.Image.Pixels.ToArray());
    }

    [Fact]
    public void Only_PaintsJustListedColors()
    {
        PixelDocument doc = Sample();
        var mask = new ColorMask { Mode = ColorMaskMode.Only };
        mask.Add(Skin);

        PaintRow(doc, mask, Red);

        Assert.Equal([Line, Red, 0u, Red], doc.ActiveLayer.Image.Pixels.ToArray());
    }

    [Fact]
    public void Only_JudgesByColorBeforeTheStroke()
    {
        // 同じひと筆で同じ画素を2回通っても、2回目は「描く前の色（肌）」で判断する
        PixelDocument doc = Sample();
        var mask = new ColorMask { Mode = ColorMaskMode.Only };
        mask.Add(Skin);
        PixelStroke stroke = doc.BeginStroke(mask);
        stroke.Plot(1, 0, Red);
        stroke.Plot(1, 0, 0xFF00FF00);
        stroke.Commit();

        Assert.Equal(0xFF00FF00u, doc.ActiveLayer.Image.GetPixel(1, 0));
    }

    [Fact]
    public void Transparent_IsOneColor()
    {
        PixelDocument doc = Sample();
        var mask = new ColorMask { Mode = ColorMaskMode.Only };
        mask.Add(0x00FFFFFF); // 透明（RGB は何でもよい）

        PaintRow(doc, mask, Red);

        Assert.Equal(Red, doc.ActiveLayer.Image.GetPixel(2, 0));
        Assert.Equal(Skin, doc.ActiveLayer.Image.GetPixel(1, 0));
    }

    [Fact]
    public void OffOrEmpty_AllowsEverything()
    {
        var mask = new ColorMask { Mode = ColorMaskMode.Off };
        mask.Add(Line);
        Assert.True(mask.Allows(Line));

        var empty = new ColorMask { Mode = ColorMaskMode.Protect };
        Assert.True(empty.Allows(Line));
    }

    [Fact]
    public void FloodFill_RespectsMask()
    {
        var doc = new PixelDocument("t", 3, 1);
        doc.ActiveLayer.Image.SetPixel(1, 0, Skin);
        var mask = new ColorMask { Mode = ColorMaskMode.Protect };
        mask.Add(0); // 透明を守る

        PixelStroke stroke = doc.BeginStroke(mask);
        stroke.FloodFill(0, 0, Red);
        stroke.Commit();

        Assert.Equal(0u, doc.ActiveLayer.Image.GetPixel(0, 0));
    }

    [Fact]
    public void Add_IgnoresDuplicates_AndStopsAtMax()
    {
        var mask = new ColorMask();
        Assert.True(mask.Add(Red));
        Assert.False(mask.Add(Red));
        for (uint i = 1; i < 20; i++)
        {
            mask.Add(0xFF000000 | i);
        }

        Assert.Equal(ColorMask.MaxColors, mask.Colors.Count);
    }
}
