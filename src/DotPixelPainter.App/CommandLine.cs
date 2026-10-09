using System.Text;

namespace DotPixelPainter;

/// <summary>コマンドラインの文字列から、開くファイルを取り出す。</summary>
internal static class CommandLine
{
    /// <summary>
    /// "--" で始まる指定と、先頭の exe 自体を除いたものをファイルとして返す。
    /// 空白を含むパスは "..." で囲まれている前提。
    /// </summary>
    public static List<string> FilesIn(string commandLine)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;
        foreach (char ch in commandLine)
        {
            if (ch == '"')
            {
                quoted = !quoted;
            }
            else if (char.IsWhiteSpace(ch) && !quoted)
            {
                Flush();
            }
            else
            {
                current.Append(ch);
            }
        }

        Flush();

        if (tokens.Count > 0 && tokens[0].EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            tokens.RemoveAt(0);
        }

        return tokens.Where(t => !t.StartsWith("--", StringComparison.Ordinal)).ToList();

        void Flush()
        {
            if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
        }
    }
}
