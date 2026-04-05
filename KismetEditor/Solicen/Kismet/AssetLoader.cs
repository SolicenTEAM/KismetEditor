using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UAssetAPI;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;

namespace Solicen.Kismet
{
    public class AssetLoader
    {
        public static UAssetAPI.UnrealTypes.EngineVersion Version = UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_AUTOMATIC_VERSION_PLUS_ONE;
        public static string MappingsPath = string.Empty;
        public static string ExceptionMessage = string.Empty;

        public static void SetVersion(EngineVersion version)
        {
            Version = version;
        }
        public static UAsset LoadAsset(string asset)
        {
            if (MappingsPath != string.Empty)
            {
                try
                {
                    ExceptionMessage = string.Empty;
                    return new UAsset(asset, Version, new Usmap(MappingsPath));
                }
                catch (Exception ex)
                {
                    CLI.Console.WriteLine("[Red][ERR] [White]Failed to load asset.");
                    ExceptionMessage = ex.Message;
                    System.Console.WriteLine($" - {ex.Message}");
                }

            }
            else
            {
                try
                {
                    ExceptionMessage = string.Empty;
                    return new UAsset(asset, Version);
                }
                catch (Exception ex)
                {
                    ExceptionMessage = ex.Message;
                    CLI.Console.WriteLine("[Red][ERR] [White]Failed to load asset.");
                    System.Console.WriteLine($" - ");
                }
            }
            return null;
        }
    }
}
