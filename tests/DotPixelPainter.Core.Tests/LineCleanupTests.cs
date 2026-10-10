using DotPixelPainter.Core;

namespace DotPixelPainter.Core.Tests;

public class LineCleanupTests
{
    private const uint K = 0xFF000000;
    private const uint W = 0xFFFFFFFF;

    /// <summary>"#" を線の色、"." を透明（または背景）にした絵を作る。</summary>
    private static PixelImage Draw(uint background, params string[] rows)
    {
        var image = new PixelImage(rows[0].Length, rows.Length);
        for (int y = 0; y < rows.Length; y++)
        {
            for (int x = 0; x < rows[y].Length; x++)
            {
                image.SetPixel(x, y, rows[y][x] == '#' ? K : background);
            }
        }

        return image;
    }

    private static string[] Rows(PixelImage image)
    {
        var rows = new string[image.Height];
        for (int y = 0; y < image.Height; y++)
        {
            rows[y] = string.Concat(Enumerable.Range(0, image.Width).Select(x => image.GetPixel(x, y) == K ? '#' : '.'));
        }

        return rows;
    }

    private static string[] Clean(params string[] rows)
    {
        PixelImage image = Draw(0, rows);
        LineCleanup.Run(image, new PixelRect(0, 0, image.Width, image.Height));
        return Rows(image);
    }

    [Fact]
    public void RemovesLCorner_InDiagonalLine()
    {
        Assert.Equal(
            ["#...", ".#..", "..#.", "...#"],
            Clean(
                "##..",
                ".#..",
                "..##",
                "...#"));
    }

    [Fact]
    public void RemovesOverlapInGentleSlope()
    {
        Assert.Equal(
            ["##....", "..##..", "....##"],
            Clean(
                "###...",
                "..###.",
                "....##"));
    }

    [Fact]
    public void KeepsRectangleCorners()
    {
        string[] box = ["####", "#..#", "#..#", "####"];
        Assert.Equal(box, Clean(box));
    }

    [Fact]
    public void KeepsFilledAreas_AndStraightLines()
    {
        string[] shape = ["###.", "###.", "....", "####"];
        Assert.Equal(shape, Clean(shape));
    }

    [Fact]
    public void FillsRemovedPixelWithSurroundingBackground()
    {
        PixelImage image = Draw(W, "##.", ".##");
        LineCleanup.Run(image, new PixelRect(0, 0, 3, 2));

        Assert.Equal(W, image.GetPixel(1, 0));
        Assert.Equal(K, image.GetPixel(1, 1));
    }

    [Fact]
    public void Document_CleanupLines_IsOneUndoStep()
    {
        var doc = new PixelDocument("t", 3, 2);
        doc.ActiveLayer.Image.SetPixel(0, 0, K);
        doc.ActiveLayer.Image.SetPixel(1, 0, K);
        doc.ActiveLayer.Image.SetPixel(1, 1, K);
        doc.ActiveLayer.Image.SetPixel(2, 1, K);

        Assert.Equal(1, doc.CleanupLines());
        Assert.Equal(0u, doc.ActiveLayer.Image.GetPixel(1, 0));

        doc.History.Undo();
        Assert.Equal(K, doc.ActiveLayer.Image.GetPixel(1, 0));
    }

    [Fact]
    public void PixelPerfectPath_DropsMiddleOfL()
    {
        var path = new PixelPerfectPath();
        Assert.Null(path.Add(0, 0));
        Assert.Null(path.Add(1, 0));
        Assert.Equal((1, 0), path.Add(1, 1));
        Assert.Null(path.Add(2, 1)); // (0,0)→(1,1)→(2,1) は L ではない
        Assert.Equal((2, 1), path.Add(2, 2));
    }

    [Fact]
    public void PixelPerfectPath_IgnoresRepeatsAndStraightLines()
    {
        var path = new PixelPerfectPath();
        path.Add(0, 0);
        Assert.Null(path.Add(0, 0));
        Assert.Null(path.Add(1, 0));
        Assert.Null(path.Add(2, 0));
        Assert.Null(path.Add(3, 0));
    }

    [Fact]
    public void Stroke_Restore_PutsBackOriginalColor()
    {
        var doc = new PixelDocument("t", 2, 1);
        doc.ActiveLayer.Image.SetPixel(1, 0, W);
        PixelStroke stroke = doc.BeginStroke();
        stroke.Plot(1, 0, K);
        Assert.True(stroke.Restore(1, 0));
        stroke.Commit();

        Assert.Equal(W, doc.ActiveLayer.Image.GetPixel(1, 0));
        Assert.False(doc.History.CanUndo);
    }
}
