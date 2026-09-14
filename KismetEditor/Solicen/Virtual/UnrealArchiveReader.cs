using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UAssetAPI;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;

namespace Solicen.UE4
{
    /// <summary>
    /// Virtual provider: reads assets directly from a .pak archive into memory
    /// (nothing is ever written to disk). Uses UAssetAPI's PakReader under the hood.
    /// </summary>
    public class UnrealArchiveReader : IDisposable
    {
        private bool PickyMode = false;
        private FileStream pakStream;
        private PakReader pakReader;

        /// <summary>Engine version used to parse the assets.</summary>
        private readonly EngineVersion Version;

        /// <summary>Optional usmap mappings for unversioned assets.</summary>
        private readonly Usmap mappings;

        /// <summary>Cached list of asset paths inside the archive (mount point applies).</summary>
        private string[] cachedFiles;

        /// <summary>
        /// Maximum allowed size (in bytes) of the combined uasset+uexp payload.
        /// Larger assets are skipped instead of being parsed. 0 = no limit.
        /// Textures/audio live in separate .ubulk entries, so .uasset+.uexp payloads
        /// are normally small; a limit of <see cref="DefaultMaxAssetBytes"/> is a safe
        /// protective default for batch runs.
        /// </summary>
        public long MaxAssetBytes = DefaultMaxAssetBytes;

        /// <summary>Default protective limit: 256 MB of combined uasset+uexp data.</summary>
        public const long DefaultMaxAssetBytes = 256L * 1024 * 1024;

        /// <summary>Last error message produced by <see cref="FromGameFile"/>.</summary>
        public static string ExceptionMessage = string.Empty;

        /// <summary>
        /// Mounts the .pak archive in memory.
        /// </summary>
        /// <param name="PakPath">Path to the .pak file on disk.</param>
        /// <param name="Version">Engine version used for asset parsing.</param>
        /// <param name="UsmapPath">Optional path to a .usmap mappings file (needed for unversioned assets).</param>
        /// <param name="AesHexKey">Optional AES key as a hex string ("0xAABB..." or "AABB...", 64 hex characters = 32 bytes).</param>
        public UnrealArchiveReader(string PakPath, EngineVersion Version, string UsmapPath = null, string AesHexKey = null)
        {
            this.Version = Version;

            // Load usmap mappings, if provided
            if (!string.IsNullOrEmpty(UsmapPath) && File.Exists(UsmapPath))
            {
                this.mappings = new Usmap(UsmapPath);
            }

            // Open the archive and mount the reader (+ AES if a key is provided)
            this.pakStream = new FileStream(PakPath, FileMode.Open, FileAccess.Read, FileShare.Read);

            var builder = new PakBuilder();
            if (!string.IsNullOrEmpty(AesHexKey))
            {
                byte[] key = ParseAesKey(AesHexKey);
                if (key != null) builder = builder.Key(key);
            }
            this.pakReader = builder.Reader(this.pakStream);

            CLI.Console.WriteLine($"[Green][Success] Pak '{Path.GetFileName(PakPath)}' mounted virtually ({Files.Length} files).");
        }

        /// <summary>
        /// Prints the list of assets found in the archive to the console.
        /// </summary>
        public void List()
        {
            foreach (var asset in GetAssets())
            {
                Console.WriteLine($"  - {asset}");
            }
        }

        /// <summary>
        /// Full list of file entries stored inside the archive.
        /// The result is read once and cached internally.
        /// </summary>
        public string[] Files
        {
            get
            {
                if (cachedFiles == null)
                    cachedFiles = pakReader?.Files() ?? Array.Empty<string>();
                return cachedFiles;
            }
        }

        public string[] NotAllowedPath = new string[] {"Engine/"};

