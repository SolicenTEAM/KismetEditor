using Solicen.UE4;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UAssetAPI.UnrealTypes;
using Xunit;

namespace KismetEditor.Tests;

public sealed class PackingTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "KissE-packing-tests-" + Guid.NewGuid());
    public PackingTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);

    [Theory]
    [InlineData("out.ucas")]
    [InlineData("out.zip")]
    [InlineData("out")]
    public void RejectsUnsupportedOutputExtensions(string name) =>
        Assert.Throws<ArgumentException>(() => ArchivePacker.ValidateRequest(Path.Combine(directory, name), EngineVersion.VER_UE4_27));

    [Fact]
    public void RejectsUnsupportedIoStoreVersion() =>
        Assert.Throws<ArgumentException>(() => ArchivePacker.ValidateRequest(Path.Combine(directory, "out.utoc"), EngineVersion.VER_UE4_18));

    [Theory]
    [InlineData("pak")]
    [InlineData("utoc")]
    [InlineData("ucas")]
    public void ExistingIoStoreCompanionsAreNeverOverwritten(string extension)
    {
        string target = Path.Combine(directory, "out.utoc");
        string existing = Path.ChangeExtension(target, extension);
        File.WriteAllText(existing, "keep me");
        Assert.Throws<IOException>(() => ArchivePacker.ValidateRequest(target, EngineVersion.VER_UE4_27));
        Assert.Equal("keep me", File.ReadAllText(existing));
    }

    [Fact]
    public void InputTreeCannotContainItsOutput()
    {
        File.WriteAllText(Path.Combine(directory, "file.txt"), "source");
        Assert.Throws<ArgumentException>(() => ArchivePacker.PackDirectory(directory, Path.Combine(directory, "out.pak"), EngineVersion.VER_UE4_27));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData("Widget.uasset")]
    [InlineData("UI/Widget.uasset")]
    [InlineData("Content/UI/Widget.uasset")]
    public void RejectsAssetsWithoutCookedProjectPath(string path) => Assert.Throws<InvalidDataException>(() => ArchivePacker.ValidateAssetPath(path));

    [Theory]
    [InlineData("Example/Content/UI/Widget.uasset")]
    [InlineData("Engine/Content/Example.umap")]
    [InlineData("Example/Plugins/MyPlugin/Content/Widget.uasset")]
    [InlineData("Example/Content/Localization/Game.locres")]
    public void AcceptsCookedAssetAndNonAssetPaths(string path) => ArchivePacker.ValidateAssetPath(path);

    [Theory]
    [InlineData("Example/Content/UI/Menu.uasset")]
    [InlineData("Example/Plugins/MyPlugin/Content/UI/Menu.uasset")]
    [InlineData("Engine/Plugins/Marketplace/MyPlugin/Content/Menu.uasset")]
    public void InfersProjectRootWithoutDroppingPluginMountPaths(string path)
    {
        string asset = Path.Combine(directory, path.Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal(directory, global::Solicen.CLI.CLIHandler.FindCookedRoot(asset));
    }

    [Fact]
    public void RejectsEscapingManifestBeforeLoadingNativeCode()
    {
        string root = Path.Combine(directory, "root");
        Directory.CreateDirectory(root);
        string outside = Path.Combine(directory, "outside.txt");
        File.WriteAllText(outside, "outside");
        Assert.Throws<InvalidDataException>(() => ArchivePacker.PackFiles(root, new[] { outside }, Path.Combine(directory, "out.pak"), EngineVersion.VER_UE4_27));
        Assert.False(File.Exists(Path.Combine(directory, "out.pak")));
    }

    [Fact]
    public void PublishOrderPlacesUtocLast() => Assert.Equal(new[] { "mod.ucas", "mod.pak", "mod.utoc" }, ArchivePacker.OutputPaths("mod.utoc"));

    [Fact]
    public void PakFolderRoundTripPreservesEveryFile()
    {
        using var resource = typeof(UnrealArchiveReader).Assembly.GetManifestResourceStream(NativeLibraryResources.BridgeResource);
        if (resource == null) return;
        string root = Path.Combine(directory, "root with spaces");
        Directory.CreateDirectory(Path.Combine(root, "Example", "Content"));
        string file = Path.Combine(root, "Example", "Content", "Text.txt");
        File.WriteAllText(file, "Привет, мир", new UTF8Encoding(false));
        string target = Path.Combine(directory, "mod_P.pak");
        ArchivePacker.PackDirectory(root, target, EngineVersion.VER_UE4_27);
        using var pak = new UnrealArchiveReader(target, EngineVersion.VER_UE4_27);
        Assert.Equal(File.ReadAllBytes(file), pak.ReadFile("Example/Content/Text.txt"));
        Assert.Empty(Directory.GetDirectories(directory, ".kisse-pack-*"));
    }

    [Fact]
    public void PackManifestDoesNotIncludeUnrelatedFiles()
    {
        using var resource = typeof(UnrealArchiveReader).Assembly.GetManifestResourceStream(NativeLibraryResources.BridgeResource);
        if (resource == null) return;
        string root = Path.Combine(directory, "prepared");
        Directory.CreateDirectory(root);
        string selected = Path.Combine(root, "current.txt");
        File.WriteAllText(selected, "current");
        File.WriteAllText(Path.Combine(root, "stale.txt"), "unrelated");
        string output = Path.Combine(directory, "current.pak");
        ArchivePacker.PackFiles(root, new[] { selected }, output, EngineVersion.VER_UE4_27);
        using var pak = new UnrealArchiveReader(output, EngineVersion.VER_UE4_27);
        Assert.Single(pak.Files);
        Assert.Null(pak.ReadFile("stale.txt"));
    }

    [Fact]
    public void FailedIoStoreConversionPublishesNoPartialFiles()
    {
        string root = Path.Combine(directory, "root", "Example", "Content");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Broken.uasset"), "not an asset");
        string target = Path.Combine(directory, "broken.utoc");
        Assert.ThrowsAny<Exception>(() => ArchivePacker.PackDirectory(Path.Combine(directory, "root"), target, EngineVersion.VER_UE4_27));
        Assert.All(ArchivePacker.OutputPaths(target), path => Assert.False(File.Exists(path)));
        Assert.Empty(Directory.GetDirectories(directory, ".kisse-pack-*"));
    }

    [Theory]
    [InlineData("--pack=out.pak")]
    [InlineData("--pack=")]
    [InlineData("--pack=out.pak --version=not-a-version")]
    [InlineData("--pack=out.pak --version=4.27 --unknown-flag")]
    public async Task CliRejectsMissingVersionAndMalformedArguments(string flags)
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "KissE.exe"))
        {
            WorkingDirectory = directory, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(directory);
        foreach (string flag in flags.Split(' ')) start.ArgumentList.Add(flag);
        using var process = Process.Start(start);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (TimeoutException) { process.Kill(true); throw; }
        Assert.Equal(1, process.ExitCode);
        string log = await stdout + await stderr;
        Assert.False(File.Exists(Path.Combine(directory, "out.pak")), log);
    }
}
