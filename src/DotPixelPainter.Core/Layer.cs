namespace DotPixelPainter.Core;

public sealed class Layer
{
    public Layer(string name, int width, int height)
        : this(name, new PixelImage(width, height))
    {
    }

    internal Layer(string name, PixelImage image)
    {
        Name = name;
        Image = image;
    }

    /// <summary>
    /// 名前・表示・不透明度は、元に戻せるよう PixelDocument の RenameLayer / SetLayerVisible / SetLayerOpacity で変える。
    /// </summary>
    public string Name { get; internal set; }

    /// <summary>画像の大きさを変える操作（PixelDocument.TransformImage）のときだけ差し替わる。</summary>
    public PixelImage Image { get; internal set; }

    public bool Visible { get; internal set; } = true;

    /// <summary>0.0〜1.0</summary>
    public double Opacity { get; internal set; } = 1.0;
}
