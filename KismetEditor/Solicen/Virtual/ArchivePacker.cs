using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UAssetAPI.UnrealTypes;

namespace Solicen.UE4
{
    internal static class ArchivePacker
    {
        public static string ValidateRequest(string output, EngineVersion version)
        {
            string extension = Path.GetExtension(output).ToLowerInvariant();
            if (extension != ".pak" && extension != ".utoc")
                throw new ArgumentException("--pack output must end in .pak or .utoc.");
            var match = Regex.Match(version.ToString(), @"\AVER_UE([45])_([0-9]+)\z");
            if (!match.Success) throw new ArgumentException("Packing requires a concrete --version, e.g. 4.27.");
            int major = int.Parse(match.Groups[1].Value), minor = int.Parse(match.Groups[2].Value);
            if ((major == 4 && minor > 27) || (major == 5 && minor > 7) || (extension == ".utoc" && major == 4 && minor < 25))
                throw new ArgumentException("Unsupported packing version. IoStore supports UE4.25 through UE5.7.");
            output = Path.ChangeExtension(Path.GetFullPath(output), extension);
            foreach (string path in OutputPaths(output))
                if (File.Exists(path) || Directory.Exists(path))
                    throw new IOException($"Refusing to overwrite existing output '{path}'.");
            return output;
        }

        internal static string[] OutputPaths(string output) => Path.GetExtension(output).Equals(".utoc", StringComparison.OrdinalIgnoreCase)
            ? new[] { Path.ChangeExtension(output, ".ucas"), Path.ChangeExtension(output, ".pak"), output }
            : new[] { output };

        public static void PackDirectory(string input, string output, EngineVersion version)
        {
            input = Path.GetFullPath(input);
            output = ValidateRequest(output, version);
            if (IsInsideOrEqual(output, input)) throw new ArgumentException("Pack output cannot be inside the input directory.");
            PackFiles(input, EnumerateFiles(input), output, version);
        }

        public static void PackFiles(string input, IEnumerable<string> files, string output, EngineVersion version)
        {
            input = Path.GetFullPath(input);
            output = ValidateRequest(output, version);
            if (IsInsideOrEqual(output, input)) throw new ArgumentException("Pack output cannot be inside the input directory.");
            var manifest = files.Select(path => NormalizeRelative(input, path)).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            if (manifest.Length == 0) throw new InvalidDataException("No files were selected for packing.");
            if (manifest.Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Length)
                throw new InvalidDataException("Duplicate packing paths.");
            foreach (string relative in manifest)
            {
                ValidateAssetPath(relative);
                CheckInputPath(input, relative);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            CheckNoLinks(Path.GetDirectoryName(output));
            NativeLibraryResources.WithFileLock(output, () =>
            {
                ValidateRequest(output, version);
                // Keep the final basename: IoStore derives its container ID from it.
                string work = Path.Combine(Path.GetDirectoryName(output), ".kisse-pack-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(work);
                var published = new List<string>();
                try
                {
                    string staged = Path.Combine(work, Path.GetFileName(output));
                    IoStoreArchiveBackend.Pack(input, JsonConvert.SerializeObject(manifest), staged, version);
                    foreach (string destination in OutputPaths(output))
                    {
                        File.Move(Path.Combine(work, Path.GetFileName(destination)), destination, false);
                        published.Add(destination);
                    }
                    CLI.Console.WriteLine($"[Green][SUCCESS] [White]Packed {manifest.Length} files: {output}");
                }
                catch
                {
                    foreach (string path in published) File.Delete(path);
                    throw;
                }
                finally { DeleteWorkDirectory(work); }
            });
        }

        internal static string[] EnumerateFiles(string root)
        {
            root = Path.GetFullPath(root);
            CheckNoLinks(root);
            var result = new List<string>();
            var directories = new Stack<string>();
            directories.Push(root);
            while (directories.Count > 0)
            {
                foreach (string path in Directory.EnumerateFileSystemEntries(directories.Pop()))
                {
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException($"Links/reparse points are not supported in packing inputs: {path}");
                    if ((attributes & FileAttributes.Directory) != 0) directories.Push(path);
                    else result.Add(path);
                }
            }
            return result.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        }

        internal static string NormalizeRelative(string root, string path)
        {
            string relative = Path.GetRelativePath(root, Path.GetFullPath(path)).Replace('\\', '/');
            if (Path.IsPathRooted(relative) || relative.Contains(':') || relative.Split('/').Any(p => p == ".." || p == "." || p.Length == 0))
                throw new InvalidDataException($"Unsafe packing path '{path}'.");
            return relative;
        }

        internal static void ValidateAssetPath(string relative)
        {
            string extension = Path.GetExtension(relative);
            if (!extension.Equals(".uasset", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".umap", StringComparison.OrdinalIgnoreCase)) return;
            var parts = relative.Replace('\\', '/').Split('/');
            if (!parts.Skip(1).Take(parts.Length - 2).Any(p => p.Equals("Content", StringComparison.Ordinal)))
                throw new InvalidDataException($"Asset '{relative}' needs its cooked <Project>/Content/... path. Select the mod root, not the Content folder.");
        }

        internal static bool IsInsideOrEqual(string child, string parent)
        {
            child = Path.TrimEndingDirectorySeparator(Path.GetFullPath(child));
            parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
            return child.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
                child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        internal static void CheckNoLinks(string path)
        {
            for (var current = new DirectoryInfo(path); current != null; current = current.Parent)
                if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"Links/reparse points are not supported: {current.FullName}");
        }

        private static void CheckInputPath(string root, string relative)
        {
            CheckNoLinks(root);
            string path = root;
            foreach (string part in relative.Split('/'))
            {
                path = Path.Combine(path, part);
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"Links/reparse points are not supported: {path}");
            }
            if (!File.Exists(path)) throw new FileNotFoundException("Packing input is missing.", path);
        }

        internal static void DeleteWorkDirectory(string directory)
        {
            try { Directory.Delete(directory, true); }
            catch (IOException ex) { CLI.Console.WriteLine($"[Yellow][WARN] Could not remove working directory '{directory}': {ex.Message}"); }
            catch (UnauthorizedAccessException ex) { CLI.Console.WriteLine($"[Yellow][WARN] Could not remove working directory '{directory}': {ex.Message}"); }
        }
    }
}
