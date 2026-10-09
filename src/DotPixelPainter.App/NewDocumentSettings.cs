using System.Globalization;

namespace DotPixelPainter;

/// <summary>
/// 「新規作成」で前回選んだサイズと背景。次回の新規作成とタブの「＋」で使う。
/// %LOCALAPPDATA%\DotPixelPainter\new-document.txt に「キー=値」で保存する。
/// </summary>
public sealed record NewDocumentSettings(int Width, int Height, bool WhiteBackground)
{
    public static readonly NewDocumentSettings Default = new(64, 64, false);

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DotPixelPainter",
        "new-document.txt");

    public static NewDocumentSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return Default;
            }

            int width = Default.Width;
            int height = Default.Height;
            bool white = Default.WhiteBackground;
            foreach (string line in File.ReadAllLines(FilePath))
            {
                string[] kv = line.Split('=', 2);
                if (kv.Length != 2)
                {
                    continue;
                }

                switch (kv[0].Trim())
                {
                    case "width" when int.TryParse(kv[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int w):
                        width = w;
                        break;
                    case "height" when int.TryParse(kv[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h):
                        height = h;
                        break;
                    case "background":
                        white = kv[1].Trim() == "white";
                        break;
                }
            }

            return IsValidSize(width) && IsValidSize(height) ? new(width, height, white) : Default;
        }
        catch (Exception)
        {
            return Default;
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, string.Create(
                CultureInfo.InvariantCulture,
                $"width={Width}\nheight={Height}\nbackground={(WhiteBackground ? "white" : "transparent")}\n"));
        }
        catch (Exception)
        {
            // 保存できなくても新規作成はできる
        }
    }

    public static bool IsValidSize(int size) => size >= 1 && size <= Core.PixelImage.MaxSize;
}
