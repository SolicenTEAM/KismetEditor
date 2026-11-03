using System;
using System.IO;
using System.Linq;
using UAssetAPI;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;

namespace Solicen.UE4
{
    /// <summary>
    /// Reads PAK and IoStore assets virtually. IoStore Zen packages are converted
    /// to legacy uasset/uexp in memory before being passed to UAssetAPI.
    /// </summary>
    public class UnrealArchiveReader : IDisposable
    {
        private IArchiveBackend backend;
        private readonly EngineVersion Version;
        private readonly Usmap mappings;

        /// <summary>Maximum combined uasset+uexp size; 0 disables the limit.</summary>
        public long MaxAssetBytes = DefaultMaxAssetBytes;
        public const long DefaultMaxAssetBytes = 256L * 1024 * 1024;
        public static string ExceptionMessage = string.Empty;
        public string[] NotAllowedPath = new[] { "Engine/" };
        public bool SkipBulkDataFiles = true;
        public bool IsClosed => backend == null;

        public UnrealArchiveReader(string PakPath, EngineVersion Version, string UsmapPath = null, string AesHexKey = null)
        {
            this.Version = Version;
            if (!string.IsNullOrEmpty(UsmapPath) && File.Exists(UsmapPath)) mappings = new Usmap(UsmapPath);
            if (!IsArchivePath(PakPath)) throw new ArgumentException("Expected a .pak, .ucas or .utoc archive.", nameof(PakPath));
            backend = IsIoStorePath(PakPath)
                ? new IoStoreArchiveBackend(PakPath, Version, AesHexKey)
                : new PakArchiveBackend(PakPath, AesHexKey);
            CLI.Console.WriteLine($"[Green][Success] Archive '{Path.GetFileName(PakPath)}' mounted virtually ({Files.Length} files).");
        }

        public static bool IsIoStorePath(string path) =>
            string.Equals(Path.GetExtension(path), ".ucas", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetExtension(path), ".utoc", StringComparison.OrdinalIgnoreCase);

        public static bool IsArchivePath(string path) => IsIoStorePath(path) ||
            string.Equals(Path.GetExtension(path), ".pak", StringComparison.OrdinalIgnoreCase);

        internal static string NormalizePath(string path)
        {
            path = path.Replace('\\', '/').TrimStart('/');
            while (path.StartsWith("../", StringComparison.Ordinal)) path = path.Substring(3);
            return path;
        }

        public string[] Files => backend?.Files ?? Array.Empty<string>();

        public void List()
        {
            foreach (var asset in GetAssets()) Console.WriteLine($"  - {asset}");
        }

        public string[] GetAssets() => Files
            .Where(x => x.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".umap", StringComparison.OrdinalIgnoreCase))
            .Where(x => !NotAllowedPath.Any(exclude => NormalizePath(x).Contains(exclude.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        /// <summary>Reads a virtual file without extracting it. IoStore package entries return legacy data.</summary>
        public byte[] ReadFile(string path)
        {
            ObjectDisposedException.ThrowIf(IsClosed, this);
            return backend.ReadFile(path, MaxAssetBytes);
        }

        internal ArchiveAssetData ReadAssetData(string path)
        {
            ObjectDisposedException.ThrowIf(IsClosed, this);
            return backend.ReadAsset(path, false, MaxAssetBytes);
        }

        public UAsset FromGameFile(string assetPath)
        {
            ExceptionMessage = string.Empty;
            try
            {
                ObjectDisposedException.ThrowIf(IsClosed, this);
                var data = backend.ReadAsset(assetPath, SkipBulkDataFiles, MaxAssetBytes);
                if (data?.Header == null) throw new FileNotFoundException($"Virtual asset '{assetPath}' was not found.");
                long total = data.Header.LongLength + (data.Exports?.LongLength ?? 0);
                if (MaxAssetBytes > 0 && total > MaxAssetBytes)
                    throw new InvalidDataException($"Asset exceeds the {MaxAssetBytes} byte limit.");
                using var combined = new MemoryStream(checked((int)total));
                combined.Write(data.Header);
                if (data.Exports != null) combined.Write(data.Exports);
                combined.Position = 0;
                var asset = new UAsset(Version, mappings) { UseSeparateBulkDataFiles = data.Exports != null };
                asset.Read(new AssetBinaryReader(combined, true, asset));
                return asset;
            }
            catch (Exception ex)
            {
                ExceptionMessage = ex.Message;
                CLI.Console.WriteLine($"[Red][ERR] Failed to convert asset '{assetPath}': {ex.Message}");
                return null;
            }
        }

        internal static byte[] ParseAesKey(string hex)
        {
            string clean = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex.Substring(2) : hex;
            if (clean.Length != 64) throw new ArgumentException("AES key must be 32 bytes (64 hex characters).");
            try { return Convert.FromHexString(clean); }
            catch (FormatException ex) { throw new ArgumentException("AES key must contain only hexadecimal characters.", ex); }
        }

        public void Close()
        {
            backend?.Dispose();
            backend = null;
        }

        public void Dispose() => Close();
    }
}
