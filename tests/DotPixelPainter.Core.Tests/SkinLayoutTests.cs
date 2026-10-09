using System.Numerics;
using DotPixelPainter.Core;

namespace DotPixelPainter.Core.Tests;

public class SkinLayoutTests
{
    [Theory]
    [InlineData(SkinPart.Head, false, SkinFace.Front, 8, 8, 8, 8)]
    [InlineData(SkinPart.Head, true, SkinFace.Front, 40, 8, 8, 8)]   // 帽子
    [InlineData(SkinPart.Body, false, SkinFace.Front, 20, 20, 8, 12)]
    [InlineData(SkinPart.RightArm, false, SkinFace.Front, 44, 20, 4, 12)]
    [InlineData(SkinPart.LeftLeg, false, SkinFace.Front, 20, 52, 4, 12)]
    [InlineData(SkinPart.Head, false, SkinFace.Top, 8, 0, 8, 8)]
    [InlineData(SkinPart.Head, false, SkinFace.Back, 24, 8, 8, 8)]
    public void FaceRect_MatchesStandardLayout(SkinPart part, bool outer, SkinFace face, int x, int y, int w, int h)
    {
        SkinBox box = SkinLayout.Boxes(slim: false, legacy: false).Single(b => b.Part == part && b.Outer == outer);

        Assert.Equal(new PixelRect(x, y, w, h), SkinLayout.FaceRect(box, face));
    }

    [Fact]
    public void SlimArm_IsThreePixelsWide()
    {
        SkinBox arm = SkinLayout.Boxes(slim: true, legacy: false).Single(b => b.Part == SkinPart.RightArm && !b.Outer);

        Assert.Equal(new PixelRect(44, 20, 3, 12), SkinLayout.FaceRect(arm, SkinFace.Front));
        Assert.Equal(new PixelRect(51, 20, 3, 12), SkinLayout.FaceRect(arm, SkinFace.Back));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Faces_StayInsideImage_AndDoNotOverlap(bool slim, bool legacy)
    {
        int height = legacy ? 32 : 64;
        var used = new HashSet<(int, int)>();
        foreach (SkinBox box in SkinLayout.Boxes(slim, legacy).Where(b => !b.Mirror))
        {
            foreach (SkinFace face in Enum.GetValues<SkinFace>())
            {
                PixelRect r = SkinLayout.FaceRect(box, face);
                for (int y = r.Y; y < r.Bottom; y++)
                {
                    for (int x = r.X; x < r.Right; x++)
                    {
                        Assert.InRange(x, 0, 63);
                        Assert.InRange(y, 0, height - 1);
                        Assert.True(used.Add((x, y)), $"{box.Part} {face} が ({x},{y}) で重なっています");
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(10, 10, SkinPart.Head, SkinFace.Front, false)]
    [InlineData(44, 12, SkinPart.Head, SkinFace.Front, true)]
    [InlineData(5, 25, SkinPart.RightLeg, SkinFace.Front, false)]
    [InlineData(37, 55, SkinPart.LeftArm, SkinFace.Front, false)]
    public void HitTest_FindsPartAndFace(int x, int y, SkinPart part, SkinFace face, bool outer)
    {
        var hit = SkinLayout.HitTest(x, y, slim: false, legacy: false);

        Assert.NotNull(hit);
        Assert.Equal(part, hit.Value.Box.Part);
        Assert.Equal(face, hit.Value.Face);
        Assert.Equal(outer, hit.Value.Box.Outer);
    }

    [Fact]
    public void HitTest_EmptyAreaIsNull()
    {
        Assert.Null(SkinLayout.HitTest(0, 0, slim: false, legacy: false)); // 頭の展開図の左上は使われない
    }

    [Fact]
    public void GuessSlim_LooksAtUnusedArmColumns()
    {
        var classic = new PixelImage(64, 64);
        var slim = new PixelImage(64, 64);
        for (int y = 20; y < 32; y++)
        {
            for (int x = 40; x < 56; x++)
            {
                classic.SetPixel(x, y, 0xFF336699);
            }

            for (int x = 40; x < 54; x++)
            {
                slim.SetPixel(x, y, 0xFF336699);
            }
        }

        Assert.False(SkinLayout.GuessSlim(classic));
        Assert.True(SkinLayout.GuessSlim(slim));
        Assert.False(SkinLayout.GuessSlim(new PixelImage(64, 64))); // 真っ白な新規はクラシック
    }

    [Fact]
    public void Quads_FrontFaceOfHead_SitsOnTopOfBody_AndFacesForward()
    {
        SkinFaceQuad front = SkinLayout.Quads(slim: false, legacy: false, includeOuter: false)
            .Single(q => q.Box.Part == SkinPart.Head && q.Face == SkinFace.Front);

        Assert.Equal(new Vector3(-4, 32, 4), front.Origin);       // 左上の角
        Assert.Equal(new Vector3(8, 0, 0), front.Across);          // 右へ
        Assert.Equal(new Vector3(0, -8, 0), front.Down);           // 下へ
        Assert.Equal(Vector3.UnitZ, front.Normal);
    }

    [Fact]
    public void Quads_AdjacentFacesShareEdges()
    {
        var quads = SkinLayout.Quads(slim: false, legacy: false, includeOuter: false)
            .Where(q => q.Box.Part == SkinPart.Body).ToDictionary(q => q.Face);

        // 正面の左端（キャラクターの右側）は、右の面の右端とつながる
        Assert.Equal(quads[SkinFace.Front].Origin, quads[SkinFace.Right].Origin + quads[SkinFace.Right].Across);
        // 上の面の手前の辺は、正面の上の辺とつながる
        Assert.Equal(quads[SkinFace.Front].Origin, quads[SkinFace.Top].Origin + quads[SkinFace.Top].Down);
    }

    [Fact]
    public void Quads_OuterLayerIsSlightlyBigger()
    {
        var all = SkinLayout.Quads(slim: false, legacy: false, includeOuter: true).ToList();
        SkinFaceQuad hat = all.Single(q => q.Box.Part == SkinPart.Head && q.Box.Outer && q.Face == SkinFace.Front);

        Assert.Equal(9f, hat.Across.X);
        Assert.Equal(4.5f, hat.Origin.Z);
        Assert.Equal(6 * 12, all.Count); // 箱12個 × 6面
    }

    [Fact]
    public void Legacy_LeftLimbsMirrorRightLimbTexture()
    {
        var quads = SkinLayout.Quads(slim: false, legacy: true, includeOuter: true).ToList();
        SkinFaceQuad leftArmFront = quads.Single(q => q.Box.Part == SkinPart.LeftArm && q.Face == SkinFace.Front);

        Assert.Equal(new PixelRect(44, 20, 4, 12), leftArmFront.Source); // 右腕の正面の絵
        Assert.Equal(-4f, leftArmFront.Across.X);                        // 左右反転して貼る
        Assert.Equal(6 * 7, quads.Count);                                // 旧形式の外側は帽子だけ
    }
}
