using System.Globalization;
using System.Text;

namespace NetOpsTerminal.App.Services;

internal static class TerminalPastePreviewFormatter
{
    public static string Format(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var preview = new StringBuilder(text.Length);
        foreach (char character in text)
        {
            if (character is '\r' or '\n' or '\t'
                || (!char.IsControl(character) && character != '\u007F'))
            {
                preview.Append(character);
            }
            else
            {
                preview.Append("<0x");
                preview.Append(((int)character).ToString("X2", CultureInfo.InvariantCulture));
                preview.Append('>');
            }
        }

        return preview.ToString();
    }
}
