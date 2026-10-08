namespace DotPixelPainter.Core;

public sealed class Layer
{
    public Layer(string name, int width, int height)
    {
        Name = name;
        Image = new PixelImage(width, height);
    }

    public string Name { get; set; }

    public PixelImage Image { get; }

    public bool Visible { get; set; } = true;

    /// <summary>0.0〜1.0</summary>
    public double Opacity { get; set; } = 1.0;
}
