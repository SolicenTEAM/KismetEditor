using System.IO;
using System.Reflection;
internal static class EnvironmentHelper
{
    public static string CurrentAssemblyDirectory => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
}