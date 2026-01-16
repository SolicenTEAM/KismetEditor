using Newtonsoft.Json;
using Solicen.JSON;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UAssetAPI;
using UAssetAPI.FieldTypes;
using UAssetAPI.Unversioned;

namespace Solicen.Kismet
{
    internal static class BytecodeExtractor
    {
        public static string MappingsPath = string.Empty;
        public static bool AllowTableExtract = false;
        public static UAsset Asset;
        public static UAssetAPI.UnrealTypes.EngineVersion Version = UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_18;

        public static UAsset LoadAsset(string asset)
        {
            if (MappingsPath != string.Empty)
            {
                try
                {
                    return Asset = new UAsset(asset, Version, new Usmap(MappingsPath));
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[ERR] Failed to load asset.");
                    Console.WriteLine($" - {ex.Message}");
                }

            }
            else
            {
                try
                {
                    return Asset = new UAsset(asset, Version);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[ERR] Failed to load asset.");
                    Console.WriteLine($" - {ex.Message}");
                }
            }
            return null;
        }
        public static void ExtractAllAndWriteUberJSON(string asset) => ExtractAllAndWriteUberJSON(new string[] { asset });
        public static void ExtractAllAndWriteUberJSON(string[] assets)
        {
            var JsonFilePath = $"{EnvironmentHelper.AssemblyDirectory}\\UberJSON.json";
            var uberJSONCollection = new List<Solicen.JSON.UberJSON>();
            foreach (var asset in assets)
            {
                GC.Collect(2);
                var FileName = Path.GetFileName(asset);
                Console.WriteLine($"[INF] ...{FileName}");
                Asset = LoadAsset(asset);
                if (Asset == null) return;
                var strings = ExtractValues(asset);
                if (strings.Length > 0)
                {
                    var uberJSON = new UberJSON(FileName);
                    uberJSON.AddRange(strings.ToArray());
                    uberJSONCollection.Add(uberJSON);
                }
            }
            var json = JsonConvert.SerializeObject(uberJSONCollection, Formatting.Indented);
            File.WriteAllText(JsonFilePath, json.ToString());
            Console.WriteLine($"[SUCCESS] File with extracted strings was successfully saved in: {JsonFilePath}\n");
        }

        public static void ExtractAndWriteCSV(string assetPath, string FileName = "")
        {
            Asset = LoadAsset(assetPath); List<string> AllExtractedStr = new List<string>();
            if (Asset == null) return;

            AllExtractedStr.AddRange(ExtractValues(assetPath));
            FileName = FileName == string.Empty ? Path.GetFileNameWithoutExtension(assetPath) : FileName;
            if (AllExtractedStr.Count == 0) return;

            #region Запись CSV
            var csvFilePath = FileName.EndsWith(".csv") ? FileName : $"{EnvironmentHelper.AssemblyDirectory}\\{FileName}.csv";
            if (File.Exists(csvFilePath))
            {
                // Если CSV уже существует, просто добавить новые строки
                var csv = File.ReadAllLines(csvFilePath);
                var lines = AllExtractedStr.Except(csv.Select(x => x.Split('|')[0])); lines.ToList().Add("\n");
                File.AppendAllLines(csvFilePath, lines);
            }
            else
            {
                // Иначе просто записать строки в файл
                File.WriteAllText(csvFilePath, string.Join("\n", AllExtractedStr));
            }
            Console.WriteLine($"[SUCCESS] File with extracted strings was successfully saved in: {csvFilePath}\n");
            #endregion
        }

        public static string[] ExtractEachStrProperty()
        {
            var strProperty = MapParser.ExtractStrProperties(Asset);
            if (strProperty != null && strProperty.Length > 0)
            {
                Console.WriteLine("------ [StrProperty] ------");
                return MapParser.ParseAsCSV(strProperty);
            }
            return new string[] { };
        }

        public static string[] ExtractEachTableValue()
        {
            if (AllowTableExtract)
            {
                var table = MapParser.ExtractStringTableEntries(Asset);
                if (table != null && table.Length > 0)
                {
                    Console.WriteLine("------ [StringTable] ------");
                    return MapParser.ParseAsCSV(table);
                }
            }
            return new string[] { };
        }

        public static string[] ExtractFromUbergraph(string assetPath)
        {
            var ubergraph = KismetExtension.GetUbergraphSerialized(Asset);
            if (ubergraph != null && ubergraph.Count > 0)
            {
                if (ProgramProcessor.DebugMode)
                {
                    var _dummyJson = KismetExtension.GetUbergraphJson(Asset);
                    var serializer = new KismetExpressionSerializer(Asset);
                    var u = KismetExtension.GetUbergraph(Asset);

                    File.WriteAllText($"{Environment.CurrentDirectory}\\{Path.GetFileNameWithoutExtension(assetPath)}_DUMMY.json",
                        serializer.SerializeExpressionArray(u).ToString());

                }
                return MapParser.ParseUbergraph(ubergraph);
            }
            else
            {
                return null;
            }

        }

        public static string[] ExtractValues(string assetPath)
        {
            List<string> AllExtractedStr = new List<string>();
            #region Получение строк любого вида
            var propStr = ExtractEachStrProperty(); // Получаем строки из каждого StrProperty
            var ubergraphStr = ExtractFromUbergraph(assetPath); // Получаем строки из ExecuteUbergraph
            var tableValues = ExtractEachTableValue();
            #endregion

            if (ubergraphStr != null) AllExtractedStr.AddRange(ubergraphStr);
            if (propStr != null) AllExtractedStr.AddRange(propStr);
            if (tableValues != null) AllExtractedStr.AddRange(tableValues);
            return AllExtractedStr.ToArray();
        }
    }
}