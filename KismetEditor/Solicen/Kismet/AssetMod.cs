using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UAssetAPI;
using UAssetAPI.UnrealTypes;

namespace Solicen.Kismet
{
    public class AssetMod
    {
        /// <summary>
        /// Return only frist texture byte array.
        /// </summary>
        /// <param name="asset"></param>
        /// <returns></returns>
        public static byte[] GetTextureData(UAsset asset)
        {
            if (asset == null) return Array.Empty<byte>();
            foreach (var export in asset.Exports)
            {
                if (export.Extras.Any())
                {
                    return export.Extras.ToArray();
                }
            }

            return Array.Empty<byte>();
        }

        public static void ReplaceTexture(ref UAsset asset, byte[] texture)
        {
            if (asset == null) return;
            for (int i = 0; i < asset.Exports.Count; i++)
            {
                if (asset.Exports[i].Extras.Any())
                {
                    asset.Exports[i].Extras = texture;
                    break;
                }
            }
        }

        public static void SaveAsset(UAsset asset, string outputPath)
        {
            asset.Write(outputPath);
        }
    }
}
