using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Solicen.JSON;
using Solicen.Kismet;
using Solicen.Translator;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UAssetAPI;
using static System.Net.WebRequestMethods;

namespace Solicen.CLI
{
    partial class CLIHandler
    {
        public static class Config
        {
            public static bool Virtual = false;
            public static bool DebugMode = false;
            public static bool AllowTable = false;
            public static bool Translate = false;
            public static string RunCommand = string.Empty;
            public static UAssetAPI.UnrealTypes.EngineVersion Version = UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_18;
            public static bool NoBak = false;
            public static bool AllowLocalizedSource = false;
            public static bool AllowUnderscore = false;
        }
        private static readonly string[] AllowedExtensionForAsset = new[] { ".uasset", ".umap" };
        private static readonly List<Argument> arguments;

        static CLIHandler()
        {
            arguments = new List<Argument>
            {
                // Флаги (аргументы без значений)
                // [WIP] new Argument("--virtual", "-v", "Activate virtual provider for (.pak|.ucas).", () => Config.Virtual = true),
                new Argument("--include:name", null, "Include namespace::value.", () => MapParser.IncludeNameSpace = true),
                new Argument("--map", "-m", "Add specified .usmap as mappings for processing.", (map) => ProcessMappings(map)),
                new Argument("--nobak", null, "Disables the creation of .bak backup files.", () => Config.NoBak = true),
                new Argument("--translate", null, "Automatically translate strings using an online translator.", () => Config.Translate = true),
                new Argument("--all", null, "Extract all string types (includes StringTable and LocalizedSource).", () => { Config.AllowLocalizedSource = true; Config.AllowTable = true; }),
                new Argument("--table", null, "Extract strings from StringTable assets.", () => Config.AllowTable = true),
                new Argument("--localized", "-l", "Extract fallback localization strings (LocalizedSource). [RISKY]", () => Config.AllowLocalizedSource = true),
                new Argument("--ext:underscore", "-u", "Allow extracting strings that contain the '_' character.", () => Config.AllowUnderscore = true),
                new Argument("--debug", "-d", "Enables debug mode with additional information output.",() => Config.DebugMode = true),
                new Argument("--help", "-h", "Show this help message.", () => Argumentor.ShowHelp(arguments)),

                // Аргументы со значениями
                new Argument("--table:only:key", null, "If key/name matches then include only this value to output.", (key) => MapParser.SearchNameSpace = key),
                new Argument("--pack:folder", "-p:f", "Translate and pack assets into auto prepared folder.", (folder) => { BytecodeModifier.PackIntoFolder = true; BytecodeModifier.PackFolder = folder; }),
                new Argument("--version", "-v", "Set the engine version for correct processing (e.g., -v=5.1).", ProcessVersion),
                new Argument("--lang:from", null, "Set the source language for translation (e.g., --lang:from=en).", (lang) => TranslateManager.LanguageFrom = lang),
                new Argument("--lang:to", null, "Set the target language for translation (e.g., --lang:to=ru).", (lang) => TranslateManager.LanguageTo = lang),
                new Argument("--endpoint", "-e", "Set the translation service endpoint (e.g., -e=Yandex).", (endpoint) => TranslateManager.Endpoint = endpoint),
                new Argument("--run", "-r", "Execute a command in the terminal after completion.", (cmd) => Config.RunCommand = cmd)
            };
        }
 

        static void ProcessTranslator()
        {
            var JsonFilePath = EnvironmentHelper.AssemblyDirectory + "\\UberJSON.json";
            if (System.IO.File.Exists(JsonFilePath))
            {
                var uber = UberJSONProcessor.ReadFile(JsonFilePath);
                var manager = new TranslateManager();
                for (int i = 0; i < uber.Length; i++)
                {
                    CLI.Console.WriteLine($"[DarkGray][INF] [White]...{uber[i].FileName}");
                    var dict = uber[i].GetValues()
                        .Where(x => 
                        !x.Key.IsUpperLower() && 
                        !x.Key.IsLower() && 
                        !x.Key.IsPath() && 
                        !x.Key.IsAllNumber() &&
                        !x.Key.IsUpper())
                        .ToDictionary<string, string>();

                    if (dict.Count > 0)
                    {
                        manager.TranslateLines(ref dict);
                        uber[i].Clear();
                        uber[i].AddRange(dict);
                    }
                }
                System.IO.File.WriteAllText(JsonFilePath, JsonConvert.SerializeObject(uber, Formatting.Indented).ToString());

            }
        }

