using Solicen.JSON;
using Solicen.Kismet;
using Solicen.UE4;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Solicen.CLI
{
    partial class CLIHandler
    {
        private static void ProcessArchivePack(string[] paths)
        {
            try
            {
                if (!Config.VersionSpecified) throw new ArgumentException("--pack requires an explicit, valid --version (e.g. --version=4.27).");
                if (Config.EnableAutoTranslate) throw new ArgumentException("Complete --translate separately before creating an archive with --pack.");
                string output = ArchivePacker.ValidateRequest(Config.PackOutput, Config.Version);
                if (paths.Length == 1 && Directory.Exists(paths[0]))
                {
                    ArchivePacker.PackDirectory(paths[0], output, Config.Version);
                }
                else
                {
                    if (paths.Length != 2 || !IsDataFile(paths[0]))
                        throw new ArgumentException("Use <cooked_mod_root> --pack=<output>, or <JSON/CSV> <asset/folder/archive> --pack=<output>.");
                    PackReplacement(paths[0], paths[1], output);
                }
                if (!string.IsNullOrWhiteSpace(Config.RunCommand)) Argumentor.RunTerminal(Config.RunCommand);
            }
            catch (Exception ex)
            {
                CLI.Console.WriteLine($"[Red][ERR] [White]Packing failed: {ex.Message}");
                if (Config.DebugMode) System.Console.WriteLine(ex);
                Environment.ExitCode = 1;
            }
        }

        private static void PackReplacement(string json, string source, string output)
        {
            source = Path.GetFullPath(source);
            var entries = Path.GetExtension(json).Equals(".csv", StringComparison.OrdinalIgnoreCase)
                ? UberJSONProcessor.Convert(json) : UberJSONProcessor.ReadFile(json);
            if (entries == null || entries.Length == 0) throw new InvalidDataException("JSON/CSV contains no entries.");
            bool archive = UnrealArchiveReader.IsArchivePath(source);
            bool directory = Directory.Exists(source);
            if (!directory && !File.Exists(source)) throw new FileNotFoundException("Replacement source does not exist.", source);
            if (archive && !Config.Virtual) throw new ArgumentException("Archive replacement requires --virtual.");
            if (directory && ArchivePacker.IsInsideOrEqual(output, source))
                throw new ArgumentException("Pack output cannot be inside the source asset directory.");
            string keep = BytecodeModifier.PackIntoFolder
                ? Path.GetFullPath(Path.IsPathRooted(BytecodeModifier.PackFolder) ? BytecodeModifier.PackFolder : Path.Combine(EnvironmentHelper.AssemblyDirectory, BytecodeModifier.PackFolder))
                : null;
            if (keep != null && (directory ? ArchivePacker.IsInsideOrEqual(keep, source) || ArchivePacker.IsInsideOrEqual(source, keep) : File.Exists(keep)))
                throw new ArgumentException("--pack-folder must not overlap the source asset tree.");
            string staging = Path.Combine(Path.GetTempPath(), "KissE", "pack", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            bool previousBackup = BytecodeModifier.CreateBak;
            UnrealArchiveReader provider = null;
            try
            {
                BytecodeModifier.CreateBak = false;
                string[] assets;
                string root = null;
                if (archive)
                {
                    provider = new UnrealArchiveReader(source, Config.Version, AssetLoader.MappingsPath, AssetLoader.AES);
                    provider.NotAllowedPath = Config.AllDirectories ? Array.Empty<string>() : NotAllowedPath;
                    AssetLoader.SetProvider(provider);
                    assets = provider.GetAssets();
                }
                else
                {
                    assets = (directory ? ArchivePacker.EnumerateFiles(source) : new[] { source })
                        .Where(p => Path.GetExtension(p).Equals(".uasset", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(p).Equals(".umap", StringComparison.OrdinalIgnoreCase))
                        .Where(p => !IsNotAllowedPath(p)).ToArray();
                    if (assets.Length == 0) throw new InvalidDataException("No source assets found.");
                    root = FindCookedRoot(assets[0]);
                }
                var jobs = new List<(string Source, string Relative, Dictionary<string, string> Values)>();
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in entries)
                {
                    var values = entry.GetValues().Where(p => !string.IsNullOrEmpty(p.Value) && p.Key != p.Value).ToDictionary();
                    if (values.Count == 0) continue;
                    string name = UnrealArchiveReader.NormalizePath(entry.FileName);
                    var exact = assets.Where(p => (archive ? UnrealArchiveReader.NormalizePath(p) : ArchivePacker.NormalizeRelative(root, p))
                        .Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
                    var matches = exact.Length > 0 ? exact : assets.Where(p => Path.GetFileNameWithoutExtension(p)
                        .Equals(Path.GetFileNameWithoutExtension(name), StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (matches.Length != 1)
                        throw new InvalidDataException($"JSON asset '{entry.FileName}' matches {matches.Length} source assets; use an unambiguous cooked path.");
                    string relative = archive ? UnrealArchiveReader.NormalizePath(matches[0]) : ArchivePacker.NormalizeRelative(root, matches[0]);
                    if (relative.Split('/').Any(p => p == "." || p == ".." || p.Contains(':')) || Path.IsPathRooted(relative))
                        throw new InvalidDataException($"Unsafe source path: {relative}");
                    ArchivePacker.ValidateAssetPath(relative);
                    if (!used.Add(relative)) throw new InvalidDataException($"Duplicate JSON target: {relative}");
                    jobs.Add((matches[0], relative, values));
                }
                if (jobs.Count == 0) throw new InvalidDataException("No non-empty translations differ from their originals; no archive was created.");
                var written = new List<string>();
                foreach (var job in jobs)
                {
                    string destination = Path.Combine(staging, job.Relative.Replace('/', Path.DirectorySeparatorChar));
                    string saved = BytecodeModifier.ModifyAsset(job.Source, job.Values, Config.AllowTable, destination);
                    if (saved == null) throw new InvalidDataException($"Failed to modify {job.Relative}; no archive was created.");
                    written.Add(saved);
                    string exports = Path.ChangeExtension(saved, ".uexp");
                    if (File.Exists(exports)) written.Add(exports);
                    var bulk = archive ? provider.ReadAssetData(job.Source) : null;
                    for (int i = 0; i < 3; i++)
                    {
                        string extension = new[] { ".ubulk", ".uptnl", ".m.ubulk" }[i];
                        string target = Path.ChangeExtension(saved, extension);
                        byte[] data = archive ? new[] { bulk?.Bulk, bulk?.OptionalBulk, bulk?.MappedBulk }[i] : null;
                        if (!File.Exists(target))
                        {
                            if (archive && data != null) File.WriteAllBytes(target, data);
                            else if (!archive && File.Exists(Path.ChangeExtension(job.Source, extension)))
                                File.Copy(Path.ChangeExtension(job.Source, extension), target, false);
                        }
                        if (File.Exists(target)) written.Add(target);
                    }
                }
                if (keep != null) SavePreparedFiles(staging, written, keep);
                ArchivePacker.PackFiles(staging, written, output, Config.Version);
            }
            finally
            {
                if (provider != null) { AssetLoader.CloseProvider(); provider.Dispose(); }
                BytecodeModifier.CreateBak = previousBackup;
                ArchivePacker.DeleteWorkDirectory(staging);
            }
        }

        internal static string FindCookedRoot(string asset)
        {
            for (var folder = new DirectoryInfo(Path.GetDirectoryName(asset)); folder != null; folder = folder.Parent)
            {
                if (!folder.Name.Equals("Content", StringComparison.Ordinal) || folder.Parent?.Parent == null) continue;
                // Plugin Content must keep its enclosing Project/Plugins/... mount path.
                for (var parent = folder.Parent; parent != null; parent = parent.Parent)
                    if (parent.Name.Equals("Plugins", StringComparison.Ordinal) && parent.Parent?.Parent != null)
                        return parent.Parent.Parent.FullName;
                return folder.Parent.Parent.FullName;
            }
            throw new InvalidDataException("Source assets need their cooked <Project>/Content/... layout.");
        }

        private static void SavePreparedFiles(string staging, IEnumerable<string> files, string output)
        {
            var copies = files.Select(path => (Source: path, Destination: Path.Combine(output, ArchivePacker.NormalizeRelative(staging, path)))).ToArray();
            foreach (var copy in copies)
                if (File.Exists(copy.Destination)) throw new IOException($"Refusing to overwrite prepared asset '{copy.Destination}'. Choose a fresh --pack-folder.");
            Directory.CreateDirectory(output);
            ArchivePacker.CheckNoLinks(output);
            foreach (var copy in copies)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(copy.Destination));
                ArchivePacker.CheckNoLinks(Path.GetDirectoryName(copy.Destination));
                File.Copy(copy.Source, copy.Destination, false);
            }
        }
    }
}
