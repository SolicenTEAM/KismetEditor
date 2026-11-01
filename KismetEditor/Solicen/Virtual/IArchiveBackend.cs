using System;

namespace Solicen.UE4
{
    internal sealed class ArchiveAssetData
    {
        public byte[] Header;
        public byte[] Exports;
        public byte[] Bulk;
    }

    internal interface IArchiveBackend : IDisposable
    {
        string[] Files { get; }
        byte[] ReadFile(string path, long maxBytes);
        ArchiveAssetData ReadAsset(string path, bool skipBulk, long maxBytes);
    }
}
