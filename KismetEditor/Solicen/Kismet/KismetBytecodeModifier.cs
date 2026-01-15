using KismetEditor.Solicen;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Solicen.JSON;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UAssetAPI;

namespace Solicen.Kismet
{
    class BytecodeModifier
    {
        public static string MappingsPath = string.Empty;
        public static bool AllowCreateBak = true;
        public static UAsset Asset; static bool UseBak = true;
        public static UAssetAPI.UnrealTypes.EngineVersion Version = UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_18;
        public static void ModifyAsset(string path, Dictionary<string, string> replacement, bool allowTable = false)
        {
            if (AllowCreateBak)
            {
                if (UseBak)
                {
                    var uexpPath = Path.ChangeExtension(path, ".uexp");
                    if (File.Exists(path + ".bak"))
                    {
                        File.Copy(uexpPath + ".bak", uexpPath, true);
                        File.Copy(path + ".bak", path, true);
                    }
                }
            }

            // Загружаем uasset файл с помощью UAssetAPI
            Asset = new UAsset(path, Version);
            var json = Asset.SerializeJson(Formatting.Indented);
            JObject jsonObject = JObject.Parse(json);

            // Сначала обрабатываем Ubergraph
            var ubergraph = KismetExtension.GetUbergraphJson(Asset);
            if (ubergraph != null)
            {
                KismetProcessor.ReplaceAllInUbergraph(jsonObject, replacement, Asset);
                Asset = UAsset.DeserializeJson(jsonObject.ToString());
            }
            // Теперь обрабатываем каждое свойство StrProperty
            KismetProcessor.ReplaceAllInStrProperties(replacement, Asset);
            if (allowTable)
                KismetProcessor.ReplaceAllInStringTable(replacement, Asset);


            // --- Отладочный вывод и безопасный режим (без сохранения) ---
            if (ProgramProcessor.DebugMode)
            {
                var _json = jsonObject.ToString();
                // Сохраняем итоговый JSON в файл для ручной проверки
                File.WriteAllText($"{Environment.CurrentDirectory}\\Ubergraph.json", _json);
                Console.WriteLine($"[INF] The modified JSON is saved in: {Environment.CurrentDirectory}\\Ubergraph.json");
            }

            #region Сохранить .bak файлы
            if (AllowCreateBak)
            {
                if (!File.Exists(Path.ChangeExtension(path, ".bak")))
                {
                    if (File.Exists(path)) File.Copy(path, path + ".bak", true);
                    if (File.Exists(Path.ChangeExtension(path, "uexp"))) File.Copy(Path.ChangeExtension(path, "uexp"), Path.ChangeExtension(path, "uexp") + ".bak", true);
                }
            }
            #endregion

            Asset.Write(path);
            Console.WriteLine($"\n[INF] Total replacements: {KismetProcessor.ModifiedCount}");
            Console.WriteLine($"[SUCCESS] The modified asset was successfully saved in: {path}");
            
        }
    }

}