        /// <summary>
        /// Returns asset paths interesting for editing (.uasset/.umap only,
        /// files under the Engine/ mount are excluded — they are not game content).
        /// </summary>
        /// <returns>Array of virtual paths inside the archive.</returns>
        public string[] GetAssets()
        {
            var validExtensions = new[] { ".uasset", ".umap" };
            return Files
                .Where(x => validExtensions.Any(ext => x.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                .Where(x => !IsNotAllowedPath(x)) // only work with files outside the exclude folders
                .ToArray();

            bool IsNotAllowedPath(string path)
            {
                return NotAllowedPath.Any(exclude => path.Contains(exclude.Replace("\\", "/")));
            }
        }

        /// <summary>
        /// When true (default), .ubulk entries (textures, audio) are never fetched —
        /// they are irrelevant for kismet/string editing and are the heaviest payloads.
        /// Set to false to restore the old behavior: .ubulk is fetched and a warning
        /// is printed that bulk data was not parsed.
        /// </summary>
        public bool SkipBulkDataFiles = true;

        /// <summary>
        /// Reads a single asset from the archive entirely into memory and parses it
        /// into a <see cref="UAsset"/>.
        /// </summary>
        /// <param name="assetPath">Virtual path of the asset inside the archive.</param>
        /// <returns>A parsed asset, or null on failure (see <see cref="ExceptionMessage"/>).</returns>
        public UAsset FromGameFile(string assetPath)
        {
            if (pakReader == null) return null;
            try
            {
                // 1. Pull the raw bytes into memory
                byte[] uassetData = PakGet(assetPath);
                if (uassetData == null)
                {
                    CLI.Console.WriteLine($"[Red][ERR] Could not find '{assetPath}' in pak.");
                    return null;
                }

                // 2. Pull the attached .uexp data (exports live there), if it exists
                byte[] uexpData = PakGet(Path.ChangeExtension(assetPath, "uexp"));

                // 2a. Optionally pull and acknowledge the .ubulk entry (bulk data is
                //     never parsed in either mode)
                byte[] ubulkData = SkipBulkDataFiles ? null : PakGet(Path.ChangeExtension(assetPath, "ubulk"));

                long total = uassetData.Length + (uexpData?.Length ?? 0);
                if (MaxAssetBytes > 0 && total > MaxAssetBytes)
                {
                    // Skip oversized assets: in practice these are bulk-bloated maps/audio,
                    // which contain no kismet bytecode or strings worth extracting.
                    CLI.Console.WriteLine($"[Yellow][WARN] Skipped '{assetPath}': {total / 1024 / 1024} MB exceeds the {MaxAssetBytes / 1024 / 1024} MB limit.");
                    return null;
                }

                // 3. Concatenate uasset + uexp into a single stream — UAssetAPI's stream-based
                //    reading expects the .uexp data to be appended at the end of the .uasset data.
                var combined = new MemoryStream((int)total);
                combined.Write(uassetData, 0, uassetData.Length);
                if (uexpData != null) combined.Write(uexpData, 0, uexpData.Length);
                combined.Seek(0, SeekOrigin.Begin);

                // 4. Parse the UAsset from memory (+ usmap if attached).
                //    IMPORTANT: loadUexp = true — otherwise UAssetAPI reads only the header
                //    and name map and silently skips all exports.
                var asset = new UAsset(Version, mappings)
                {
                    UseSeparateBulkDataFiles = uexpData != null
                };
                asset.Read(new AssetBinaryReader(combined, true, asset));
                combined.Dispose();

                if (ubulkData != null)
                {
                    if (PickyMode)
                        CLI.Console.WriteLine($"[Yellow][WARN] Asset '{assetPath}' has .ubulk — bulk data was not parsed.");
                }
                return asset;
            }
            catch (Exception ex)
            {
                CLI.Console.WriteLine($"[Red][ERR] Failed to convert asset '{assetPath}': {ex.Message}");
                ExceptionMessage = ex.Message;
                return null;
            }
        }

        /// <summary>
        /// Reads a single archive entry by its virtual path.
        /// Entry paths may or may not include the mount point — both variants are tried,
        /// the result is cached for the mount point lookup.
        /// </summary>
        /// <param name="path">Virtual path of the entry inside the archive.</param>
        /// <returns>Entry bytes, or null if the entry does not exist.</returns>
        private byte[] PakGet(string path)
        {
            string mount = MountPoint;
            if (!string.IsNullOrEmpty(mount) && mount != "/" && path.Contains(mount))
            {
                // Variant with the mount point already included — try as-is first
                byte[] data = pakReader.Get(pakStream, path);
                if (data != null) return data;
            }
            return pakReader.Get(pakStream, path) ?? pakReader.Get(pakStream, mount + path);
        }

        /// <summary>Mount point of the archive, resolved once and cached.</summary>
        private string mountPoint;
        private string MountPoint
        {
            get
            {
                if (mountPoint == null)
                {
                    try { mountPoint = pakReader.GetMountPoint(); }
                    catch { mountPoint = string.Empty; }
                }
                return mountPoint;
            }
        }

        /// <summary>
        /// Parses an AES key out of a hex string like "0xAABB..." or "AABB...".
        /// </summary>
        /// <param name="hex">Hex-encoded 32-byte AES key.</param>
        /// <returns>Key bytes, or null if the string is invalid.</returns>
        private static byte[] ParseAesKey(string hex)
        {
            try
            {
                string clean = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? hex.Substring(2) : hex;
                if (clean.Length != 64) // AES-128 => 32 bytes
                {
                    CLI.Console.WriteLine("[Red][ERR] AES key must be 32 bytes (64 hex characters).");
                    return null;
                }
                byte[] key = new byte[clean.Length / 2];
                for (int i = 0; i < key.Length; i++)
                    key[i] = Convert.ToByte(clean.Substring(i * 2, 2), 16);
                return key;
            }
            catch (Exception ex)
            {
                CLI.Console.WriteLine($"[Red][ERR] Failed to parse AES key: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Releases the archive reader and the underlying file handle.
        /// Call this when the provider is no longer needed (e.g. after a batch run).
        /// </summary>
        public void Close()
        {
            pakReader?.Dispose();
            pakReader = null;
            pakStream?.Dispose();
            pakStream = null;
        }

        public void Dispose()
        {
            Close();
        }
    }
}
