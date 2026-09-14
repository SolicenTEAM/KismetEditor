using System.IO;
using System.Text.RegularExpressions;

public static class UnrealPath
{
    public static string VirtualFolderWithoutFileName(this string unrealFile)
    {
        var match = new Regex(@"([^\/]+)\/Content\/.+").Match(unrealFile).Value.TrimEnd('\"');
        return string.IsNullOrWhiteSpace(match) ? unrealFile : match.Replace(Path.GetFileName(unrealFile), "");
    }
    public static string VirtualFolderWithFileName(this string unrealFile)
    {
        var match = new Regex(@"([^\/]+)\/Content\/.+").Match(unrealFile).Value.TrimEnd('\"');
        return string.IsNullOrWhiteSpace(match) ? unrealFile : match;
    }
    public static string FolderWithFileName(this string unrealFile)
    {
        var match = new Regex(@"\\([^\\]+)\\Content(?!.*Paks)\\.+").Match(unrealFile).Value.TrimEnd('\"');
        return string.IsNullOrWhiteSpace(match) ? unrealFile : match;
    }
    public static string FolderWithoutFileName(this string unrealFile)
    {
        var match = new Regex(@"\\([^\\]+)\\Content(?!.*Paks)\\.+").Match(unrealFile).Value.TrimEnd('\"');
        return string.IsNullOrWhiteSpace(match) ? unrealFile : match.Replace(Path.GetFileName(unrealFile), "");
    }
}
