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
        public static bool IsVirtual => provider != null && !provider.IsClosed;
        private static UnrealArchiveReader provider = null;
        public static string FullPakPath = string.Empty;

        public static void CloseProvider()
        {
            var previous = provider;
            provider = null;
            previous?.Close();
        }
        public static void SetProvider(UnrealArchiveReader _provider)
        {
            if (ReferenceEquals(provider, _provider)) return;
            CloseProvider();
            provider = _provider;
        }
        #endregion

        public static UAsset LoadAsset(string asset)
        {
            if (provider!=null)
            {
                try
                {
                    var result = provider.FromGameFile(asset);
                    ExceptionMessage = UnrealArchiveReader.ExceptionMessage;
                    return result;
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
