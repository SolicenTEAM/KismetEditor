using Solicen.UE4;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace KismetEditor.Tests;

public sealed class NativeLibraryResourceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "KissE-resources-" + Guid.NewGuid());
    public NativeLibraryResourceTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);

    private void Extract(string content, string file = "native.bin")
    {
        using var resource = new MemoryStream(Encoding.UTF8.GetBytes(content));
        NativeLibraryResources.EnsureFile(resource, Path.Combine(directory, file));
    }

    [Fact]
    public void ExtractsMissingFileAndDoesNotRewriteMatchingContent()
    {
        string path = Path.Combine(directory, "native.bin");
        Extract("embedded library");
        Assert.Equal("embedded library", File.ReadAllText(path));
        var timestamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, timestamp);
        Extract("embedded library");
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void ReplacesDifferentContentEvenWhenLengthMatches()
    {
        Extract("old library");
        Extract("new library");
        Assert.Equal("new library", File.ReadAllText(Path.Combine(directory, "native.bin")));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public void ConcurrentExtractionsProduceOneCompleteFile()
    {
        string content = new string('x', 128 * 1024);
        Parallel.For(0, 12, _ => Extract(content));
        Assert.Equal(content, File.ReadAllText(Path.Combine(directory, "native.bin")));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void LockedMatchingLibraryNeedsNoWriteAccess()
    {
        Extract("same library");
        using var locked = File.Open(Path.Combine(directory, "native.bin"), FileMode.Open, FileAccess.Read, FileShare.Read);
        Extract("same library");
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public void CachedLibrariesUseContentAddressedPathsAndPreservePreviousVersions()
    {
        string Cache(string content)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            return NativeLibraryResources.CacheFile(stream, "library.dll", directory);
        }
        string first = Cache("version one");
        var timestamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(first, timestamp);
        Assert.Equal(first, Cache("version one"));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(first));
        using var locked = File.Open(first, FileMode.Open, FileAccess.Read, FileShare.Read);
        string second = Cache("version two");
        Assert.NotEqual(first, second);
        Assert.Equal("version one", File.ReadAllText(first));
        Assert.Equal("version two", File.ReadAllText(second));
        Assert.Empty(Directory.GetFiles(directory));
        Assert.NotEqual(AppContext.BaseDirectory, Path.GetDirectoryName(first));
    }

    [Fact]
    public void ConcurrentCacheExtractionsUseOneCompleteContentAddressedFile()
    {
        Parallel.For(0, 12, _ =>
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("same bytes"));
            NativeLibraryResources.CacheFile(stream, "library.dll", directory);
        });
        string path = Assert.Single(Directory.GetFiles(directory, "*", SearchOption.AllDirectories));
        Assert.Equal("same bytes", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public void NativeCacheIsOutsideTheApplicationDirectory()
    {
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), NativeLibraryResources.CacheDirectory);
        Assert.Contains(Path.Combine("KissE", "native", "win-x64"), NativeLibraryResources.CacheDirectory);
        Assert.NotEqual(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), NativeLibraryResources.CacheDirectory);
    }

    [Fact]
    public void LockedOutdatedLibraryIsPreservedAndTemporaryFileIsRemoved()
    {
        if (!OperatingSystem.IsWindows()) return;
        Extract("old library");
        string path = Path.Combine(directory, "native.bin");
        using var locked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var error = Assert.Throws<IOException>(() => Extract("new library"));
        Assert.Contains("close other KissE instances", error.Message);
        Assert.Equal("old library", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }
}
