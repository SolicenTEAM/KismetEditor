using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using UAssetAPI.Kismet;
using UAssetAPI.Kismet.Bytecode;

namespace Solicen.Kismet
{
    internal static class InstructionSearchSize
    {
        public static int GetSize(UAssetAPI.UAsset asset, JObject expression, KismetExpression[] ubergraph)
        {
            if (expression == null) return 0;
            int totalSize = 0; KismetExpression expAsset = null;
            var expJson = expression.ToString();
            if (expJson.Contains("$type"))
            {
                expAsset = ubergraph.First(x =>
                    JTokenEquals(
                        JToken.Parse(asset.SerializeJsonObject(x, Formatting.None)),
                        expression));
            }
            else
            {
                expAsset = ubergraph.First(x => x.ToString() == expJson);
            }

            KismetSerializer.asset = asset;
            KismetSerializer.SerializeExpression(expAsset, ref totalSize, true);
            return totalSize;
        }

        private static bool JTokenEquals(JToken a, JToken b)
        {
            if (JToken.DeepEquals(a, b)) return true;

            // FPackageIndex deserialization artifact: FPackageIndex(Index=0)
            // should be null — treat null ↔ 0 as equal
            if (IsNull(a) && IsZero(b)) return true;
            if (IsNull(b) && IsZero(a)) return true;

            if (a is JObject objA && b is JObject objB)
            {
                if (objA.Count != objB.Count) return false;
                foreach (var prop in objA.Properties())
                {
                    var propB = objB.Property(prop.Name);
                    if (propB == null) return false;
                    if (!JTokenEquals(prop.Value, propB.Value)) return false;
                }
                return true;
            }

            if (a is JArray arrA && b is JArray arrB)
            {
                if (arrA.Count != arrB.Count) return false;
                for (int i = 0; i < arrA.Count; i++)
                {
                    if (!JTokenEquals(arrA[i], arrB[i])) return false;
                }
                return true;
            }

            return false;
        }

        private static bool IsNull(JToken t) => t.Type == JTokenType.Null;
        private static bool IsZero(JToken t)
        {
            if (t.Type == JTokenType.Integer) return (int)t == 0;
            if (t.Type == JTokenType.Float) return (double)t == 0;
            return false;
        }
    }
}
