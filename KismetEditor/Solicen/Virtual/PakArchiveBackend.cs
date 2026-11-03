using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UAssetAPI;

namespace Solicen.UE4
{
    internal sealed class PakArchiveBackend : IArchiveBackend
    {
        private FileStream stream;
        private PakReader reader;
        private readonly Dictionary<string, string> paths;
        private readonly string mount;
        public string[] Files { get; }

        public PakArchiveBackend(string path, string aes)
        {
            try
            {
                stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var builder = new PakBuilder();
                if (!string.IsNullOrEmpty(aes)) builder = builder.Key(UnrealArchiveReader.ParseAesKey(aes));
                reader = builder.Reader(stream);
                Files = reader.Files();
                mount = reader.GetMountPoint() ?? string.Empty;
                paths = Files.GroupBy(UnrealArchiveReader.NormalizePath, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public byte[] ReadFile(string path, long maxBytes)
        {
            ObjectDisposedException.ThrowIf(reader == null, this);
            if (!paths.TryGetValue(UnrealArchiveReader.NormalizePath(path), out var actual) &&
                !paths.TryGetValue(UnrealArchiveReader.NormalizePath(mount + path), out actual)) return null;
            byte[] data = reader.Get(stream, actual) ?? reader.Get(stream, mount + actual);
            if (maxBytes > 0 && data?.LongLength > maxBytes)
                throw new InvalidDataException($"'{path}' exceeds the {maxBytes} byte limit.");
            return data;
        }

        public ArchiveAssetData ReadAsset(string path, bool skipBulk, long maxBytes)
        {
            var header = ReadFile(path, maxBytes);
            if (header == null) return null;
            var exports = ReadFile(Path.ChangeExtension(path, "uexp"), maxBytes);
            if (maxBytes > 0 && header.LongLength + (exports?.LongLength ?? 0) > maxBytes)
                throw new InvalidDataException($"'{path}' exceeds the {maxBytes} byte asset limit.");
            return new ArchiveAssetData
            {
                Header = header,
                Exports = exports,
                Bulk = skipBulk ? null : ReadFile(Path.ChangeExtension(path, "ubulk"), maxBytes),
                OptionalBulk = skipBulk ? null : ReadFile(Path.ChangeExtension(path, "uptnl"), maxBytes),
                MappedBulk = skipBulk ? null : ReadFile(Path.ChangeExtension(path, "m.ubulk"), maxBytes)
            };
        }

        public void Dispose()
        {
            reader?.Dispose();
            reader = null;
            stream?.Dispose();
            stream = null;
        }
    }
}
