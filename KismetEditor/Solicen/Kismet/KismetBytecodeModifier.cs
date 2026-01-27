using KismetEditor.Solicen;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Solicen.JSON;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UAssetAPI;

namespace Solicen.Kismet
{
    class BytecodeModifier
    {
        public static bool PackIntoFolder = false;
        public static string PackFolder = "SolUber_PAK";
        public static bool AllowCreateBak = true;
        public static UAsset Asset; static bool UseBak = true;

        private static Dictionary<string,string> RemoveAnyCode(Dictionary<string, string> replacement)
        {
            return replacement.Where(x => !MapParser.IsCodePart(x.Key)).ToDictionary();
        }

        public static void ModifyAsset(string path, Dictionary<string, string> replacement, bool allowTable = false)
        {
            JObject jsonObject = null;
            var json = string.Empty;
            Solicen.CLI.Console.StartProgress($"Replace process for: {Path.GetFileName(path)}");

            replacement = replacement.Where(x => x.Key != x.Value).ToDictionary();
            replacement = replacement.Select(x => (x.Key.Unescape(), x.Value.Unescape())).ToDictionary();

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
            try
            {
                // Загружаем uasset файл с помощью UAssetAPI
                Asset = AssetLoader.LoadAsset(path);
                if (Asset == null) return;
                json = Asset.SerializeJson(Formatting.Indented);
                jsonObject = JObject.Parse(json);
            }
            catch
            {
                CLI.Console.WriteLine($"[Red][ERR] [White]Failed to load asset. The wrong engine version maybe assigned.");
                return;
            }

            // Сначала обрабатываем Ubergraph
            var ubergraph = KismetExtension.GetUbergraphJson(Asset);
            if (ubergraph != null)
            {
                replacement = RemoveAnyCode(replacement);
                KismetProcessor.ReplaceAllInUbergraph(jsonObject, replacement, Asset);
                Asset = UAsset.DeserializeJson(jsonObject.ToString());
            }
            // Теперь обрабатываем каждое свойство Text/Str Property 
            KismetProcessor.ReplaceAllInStrProperties(replacement, Asset);
            KismetProcessor.ReplaceAllInTextProperties(replacement, Asset);
            if (allowTable)
            {
                KismetProcessor.ReplaceAllInStringTable(replacement, Asset);
                KismetProcessor.ReplaceAllInDataTable(replacement, Asset);

            }
               


            // --- Отладочный вывод и безопасный режим (без сохранения) ---
            if (CLI.CLIHandler.Config.DebugMode)
            {
                var _json = jsonObject.ToString();
                // Сохраняем итоговый JSON в файл для ручной проверки
                File.WriteAllText($"{Environment.CurrentDirectory}\\Ubergraph.json", _json);
                Console.WriteLine($"[INF] The modified JSON is saved in: {Environment.CurrentDirectory}\\Ubergraph.json");
            }

            if (PackIntoFolder)
            {
                var fileName = Path.GetFileName(path);
                var virtualPath = Asset.FolderName != null ? Path.GetDirectoryName(Asset.FolderName.Value) : "";
                if (virtualPath == "")
                {
                    // Если не найден виртуальный путь в ассетах (отсутствуют mappings)
                    // Пытаемся получить путь текущего распакованного архива .pak|.ucas
                    virtualPath = path.UE_FolderWithoutFileName();
                }
                var folderPath = PackFolder.Contains("\\") ? PackFolder+$"\\{virtualPath}\\" : EnvironmentHelper.AssemblyDirectory + $"\\{PackFolder}\\{virtualPath}";
                path = $"{folderPath}\\{fileName}";
                Directory.CreateDirectory(folderPath);
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
            Solicen.CLI.Console.StopProgress($"[Green][SUCCESS] [White]...{Path.GetFileName(path)}");
            
        }
    }

}
