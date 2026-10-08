using DotPixelPainter.Core;

namespace DotPixelPainter.Core.Tests;

public class UndoHistoryTests
{
    private static void Draw(PixelDocument doc, uint color, params (int X, int Y)[] points)
    {
        PixelStroke stroke = doc.BeginStroke();
        foreach ((int x, int y) in points)
        {
            stroke.Plot(x, y, color);
        }

        stroke.Commit();
    }

    [Fact]
    public void Undo_RestoresWholeStroke_AndRedoReappliesIt()
    {
        var doc = new PixelDocument("t", 4, 4);
        Draw(doc, 0xFFFF0000, (0, 0), (1, 0), (2, 0));

        Assert.True(doc.History.Undo());
        Assert.Equal(0u, doc.ActiveLayer.Image.GetPixel(1, 0));

        Assert.True(doc.History.Redo());
        Assert.Equal(0xFFFF0000u, doc.ActiveLayer.Image.GetPixel(2, 0));
    }

    [Fact]
    public void Stroke_OverSamePixelTwice_RestoresOriginalColor()
    {
        var doc = new PixelDocument("t", 2, 2);
        doc.ActiveLayer.Image.SetPixel(0, 0, 0xFF00FF00);
        PixelStroke stroke = doc.BeginStroke();
        stroke.Plot(0, 0, 0xFFFF0000);
        stroke.Plot(0, 0, 0xFF0000FF);
        stroke.Commit();

        doc.History.Undo();

        Assert.Equal(0xFF00FF00u, doc.ActiveLayer.Image.GetPixel(0, 0));
    }

    [Fact]
    public void Stroke_WithNoRealChange_AddsNothing()
    {
        var doc = new PixelDocument("t", 2, 2);
        Draw(doc, 0u, (0, 0)); // 透明の上を透明で塗る

        Assert.False(doc.History.CanUndo);
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void NewStroke_DiscardsRedo()
    {
        var doc = new PixelDocument("t", 2, 2);
        Draw(doc, 0xFFFF0000, (0, 0));
        doc.History.Undo();
        Draw(doc, 0xFF0000FF, (1, 1));

        Assert.False(doc.History.CanRedo);
        Assert.Equal(1, doc.History.UndoCount);
    }

    [Fact]
    public void IsDirty_FollowsSavedPosition()
    {
        var doc = new PixelDocument("t", 2, 2);
        Draw(doc, 0xFFFF0000, (0, 0));
        Assert.True(doc.IsDirty);

        doc.MarkSaved();
        Assert.False(doc.IsDirty);

        Draw(doc, 0xFF0000FF, (1, 0));
        Assert.True(doc.IsDirty);

        doc.History.Undo();
        Assert.False(doc.IsDirty); // 保存した状態に戻った
    }

    [Fact]
    public void IsDirty_StaysTrue_WhenSavedStateIsNoLongerReachable()
    {
        var doc = new PixelDocument("t", 2, 2);
        Draw(doc, 0xFFFF0000, (0, 0));
        doc.MarkSaved();
        doc.History.Undo();
        Draw(doc, 0xFF0000FF, (1, 1)); // 保存時の操作をやり直せなくなる

        doc.History.Undo();

        Assert.True(doc.IsDirty);
    }

    [Fact]
    public void MemoryLimit_DropsOldestEdits()
    {
        // 1画素の操作は約76バイトなので、上限200バイトなら2件まで残る
        var doc = new PixelDocument("t", 8, 8, undoMemoryLimit: 200);
        for (int i = 0; i < 5; i++)
        {
            Draw(doc, 0xFFFF0000, (i, 0));
        }

        Assert.Equal(2, doc.History.UndoCount);
        doc.History.Undo();
        doc.History.Undo();
        Assert.False(doc.History.CanUndo);
        Assert.Equal(0xFFFF0000u, doc.ActiveLayer.Image.GetPixel(2, 0)); // 捨てた操作は戻らない
        Assert.Equal(0u, doc.ActiveLayer.Image.GetPixel(3, 0));
        Assert.True(doc.IsDirty);
    }

    [Fact]
    public void Undo_StillTargetsRightLayer_AfterLayerIsInsertedBelow()
    {
        var doc = new PixelDocument("t", 2, 2);
        Draw(doc, 0xFFFF0000, (0, 0));
        Layer drawn = doc.ActiveLayer;
        doc.AddLayer("上");

        doc.History.Undo();

        Assert.Equal(0u, drawn.Image.GetPixel(0, 0));
    }
}
