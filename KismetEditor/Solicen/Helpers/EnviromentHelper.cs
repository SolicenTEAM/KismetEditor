using System;
using System.IO;
using System.Reflection;
internal static class EnvironmentHelper
{
    public static string AssemblyDirectory => Path.GetDirectoryName(AppContext.BaseDirectory);
}