        static void ProcessMappings(string map)
        {
            var anyMappings = Directory.GetFiles(EnvironmentHelper.AssemblyDirectory, "*.usmap")
                .Where(x => Path.GetFileName(x).StartsWith(map));
            
            var path = anyMappings.FirstOrDefault();
            if (System.IO.File.Exists(path))
            {
                AssetLoader.MappingsPath = path;
            }
        }

        private static StringBuilder FilelistBuilder = new StringBuilder();
        private static void AddToFileList(string path) => FilelistBuilder.AppendLine($"\"{path}\"");
        private static bool IsAsset(string file)
        {
            if (AllowedExtensionForAsset.Any(x => file.Trim('\"').EndsWith(x))) return true;
            return false;
        }
        #region Устаревший фунционал Unpack
        private static string GetUnpackCsvPath(string file)
        {
            var csvFile = string.IsNullOrWhiteSpace(file) ? "" : Path.GetFileNameWithoutExtension(file) + ".csv";
            var directoryName = string.IsNullOrWhiteSpace(file) ? "" : $"\\{Path.GetFileName(Path.GetDirectoryName(file))}\\";
            var unpackDirectory = EnvironmentHelper.AssemblyDirectory + "\\Unpack\\" + directoryName;
            Directory.CreateDirectory(unpackDirectory);
            return unpackDirectory + csvFile;
        }
        #endregion

