using KismetEditor.Solicen;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UAssetAPI;

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
                if (l.Contains("OriginalText") && l.Contains("Translation")) continue; // Пропускаем строку заголовка
                if (l.StartsWith("//")) continue; // Если строка начинается с символов комментирования - пропустить

                try
                {
                    var values = l.Split('|');
                    var key = values[0].Trim();
                    var value = values[1].Trim();

                    value = StringHelper.UnEscapeKey(value);

                    try { if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value)) csvValues.Add(key, value); }
                    catch (Exception ex) { Console.WriteLine($"[{key}] Элемент с таким ключом уже был добавлен."); }
                }
                catch
                {

                }




            }
            return csvValues;
        }

        public static void ExtractAndWriteCSV(string assetPath, string _fileName = "")
        {

            UAsset asset = new UAsset(assetPath, Version);
            var json = asset.SerializeJson(Newtonsoft.Json.Formatting.Indented);
            var ubergraph = KismetExtension.GetUbergraphSerialized(asset);

            if (ProgramProcessor.DebugMode)
            {
                var _dummyJson = KismetExtension.GetUbergraphJson(asset); //JsonConvert.SerializeObject(ubergraph, Formatting.Indented);
                File.WriteAllText($"{Environment.CurrentDirectory}\\{Path.GetFileNameWithoutExtension(assetPath)}_DUMMY.json", _dummyJson.ToString());

            }

            var strings = MapParser.ParseAsCSV(ubergraph);
            _fileName = _fileName == "" ? Path.GetFileNameWithoutExtension(assetPath) : _fileName;
            if (strings.Length == 0) return;

            #region Запись CSV
            var csvFilePath = $"{Environment.CurrentDirectory}\\{_fileName}.csv";
            Console.WriteLine($"Write CSV to {csvFilePath}");
            if (File.Exists(csvFilePath))
            {
                // Если CSV уже существует, просто добавить новые строки
                var csv = File.ReadAllLines(csvFilePath);
                var lines = strings.Except(csv.Select(x => x.Split('|')[0])); lines.ToList().Add("\n");
                File.AppendAllLines(csvFilePath, lines);
            }           
            else
            {
                // Иначе просто записать строки в файл
                File.WriteAllText(csvFilePath, string.Join("\n", strings));
            }

            #endregion
        }


        static bool UseBak = true;
        public static void ModifyAsset(string path, Dictionary<string, string> replacement)
        {
            if (UseBak)
            {
                var uexpPath = Path.ChangeExtension(path, ".uexp");
                if (File.Exists(path+".bak"))
                {
                    File.Copy(uexpPath+".bak", uexpPath, true);
                    File.Copy(path + ".bak", path, true);
                }

            }

            // Загружаем uasset файл с помощью UAssetAPI
            UAsset asset = new UAsset(path, Version);
            var json = asset.SerializeJson(Formatting.Indented);
            JObject jsonObject = JObject.Parse(json);
            Console.WriteLine("[Replace string]");
            var ubergraphExpressions = KismetExtension.GetUbergraphJson(asset);
            if (ubergraphExpressions == null)
            {
                Console.WriteLine("[ERROR] Не удалось получить JArray уберграфа из ассета.");
                return;
            }

            KismetProcessor.ReplaceAll(jsonObject, replacement, asset);

            // --- Отладочный вывод и безопасный режим (без сохранения) ---

            var ModdedAsset = UAsset.DeserializeJson(jsonObject.ToString());
            if (ProgramProcessor.DebugMode)
            {
                var _json = jsonObject.ToString();
                // Сохраняем итоговый JSON в файл для ручной проверки
                File.WriteAllText($"{Environment.CurrentDirectory}\\Ubergraph.json", _json);
                Console.WriteLine($"[INFO] Модифицированный JSON сохранен в: {Environment.CurrentDirectory}\\Ubergraph.json");
            }
            if (!File.Exists(Path.ChangeExtension(path, ".bak")))
            {
                if (File.Exists(path)) File.Copy(path, path + ".bak", true);
                if (File.Exists(Path.ChangeExtension(path, "uexp"))) File.Copy(Path.ChangeExtension(path, "uexp"), Path.ChangeExtension(path, "uexp") + ".bak", true);
            }

            ModdedAsset.Write(path);
            Console.WriteLine($"[INFO] Всего было произведено замен: {KismetProcessor.ModifiedInstCount}");
            Console.WriteLine($"[SUCCESS] Модифицированный ассет успешно сохранен в: {path}");
            
        }
    }

}
