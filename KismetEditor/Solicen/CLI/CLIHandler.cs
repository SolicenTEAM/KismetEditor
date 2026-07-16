using Solicen.JSON;
using Solicen.Kismet;
using Solicen.Translator;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Solicen.CLI
{
    partial class CLIHandler
    {
        public static class Config
        {
            public static bool AllFunctionStringConst = false;
            public static bool AllDirectories = false;
            public static bool IncludeUexpFiles = false;
            public static bool Heuristics = false;
            public static bool Virtual = false;
            public static bool DebugMode = false;
            public static bool AllowTable = false;
            public static bool Translate = false;
            public static string RunCommand = string.Empty;
            public static UAssetAPI.UnrealTypes.EngineVersion Version = UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_18;
            public static bool NoBak = false;
            public static bool AllowTextProperty = false;
            public static bool AllowLocalizedSource = false;
            public static bool AllowUnderscore = true;
            public static bool PatchAllFunctions { get; set; } = false;
            public static bool PatchAssignments { get; set; } = false;
        }
        private static readonly string[] NotAllowedPath = new[] { 
            "\\ThirdParty\\", "\\Materials\\", "\\Terrain\\", "\\Effects\\", "\\FX\\",
            "\\Engine\\", "\\Physics\\", "\\Plugins\\", "\\Config\\", "\\Mannequin\\", "\\StarterContent\\" };
        private static List<string> AllowedExtensionForAsset = new List<string>() { ".uasset", ".umap" };
        private static readonly List<Argument> arguments;

        static CLIHandler()
        {
            arguments = new List<Argument>
            {
                // [WIP] new Argument("--virtual", "-v", "Activate virtual provider for (.pak|.ucas).", () => Config.Virtual = true),
                // By default, StringConst is enabled only in Ubergraph and occurrences of StrProperty. You can extend the extraction with the arguments below.
                new Argument("--StringConst", "-sc", "Parses all UFunction with ScriptBytecode and extracts all strings of type EX_StringConst.", () => Config.AllFunctionStringConst = true),
                new Argument("--TextProperty", "-t", "Allow to extract string with TextProperty type.", () => Config.AllowTextProperty = true),
                new Argument("--LocalizedSource", "-l", "Extract fallback localization strings (LocalizedSource).", () => Config.AllowLocalizedSource = true),
                new Argument("--Table", "-t", "Extract strings from Data/String Table assets.", () => Config.AllowTable = true),
                new Argument("--AllType", "-all", "Extract all string types (includes Table and LocalizedSource and TextProperty).", 
                () => { Config.AllowLocalizedSource = true; Config.AllowTable = true; Config.AllowTextProperty = true; Config.AllFunctionStringConst = true; }),
      
                new Argument("--No-underscore", "-n:un", "Skips strings that contain the '_' character.", () => Config.AllowUnderscore = false),
                new Argument("--Map", "-m", "Add specified .usmap nearby .exe as mappings for processing (e.g., -m='Gori_umap.usmap').", (map) => ProcessMappings(map)),
                new Argument("--Map-nearby", "-u:m", "Uses the usmap file if it finds it nearby.", () => UseAnyMappingNearby()),
                new Argument("--NoBak", "-bak", "Disables the creation of .bak backup files.", () => Config.NoBak = true),
                new Argument("--Translate", null, "Automatically translate strings using an online translator.", () => Config.Translate = true),
                new Argument("--Patch-all-functions", "-paf", "Iterate the bytecode-replacement pipeline over every UFunction with a ScriptBytecode (not just ExecuteUbergraph_*). Needed for widget event handlers and other functions that hold their EX_StringConst outside the ubergraph.", () => Config.PatchAllFunctions = true),
                new Argument("--Patch-assignments", "-pa", "Also replace EX_StringConst inside an AssignmentExpression in the ubergraph (off by default; opt-in for game text hardcoded via 'Set Text' / 'Print String' Blueprint nodes).", () => Config.PatchAssignments = true),
                new Argument("--Pack-folder", "-p:f", "Translate and pack assets into auto prepared folder (e.g., 'ManicMiners_RUS')", (folder) => { BytecodeModifier.PackIntoFolder = true; BytecodeModifier.PackFolder = folder; }),
                new Argument("--Version", "-v", "Set the engine version for correct processing (e.g., -v=5.1).", ProcessVersion),
                new Argument("--Run", "-r", "Execute a command in the terminal after completion (e.g., --run=[CommandArgs])", (cmd) => Config.RunCommand = cmd),

                new Argument("--All-directories", "-all:dir", "Process all directories.", () => Config.AllDirectories = true),
                new Argument("--Include-name", "-i:name", "Include namespace::value.", () => MapParser.IncludeNameSpace = true),
                new Argument("--Uexp", "-exp", "Include uexp files to process.", () => Config.IncludeUexpFiles = true),
                new Argument("--Table-only-key", "-t:o:k", "If key/name matches then include only this value to output (e.g., --Table-only-key=ENG).", (key) => MapParser.SearchNameSpace = key),
                new Argument("--Debug", "-d", "Enables debug mode with additional information output.",() => Config.DebugMode = true),

                new Argument("--Api-key", "-a", "Set apikey for OpenRouter.", (key) => Translator.UberTranslator.OpenRouterApiKey = key),
                new Argument("--Api-model", "-a:m", "Set model for OpenRouter (e.g, -a:model=tngtech/deepseek-r1t2-chimera:free)", (model) => Translator.UberTranslator.OpenRouterModel  = model),
                new Argument("--Language-source", "-l:s", "Set the source language for translation (e.g., -l:s=en).", (lang) => UberTranslator.LanguageFrom = lang),
                new Argument("--Language-to", "-l:t", "Set the target language for translation (e.g., -l:t=ru).", (lang) => UberTranslator.LanguageTo = lang),
                new Argument("--Endpoint", "-e", "Set the translation service endpoint (e.g., -e=yandex).", (endpoint) => UberTranslator.Endpoint = endpoint),

                new Argument("--help", "-h", "Show this help message.", () => Argumentor.ShowHelp(arguments))
            };          
        }
 

        static void ProcessTranslator(string path)
        {
            var JsonFileName = Path.GetFileName(path);
            if (!JsonFileName.EndsWith(".json")) return;
            var JsonFilePath = EnvironmentHelper.AssemblyDirectory + $"\\{JsonFileName}";
            if (System.IO.File.Exists(JsonFilePath))
            {
                var uber = UberJSONProcessor.ReadFile(JsonFilePath);
                var manager = new UberTranslator();
                if (UberTranslator.Endpoint == "router")
                {
                    var allValues = uber.GetAllValues().Where(x => string.IsNullOrWhiteSpace(x.Value)).ToDictionary();
                    if (allValues.Count > 0)
                    {
                        manager.TranslateLines(ref allValues);
                        uber.ReplaceAll(allValues);
                    }
                    else // Повторно переводим что уже есть в базе
                    {
                        allValues = uber.GetAllValues();
                        uber.ReplaceAll(allValues);
                    }

                }
                else
                {
                    for (int i = 0; i < uber.Length; i++)
                    {
                        CLI.Console.WriteLine($"[DarkGray][INF] [White]...{uber[i].FileName}");
                        var dict = uber[i].GetValues()
                            .Where(x =>!MapParser.IsNotAllowedString(x.Key))
                            .ToDictionary<string, string>();

                        if (dict.Count > 0)
                        {
                            manager.TranslateLines(ref dict);
                            uber[i].Clear();
                            uber[i].AddRange(dict);
                        }
                    }
                }
                uber.SaveFile(JsonFilePath);
            }
        }

        static void UseAnyMappingNearby()
        {
            var anyMappings = Directory.GetFiles(EnvironmentHelper.AssemblyDirectory, "*.usmap");
            var path = anyMappings.FirstOrDefault();
            if (System.IO.File.Exists(path))
            {
                AssetLoader.MappingsPath = path;
            }
        }

        static void ProcessMappings(string map)
        {
            var path = string.Empty; 
            if (!map.Contains("\\"))
            {
                var anyMappings = Directory.GetFiles(EnvironmentHelper.AssemblyDirectory, "*.usmap")
                .Where(x => Path.GetFileName(x).StartsWith(map));
                path = anyMappings.FirstOrDefault();      
            }
            else
                path = map;
            if (System.IO.File.Exists(path))
            {
                AssetLoader.MappingsPath = path;
            }
        }

        private static StringBuilder FilelistBuilder = new StringBuilder();
        private static void AddToFileList(string path) => FilelistBuilder.AppendLine($"\"{path}\"");

        private static bool IsNotAllowedPath(string path)
        {
            if (Config.AllDirectories) return false;
            if (NotAllowedPath.Any(x => path.Contains(x))) return true;
            return false;
        }
        private static bool IsAsset(string file)
        {
            if (Config.IncludeUexpFiles) AllowedExtensionForAsset.Add(".uexp");
            if ((AllowedExtensionForAsset.Any(x => file.Trim('\"').EndsWith(x))) && !IsNotAllowedPath(file)) return true;
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
            BytecodeExtractor.AllowTableExtract = Config.AllowTable;
            BytecodeExtractor.AllowTextProperty = Config.AllowTextProperty;
            BytecodeExtractor.AllFunctionStringConst = Config.AllFunctionStringConst;
            BytecodeModifier.AllowCreateBak = !Config.NoBak;
            AssetLoader.Version = Config.Version;

            // Устанавливаем флаги для парсера напрямую из конфигурации
            MapParser.AllowUnderscore = Config.AllowUnderscore;
            MapParser.AllowLocalizedSource = Config.AllowLocalizedSource;

            var UberJSONName = string.Empty;

            // (Откуда) JSON => Folder/Asset (куда)
            // Иначе: Asset/Folder => JSON
            if (onlyArgs.Length > 0)
            {
                var assetFile = onlyArgs.FirstOrDefault(x => IsAsset(x));
                var csvFile = onlyArgs.FirstOrDefault(x => x.Contains(".csv"));
                var uberJson = onlyArgs.FirstOrDefault(x => x.Contains(".json"));

                bool isPack = onlyArgs[0].EndsWith(".json") || onlyArgs[0].EndsWith(".csv");
                #region Запаковка строк
                if (isPack && onlyArgs.Length>1) // Если это UberJSON/CSV => Folder/Asset 
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
                            if (values.Count > 0 && assetFile != null)
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
                        var uber = uberJSONCollection.FirstOrDefault(x => Path.GetFileNameWithoutExtension(assetFile) == Path.GetFileNameWithoutExtension(x.FileName));
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

                        UberJSONName = Path.GetFileNameWithoutExtension(assetFile);
                        BytecodeExtractor.ExtractAndWriteUJson(assetFile);
                    }
                    if (Directory.Exists(onlyArgs[0]))// Это папка
                    {
                        CLI.Console.StartProgress("Counting assets for processing");
                        var files = Directory.GetFiles($@"{onlyArgs[0]}", "*", SearchOption.AllDirectories).Where(x => IsAsset(x)).ToArray();
                        CLI.Console.StopProgress($"[DarkGray][INF] [White]Assets (.uasset|.umap) found: {files.Length}");
                        CLI.Console.Separator(64);

                        Console.WriteLine($"[DarkGray] Extract mode / [Magenta]UberJSON");
                        CLI.Console.Separator(64);

                        UberJSONName = Path.GetFileName(onlyArgs[0]);
                        Kismet.BytecodeExtractor.ExtractAndWriteUJson(files, UberJSONName);
                    }
                }
                #endregion
            }

            var _tJson = onlyArgs.FirstOrDefault(x => x.EndsWith(".json"));
            UberJSONName = _tJson != null ? Path.GetFileName(_tJson) : UberJSONName;

            if (FilelistBuilder.Length > 0)
                System.IO.File.WriteAllText(EnvironmentHelper.AssemblyDirectory
                    + "\\mod_filelist.txt", FilelistBuilder.ToString());

            #region Запускаем автоматический перевод
            if (Config.Translate) ProcessTranslator(UberJSONName);
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