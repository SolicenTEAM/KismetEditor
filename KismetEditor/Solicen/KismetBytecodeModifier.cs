using System;
using UAssetAPI;
using System.IO;

using static Solicen.Kismet.KismetExtension;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Collections.Generic;
namespace Solicen.Kismet
{
    class BytecodeModifier
    {
        public static UAssetAPI.UnrealTypes.EngineVersion Version = UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_18;

        public static Dictionary<string,string> TranslateFromCSV(string filepath)
        {
            Dictionary<string, string> csvValues = new Dictionary<string, string>();
            var lines = File.ReadAllLines(filepath);
            foreach(var l in lines)
            {
                if ("Original | Translation" == l) continue;
                if (l.StartsWith("//")) continue;
                var values = l.Split('|');
                var key = values[0].Trim();
                var value = values[1].Trim();

                csvValues.Add(key, value);
            }
            return csvValues;
        }

        public static void WriteStringsFile(string assetPath)
        {
            UAsset asset = new UAsset(assetPath, Version);
            var json = asset.SerializeJson(Newtonsoft.Json.Formatting.Indented);
            JObject jsonObject = JObject.Parse(json);
            var ubergraph = KismetExtension.GetUbergraphJson(UAsset.DeserializeJson(json));
            var strings = KismetObject.ToCSV(ubergraph);
            File.WriteAllText($"{Environment.CurrentDirectory}\\{Path.GetFileNameWithoutExtension(assetPath)}_strings.csv", string.Join("\n", strings));
        }

        public static void ModifyAsset(string path, Dictionary<string, string> replacement)
        {
            // Загружаем uasset файл с помощью UAssetAPI
            UAsset asset = new UAsset(path, Version);
            var json = asset.SerializeJson(Newtonsoft.Json.Formatting.Indented);
            JObject jsonObject = JObject.Parse(json);

            var kismet = new KismetExtension(jsonObject);

            Console.WriteLine("Replace string");
            kismet.ReplaceAllInst(replacement);
            jsonObject = kismet.JsonObject;

            var _json = jsonObject.ToString();

            if (File.Exists(path)) File.Copy(path, path + ".bak", true);
            if (File.Exists(Path.ChangeExtension(path, "uexp"))) File.Copy(Path.ChangeExtension(path, "uexp"), Path.ChangeExtension(path, "uexp") + ".bak", true);
            UAsset.DeserializeJson(_json).Write(path);

            /*
            File.WriteAllText($"{Environment.CurrentDirectory}\\JsonModded.json", _json);

            var ubergraph = KismetExtension.GetUbergraphJson(UAsset.DeserializeJson(_json));
            File.WriteAllText($"{Environment.CurrentDirectory}\\JsonModdedUbergraph.json", ubergraph.ToString());
            */

            Console.WriteLine($"Successfully modified [{kismet.ModifiedInst}] instructions.");
        }
    }

}

