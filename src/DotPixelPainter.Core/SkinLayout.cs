using System.Numerics;

namespace DotPixelPainter.Core;

public enum SkinPart
{
    Head,
    Body,
    RightArm,
    LeftArm,
    RightLeg,
    LeftLeg,
}

/// <summary>箱の面。右・左はキャラクターから見た向き（正面から見ると右腕は左側）。</summary>
public enum SkinFace
{
    Top,
    Bottom,
    Right,
    Front,
    Left,
    Back,
}

/// <summary>
/// スキンの箱1つ。U, V は展開図の左上、W・H・D は幅・高さ・奥行き（画素）。
/// X, Y, Z は立体での最小の角（単位は画素。足元の中心が原点、Y が上、Z が正面向き）。
/// Mirror は旧形式（64×32）の左腕・左脚で、右側の絵を左右反転して使う箱。
/// </summary>
public readonly record struct SkinBox(SkinPart Part, bool Outer, int U, int V, int W, int H, int D, float X, float Y, float Z, bool Mirror);

/// <summary>立体の面1枚: 展開図の範囲と、立体での左上の角・横方向・縦方向のベクトル。</summary>
public readonly record struct SkinFaceQuad(SkinBox Box, SkinFace Face, PixelRect Source, Vector3 Origin, Vector3 Across, Vector3 Down)
{
    /// <summary>面の外向きの向き（裏から見えているかの判定と、明るさに使う）。</summary>
    public Vector3 Normal => Face switch
    {
        SkinFace.Front => Vector3.UnitZ,
        SkinFace.Back => -Vector3.UnitZ,
        SkinFace.Right => -Vector3.UnitX,
        SkinFace.Left => Vector3.UnitX,
        SkinFace.Top => Vector3.UnitY,
        _ => -Vector3.UnitY,
    };
}

/// <summary>
/// Minecraft のスキン（64×64 の今の形式と、64×32 の旧形式）の展開図と立体の形。
/// 腕はクラシック（4px）とスリム（3px）の2種類。
/// </summary>
public static class SkinLayout
{
    /// <summary>外側のレイヤー（帽子・上着・袖・ズボン）を、元の箱より各面 0.5px 外に出す量。</summary>
    public const float OuterInflate = 0.5f;

    public static bool IsSkinSize(int width, int height) => width == 64 && (height == 64 || height == 32);

    public static string PartName(SkinPart part) => part switch
    {
        SkinPart.Head => "頭",
        SkinPart.Body => "体",
        SkinPart.RightArm => "右腕",
        SkinPart.LeftArm => "左腕",
        SkinPart.RightLeg => "右脚",
        _ => "左脚",
    };

    public static string FaceName(SkinFace face) => face switch
    {
        SkinFace.Top => "上",
        SkinFace.Bottom => "下",
        SkinFace.Right => "右",
        SkinFace.Front => "前",
        SkinFace.Left => "左",
        _ => "後",
    };

    /// <summary>すべての箱。legacy は 64×32 の旧形式（外側は頭だけ、左腕・左脚は右側の反転）。</summary>
    public static IReadOnlyList<SkinBox> Boxes(bool slim, bool legacy)
    {
        int arm = slim ? 3 : 4;
        var boxes = new List<SkinBox>
        {
            new(SkinPart.Head, false, 0, 0, 8, 8, 8, -4, 24, -4, false),
            new(SkinPart.Head, true, 32, 0, 8, 8, 8, -4, 24, -4, false),
            new(SkinPart.Body, false, 16, 16, 8, 12, 4, -4, 12, -2, false),
            new(SkinPart.RightArm, false, 40, 16, arm, 12, 4, -4 - arm, 12, -2, false),
            new(SkinPart.RightLeg, false, 0, 16, 4, 12, 4, -4, 0, -2, false),
        };

        if (legacy)
        {
            boxes.Add(new(SkinPart.LeftArm, false, 40, 16, arm, 12, 4, 4, 12, -2, true));
            boxes.Add(new(SkinPart.LeftLeg, false, 0, 16, 4, 12, 4, 0, 0, -2, true));
            return boxes;
        }

        boxes.Add(new(SkinPart.LeftArm, false, 32, 48, arm, 12, 4, 4, 12, -2, false));
        boxes.Add(new(SkinPart.LeftLeg, false, 16, 48, 4, 12, 4, 0, 0, -2, false));
        boxes.Add(new(SkinPart.Body, true, 16, 32, 8, 12, 4, -4, 12, -2, false));
        boxes.Add(new(SkinPart.RightArm, true, 40, 32, arm, 12, 4, -4 - arm, 12, -2, false));
        boxes.Add(new(SkinPart.LeftArm, true, 48, 48, arm, 12, 4, 4, 12, -2, false));
        boxes.Add(new(SkinPart.RightLeg, true, 0, 32, 4, 12, 4, -4, 0, -2, false));
        boxes.Add(new(SkinPart.LeftLeg, true, 0, 48, 4, 12, 4, 0, 0, -2, false));
        return boxes;
    }

