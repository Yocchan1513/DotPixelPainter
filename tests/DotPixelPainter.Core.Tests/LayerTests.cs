using DotPixelPainter.Core;

namespace DotPixelPainter.Core.Tests;

public class LayerTests
{
    private static string Names(PixelDocument doc) => string.Join(",", doc.Layers.Select(l => l.Name));

    [Fact]
    public void AddLayer_GoesAboveActive_AndIsSelected_AndUndoes()
    {
        var doc = new PixelDocument("t", 4, 4);
        doc.AddLayer();
        doc.SelectLayer(0);
        doc.AddLayer();

        Assert.Equal("レイヤー 1,レイヤー 3,レイヤー 2", Names(doc));
        Assert.Equal(1, doc.ActiveLayerIndex);

        doc.History.Undo();
        Assert.Equal("レイヤー 1,レイヤー 2", Names(doc));
        Assert.Equal(0, doc.ActiveLayerIndex); // 追加前に選んでいたレイヤーに戻る

        doc.History.Redo();
        Assert.Equal("レイヤー 1,レイヤー 3,レイヤー 2", Names(doc));
    }

    [Fact]
    public void DeleteLayer_KeepsAtLeastOne_AndUndoes()
    {
        var doc = new PixelDocument("t", 4, 4);
        Assert.False(doc.DeleteLayer(0));

        Layer top = doc.AddLayer("上");
        top.Image.SetPixel(1, 1, 0xFF00FF00);
        Assert.True(doc.DeleteLayer(1));
        Assert.Single(doc.Layers);

        doc.History.Undo();
        Assert.Same(top, doc.Layers[1]);
        Assert.Equal(0xFF00FF00u, doc.Layers[1].Image.GetPixel(1, 1));
        Assert.Equal(1, doc.ActiveLayerIndex);
    }

    [Fact]
    public void DuplicateLayer_CopiesPixelsAndSettings()
    {
        var doc = new PixelDocument("t", 4, 4);
        doc.ActiveLayer.Image.SetPixel(0, 0, 0xFFABCDEF);
        doc.SetLayerOpacity(0, 0.5);

        Layer copy = doc.DuplicateLayer(0);
        copy.Image.SetPixel(0, 0, 0xFF000000); // 複製をいじっても元は変わらない

        Assert.Equal("レイヤー 1 のコピー", copy.Name);
        Assert.Equal(0.5, copy.Opacity);
        Assert.Equal(0xFFABCDEFu, doc.Layers[0].Image.GetPixel(0, 0));
    }

    [Fact]
    public void MoveLayer_ReordersAndUndoes()
    {
        var doc = new PixelDocument("t", 4, 4);
        doc.AddLayer("B");
        doc.AddLayer("C");

        Assert.True(doc.MoveLayer(2, 0));
        Assert.Equal("C,レイヤー 1,B", Names(doc));
        Assert.Equal(0, doc.ActiveLayerIndex);
        Assert.False(doc.MoveLayer(0, 0));

        doc.History.Undo();
        Assert.Equal("レイヤー 1,B,C", Names(doc));
        Assert.Equal(2, doc.ActiveLayerIndex);
    }

    [Fact]
    public void MergeDown_BlendsWithOpacity_AndUndoes()
    {
        var doc = new PixelDocument("t", 1, 1);
        doc.ActiveLayer.Image.SetPixel(0, 0, 0xFF000000);
        doc.AddLayer("上");
        doc.ActiveLayer.Image.SetPixel(0, 0, 0xFFFFFFFF);
        doc.SetLayerOpacity(1, 0.5);
        uint shown = doc.Composite().GetPixel(0, 0);

        Assert.True(doc.MergeDown(1));
        Assert.Single(doc.Layers);
        Assert.Equal(shown, doc.Layers[0].Image.GetPixel(0, 0)); // 見た目は変わらない

        doc.History.Undo();
        Assert.Equal(2, doc.Layers.Count);
        Assert.Equal(0xFF000000u, doc.Layers[0].Image.GetPixel(0, 0));
        Assert.False(doc.MergeDown(0)); // いちばん下は結合できない
    }

    [Fact]
    public void MergeDown_HiddenUpperLayer_LeavesLowerUnchanged()
    {
        var doc = new PixelDocument("t", 1, 1);
        doc.AddLayer("上");
        doc.ActiveLayer.Image.SetPixel(0, 0, 0xFFFFFFFF);
        doc.SetLayerVisible(1, false);

        doc.MergeDown(1);

        Assert.Equal(0u, doc.Layers[0].Image.GetPixel(0, 0));
    }

    [Fact]
    public void Properties_AreUndoable_AndMarkDirty()
    {
        var doc = new PixelDocument("t", 2, 2);
        doc.SetLayerVisible(0, false);
        doc.RenameLayer(0, "  背景  ");

        Assert.True(doc.IsDirty);
        Assert.Equal("背景", doc.Layers[0].Name);

        doc.History.Undo();
        Assert.Equal("レイヤー 1", doc.Layers[0].Name);
        doc.History.Undo();
        Assert.True(doc.Layers[0].Visible);
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void Opacity_ConsecutiveChangesBecomeOneStep()
    {
        var doc = new PixelDocument("t", 2, 2);
        for (int i = 9; i >= 2; i--)
        {
            doc.SetLayerOpacity(0, i / 10.0); // スライダーを動かし続けた想定
        }

        Assert.Equal(1, doc.History.UndoCount);
        doc.History.Undo();
        Assert.Equal(1.0, doc.Layers[0].Opacity);
    }

    [Fact]
    public void Opacity_DoesNotMergeAcrossSave()
    {
        var doc = new PixelDocument("t", 2, 2);
        doc.SetLayerOpacity(0, 0.5);
        doc.MarkSaved();
        doc.SetLayerOpacity(0, 0.3);

        Assert.Equal(2, doc.History.UndoCount);
        doc.History.Undo();
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void RenameLayer_IgnoresBlank()
    {
        var doc = new PixelDocument("t", 2, 2);
        doc.RenameLayer(0, "   ");

        Assert.Equal(0, doc.History.UndoCount);
    }
}
