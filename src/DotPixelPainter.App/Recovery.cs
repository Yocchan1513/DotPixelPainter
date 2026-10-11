using System.Globalization;
using DotPixelPainter.Core;

namespace DotPixelPainter;

/// <summary>
/// 作業の控え（自動バックアップ）と、前回が正しく終了したかの記録。
/// %LOCALAPPDATA%\DotPixelPainter\recovery\ に、保存していないタブを {番号}.dotpix と {番号}.txt（元の名前と場所）で置く。
/// 起動中は running という印のファイルを置き、正しく終了したら消す。次の起動で印が残っていれば、前回は途中で落ちたと分かる。
/// </summary>
internal static class Recovery
{
    private static string Root => AppData.Folder;

    private static string Folder => Path.Combine(Root, "recovery");

    private static string MarkerPath => Path.Combine(Root, "running");

    /// <summary>控えから復元できる絵。</summary>
    public sealed record Backup(string Id, string Name, string? OriginalPath, string DataPath);

    /// <summary>起動の印を置く。前回の印が残っていたら（正しく終了しなかったら）true。</summary>
    public static bool BeginSession()
    {
        try
        {
            bool crashed = File.Exists(MarkerPath);
            Directory.CreateDirectory(Root);
            File.WriteAllText(MarkerPath, DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
            return crashed;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("起動の印", ex);
            return false;
        }
    }

    /// <summary>正しく終了するとき。印と控えを消す（保存しなかった絵は、終了するときに確かめてある）。</summary>
    public static void EndSession()
    {
        try
        {
            File.Delete(MarkerPath);
            if (Directory.Exists(Folder))
            {
                Directory.Delete(Folder, recursive: true);
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("終了の後片づけ", ex);
        }
    }

    /// <summary>控えを書く（.dotpix の中身はすでに作ってあるものを渡す）。</summary>
    public static void Write(string id, string name, string? originalPath, byte[] dotpix)
    {
        Directory.CreateDirectory(Folder);
        string data = Path.Combine(Folder, id + DotPixFile.Extension);
        string temp = data + ".saving";
        File.WriteAllBytes(temp, dotpix);
        File.Move(temp, data, overwrite: true);
        File.WriteAllLines(Path.Combine(Folder, id + ".txt"), [name, originalPath ?? ""]);
    }

    public static void Delete(string id)
    {
        try
        {
            File.Delete(Path.Combine(Folder, id + DotPixFile.Extension));
            File.Delete(Path.Combine(Folder, id + ".txt"));
        }
        catch (Exception ex)
        {
            ErrorLog.Write("控えを消す", ex);
        }
    }

    /// <summary>残っている控え（前回の起動のもの）。</summary>
    public static List<Backup> List()
    {
        var backups = new List<Backup>();
        try
        {
            if (!Directory.Exists(Folder))
            {
                return backups;
            }

            foreach (string data in Directory.GetFiles(Folder, "*" + DotPixFile.Extension))
            {
                string id = Path.GetFileNameWithoutExtension(data);
                string info = Path.Combine(Folder, id + ".txt");
                string[] lines = File.Exists(info) ? File.ReadAllLines(info) : [];
                string name = lines.Length > 0 && lines[0].Length > 0 ? lines[0] : id;
                string? original = lines.Length > 1 && lines[1].Length > 0 ? lines[1] : null;
                backups.Add(new Backup(id, name, original, data));
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("控えの一覧", ex);
        }

        return backups;
    }

    public static void DeleteAll()
    {
        foreach (Backup backup in List())
        {
            Delete(backup.Id);
        }
    }
}
