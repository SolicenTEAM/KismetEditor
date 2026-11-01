using Solicen.UE4;
using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace KismetEditor.Tests;

public sealed class OodleLibraryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "KissE-oodle-" + Guid.NewGuid());
    public OodleLibraryTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);
    private string Destination => Path.Combine(directory, OodleLibrary.FileName);
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    private static byte[] Archive(string name = OodleLibrary.EntryName)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            using var entry = zip.CreateEntry(name).Open();
            entry.Write(Encoding.UTF8.GetBytes("test library, never loaded"));
        }
        return output.ToArray();
    }

    [Fact]
    public void VerifiedArchiveSuppliesOnlyTheExpectedDll()
    {
        byte[] archive = Archive();
        Assert.Equal("test library, never loaded", Encoding.UTF8.GetString(OodleLibrary.ReadLibrary(archive, Hash(archive))));
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public void RejectsTamperedArchiveBeforeExtraction()
    {
        byte[] archive = Archive();
        string hash = Hash(archive);
        archive[0] ^= 1;
        Assert.Throws<InvalidDataException>(() => OodleLibrary.ReadLibrary(archive, hash));
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public void DoesNotExtractOtherZipPaths()
    {
        byte[] archive = Archive("../../unexpected.dll");
        Assert.Throws<InvalidDataException>(() => OodleLibrary.ReadLibrary(archive, Hash(archive)));
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public void ExistingUserLibraryIsPreservedEvenWhenOffline()
    {
        File.WriteAllText(Destination, "user library");
        OodleLibrary.EnsureAvailable(directory, () => throw new Exception("Must not download"), false);
        Assert.Equal("user library", File.ReadAllText(Destination));
    }

    [Fact]
    public void ModernLocalLibraryWorksOfflineWithoutChangingTheOriginal()
    {
        string modern = Path.Combine(directory, "oodle-data-shared.dll");
        File.WriteAllText(modern, "modern user library");
        OodleLibrary.EnsureAvailable(directory, () => throw new Exception("Must not download"), false);
        Assert.Equal("modern user library", File.ReadAllText(Destination));
        Assert.Equal("modern user library", File.ReadAllText(modern));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public void DisabledDownloadLeavesNoFile()
    {
        var error = Assert.Throws<InvalidOperationException>(() => OodleLibrary.EnsureAvailable(directory, () => throw new Exception("Must not download"), false));
        Assert.Contains("disabled", error.Message);
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public void ConcurrentRequestsDownloadOnceAndUseTheRetocFileName()
    {
        byte[] archive = Archive();
        int downloads = 0;
        Parallel.For(0, 8, _ => OodleLibrary.EnsureAvailable(directory, () =>
        {
            Interlocked.Increment(ref downloads);
            return archive;
        }, true, Hash(archive)));
        Assert.Equal(1, downloads);
        Assert.Equal("test library, never loaded", File.ReadAllText(Destination));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void NetworkFailureIsActionableAndLeavesNoPartialFile()
    {
        var error = Assert.Throws<InvalidOperationException>(() => OodleLibrary.EnsureAvailable(directory, () => throw new HttpRequestException("offline"), true));
        Assert.Contains("manually", error.Message);
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public void UnverifiedDownloadIsNotInstalled()
    {
        var error = Assert.Throws<InvalidOperationException>(() => OodleLibrary.EnsureAvailable(directory, () => Archive(), true));
        Assert.Contains("SHA-256", error.Message);
        Assert.Empty(Directory.GetFiles(directory));
    }
}
