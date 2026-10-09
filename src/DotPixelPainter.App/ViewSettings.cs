using System.Globalization;

namespace DotPixelPainter;

/// <summary>
/// 表示の設定（透明部分の色やプレビューの高さなど）を %LOCALAPPDATA%\DotPixelPainter\view.txt に「名前=値」で保存する。
/// 初めて使うときに読み込み、変えるたびに書き出す。
/// </summary>
internal static class ViewSettings
{
    private static Dictionary<string, string>? _values;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DotPixelPainter",
        "view.txt");

    public static string? Get(string key) => Values.TryGetValue(key, out string? value) ? value : null;

    public static double? GetDouble(string key) =>
        double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;

    public static void Set(string key, string value)
    {
        Values[key] = value;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllLines(FilePath, Values.Select(kv => $"{kv.Key}={kv.Value}"));
        }
        catch (Exception ex)
        {
            ErrorLog.Write("表示の設定の保存", ex);
        }
    }

    public static void SetDouble(string key, double value) => Set(key, value.ToString("0", CultureInfo.InvariantCulture));

    private static Dictionary<string, string> Values => _values ??= Load();

    private static Dictionary<string, string> Load()
    {
        var values = new Dictionary<string, string>();
        try
        {
            if (File.Exists(FilePath))
            {
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string[] kv = line.Split('=', 2);
                    if (kv.Length == 2)
                    {
                        values[kv[0]] = kv[1];
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("表示の設定の読み込み", ex);
        }

        return values;
    }
}
