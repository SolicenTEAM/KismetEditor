using Solicen.Kismet;
using Solicen.UE4;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UAssetAPI.UnrealTypes;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace KismetEditor.Tests;

public class ArchiveTests
{
    [Theory]
    [InlineData("test.pak", true, false)]
    [InlineData("test.PAK", true, false)]
    [InlineData("test.ucas", true, true)]
    [InlineData("test.UTOC", true, true)]
    [InlineData("test.uasset", false, false)]
    [InlineData("test.ucas.bak", false, false)]
    public void RecognizesArchiveFormats(string path, bool archive, bool ioStore)
    {
        Assert.Equal(archive, UnrealArchiveReader.IsArchivePath(path));
        Assert.Equal(ioStore, UnrealArchiveReader.IsIoStorePath(path));
    }

    [Fact]
    public void RejectsMissingCompanionWithoutLoadingNativeCode()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ucas");
        Assert.Throws<FileNotFoundException>(() => new UnrealArchiveReader(path, EngineVersion.VER_UE4_27));
    }

    [Fact]
    public void PakReadsNormalizedVirtualPathsAndClosesIdempotently()
    {
        string path = CreatePak();
        try
        {
            using var reader = new UnrealArchiveReader(path, EngineVersion.VER_UE4_27);
            Assert.Single(reader.Files);
            Assert.Equal("hello", Encoding.UTF8.GetString(reader.ReadFile("GAME\\CONTENT\\TEST.TXT")));
            Assert.Null(reader.ReadFile("Game/Content/Missing.txt"));
            reader.MaxAssetBytes = 1;
            Assert.Throws<InvalidDataException>(() => reader.ReadFile("Game/Content/Test.txt"));
            reader.Close();
            reader.Close();
            Assert.Empty(reader.Files);
            Assert.Throws<ObjectDisposedException>(() => reader.ReadFile("Game/Content/Test.txt"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReplacingAndClosingProviderReleasesPreviousArchive()
    {
        string path = CreatePak();
        try
        {
            var first = new UnrealArchiveReader(path, EngineVersion.VER_UE4_27);
            var second = new UnrealArchiveReader(path, EngineVersion.VER_UE4_27);
            AssetLoader.SetProvider(first);
            Assert.True(AssetLoader.IsVirtual);
            AssetLoader.SetProvider(second);
            Assert.True(first.IsClosed);
            AssetLoader.CloseProvider();
            AssetLoader.CloseProvider();
            Assert.True(second.IsClosed);
            Assert.False(AssetLoader.IsVirtual);
        }
        finally { AssetLoader.CloseProvider(); File.Delete(path); }
    }

    [Theory]
    [InlineData("short")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void RejectsInvalidAesAndReleasesFile(string aes)
    {
        string path = CreatePak();
        try
        {
            Assert.Throws<ArgumentException>(() => new UnrealArchiveReader(path, EngineVersion.VER_UE4_27, AesHexKey: aes));
            using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false, "requires")]
    [InlineData(true, "matching .utoc")]
    public void CliRequiresVirtualAndReportsMissingCompanion(bool useVirtual, string expected)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ucas");
        File.WriteAllBytes(path, Array.Empty<byte>());
        var previousOutput = Console.Out;
        int previousExit = Environment.ExitCode;
        var cli = typeof(AssetLoader).Assembly.GetType("Solicen.CLI.CLIHandler");
        var config = cli.GetNestedType("Config", System.Reflection.BindingFlags.Public);
        config.GetField("Virtual").SetValue(null, false);
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            var args = useVirtual ? new[] { path, "--virtual", "--version=4.27" } : new[] { path };
            cli.GetMethod("ProcessProgram").Invoke(null, new object[] { args });
            Assert.Contains(expected, output.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, Environment.ExitCode);
            Assert.False(AssetLoader.IsVirtual);
        }
        finally
        {
            config.GetField("Virtual").SetValue(null, false);
            Console.SetOut(previousOutput);
            Environment.ExitCode = previousExit;
            File.Delete(path);
        }
    }

    [Fact]
    public void MissingSidecarIsExtractedOnDemandOrReportedWhenNotEmbedded()
    {
        string directory = Path.Combine(Path.GetTempPath(), "KissE-test-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            CopyRuntime(directory);
            string input = Path.Combine(directory, "empty.ucas");
            File.WriteAllBytes(input, Array.Empty<byte>());
            File.WriteAllBytes(Path.ChangeExtension(input, ".utoc"), Array.Empty<byte>());
            var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(directory, "KissE.exe"))
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = directory,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (string arg in new[] { input, "--virtual", "--version=4.27" }) start.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(start);
            string output = process.StandardOutput.ReadToEnd();
            Assert.True(process.WaitForExit(10000));
            Assert.Equal(1, process.ExitCode);
            using var resource = typeof(UnrealArchiveReader).Assembly.GetManifestResourceStream(NativeLibraryResources.BridgeResource);
            string bridge = Path.Combine(directory, "kisse_iostore.dll");
            if (resource == null)
            {
                Assert.True(output.Contains("scripts/publish-portable.ps1"), output + process.StandardError.ReadToEnd());
                Assert.False(File.Exists(bridge));
            }
            else
            {
                byte[] expected = SHA256.HashData(resource);
                string cached = Path.Combine(NativeLibraryResources.CacheDirectory, Convert.ToHexString(expected), "kisse_iostore.dll");
                using var extracted = File.OpenRead(cached);
                Assert.Equal(expected, SHA256.HashData(extracted));
                Assert.False(File.Exists(bridge));
                Assert.DoesNotContain("bridge is missing", output);
            }
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void PakDoesNotExtractEmbeddedLibraries()
    {
        string directory = Path.Combine(Path.GetTempPath(), "KissE-test-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        string pak = CreatePak();
        try
        {
            CopyRuntime(directory);
            var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(directory, "KissE.exe"))
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = directory,
                StandardOutputEncoding = Encoding.UTF8
            };
            foreach (string arg in new[] { pak, Path.Combine(directory, "strings.json"), "--virtual", "--version=4.27" }) start.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(start);
            string output = process.StandardOutput.ReadToEnd();
            Assert.True(process.WaitForExit(10000));
            Assert.True(process.ExitCode == 0, output + process.StandardError.ReadToEnd());
            Assert.False(File.Exists(Path.Combine(directory, "kisse_iostore.dll")));
            Assert.False(File.Exists(Path.Combine(directory, "oo2core_9_win64.dll")));
        }
        finally { File.Delete(pak); Directory.Delete(directory, true); }
    }

    private static void CopyRuntime(string directory)
    {
        foreach (string source in Directory.GetFiles(AppContext.BaseDirectory))
        {
            string name = Path.GetFileName(source);
            if (name.Equals("kisse_iostore.dll", StringComparison.OrdinalIgnoreCase) || name.Equals("oo2core_9_win64.dll", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.EndsWith(".dll") || name.StartsWith("KissE.")) File.Copy(source, Path.Combine(directory, name));
        }
    }

    // Small, deterministic uncompressed PAK v3 fixture, independent of proprietary game data.
    private static string CreatePak()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pak");
        byte[] payload = Encoding.UTF8.GetBytes("hello");
        byte[] hash = SHA1.HashData(payload);
        using var output = new BinaryWriter(File.Create(path));
        output.Write(new byte[1024]); // Let PakReader probe newer footer sizes without seeking before the file.
        long entryOffset = output.BaseStream.Position;
        void Entry(BinaryWriter writer)
        {
            writer.Write(entryOffset);
            writer.Write((long)payload.Length);
            writer.Write((long)payload.Length);
            writer.Write(0);
            writer.Write(hash);
            writer.Write((byte)0);
            writer.Write(0);
        }
        void FString(BinaryWriter writer, string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text + "\0");
            writer.Write(data.Length);
            writer.Write(data);
        }
        Entry(output);
        output.Write(payload);
        long indexOffset = output.BaseStream.Position;
        using var indexStream = new MemoryStream();
        using var index = new BinaryWriter(indexStream, Encoding.UTF8, true);
        FString(index, "../../../");
        index.Write(1);
        FString(index, "Game/Content/Test.txt");
        Entry(index);
        byte[] bytes = indexStream.ToArray();
        output.Write(bytes);
        output.Write(0x5A6F12E1u);
        output.Write(3u);
        output.Write(indexOffset);
        output.Write((long)bytes.Length);
        output.Write(SHA1.HashData(bytes));
        return path;
    }
}
