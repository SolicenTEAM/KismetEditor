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
            /// <summary>
            /// Switches EX_StringConst strings extract from only Ubergraph, to process all UFunction with ScriptBytecode structure.
            /// </summary>
            public static bool AllFunctionStringConst = false;
            /// <summary>
            /// Disables filter with specifed directory names for analyze and asset processing.
            /// </summary>
            public static bool AllDirectories = false;
            public static bool IgnoreStringFilter = false;
            public static bool IncludeUexpFiles = false;
            public static bool Heuristics = false;
            public static bool Virtual = false;
            public static bool DebugMode = false;

            #region Fallback Localization
            /// <summary>
            /// Enables extraction localization strings from Data/String Table assets as fallback.
            /// </summary>
            public static bool AllowTable = false;

            /// <summary>
            /// Enables extraction localization strings with TextPropery type as fallback.
            /// </summary>
            public static bool AllowTextProperty = false;

            /// <summary>
            /// Enables extraction localization strings (LocalizedSource) as fallback.
            /// </summary>
            public static bool AllowLocalizedSource = false;
            #endregion

            public static bool EnableAutoTranslate = false;

            /// <summary>
            /// Specifies a specific command-line command to execute after completing all the main processes of the utility.
            /// </summary>
            public static string RunCommand = string.Empty;

            /// <summary>
            /// Sets the Unreal Engine version based on the UAssetAPI.
            /// </summary>
            public static UAssetAPI.UnrealTypes.EngineVersion Version = UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_18;

            /// <summary>
            /// Disables the creation of a backup file when writing changes.
            /// </summary>
            public static bool NoBak = false;

            /// <summary>
            /// Allows or prohibits extracting strings with an underscore.
            /// </summary>
            public static bool AllowUnderscore = true;

            /// <summary>
            /// Iterates EX_StringConst replace + offset-recalc pipeline over every UFunction with a non - empty ScriptBytecode.
            /// Developed by <see href="https://github.com/Shayano">@Shayano</see>.
            /// </summary>
            public static bool PatchAllFunctions { get; set; } = false;

            /// <summary>
            /// When true, the AssignmentExpression filter is skipped, so those EX_StringConst nodes become eligible for replacement.
            /// Developed by <see href="https://github.com/Shayano">@Shayano</see>.
            /// </summary>
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
                new Argument("--sconst",    "-sc",  "Extract strings EX_StringConst from all UFunction with ScriptBytecode.", () => Config.AllFunctionStringConst = true),
                new Argument("--tprop",     "-tp",  "Extract fallback localization strings with TextProperty type.", () => Config.AllowTextProperty = true),
                new Argument("--lsource",   "-ls",  "Extract fallback localization strings with LocalizedSource type.", () => Config.AllowLocalizedSource = true),
                new Argument("--dstable",   "-dst", "Extract fallback localization strings from Data/String Table assets.", () => Config.AllowTable = true),
                new Argument("--alltypes",  "-all", "Extract strings from all possible types (includes Table and LocalizedSource and TextProperty).", 
                () => {
                    Config.AllowTable = true; 
                    Config.AllowLocalizedSource = true;
                    Config.AllowTextProperty = true; 
                    Config.AllFunctionStringConst = true; 
                }),
      
                new Argument("--no-filter", "-nf", "Disables string filter function while processing.", () => Config.IgnoreStringFilter = true),
                new Argument("--no-backup", "-nobak", "Disables the creation of .bak backup files.", () => Config.NoBak = true),
                new Argument("--no-underscore", "-un", "Excludes strings that contain the '_' character.", () => Config.AllowUnderscore = false),
                new Argument("--mapping", "-map", "Add specified .usmap nearby .exe as mappings for processing (e.g., -map='Gori_umap.usmap' or -m).", (map) => ProcessMappings(map), "-m"),
                new Argument("--mapping-auto", "-ma", "Uses any .usmap file if it finds it nearby.", () => UseAnyMappingNearby()),
                new Argument("--translate", "-tr", "Automatically translate strings using an online translator.", () => Config.EnableAutoTranslate = true),
                new Argument("--patch-all-functions", "-paf", "Iterate the bytecode-replacement pipeline over every UFunction with a ScriptBytecode (not just ExecuteUbergraph_*). Needed for widget event handlers and other functions that hold their EX_StringConst outside the ubergraph.", () => Config.PatchAllFunctions = true),
                new Argument("--patch-assignments", "-pa", "Also replace EX_StringConst inside an AssignmentExpression in the ubergraph (off by default; opt-in for game text hardcoded via 'Set Text' / 'Print String' Blueprint nodes).", () => Config.PatchAssignments = true),
                new Argument("--pack-folder", "-pf", "Translate and pack assets into auto prepared folder (e.g., 'ManicMiners_RUS')", (folder) => { BytecodeModifier.PackIntoFolder = true; BytecodeModifier.PackFolder = folder; }),
                new Argument("--version", "-v", "Set the engine version for correct processing (e.g., -v=5.1).", ProcessVersion),
                new Argument("--run", "-r", "Execute a command in the terminal after completion (e.g., --run=[CommandArgs])", (cmd) => Config.RunCommand = cmd),

                new Argument("--all-directories", "-alldir", "Disables filter with specifed directory names for analyze and asset processing.", () => Config.AllDirectories = true),
                new Argument("--namespace", "-ns", "Include namespace::value in output JSON", () => MapParser.IncludeNameSpace = true),
                new Argument("--uexp", "-xp", "Include uexp files to analyze and process.", () => Config.IncludeUexpFiles = true),
                new Argument("--only-key", "-tok", "If key/name matches in Table structure then include only this value to output (e.g., --OnlyKey=ENG).", (key) => MapParser.SearchNameSpace = key),
                new Argument("--debug", "-d", "Enables debug mode with additional information output.",() => Config.DebugMode = true),

                new Argument("--api-key", "-api", "Set key for OpenRouter.", (key) => Translator.UberTranslator.OpenRouterApiKey = key),
                new Argument("--api-Model", "-model", "Set model for OpenRouter (e.g, -a:model=tngtech/deepseek-r1t2-chimera:free)", (model) => Translator.UberTranslator.OpenRouterModel  = model),
                new Argument("--source-lang", "-sl", "Set the source language for translation (e.g., -sl=en).", (lang) => UberTranslator.LanguageFrom = lang),
                new Argument("--target-lang", "-tl", "Set the target language for translation (e.g., -tl=ru).", (lang) => UberTranslator.LanguageTo = lang),
                new Argument("--endpoint", "-e", "Set the translation service endpoint (e.g., -e=yandex).", (endpoint) => UberTranslator.Endpoint = endpoint),

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
            try
            {
                string exeDir = EnvironmentHelper.AssemblyDirectory;
                if (!Directory.Exists(exeDir))
                {
                    CLI.Console.WriteLine($"[Yellow][WARN] [White]Cannot search .usmap: exe directory not found '[Yellow]{exeDir}[White]'");
                    return;
                }
                var anyMappings = Directory.GetFiles(exeDir, "*.usmap");
                if (anyMappings.Length == 0)
                {
                    // Сообщение выводится в Argumentor, здесь тихо
                    return;
                }
                var path = anyMappings.FirstOrDefault();
                if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                {
                    AssetLoader.MappingsPath = path;
                }
            }
            catch (Exception ex)
            {
                CLI.Console.WriteLine($"[Yellow][WARN] [White]Failed to auto-find .usmap: {ex.Message}");
            }
        }

        static void ProcessMappings(string map)
        {
            if (string.IsNullOrWhiteSpace(map))
            {
                CLI.Console.WriteLine($"[Red][ERR] [White]Mapping value is empty. Example: [Cyan]--mapping=Gori.usmap[White]");
                return;
            }

            string cleaned = map.Trim().Trim('"').Trim('\'');
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                CLI.Console.WriteLine($"[Red][ERR] [White]Mapping value is empty after trimming.");
                return;
            }

            string path = string.Empty;
            try
            {
                // Если содержит путь (слеш) — трактуем как полный путь
                if (cleaned.Contains("\\") || cleaned.Contains("/") || Path.IsPathRooted(cleaned))
                {
                    path = cleaned;
                    // Если указана директория + неполное имя? Попробуем резолвить
                    if (!File.Exists(path))
                    {
                        // Попытка найти файл по имени в указанной директории
                        string dir = Path.GetDirectoryName(path);
                        string fileName = Path.GetFileName(path);
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir) && !string.IsNullOrEmpty(fileName))
                        {
                            var candidates = Directory.GetFiles(dir, "*.usmap")
                                .Where(x => Path.GetFileName(x).Equals(fileName, StringComparison.OrdinalIgnoreCase) ||
                                            Path.GetFileName(x).StartsWith(fileName, StringComparison.OrdinalIgnoreCase) ||
                                            Path.GetFileNameWithoutExtension(x).StartsWith(Path.GetFileNameWithoutExtension(fileName), StringComparison.OrdinalIgnoreCase));
                            path = candidates.FirstOrDefault() ?? cleaned;
                        }
                    }
                }
                else
                {
                    // Поиск по имени рядом с exe (частичное совпадение)
                    string exeDir = EnvironmentHelper.AssemblyDirectory;
                    if (!Directory.Exists(exeDir))
                    {
                        CLI.Console.WriteLine($"[Yellow][WARN] [White]Exe directory not found: {exeDir}");
                        path = cleaned;
                    }
                    else
                    {
                        var anyMappings = Directory.GetFiles(exeDir, "*.usmap");
                        if (anyMappings.Length == 0)
                        {
                            path = cleaned; // пусть пост-валидация сообщит
                        }
                        else
                        {
                            // Точное совпадение имени имеет приоритет
                            var exact = anyMappings.FirstOrDefault(x => Path.GetFileName(x).Equals(cleaned, StringComparison.OrdinalIgnoreCase));
                            if (exact != null)
                                path = exact;
                            else
                            {
                                // Частичное StartsWith по имени файла и без расширения
                                string nameNoExt = Path.GetFileNameWithoutExtension(cleaned);
                                var partial = anyMappings.Where(x =>
                                    Path.GetFileName(x).StartsWith(cleaned, StringComparison.OrdinalIgnoreCase) ||
                                    Path.GetFileNameWithoutExtension(x).StartsWith(nameNoExt, StringComparison.OrdinalIgnoreCase) ||
                                    Path.GetFileNameWithoutExtension(x).IndexOf(nameNoExt, StringComparison.OrdinalIgnoreCase) >= 0
                                ).ToArray();
                                if (partial.Length == 1)
                                    path = partial[0];
                                else if (partial.Length > 1)
                                {
                                    // Несколько кандидатов — берем первый, но предупредим
                                    path = partial[0];
                                    CLI.Console.WriteLine($"[Yellow][WARN] [White]Multiple .usmap match '[Yellow]{cleaned}[White]' ({partial.Length} files):");
                                    foreach (var p in partial.Take(5))
                                        CLI.Console.WriteLine($"[DarkGray]  - {Path.GetFileName(p)}[White]");
                                    CLI.Console.WriteLine($"[DarkGray]  Using first: [Cyan]{Path.GetFileName(path)}[White]");
                                }
                                else
                                {
                                    // Ничего не найдено — оставляем как есть, пост-валидация сообщит
                                    path = cleaned;
                                    // Также попробуем найти по полному пути если cleaned без слешей но файл лежит рядом с точным именем + расширением
                                    string withExt = cleaned.EndsWith(".usmap", StringComparison.OrdinalIgnoreCase) ? cleaned : cleaned + ".usmap";
                                    string tryPath = Path.Combine(exeDir, withExt);
                                    if (File.Exists(tryPath))
                                        path = tryPath;
                                }
                            }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    AssetLoader.MappingsPath = path;
                }
                else
                {
                    // Не устанавливаем, пусть Argumentor покажет детальную ошибку
                    // Но для отладки можно сохранить попытку
                }
            }
            catch (Exception ex)
            {
                CLI.Console.WriteLine($"[Red][ERR] [White]Error processing mapping '{cleaned}': {ex.Message}");
            }
        }

        private static StringBuilder FilelistBuilder = new StringBuilder();
        private static void AddToFileList(string path) => FilelistBuilder.AppendLine($"\"{path}\"");

        private static bool IsNotAllowedPath(string path)
        {
            if (Config.AllDirectories) return false;
            if (string.IsNullOrEmpty(path)) return false;
            if (NotAllowedPath.Any(x => path.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0)) return true;
            return false;
        }
        private static bool IsAsset(string file)
        {
            if (string.IsNullOrWhiteSpace(file)) return false;
            string cleaned = file.Trim().Trim('"').Trim('\'');
            string ext = Path.GetExtension(cleaned);
            if (string.IsNullOrEmpty(ext)) return false;
            // Не мутируем глобальный список каждый вызов — проверяем динамически
            bool isAllowedExt = AllowedExtensionForAsset.Any(x => ext.Equals(x, StringComparison.OrdinalIgnoreCase));
            if (!isAllowedExt && Config.IncludeUexpFiles && ext.Equals(".uexp", StringComparison.OrdinalIgnoreCase))
                isAllowedExt = true;
            if (!isAllowedExt) return false;
            if (IsNotAllowedPath(cleaned)) return false;
            return true;
        }

        private static bool IsDataFile(string file)
        {
            if (string.IsNullOrWhiteSpace(file)) return false;
            string cleaned = file.Trim().Trim('"').Trim('\'');
            string ext = Path.GetExtension(cleaned);
            return ext.Equals(".json", StringComparison.OrdinalIgnoreCase) || ext.Equals(".csv", StringComparison.OrdinalIgnoreCase);
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

            // Если запрошена справка и нет позиционных аргументов — дальше не идем (help уже показан)
            bool helpRequested = originalArgs.Any(a => {
                var kp = Argument.GetKeyPart(a).Trim();
                return kp.Equals("--help", StringComparison.OrdinalIgnoreCase) || kp.Equals("-h", StringComparison.OrdinalIgnoreCase);
            });
            if (helpRequested && onlyArgs.Length == 0)
            {
                // Дополнительно показать подсказку по использованию если только хелп
                return;
            }

            // Если вообще нет позиционных аргументов — показать понятное сообщение + хелп
            if (onlyArgs.Length == 0)
            {
                // Но если были только флаги без файлов — это не всегда ошибка (например, --help уже обработан)
                // Проверяем, были ли вообще переданы аргументы
                if (args == null || args.Length == 0)
                {
                    CLI.Console.WriteLine($"[Yellow][WARN] [White]No input file or folder specified.");
                    CLI.Console.WriteLine($"[DarkGray]  Drag & drop a [White].uasset[DarkGray]/[White].umap[DarkGray] file or folder onto the exe, or pass a path as argument.[White]");
                    Argumentor.ShowHelp(arguments);
                    return;
                }
                else if (!helpRequested)
                {
                    // Были флаги но не было файлов — возможно пользователь забыл указать путь
                    bool hasOnlyFlags = originalArgs.All(a => a.TrimStart().StartsWith("-"));
                    if (hasOnlyFlags)
                    {
                        CLI.Console.WriteLine($"[Yellow][WARN] [White]No input file/folder specified. Only options were provided.");
                        CLI.Console.WriteLine($"[DarkGray]  Example: [Cyan]KissE.exe \"C:\\Game\\Content\\Asset.uasset\"[White]");
                        CLI.Console.WriteLine($"[DarkGray]  Example: [Cyan]KissE.exe \"C:\\out.json\" \"C:\\Game\\Content\"[White]");
                        Argumentor.ShowHelp(arguments);
                        return;
                    }
                    // Иначе были позиционные, но они все оказались невалидными (FileNotFound уже показан в Argumentor)
                    // Добавим итоговое сообщение
                    CLI.Console.WriteLine($"[Red][ERR] [White]No valid input path found. Check errors above.");
                    Argumentor.ShowHelp(arguments);
                    return;
                }
            }

            // 2. Применяем конфигурацию к другим модулям
            BytecodeExtractor.AllowTableExtract = Config.AllowTable;
            BytecodeExtractor.AllowTextProperty = Config.AllowTextProperty;
            BytecodeExtractor.AllFunctionStringConst = Config.AllFunctionStringConst;
            BytecodeModifier.CreateBak = !Config.NoBak;
            AssetLoader.Version = Config.Version;

            // Устанавливаем флаги для парсера напрямую из конфигурации
            MapParser.AllowUnderscore = Config.AllowUnderscore;
            MapParser.AllowLocalizedSource = Config.AllowLocalizedSource;
            MapParser.IgnoreStringFilter = Config.IgnoreStringFilter;

            var UberJSONName = string.Empty;

            // (Откуда) JSON => Folder/Asset (куда)
            // Иначе: Asset/Folder => JSON
            if (onlyArgs.Length > 0)
            {
                var assetFile = onlyArgs.FirstOrDefault(x => IsAsset(x) && File.Exists(x.Trim().Trim('"').Trim('\'')));
                // Fallback: если файл не найден на диске, но имеет расширение ассета — все равно берем для диагностики (но позже покажем ошибку)
                if (assetFile == null) assetFile = onlyArgs.FirstOrDefault(x => IsAsset(x));
                var csvFile = onlyArgs.FirstOrDefault(x => x.Trim().EndsWith(".csv", StringComparison.OrdinalIgnoreCase));
                var uberJson = onlyArgs.FirstOrDefault(x => x.Trim().EndsWith(".json", StringComparison.OrdinalIgnoreCase));

                string firstExt = Path.GetExtension(onlyArgs[0].Trim().Trim('"').Trim('\''))?.ToLowerInvariant() ?? "";
                bool isPack = firstExt == ".json" || firstExt == ".csv";
                #region Запаковка строк
                if (isPack) // JSON/CSV -> Asset/Folder
                {
                    if (onlyArgs.Length < 2)
                    {
                        CLI.Console.WriteLine($"[Red][ERR] [White]Pack mode requires two arguments: [Cyan]<json_or_csv> <asset_or_folder>[White]");
                        CLI.Console.WriteLine($"[DarkGray]  You provided only: [Yellow]{onlyArgs[0]}[White]");
                        CLI.Console.WriteLine($"[DarkGray]  Example: [Cyan]KissE.exe \"C:\\out.json\" \"C:\\Game\\Content\"[White]");
                        CLI.Console.WriteLine($"[DarkGray]  Example: [Cyan]KissE.exe \"C:\\out.json\" \"C:\\Game\\Content\\Asset.uasset\"[White]");
                        Argumentor.ShowHelp(arguments);
                        return;
                    }

                    // Второй аргумент должен существовать (папка или ассет)
                    string targetPath = onlyArgs[1];
                    string targetClean = targetPath.Trim().Trim('"').Trim('\'');
                    bool targetIsDir = Directory.Exists(targetClean);
                    bool targetIsAsset = File.Exists(targetClean) && IsAsset(targetClean);
                    bool targetExists = targetIsDir || targetIsAsset || File.Exists(targetClean) || Directory.Exists(targetClean);

                    if (!targetExists)
                    {
                        CLI.Console.WriteLine($"[Red][ERR] [White]Pack target not found: '[Yellow]{targetPath}[White]'");
                        CLI.Console.WriteLine($"[DarkGray]  Second argument must be an existing [White].uasset[DarkGray]/[White].umap[DarkGray] file or folder containing assets.[White]");
                        string parent = null;
                        try { parent = Path.GetDirectoryName(Path.GetFullPath(targetPath)); } catch { parent = Path.GetDirectoryName(targetPath); }
                        if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                        {
                            CLI.Console.WriteLine($"[DarkGray]  Parent exists: [White]{parent}[White]. Check filename spelling.[White]");
                        }
                        return;
                    }

                    // Загружаем JSON/CSV
                    JSON.UberJSON[] uberJSONCollection = null;
                    try
                    {
                        if (uberJson != null)
                        {
                            if (!File.Exists(uberJson))
                            {
                                CLI.Console.WriteLine($"[Red][ERR] [White]JSON file not found: '[Yellow]{uberJson}[White]'");
                                return;
                            }
                            uberJSONCollection = JSON.UberJSONProcessor.ReadFile(uberJson);
                        }
                        else if (csvFile != null)
                        {
                            if (!File.Exists(csvFile))
                            {
                                CLI.Console.WriteLine($"[Red][ERR] [White]CSV file not found: '[Yellow]{csvFile}[White]'");
                                return;
                            }
                            uberJSONCollection = JSON.UberJSONProcessor.Convert(csvFile);
                        }
                        else
                        {
                            CLI.Console.WriteLine($"[Red][ERR] [White]Could not determine JSON/CSV source. First arg: '{onlyArgs[0]}'");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        CLI.Console.WriteLine($"[Red][ERR] [White]Failed to read JSON/CSV '{onlyArgs[0]}': {ex.Message}");
                        if (Config.DebugMode) CLI.Console.WriteLine($"[DarkGray]{ex.StackTrace}[White]");
                        return;
                    }

                    if (uberJSONCollection == null || uberJSONCollection.Length == 0)
                    {
                        CLI.Console.WriteLine($"[Yellow][WARN] [White]JSON/CSV contains no entries or failed to parse: '{onlyArgs[0]}'");
                        return;
                    }

                    if (Directory.Exists(targetPath))
                    {
                        CLI.Console.StartProgress("Counting assets for processing");
                        var files = Directory.GetFiles(targetPath, "*", SearchOption.AllDirectories).Where(x => IsAsset(x)).ToArray();
                        var sortedFiles = files.Where(x => uberJSONCollection.Any(f => Path.GetFileNameWithoutExtension(f.FileName) == Path.GetFileNameWithoutExtension(x))).ToArray();

                        CLI.Console.StopProgress($"[DarkGray][INF] [White]Assets (.uasset|.umap) for replace found: {sortedFiles.Length} (total in folder: {files.Length})");
                        if (files.Length > 0 && sortedFiles.Length == 0)
                        {
                            CLI.Console.WriteLine($"[Yellow][WARN] [White]No assets matched JSON entries by filename. Check that JSON Filenames correspond to asset names.");
                            CLI.Console.WriteLine($"[DarkGray]  JSON contains: {string.Join(", ", uberJSONCollection.Take(3).Select(u => u.FileName))} ...[White]");
                            CLI.Console.WriteLine($"[DarkGray]  Folder assets: {string.Join(", ", files.Take(3).Select(Path.GetFileName))} ...[White]");
                        }
                        CLI.Console.Separator(64);
                        CLI.Console.WriteLine($"[DarkGray][INF] [Wihte]Replacement mode / [Magenta]UberJSON");
                        CLI.Console.Separator(64);

                        int modified = 0;
                        foreach (var uber in uberJSONCollection)
                        {
                            assetFile = sortedFiles.FirstOrDefault(x => Path.GetFileNameWithoutExtension(x) == Path.GetFileNameWithoutExtension(uber.FileName));
                            if (assetFile == null)
                            {
                                if (Config.DebugMode)
                                    CLI.Console.WriteLine($"[DarkGray][INF] No matching asset for JSON '{uber.FileName}' — skipped.[White]");
                                continue;
                            }
                            var values = uber.GetValues().Where(x => !string.IsNullOrEmpty(x.Value)).ToDictionary();
                            if (values.Count > 0)
                            {
                                Kismet.BytecodeModifier.ModifyAsset(assetFile, values, Config.AllowTable);
                                AddToFileList(assetFile);
                                modified++;
                            }
                            else
                            {
                                CLI.Console.WriteLine($"[Yellow][WARN] [White]No translated values for '{uber.FileName}' — skipped (all values empty). Fill 'Translation' column in JSON.");
                            }
                        }
                        if (modified == 0)
                            CLI.Console.WriteLine($"[Yellow][WARN] [White]No assets were modified. Check that JSON contains non-empty translations.");
                    }
                    else if (targetIsAsset)
                    {
                        CLI.Console.WriteLine($"[DarkGray][INF] [Wihte]Replacement mode / [Magenta]UberJSON");
                        CLI.Console.Separator(64);
                        // В режиме single-asset assetFile может быть null если IsAsset фильтр не прошел из-за NotAllowedPath
                        if (assetFile == null) assetFile = targetPath;
                        var uber = uberJSONCollection.FirstOrDefault(x => Path.GetFileNameWithoutExtension(assetFile).Equals(Path.GetFileNameWithoutExtension(x.FileName), StringComparison.OrdinalIgnoreCase));
                        if (uber == null)
                        {
                            CLI.Console.WriteLine($"[Yellow][WARN] [White]No JSON entry matches asset '{Path.GetFileName(assetFile)}'. Available JSON files: {string.Join(", ", uberJSONCollection.Select(u => u.FileName))}");
                            // Попробуем взять первый если всего один
                            if (uberJSONCollection.Length == 1)
                            {
                                uber = uberJSONCollection[0];
                                CLI.Console.WriteLine($"[DarkGray]  Using single JSON '{uber.FileName}' anyway.[White]");
                            }
                        }
                        if (uber != null)
                        {
                            var values = uber.GetValues().Where(x => !string.IsNullOrEmpty(x.Value)).ToDictionary();
                            if (values.Count == 0)
                            {
                                CLI.Console.WriteLine($"[Yellow][WARN] [White]JSON '{uber.FileName}' has no non-empty translations — nothing to pack.");
                            }
                            else
                            {
                                Kismet.BytecodeModifier.ModifyAsset(assetFile, values, Config.AllowTable);
                                AddToFileList(assetFile);
                            }
                        }
                    }
                    else
                    {
                        CLI.Console.WriteLine($"[Red][ERR] [White]Second argument must be a valid .uasset/.umap file or directory. Got: '{targetPath}' (ext: '{Path.GetExtension(targetPath)}')");
                        CLI.Console.WriteLine($"[DarkGray]  If this is a .uexp file, enable [Cyan]--uexp[White] flag.[White]");
                    }

                }
                #endregion
                #region Излечение строк
                else // Если это Folder/Asset => JSON (Extract)
                {
                    string firstClean = onlyArgs[0].Trim().Trim('"').Trim('\'');
                    bool existsAsFile = File.Exists(firstClean);
                    bool existsAsDir = Directory.Exists(firstClean);
                    bool isFolder = existsAsDir;
                    bool isAsset = existsAsFile && IsAsset(firstClean);
                    bool isAssetByExt = IsAsset(firstClean); // для диагностики (расширение подходит но файла нет)

                    if (!isAsset && !isFolder)
                    {
                        // Путь существует но не является валидным ассетом/папкой, либо не существует вовсе (уже сообщено в Argumentor)
                        if (existsAsFile)
                        {
                            string ext = Path.GetExtension(firstClean);
                            CLI.Console.WriteLine($"[Red][ERR] [White]File '[Yellow]{onlyArgs[0]}[White]' has unsupported extension '[Yellow]{ext}[White]'. Expected [Cyan].uasset[White]/[Cyan].umap[White] or a folder.");
                            if (ext.Equals(".uexp", StringComparison.OrdinalIgnoreCase) && !Config.IncludeUexpFiles)
                                CLI.Console.WriteLine($"[DarkGray]  Hint: add [Cyan]--uexp[White] to process .uexp files.[White]");
                            if (IsNotAllowedPath(firstClean))
                                CLI.Console.WriteLine($"[Yellow][WARN] [White]Path is in filtered directory (ThirdParty/Materials/Engine/...). Use [Cyan]--all-directories[White] to include it.");
                        }
                        else if (!existsAsFile && !existsAsDir)
                        {
                            // Already reported in Argumentor, just ensure help
                            if (isAssetByExt)
                                CLI.Console.WriteLine($"[Red][ERR] [White]Extract source file not found: '[Yellow]{onlyArgs[0]}[White]' (extension correct, but file missing).");
                            else
                                CLI.Console.WriteLine($"[Red][ERR] [White]Extract source not found or invalid: '[Yellow]{onlyArgs[0]}[White]'");
                        }
                        else if (isFolder)
                        {
                            // Folder exists but no assets?
                        }
                        // Не прерываем, проверим остальные условия ниже
                    }

                    // Проверяем, указан ли кастомный выходной JSON как второй позиционный аргумент (например: asset.uasset MyOutput.json)
                    string customOutputJson = null;
                    if (onlyArgs.Length >= 2)
                    {
                        string secondArg = onlyArgs[1].Trim().Trim('"').Trim('\'');
                        string secExt = Path.GetExtension(secondArg)?.ToLowerInvariant();
                        if (secExt == ".json" || secExt == ".csv")
                        {
                            customOutputJson = secondArg;
                        }
                        else if (onlyArgs.Length >= 3)
                        {
                            // Также проверяем третий позиционный если второй - не json (на случай доп. файлов)
                            string third = onlyArgs[2].Trim().Trim('"').Trim('\'');
                            string thirdExt = Path.GetExtension(third)?.ToLowerInvariant();
                            if (thirdExt == ".json" || thirdExt == ".csv")
                                customOutputJson = third;
                        }
                    }

                    if (isAsset)
                    {
                        bool notSpecifedAsset = string.IsNullOrWhiteSpace(assetFile);
                        if (notSpecifedAsset)
                        {
                            CLI.Console.Separator(64);
                            System.Console.WriteLine("Drag&Drop UE file (.uasset|.umap) and press ENTER:");
                            CLI.Console.Separator(64);
                            assetFile = System.Console.ReadLine()?.Trim('\"').Trim('\'');
                            if (string.IsNullOrWhiteSpace(assetFile) || !IsAsset(assetFile))
                            {
                                System.Console.WriteLine($"{Path.GetExtension(assetFile)} is invalid file type.");
                                System.Console.ReadLine(); Environment.Exit(1);
                            }
                            CLI.Console.Separator(64);
                        }

                        CLI.Console.WriteLine($"[DarkGray][INF] [White]Extract mode / [Magenta]UberJSON");
                        CLI.Console.Separator(64);

                        if (!string.IsNullOrWhiteSpace(customOutputJson))
                        {
                            UberJSONName = Path.GetFileNameWithoutExtension(customOutputJson);
                            // Если указан полный путь — передаем его целиком, Extractor обработает директорию
                            string passedName = customOutputJson;
                            bool hasDir = customOutputJson.Contains("\\") || customOutputJson.Contains("/") || Path.IsPathRooted(customOutputJson);
                            if (hasDir)
                                passedName = customOutputJson;
                            else
                                passedName = UberJSONName;

                            // Формируем полный путь для отображения (абсолютизируем относительно exe директории если относительный)
                            string displayPath;
                            if (Path.IsPathRooted(passedName))
                                displayPath = passedName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? passedName : passedName + ".json";
                            else if (hasDir)
                                displayPath = Path.GetFullPath(Path.Combine(EnvironmentHelper.AssemblyDirectory, passedName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? passedName : passedName + ".json"));
                            else
                                displayPath = Path.Combine(EnvironmentHelper.AssemblyDirectory, passedName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? passedName : passedName + ".json");

                            CLI.Console.WriteLine($"[DarkGray][INF] [White]Custom output: [Cyan]{customOutputJson}[White] -> [DarkGray]{displayPath}[White]");
                            BytecodeExtractor.ExtractAndWriteUJson(assetFile, passedName);
                            UberJSONName = Path.GetFileNameWithoutExtension(passedName);
                        }
                        else
                        {
                            UberJSONName = Path.GetFileNameWithoutExtension(assetFile);
                            BytecodeExtractor.ExtractAndWriteUJson(assetFile, UberJSONName);
                        }
                    }
                    if (isFolder)// Это папка
                    {
                        CLI.Console.StartProgress("Counting assets for processing");
                        var files = Directory.GetFiles(onlyArgs[0], "*", SearchOption.AllDirectories).Where(x => IsAsset(x)).ToArray();
                        CLI.Console.StopProgress($"[DarkGray][INF] [White]Assets (.uasset|.umap) found: {files.Length}");
                        if (files.Length == 0)
                        {
                            CLI.Console.WriteLine($"[Yellow][WARN] [White]No .uasset/.umap files found in folder '[Yellow]{onlyArgs[0]}[White]' (including subfolders).");
                            if (!Config.AllDirectories)
                                CLI.Console.WriteLine($"[DarkGray]  Note: some directories are filtered (Engine/ThirdParty/...). Use [Cyan]--all-directories[White] to scan everything.");
                            if (!Config.IncludeUexpFiles)
                                CLI.Console.WriteLine($"[DarkGray]  Note: .uexp files are ignored unless [Cyan]--uexp[White] is used.");
                        }
                        else
                        {
                            CLI.Console.Separator(64);
                            CLI.Console.WriteLine($"[DarkGray][INF] [White]Extract mode / [Magenta]UberJSON");
                            CLI.Console.Separator(64);

                            string folderUberName = Path.GetFileName(onlyArgs[0].TrimEnd('\\', '/'));
                            if (string.IsNullOrWhiteSpace(folderUberName)) folderUberName = "UberJSON";
                            // Если указан кастомный выходной файл для папки — используем его
                            if (!string.IsNullOrWhiteSpace(customOutputJson))
                            {
                                string passedName = customOutputJson;
                                bool hasDir2 = customOutputJson.Contains("\\") || customOutputJson.Contains("/") || Path.IsPathRooted(customOutputJson);
                                if (hasDir2)
                                    passedName = customOutputJson;
                                else
                                    passedName = Path.GetFileNameWithoutExtension(customOutputJson);

                                string displayPath2;
                                if (Path.IsPathRooted(passedName))
                                    displayPath2 = passedName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? passedName : passedName + ".json";
                                else if (hasDir2)
                                    displayPath2 = Path.GetFullPath(Path.Combine(EnvironmentHelper.AssemblyDirectory, passedName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? passedName : passedName + ".json"));
                                else
                                    displayPath2 = Path.Combine(EnvironmentHelper.AssemblyDirectory, passedName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? passedName : passedName + ".json");

                                CLI.Console.WriteLine($"[DarkGray][INF] [White]Custom output: [Cyan]{customOutputJson}[White] -> [DarkGray]{displayPath2}[White]");
                                UberJSONName = Path.GetFileNameWithoutExtension(passedName);
                                Kismet.BytecodeExtractor.ExtractAndWriteUJson(files, passedName);
                            }
                            else
                            {
                                UberJSONName = folderUberName;
                                Kismet.BytecodeExtractor.ExtractAndWriteUJson(files, UberJSONName);
                            }
                        }
                    }

                    // Если ни isAsset ни isFolder — показать хелп
                    if (!isAsset && !isFolder)
                    {
                        if (onlyArgs.Length > 0 && !helpRequested)
                        {
                            // Если файл по расширению похож на ассет но не существует — уже сообщили выше, не дублируем
                            if (!existsAsFile && !existsAsDir)
                            {
                                CLI.Console.WriteLine($"[DarkGray]Hint: check that file/folder exists and path is quoted if it contains spaces.[White]");
                            }
                            else
                            {
                                CLI.Console.WriteLine($"[DarkGray]Hint: valid extract inputs are [White].uasset[DarkGray]/[White].umap[DarkGray] files or folders. Check path and extension.[White]");
                            }
                            Argumentor.ShowHelp(arguments);
                            return;
                        }
                    }
                }
                #endregion
            }

            var _tJson = onlyArgs.FirstOrDefault(x => x.Trim().EndsWith(".json", StringComparison.OrdinalIgnoreCase));
            UberJSONName = _tJson != null ? Path.GetFileName(_tJson) : UberJSONName;
            if (string.IsNullOrWhiteSpace(UberJSONName) && onlyArgs.Length > 0)
            {
                // Fallback: try to infer from pack output if not set
                var possibleJson = onlyArgs.FirstOrDefault(x => x.Trim().EndsWith(".json", StringComparison.OrdinalIgnoreCase) || x.Trim().EndsWith(".csv", StringComparison.OrdinalIgnoreCase));
                if (possibleJson != null) UberJSONName = Path.GetFileName(possibleJson);
            }

            if (FilelistBuilder.Length > 0)
                System.IO.File.WriteAllText(EnvironmentHelper.AssemblyDirectory
                    + "\\mod_filelist.txt", FilelistBuilder.ToString());

            #region Запускаем автоматический перевод
            if (Config.EnableAutoTranslate) ProcessTranslator(UberJSONName);
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
            if (string.IsNullOrWhiteSpace(version))
            {
                CLI.Console.WriteLine($"[Red][ERR] [White]--version requires a value. Example: [Cyan]-v=5.1[White] or [Cyan]--version=4.27[White]");
                return;
            }
            string original = version.Trim().Trim('"').Trim('\'');
            if (string.IsNullOrWhiteSpace(original))
            {
                CLI.Console.WriteLine($"[Red][ERR] [White]--version value is empty.");
                return;
            }

            UAssetAPI.UnrealTypes.EngineVersion engineVersion = Config.Version;
            bool parsed = false;

            try
            {
                if (original.StartsWith("UE3", StringComparison.OrdinalIgnoreCase))
                {
                    engineVersion = UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_OLDEST_LOADABLE_PACKAGE;
                    parsed = true;
                }
                else
                {
                    string normalized = original;
                    // Поддержка форматов: "5.1", "4.27", "UE4_27", "VER_UE4_27", "UE5_3"
                    if (normalized.Contains("."))
                    {
                        // "5.1" -> "UE5_1", "4.18" -> "UE4_18"
                        normalized = $"UE{normalized.Replace(".", "_")}";
                    }
                    // Убираем префикс VER_ если есть, TryParse добавит его
                    if (normalized.StartsWith("VER_", StringComparison.OrdinalIgnoreCase))
                        normalized = normalized.Substring(4);

                    // Пробуем несколько вариантов
                    parsed = Enum.TryParse($"VER_{normalized}", true, out engineVersion);
                    if (!parsed)
                        parsed = Enum.TryParse(normalized, true, out engineVersion);
                    if (!parsed)
                    {
                        // Попытка с префиксом UE
                        string withUE = normalized.StartsWith("UE", StringComparison.OrdinalIgnoreCase) ? normalized : "UE" + normalized;
                        parsed = Enum.TryParse($"VER_{withUE}", true, out engineVersion);
                    }
                }
            }
            catch (Exception ex)
            {
                CLI.Console.WriteLine($"[Red][ERR] [White]Failed to parse version '{original}': {ex.Message}");
                return;
            }

            if (!parsed || engineVersion == 0)
            {
                CLI.Console.WriteLine($"[Yellow][WARN] [White]Version '[Yellow]{original}[White]' could not be parsed or is unknown. Using default [Cyan]{Config.Version}[White].");
                CLI.Console.WriteLine($"[DarkGray]  Valid examples: [Cyan]4.18[DarkGray], [Cyan]5.1[DarkGray], [Cyan]UE4_27[DarkGray], [Cyan]UE5_3[White]");
                CLI.Console.WriteLine($"[DarkGray]  Full list: check UAssetAPI.EngineVersion enum.[White]");
                // Не меняем Config.Version
                return;
            }

            Config.Version = engineVersion;
            // Инфо выводится в Argumentor после успешного парса (для консистентности)
        }
    }
}