using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Solicen.UE4
{
    internal static class OodleLibrary
    {
        // Same pinned OodleUE release as CUE4Parse's OodleHelper (Oodle 2.9.16).
        internal const string DownloadUrl = "https://github.com/WorkingRobot/OodleUE/releases/download/2026-06-04-1357/clang-cl-x64-release.zip";
        internal const string ArchiveHash = "37F816A3E8F5A65A11F6DEFC8C28C0508287602767741BA4803C6A21A62A877B";
        internal const string EntryName = "bin/oodle-data-shared.dll";
        internal const string FileName = "oo2core_9_win64.dll";
        private const int MaxDownloadBytes = 16 * 1024 * 1024;
        public static bool AllowDownload = true;

        public static string EnsureAvailable()
        {
            string embedded = NativeLibraryResources.ExtractResource(NativeLibraryResources.OodleResource, FileName);
            if (embedded != null) return embedded;
            foreach (string name in new[] { FileName, "oodle-data-shared.dll" })
            {
                string external = Path.Combine(AppContext.BaseDirectory, name);
                if (!File.Exists(external)) continue;
                using var local = File.OpenRead(external);
                return NativeLibraryResources.CacheFile(local, FileName, NativeLibraryResources.CacheDirectory);
            }
            string directory = Path.Combine(NativeLibraryResources.CacheDirectory, "downloads", ArchiveHash);
            Directory.CreateDirectory(directory);
            EnsureAvailable(directory, () => DownloadArchiveAsync().GetAwaiter().GetResult(), AllowDownload);
            using var downloaded = File.OpenRead(Path.Combine(directory, FileName));
            return NativeLibraryResources.CacheFile(downloaded, FileName, NativeLibraryResources.CacheDirectory);
        }

        internal static void EnsureAvailable(string directory, Func<byte[]> download, bool allowDownload, string expectedHash = ArchiveHash)
        {
            string destination = Path.Combine(directory, FileName);
            NativeLibraryResources.WithFileLock(destination, () =>
            {
                // User-supplied and embedded copies take priority and are never downloaded over.
                if (File.Exists(destination)) return;
                string modernPath = Path.Combine(directory, "oodle-data-shared.dll");
                if (File.Exists(modernPath))
                {
                    using var local = File.OpenRead(modernPath);
                    NativeLibraryResources.EnsureFile(local, destination);
                    return;
                }
                if (!allowDownload)
                    throw new InvalidOperationException($"Oodle download is disabled. Supply '{destination}' or embed OodleLibraryPath at build time.");
                try
                {
                    byte[] library = ReadLibrary(download(), expectedHash);
                    using var resource = new MemoryStream(library, false);
                    NativeLibraryResources.EnsureFile(resource, destination);
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is HttpRequestException || ex is OperationCanceledException || ex is UnauthorizedAccessException)
                {
                    throw new InvalidOperationException($"Could not obtain Oodle from '{DownloadUrl}': {ex.Message} Supply '{destination}' manually or embed OodleLibraryPath at build time.", ex);
                }
            });
        }

        internal static byte[] ReadLibrary(byte[] archive, string expectedHash = ArchiveHash)
        {
            if (archive.Length > MaxDownloadBytes ||
                !CryptographicOperations.FixedTimeEquals(SHA256.HashData(archive), Convert.FromHexString(expectedHash)))
                throw new InvalidDataException("OodleUE archive SHA-256 mismatch or size limit exceeded. Nothing was installed.");
            using var source = new MemoryStream(archive, false);
            using var zip = new ZipArchive(source, ZipArchiveMode.Read);
            var entry = zip.GetEntry(EntryName) ?? throw new InvalidDataException($"OodleUE archive is missing '{EntryName}'.");
            if (entry.Length <= 0 || entry.Length > MaxDownloadBytes)
                throw new InvalidDataException("Oodle DLL has an invalid size.");
            using var input = entry.Open();
            using var output = new MemoryStream();
            CopyBounded(input, output);
            return output.ToArray();
        }

        private static async Task<byte[]> DownloadArchiveAsync()
        {
            if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
                throw new PlatformNotSupportedException("Automatic Oodle acquisition requires Windows x64.");
            CLI.Console.WriteLine("[DarkGray][INF] [White]Downloading Oodle 2.9.16 from the pinned OodleUE release...");
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("KissE-IoStore");
            using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxDownloadBytes)
                throw new InvalidDataException("OodleUE download exceeds the size limit.");
            await using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var output = new MemoryStream();
            byte[] buffer = new byte[81920];
            int read;
            while ((read = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (output.Length + read > MaxDownloadBytes) throw new InvalidDataException("OodleUE download exceeds the size limit.");
                output.Write(buffer, 0, read);
            }
            return output.ToArray();
        }

        private static void CopyBounded(Stream input, Stream output)
        {
            byte[] buffer = new byte[81920];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > MaxDownloadBytes) throw new InvalidDataException("Oodle DLL exceeds the size limit.");
                output.Write(buffer, 0, read);
            }
        }
    }
}
