using System.Text;

namespace NetOpsTerminal.Terminal.RoyalTerminal;

/// <summary>Removes only ASCII space padding at line ends in text copied from this terminal.</summary>
internal static class TerminalCopyTextFormatter
{
    internal static string TrimLineEnds(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = new StringBuilder(text.Length);
        int start = 0;
        for (int index = 0; index <= text.Length; index++)
        {
            if (index < text.Length && text[index] is not ('\r' or '\n'))
                continue;

            int end = index;
            while (end > start && text[end - 1] == ' ')
                end--;
            result.Append(text.AsSpan(start, end - start));
            if (index < text.Length)
                result.Append(text[index]);
            start = index + 1;
        }

        return result.Length == text.Length ? text : result.ToString();
    }
}