    /// <summary>箱の面が、展開図のどこにあるか。</summary>
    public static PixelRect FaceRect(SkinBox b, SkinFace face) => face switch
    {
        SkinFace.Top => new PixelRect(b.U + b.D, b.V, b.W, b.D),
        SkinFace.Bottom => new PixelRect(b.U + b.D + b.W, b.V, b.W, b.D),
        SkinFace.Right => new PixelRect(b.U, b.V + b.D, b.D, b.H),
        SkinFace.Front => new PixelRect(b.U + b.D, b.V + b.D, b.W, b.H),
        SkinFace.Left => new PixelRect(b.U + b.D + b.W, b.V + b.D, b.D, b.H),
        _ => new PixelRect(b.U + b.D * 2 + b.W, b.V + b.D, b.W, b.H),
    };

    /// <summary>展開図の (x, y) が、どの部位のどの面か。どこにも当たらなければ null。</summary>
    public static (SkinBox Box, SkinFace Face)? HitTest(int x, int y, bool slim, bool legacy)
    {
        foreach (SkinBox box in Boxes(slim, legacy))
        {
            if (box.Mirror)
            {
                continue; // 反転して使う箱は、展開図では右側と同じ場所
            }

            foreach (SkinFace face in Enum.GetValues<SkinFace>())
            {
                if (FaceRect(box, face).Contains(x, y))
                {
                    return (box, face);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 腕がスリム（3px）の絵かを推測する。スリムでは右腕の後ろ側の右端2列（x=54〜55, y=20〜31）が使われず透明になる。
    /// </summary>
    public static bool GuessSlim(PixelImage image)
    {
        if (!IsSkinSize(image.Width, image.Height))
        {
            return false;
        }

        for (int y = 20; y < 32; y++)
        {
            for (int x = 54; x < 56; x++)
            {
                if (image.GetPixel(x, y) >> 24 != 0)
                {
                    return false;
                }
            }
        }

        // 右腕そのものに色があるときだけスリムと判断する（真っ白な新規画像はクラシック扱い）
        for (int y = 20; y < 32; y++)
        {
            for (int x = 44; x < 47; x++)
            {
                if (image.GetPixel(x, y) >> 24 != 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>立体の面をすべて返す。外側の箱は少し大きくする。</summary>
    public static IEnumerable<SkinFaceQuad> Quads(bool slim, bool legacy, bool includeOuter)
    {
        foreach (SkinBox box in Boxes(slim, legacy))
        {
            if (box.Outer && !includeOuter)
            {
                continue;
            }

            float grow = box.Outer ? OuterInflate : 0;
            float x0 = box.X - grow;
            float y0 = box.Y - grow;
            float z0 = box.Z - grow;
            float w = box.W + grow * 2;
            float h = box.H + grow * 2;
            float d = box.D + grow * 2;
            float x1 = x0 + w;
            float y1 = y0 + h;
            float z1 = z0 + d;

            // 展開図での向き: 上→下が立体の上→下、各面の左→右は、その面を外から見たときの左→右
            var quads = new (SkinFace Face, Vector3 Origin, Vector3 Across, Vector3 Down)[]
            {
                (SkinFace.Front, new(x0, y1, z1), new(w, 0, 0), new(0, -h, 0)),
                (SkinFace.Back, new(x1, y1, z0), new(-w, 0, 0), new(0, -h, 0)),
                (SkinFace.Right, new(x0, y1, z0), new(0, 0, d), new(0, -h, 0)),
                (SkinFace.Left, new(x1, y1, z1), new(0, 0, -d), new(0, -h, 0)),
                (SkinFace.Top, new(x0, y1, z0), new(w, 0, 0), new(0, 0, d)),
                (SkinFace.Bottom, new(x0, y0, z1), new(w, 0, 0), new(0, 0, -d)),
            };

            foreach (var (face, origin, across, down) in quads)
            {
                if (!box.Mirror)
                {
                    yield return new SkinFaceQuad(box, face, FaceRect(box, face), origin, across, down);
                    continue;
                }

                // 旧形式の左腕・左脚: 右側の絵を左右反転して貼る。
                // 反転すると左右の面が入れ替わるので、右の面の絵を左の面に、左の面の絵を右の面に使う
                SkinFace source = face switch
                {
                    SkinFace.Right => SkinFace.Left,
                    SkinFace.Left => SkinFace.Right,
                    _ => face,
                };
                yield return new SkinFaceQuad(box, face, FaceRect(box, source), origin + across, -across, down);
            }
        }
    }
}
