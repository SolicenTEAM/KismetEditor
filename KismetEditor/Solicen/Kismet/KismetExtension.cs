// Выполнено Денисом Солиценом в 2024 году
// : https://github.com/DenisSolicen
// Техническая помощь с заполнителем строки ув. Ambi
// Основано на UAssetAPI и ресурсах Unreal Engine 4

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UAssetAPI.ExportTypes;
using UAssetAPI.Kismet.Bytecode;
using UAssetAPI.Kismet;
using UAssetAPI;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace Solicen.Kismet
{
    internal class KismetExtension
    {
        public static JArray GetUbergraphJson(UAsset asset)
        {
            var ubergraph = GetUbergraph(asset);
            if (ubergraph != null)
            {
                KismetSerializer.asset = asset;
                var JArray = KismetSerializer.SerializeScript(ubergraph);
                return JArray;
            }
            return null;
        }

        const int EX_ConstString = 2; // Размер инструкции ConstString 
        const int EX_Jump = 5; // Размер инструкции прыжка
        public static string PlaceholderBySize(int size)
        {
            string s = ""; // Заполнитель строки
            // Отнимает от общего размера магическое число 7
            int resultSize = size - (EX_ConstString + EX_Jump);
            for (int i = 0; i < resultSize; i++)
            {
                if (i < 7) s += "a";
                else s += "1";
            }
            return s;
        }
        public static JArray GetUbergraphSerialized(UAsset asset)
        {
            return new KismetExpressionSerializer(asset).SerializeScript(GetUbergraph(asset));
        }
        public static JArray GetUbergraph(JObject jsonObject)
        {
            JArray exports = (JArray)jsonObject["Exports"];
            if (exports == null) return null;

            // Prefer ObjectName "ExecuteUbergraph*" (stable, matches the UAsset overload
            // below); fall back to the size heuristic when ObjectName isn't in the JSON.
            var byName = exports
                .OfType<JObject>()
                .FirstOrDefault(e => e.ContainsKey("ScriptBytecode")
                    && e["ObjectName"]?.ToString() is string name
                    && name.StartsWith("ExecuteUbergraph", StringComparison.Ordinal));
            if (byName != null)
            {
                return byName["ScriptBytecode"] as JArray;
            }

            var ubergraphExport = exports
                .OfType<JObject>()
                .FirstOrDefault(e => e.ContainsKey("ScriptBytecode")
                && int.Parse(e["ScriptBytecodeSize"].ToString()) > 100);
            return ubergraphExport?["ScriptBytecode"] as JArray;
        }

        public static int GetExportCount(UAsset asset)
        {
            if (asset == null) return 0;
            return asset.Exports.Count;
        }

        public static KismetExpression[] GetUbergraph(UAsset asset)
        {
            if (asset != null)
            {
                var ubergraph = asset.Exports.FirstOrDefault(x => x.ObjectName.ToString().StartsWith("ExecuteUbergraph"));
                if (ubergraph != null)
                {
                    if (ubergraph is StructExport structExport) return structExport.ScriptBytecode;
                }
            }
            return null;

        }

        /// <summary>
        /// Returns every export with a non-empty ScriptBytecode, paired with the corresponding
        /// JArray inside <paramref name="jsonObject"/> (the live, mutable copy used for
        /// replacement) and the deserialized <c>KismetExpression[]</c> from
        /// <paramref name="asset"/> (used for size/offset calculations).
        /// Used by --patch-all-functions to extend the replace pipeline beyond the single
        /// ExecuteUbergraph_* function.
        /// </summary>
        public static List<(string name, JArray jsonBytecode, KismetExpression[] exprBytecode)>
            GetAllScriptBytecode(JObject jsonObject, UAsset asset)
        {
            var result = new List<(string, JArray, KismetExpression[])>();
            if (asset == null || jsonObject == null)
            {
                return result;
            }

            var assetByName = new Dictionary<string, KismetExpression[]>();
            foreach (var ex in asset.Exports)
            {
                if (ex is StructExport se && se.ScriptBytecode != null && se.ScriptBytecode.Length > 0)
                {
                    assetByName[ex.ObjectName.ToString()] = se.ScriptBytecode;
                }
            }

            if (jsonObject["Exports"] is not JArray jExports)
            {
                return result;
            }

            foreach (var jExp in jExports.OfType<JObject>())
            {
                if (!jExp.ContainsKey("ScriptBytecode"))
                {
                    continue;
                }
                var name = jExp["ObjectName"]?.ToString();
                if (name == null)
                {
                    continue;
                }
                if (!assetByName.TryGetValue(name, out var expr))
                {
                    continue;
                }
                if (jExp["ScriptBytecode"] is not JArray jBc || jBc.Count == 0)
                {
                    continue;
                }
                result.Add((name, jBc, expr));
            }
            return result;
        }
    }
}