using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Solicen.UE4
{
    internal static class NativeLibraryResources
    {
        internal const string BridgeResource = "Solicen.Native.kisse_iostore.dll";
        internal const string OodleResource = "Solicen.Native.oo2core_9_win64.dll";
        internal const string VcRuntimeResource = "Solicen.Native.vcruntime140.dll";
        private static readonly Lazy<IntPtr> OodleRuntime = new Lazy<IntPtr>(() =>
        {
            string path = ExtractResource(VcRuntimeResource, "vcruntime140.dll");
            return path == null ? IntPtr.Zero : System.Runtime.InteropServices.NativeLibrary.Load(path);
        });

        internal static void EnsureOodleRuntimeAvailable() => _ = OodleRuntime.Value;

        internal static string CacheDirectory
        {
            get
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(root) || !Path.IsPathFullyQualified(root))
                    throw new IOException("The per-user application data directory is unavailable for the native library cache.");
                return Path.Combine(root, "KissE", "native", "win-x64");
            }
        }

        public static string EnsureBridgeAvailable() => ExtractResource(BridgeResource, "kisse_iostore.dll")
            ?? Path.Combine(AppContext.BaseDirectory, "kisse_iostore.dll");

        internal static string ExtractResource(string resourceName, string fileName)
        {
            using var resource = typeof(NativeLibraryResources).Assembly.GetManifestResourceStream(resourceName);
            return resource == null ? null : CacheFile(resource, fileName, CacheDirectory);
        }

        internal static string CacheFile(Stream resource, string fileName, string directory)
        {
            long start = resource.Position;
            string hash = Convert.ToHexString(SHA256.HashData(resource));
            resource.Position = start;
            string destination = Path.Combine(directory, hash, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            EnsureFile(resource, destination);
            return destination;
        }

        internal static void WithFileLock(string destination, Action action)
        {
            string lockPath = Path.GetFullPath(destination);
            if (OperatingSystem.IsWindows()) lockPath = lockPath.ToUpperInvariant();
            string lockName = "KissE.Native." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(lockPath)));
            using var mutex = new Mutex(false, lockName);
            bool acquired = false;
            try
            {
                try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(90)); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new TimeoutException($"Timed out waiting to prepare '{destination}'. Close other KissE instances and retry.");
                action();
            }
            finally
            {
                if (acquired) mutex.ReleaseMutex();
            }
        }

        internal static void EnsureFile(Stream resource, string destination)
        {
            destination = Path.GetFullPath(destination);
            long start = resource.Position;
            byte[] expectedHash = SHA256.HashData(resource);
            resource.Position = start;
            WithFileLock(destination, () =>
            {
                string temporary = null;
                try
                {
                    if (File.Exists(destination))
                    {
                        using var existing = File.OpenRead(destination);
                        if (CryptographicOperations.FixedTimeEquals(SHA256.HashData(existing), expectedHash)) return;
                    }

                    temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        resource.CopyTo(output);
                        output.Flush(true);
                    }
                    // A crash or competing reader must never observe a partially written DLL.
                    File.Move(temporary, destination, true);
                    temporary = null;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    throw new IOException($"Could not extract native library '{destination}'. The destination cache directory must be writable. If a damaged DLL is in use, close other KissE instances before repairing it.", ex);
                }
                finally
                {
                    if (temporary != null)
                    {
                        try { File.Delete(temporary); }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                }
            });
        }
    }
}
