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
            Parallel.For(0, resultSize, (i) =>
            {
                if (i < 7) s += "a";
                else s += "1";
            });
            return s;
        }

        public static string FillString(string oldString)
        {
            string s = ""; // Заполнитель строки
            int all = (9 + 9 + (oldString.Length + 2)) - 7;
            Parallel.For(0, all, (i) =>
            {
                if (i < 7) s += "a";
                else s += "1";
            });
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

            var ubergraphExport = exports
                .OfType<JObject>()
                .FirstOrDefault(e => e.ContainsKey("ScriptBytecode") && e["ScriptBytecode"] is JArray arr && arr.Count > 0);

            return ubergraphExport?["ScriptBytecode"] as JArray;
        }

        public static KismetExpression[] GetUbergraph(UAsset asset)
        {
            var ubergraph = asset.Exports.FirstOrDefault(x => x.ObjectName.ToString().StartsWith("ExecuteUbergraph"));
            if (ubergraph != null)
            {
                if (ubergraph is StructExport structExport) return structExport.ScriptBytecode;
            }
            return null;

        }


    }
}