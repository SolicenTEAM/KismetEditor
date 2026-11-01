using Microsoft.Win32.SafeHandles;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UAssetAPI.UnrealTypes;

namespace Solicen.UE4
{
    internal sealed class IoStoreArchiveBackend : IArchiveBackend
    {
        private IoStoreHandle handle;
        private readonly Dictionary<string, string> paths;
        public string[] Files { get; }

        static IoStoreArchiveBackend()
        {
            NativeLibrary.SetDllImportResolver(typeof(IoStoreArchiveBackend).Assembly, (name, assembly, searchPath) =>
                name == "kisse_iostore" ? NativeLibrary.Load(NativeLibraryResources.EnsureBridgeAvailable()) : IntPtr.Zero);
        }

        public IoStoreArchiveBackend(string path, EngineVersion version, string aes)
        {
            string toc = Path.ChangeExtension(Path.GetFullPath(path), ".utoc");
            if (!File.Exists(toc)) throw new FileNotFoundException("IoStore requires the matching .utoc next to the .ucas.", toc);
            string cas = Path.ChangeExtension(toc, ".ucas");
            if (!File.Exists(cas)) throw new FileNotFoundException("IoStore requires the matching .ucas next to the .utoc.", cas);
            if (!string.IsNullOrEmpty(aes)) aes = Convert.ToHexString(UnrealArchiveReader.ParseAesKey(aes));
            try
            {
                int status = Native.Open(toc, aes ?? string.Empty, version.ToString().Replace("VER_", ""), out var raw, out var error);
                if (status == 2)
                {
                    Native.Free(error);
                    string oodle = OodleLibrary.EnsureAvailable();
                    NativeLibraryResources.EnsureOodleRuntimeAvailable();
                    Check(Native.SetOodle(oodle, out error), error);
                    status = Native.Open(toc, aes ?? string.Empty, version.ToString().Replace("VER_", ""), out raw, out error);
                }
                Check(status, error);
                handle = new IoStoreHandle(raw);
                try
                {
                    Check(Native.Files(handle, out var files, out error), error);
                    Files = JsonConvert.DeserializeObject<string[]>(TakeText(files)) ?? Array.Empty<string>();
                    paths = Files.GroupBy(UnrealArchiveReader.NormalizePath, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                }
                catch
                {
                    handle.Dispose();
                    throw;
                }
            }
            catch (DllNotFoundException ex)
            {
                throw new InvalidOperationException("IoStore bridge is missing. Rebuild the portable EXE with scripts/publish-portable.ps1, or supply kisse_iostore.dll for a developer build.", ex);
            }
            catch (BadImageFormatException ex)
            {
                throw new InvalidOperationException("IoStore requires the Windows x64 bridge and an x64 process.", ex);
            }
        }

        public ArchiveAssetData ReadAsset(string path, bool skipBulk, long maxBytes)
        {
            ThrowIfClosed();
            if (!paths.TryGetValue(UnrealArchiveReader.NormalizePath(path), out var actual)) return null;
            Check(Native.ReadAsset(handle, actual, skipBulk ? 1 : 0, Limit(maxBytes), out var data, out var error), error);
            try
            {
                return new ArchiveAssetData { Header = Copy(data.Header), Exports = Copy(data.Exports), Bulk = skipBulk ? null : Copy(data.Bulk) };
            }
            finally
            {
                Native.Free(data.Header);
                Native.Free(data.Exports);
                Native.Free(data.Bulk);
            }
        }

        public byte[] ReadFile(string path, long maxBytes)
        {
            ThrowIfClosed();
            if (!paths.TryGetValue(UnrealArchiveReader.NormalizePath(path), out var actual)) return null;
            string ext = Path.GetExtension(actual).ToLowerInvariant();
            if (ext == ".uasset" || ext == ".umap") return ReadAsset(actual, true, maxBytes)?.Header;
            if (ext == ".uexp") return ReadAsset(actual, true, maxBytes)?.Exports;
            Check(Native.ReadFile(handle, actual, Limit(maxBytes), out var bytes, out var error), error);
            try { return Copy(bytes); }
            finally { Native.Free(bytes); }
        }

        private static ulong Limit(long bytes) => bytes > 0 ? (ulong)bytes : 0;
        private void ThrowIfClosed() => ObjectDisposedException.ThrowIf(handle == null || handle.IsClosed, this);
        public void Dispose()
        {
            handle?.Dispose();
            handle = null;
        }

        private static byte[] Copy(NativeBuffer buffer)
        {
            int length = checked((int)buffer.Length);
            var bytes = new byte[length];
            if (length > 0) Marshal.Copy(buffer.Data, bytes, 0, length);
            return bytes;
        }
        private static string TakeText(NativeBuffer buffer)
        {
            try { return Encoding.UTF8.GetString(Copy(buffer)); }
            finally { Native.Free(buffer); }
        }
        private static void Check(int status, NativeBuffer error)
        {
            if (status != 0) throw new InvalidDataException(TakeText(error));
            Native.Free(error);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeBuffer
        {
            public IntPtr Data;
            public UIntPtr Length;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeAsset
        {
            public NativeBuffer Header;
            public NativeBuffer Exports;
            public NativeBuffer Bulk;
        }
        private sealed class IoStoreHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public IoStoreHandle(IntPtr value) : base(true) { SetHandle(value); }
            protected override bool ReleaseHandle()
            {
                Native.Close(handle);
                return true;
            }
        }
        private static class Native
        {
            private const string Library = "kisse_iostore";
            [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
            [DllImport(Library, EntryPoint = "kisse_io_set_oodle", CallingConvention = CallingConvention.Cdecl)]
            public static extern int SetOodle([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out NativeBuffer error);
            [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
            [DllImport(Library, EntryPoint = "kisse_io_open", CallingConvention = CallingConvention.Cdecl)]
            public static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string aes, [MarshalAs(UnmanagedType.LPUTF8Str)] string version, out IntPtr handle, out NativeBuffer error);
            [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
            [DllImport(Library, EntryPoint = "kisse_io_files", CallingConvention = CallingConvention.Cdecl)]
            public static extern int Files(IoStoreHandle handle, out NativeBuffer output, out NativeBuffer error);
            [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
            [DllImport(Library, EntryPoint = "kisse_io_read_asset", CallingConvention = CallingConvention.Cdecl)]
            public static extern int ReadAsset(IoStoreHandle handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int skipBulk, ulong maxBytes, out NativeAsset output, out NativeBuffer error);
            [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
            [DllImport(Library, EntryPoint = "kisse_io_read_file", CallingConvention = CallingConvention.Cdecl)]
            public static extern int ReadFile(IoStoreHandle handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, ulong maxBytes, out NativeBuffer output, out NativeBuffer error);
            [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
            [DllImport(Library, EntryPoint = "kisse_io_free", CallingConvention = CallingConvention.Cdecl)]
            public static extern void Free(NativeBuffer buffer);
            [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
            [DllImport(Library, EntryPoint = "kisse_io_close", CallingConvention = CallingConvention.Cdecl)]
            public static extern void Close(IntPtr handle);
        }
    }
}
