using Solicen.UE4;
using System;
using System.IO;
using System.Linq;
using UAssetAPI;
using UAssetAPI.UnrealTypes;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: IoStore.Smoke.exe <archive> <UE version, e.g. 4.27> [round-trip output directory] [usmap]");
    return 2;
}
var version = Enum.Parse<EngineVersion>("VER_UE" + args[1].Replace('.', '_'));
using var provider = new UnrealArchiveReader(args[0], version, args.Length > 3 ? args[3] : null);
var assets = provider.GetAssets();
Console.WriteLine($"FILES={provider.Files.Length} ASSETS={assets.Length}");
foreach (string file in assets.Take(15)) Console.WriteLine(file);
if (assets.Length == 0) throw new Exception("No assets mounted.");
int parsed = 0;
int bytecodePackages = 0;
foreach (string file in assets.OrderByDescending(p => p.Contains("/Widgets/", StringComparison.OrdinalIgnoreCase) || p.Contains("/Blueprints/", StringComparison.OrdinalIgnoreCase)).Take(300))
{
    var asset = provider.FromGameFile(file);
    if (asset?.Exports == null || asset.Exports.Count == 0) continue;
    if (asset.Exports.OfType<UAssetAPI.ExportTypes.FunctionExport>().Any(e => e.ScriptBytecode?.Length > 0)) bytecodePackages++;
    Console.WriteLine($"PARSED={file} EXPORTS={asset.Exports.Count} TYPES={string.Join(",", asset.Exports.Select(e => e.GetType().Name).Distinct())}");
    byte[] header = provider.ReadFile(file.ToUpperInvariant().Replace('/', '\\'));
    if (header == null || BitConverter.ToUInt32(header) != 0x9E2A83C1) throw new Exception("Legacy header magic mismatch.");
    if (provider.ReadFile(Path.ChangeExtension(file, ".uexp"))?.Length == 0) throw new Exception("Missing exports payload.");
    if (args.Length > 2)
    {
        Directory.CreateDirectory(args[2]);
        string output = Path.Combine(args[2], Path.GetFileName(file));
        asset.Write(output);
        var reloaded = new UAsset(output, version, args.Length > 3 ? new UAssetAPI.Unversioned.Usmap(args[3]) : null);
        if (reloaded.Exports.Count != asset.Exports.Count) throw new Exception("Round-trip export count mismatch.");
        Console.WriteLine($"ROUNDTRIP={output}");
    }
    if (++parsed >= 5 && bytecodePackages > 0) break;
}
if (parsed < 5 || bytecodePackages == 0) throw new Exception($"Parsed {parsed} packages, {bytecodePackages} with bytecode; at least 5 packages and one with bytecode required.");
provider.MaxAssetBytes = 1;
try
{
    provider.ReadFile(assets[0]);
    throw new Exception("Asset size limit was not enforced.");
}
catch (InvalidDataException ex) when (ex.Message.Contains("limit", StringComparison.OrdinalIgnoreCase)) { }
provider.Close();
provider.Close();
using var reopened = new UnrealArchiveReader(Path.ChangeExtension(args[0], ".UTOC"), version, args.Length > 3 ? args[3] : null);
if (!provider.IsClosed || provider.Files.Length != 0) throw new Exception("Unexpected closed-provider state.");
if (reopened.GetAssets().Length != assets.Length) throw new Exception("Reopen asset count mismatch.");
Console.WriteLine("SMOKE PASS");
return 0;