        public static void ProcessProgram(string[] args)
        { 
            // 1. Разбираем аргументы и настраиваем конфигурацию
            var originalArgs = Argumentor.SplitArgs(args);
            var onlyArgs = Argumentor.Process(originalArgs, arguments);

            // 2. Применяем конфигурацию к другим модулям
            BytecodeModifier.AllowCreateBak = !Config.NoBak;
            AssetLoader.Version = Config.Version;

            // Устанавливаем флаги для парсера напрямую из конфигурации
            MapParser.AllowUnderscore = Config.AllowUnderscore;
            MapParser.AllowLocalizedSource = Config.AllowLocalizedSource;

            // (Откуда) JSON => Folder/Asset (куда)
            // Иначе: Asset/Folder => JSON
            if (onlyArgs.Length > 0)
            {
                var assetFile = onlyArgs.FirstOrDefault(x => IsAsset(x));
                var csvFile = onlyArgs.FirstOrDefault(x => x.Contains(".csv"));
                var uberJson = onlyArgs.FirstOrDefault(x => x.Contains(".json"));

                bool isPack = onlyArgs[0].EndsWith(".json") || onlyArgs[0].EndsWith(".csv");
                #region Запаковка строк
                if (isPack) // Если это UberJSON/CSV => Folder/Asset 
                {
                    bool isFolder = !IsAsset(onlyArgs[1]);
                    bool isAsset = IsAsset(onlyArgs[1]);
                    var uberJSONCollection = uberJson != null ? JSON.UberJSONProcessor.ReadFile(uberJson) : JSON.UberJSONProcessor.Convert(csvFile);

                    if (Directory.Exists(onlyArgs[1]))
                    {
                        CLI.Console.StartProgress("Counting assets for processing");
                        var files = Directory.GetFiles($@"{onlyArgs[1]}", "*", SearchOption.AllDirectories).Where(x => IsAsset(x)).ToArray();
                        var sortedFiles = files.Where(x => uberJSONCollection.Any(f => Path.GetFileNameWithoutExtension(f.FileName) == Path.GetFileNameWithoutExtension(x))).ToArray();

                        CLI.Console.StopProgress($"[DarkGray][INF] [White]Assets (.uasset|.umap) for replace found: {sortedFiles.Length}");
                        CLI.Console.Separator(64);

                        Console.WriteLine($"[DarkGray] Replacement mode / [Magenta]UberJSON");
                        CLI.Console.Separator(64);

                        foreach (var uber in uberJSONCollection)
                        {
                            assetFile = sortedFiles.FirstOrDefault(x => Path.GetFileNameWithoutExtension(x) == Path.GetFileNameWithoutExtension(uber.FileName));
                            var values = uber.GetValues().Where(x => !string.IsNullOrEmpty(x.Value)).ToDictionary();
                            if (values.Count > 0)
                            {
                                Kismet.BytecodeModifier.ModifyAsset(assetFile, values, Config.AllowTable);
                                AddToFileList(assetFile);
                            }
                        }
                    }
                    else if (isAsset)
                    {
                        Console.WriteLine($"[DarkGray] Replacement mode / [Magenta]UberJSON");
                        CLI.Console.Separator(64);
                        var uber = uberJSONCollection.FirstOrDefault(x => Path.GetFileName(assetFile) == Path.GetFileNameWithoutExtension(x.FileName));
                        if (uber != null)
                        {
                            var values = uber.GetValues().Where(x => !string.IsNullOrEmpty(x.Value)).ToDictionary();
                            if (uber.GetValues().Count > 0)
                            {
                                Kismet.BytecodeModifier.ModifyAsset(assetFile, values, Config.AllowTable);
                                AddToFileList(assetFile);
                            }
                        }
                    }

                }
                #endregion
                #region Излечение строк
                else // Если это Folder/Asset => JSON 
                {
                    bool isFolder = !IsAsset(onlyArgs[0]);
                    bool isAsset = IsAsset(onlyArgs[0]);

                    if (isAsset)
                    {
                        bool notSpecifedAsset = string.IsNullOrWhiteSpace(assetFile);
                        if (notSpecifedAsset)
                        {
                            CLI.Console.Separator(64);
                            System.Console.WriteLine("Drag&Drop UE file (.uasset|.umap) and press ENTER:");
                            CLI.Console.Separator(64);
                            assetFile = System.Console.ReadLine().Trim('\"');
                            if (!IsAsset(assetFile))
                            {
                                System.Console.WriteLine($"{Path.GetExtension(assetFile)} is invalid file type.");
                                System.Console.ReadLine(); Environment.Exit(1);
                            }
                            CLI.Console.Separator(64);
                        }

                        Console.WriteLine($"[DarkGray] Extract mode / [Magenta]UberJSON");
                        CLI.Console.Separator(64);
                        BytecodeExtractor.AllowTableExtract = Config.AllowTable;
                        BytecodeExtractor.ExtractAllAndWriteUberJSON(assetFile, Config.AllowUnderscore, Config.AllowLocalizedSource);
                    }
                    if (Directory.Exists(onlyArgs[0]))// Это папка
                    {
                        CLI.Console.StartProgress("Counting assets for processing");
                        var files = Directory.GetFiles($@"{onlyArgs[0]}", "*", SearchOption.AllDirectories).Where(x => IsAsset(x)).ToArray();
                        CLI.Console.StopProgress($"[DarkGray][INF] [White]Assets (.uasset|.umap) found: {files.Length}");
                        CLI.Console.Separator(64);

                        Console.WriteLine($"[DarkGray] Extract mode / [Magenta]UberJSON");
                        CLI.Console.Separator(64);

                        Kismet.BytecodeExtractor.AllowTableExtract = Config.AllowTable;
                        Kismet.BytecodeExtractor.ExtractAllAndWriteUberJSON(files, Config.AllowUnderscore, Config.AllowLocalizedSource);
                    }
                }
                #endregion
            }

            if (FilelistBuilder.Length > 0)
                System.IO.File.WriteAllText(EnvironmentHelper.AssemblyDirectory
                    + "\\mod_filelist.txt", FilelistBuilder.ToString());

            #region Запускаем автоматический перевод
            if (Config.Translate) ProcessTranslator();
            #endregion

            #region Запускаем командную строку
            if (!string.IsNullOrWhiteSpace(Config.RunCommand)) Argumentor.RunTerminal(Config.RunCommand);
            #endregion
        }

        /// <summary>
        /// Обрабатывает и устанавливает версию движка.
        /// </summary>
        private static void ProcessVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) return;
            UAssetAPI.UnrealTypes.EngineVersion engineVersion;
            if (version.StartsWith("UE3"))
                engineVersion = UAssetAPI.UnrealTypes.EngineVersion
                    .VER_UE4_OLDEST_LOADABLE_PACKAGE;
            else
            {
                if (version.Contains(".")) version = $"UE{version.Replace(".", "_")}";
                Enum.TryParse($"VER_{version}", out engineVersion);
            }

            Config.Version = engineVersion;
        }
    }
}