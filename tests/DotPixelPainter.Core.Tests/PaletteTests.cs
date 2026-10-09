using DotPixelPainter.Core;

namespace DotPixelPainter.Core.Tests;

public class PaletteTests
{
    [Fact]
    public void HexList_RoundTrip_KeepsAlpha()
    {
        var palette = new Palette();
        palette.Add(0x80FF0000);
        palette.Add(0xFF00FF00);

        var copy = Palette.FromHexList(palette.ToHexList());

        Assert.Equal(palette.Colors, copy.Colors);
    }

    [Fact]
    public void Gpl_RoundTrip_KeepsRgbAndName()
    {
        var palette = new Palette("テスト");
        palette.Add(0xFF102030);
        palette.Add(0xFFFFFFFF);

        var copy = Palette.FromGpl(palette.ToGpl());

        Assert.Equal("テスト", copy.Name);
        Assert.Equal(palette.Colors, copy.Colors);
    }

    [Fact]
    public void FromGpl_ReadsGimpStyleFile()
    {
        const string text = "GIMP Palette\r\nName: Sample\r\nColumns: 4\r\n#\r\n  0   0   0\tBlack\r\n255 128   0\tOrange\r\n";

        var palette = Palette.FromGpl(text);

        Assert.Equal("Sample", palette.Name);
        Assert.Equal([0xFF000000u, 0xFFFF8000u], palette.Colors);
    }

    [Fact]
    public void FromGpl_RejectsOtherFiles()
    {
        Assert.Throws<FormatException>(() => Palette.FromGpl("hello"));
    }

    [Fact]
    public void Add_StopsAtMaxColors()
    {
        var palette = new Palette();
        for (int i = 0; i < Palette.MaxColors; i++)
        {
            Assert.True(palette.Add((uint)i));
        }

        Assert.False(palette.Add(0xFFFFFFFF));
    }
}
