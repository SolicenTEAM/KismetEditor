using System;
using System.Linq;

static internal class StringHelper
{
    /// <summary>
    /// Simplified string between quotation marks.
    /// </summary>
    /// <param name="str"></param>
    /// <returns></returns>
    public static string SimplifyQMarksInStr(this string str)
    {
        if (str == null) return null;
        return str.StartsWith("\"") && str.Contains(',') && str.EndsWith("\"") ? str.Trim('\"') : str;
    }

    public static string Escape(this string str)
    {
        return str

            .Replace("\n\n", "\\n\\n")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t")
            .Replace("\\r\\n", "<cf>")
            .Replace("\"", "\"\"");
    }

    public static string Unescape(this string str)
    {
        return str
            .Replace("<cf>", "\\r\\n")
            .Replace("\\n\\n", "\n\n")
            .Replace("\\n", "\n")
            .Replace("\\r", "\r")
            .Replace("\\t", "\t")
            .Replace("\"\"", "\"");
    }

    public static bool IsGUID(this string str)
    {
        if (str.Length >= 32)
        {
            foreach (char c in str)
            {
                if (((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F')))
                    return true;
            }
        }
        return false;
    }

}
