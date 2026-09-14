using Solicen.UE4;
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
        public static EngineVersion Version = EngineVersion.VER_UE4_AUTOMATIC_VERSION_PLUS_ONE;
        public static string MappingsPath = string.Empty;
        public static string ExceptionMessage = string.Empty;
        public static string AES = string.Empty;

        public static void SetVersion(EngineVersion version) => Version = version;

        #region Virtual Provider Zone
        public static bool IsVirtual => provider!=null? true: false;
        private static UnrealArchiveReader provider = null;
        public static string FullPakPath = string.Empty;

        public static void CloseProvider()
        {
            provider.Close();
            provider = null;
        }
        public static void SetProvider(UnrealArchiveReader _provider)
        {
            if (provider == null && provider != _provider)
            {
                provider = _provider;
            } 
        }
        #endregion

        public static UAsset LoadAsset(string asset)
        {
            if (provider!=null)
            {
                try
                {
                    return provider.FromGameFile(asset);
                }
                catch (Exception ex)
                {
                    CLI.Console.WriteLine("[Red][ERR] [White]Failed to load asset.");
                    System.Console.WriteLine($" - {ex.Message}");
                }

                return null;
            }
            else
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
                        System.Console.WriteLine($" - {ex.Message}");
                    }
                }
                return null;
            }

        }
    }
}
