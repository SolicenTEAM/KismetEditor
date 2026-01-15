using Newtonsoft.Json;
using Solicen.JSON;
using Solicen.Translator;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UAssetAPI;
using static System.Net.WebRequestMethods;

namespace Solicen.Kismet
{
    class ProgramProcessor
    {
        public static bool DebugMode = false;
        private static bool AllowTable = false, AllowTranslate, isExtract = true, packFiles = false;
        public static UAssetAPI.UnrealTypes.EngineVersion Version = UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_18;

        private static readonly string[] AllowedExtensionForAsset = new string[] { ".uasset", ".umap" };
        static List<Argument> arguments = new List<Argument>
        {
            new Argument("--translate", "online translate all values", () => AllowTranslate = true),
            new Argument("--all", "allow extract all values", () => { MapParser.AllowLocalizedSource = true; AllowTable = true; }),
            new Argument("--table", "allow extract StringTable values (.locres)", () => AllowTable = true),
            new Argument("--localized", "allow extract LocalizedSource values (.locres)", () => MapParser.AllowLocalizedSource = true),
            new Argument("--underscore", "allow underscore in values to extracts", () => MapParser.AllowUnderscore = true),
            new Argument("--pack", "[Directory] pack translate from csv to files in directory", () => packFiles=true),
            new Argument("--debug", "[File] write additional files for debug",() => DebugMode = true),
            new Argument("--version", "[Utility] set specific unreal version: --version=4.18",null),
            new Argument("--help", "Show help information", () => ShowHelp(arguments))
        };

        class Argument
        {
            public string Key { get; }
            public string Description { get; }
            public Action Action { get; }

            public Argument(string name, string description, Action action)
            {
                Key = name;
                Description = description;
                Action = action;
            }
        }

        static void ShowHelp(List<Argument> arguments)
        {
            Console.WriteLine("Available arguments:");
            foreach (var argument in arguments)
            {
                Console.WriteLine($"{argument.Key}: {argument.Description}");
            }
        }

        #region Расширенное управление терминалом
        static string GetRunCommand(string[] args) => Regex.Match(string.Join(" ", args), @"[[].*[]]").Value.Trim('[', ']');
        static void RunTerminal(string anyCommand) => System.Diagnostics.Process.Start("CMD.exe", "/c " + anyCommand);
        static string[] SplitArgs(string[] args)
        {
            var regex = new Regex(@"[""](.*?)[""]|[[](.*?)[]]");
            var matches = regex.Matches(string.Join(" ", args.Select(x => $"\"{x}\"")));
            return matches.Cast<Match>().Select(m => m.Groups[1].Value.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        }
        #endregion


        static void ProcessTranslator()
        {
            var JsonFilePath = EnvironmentHelper.CurrentAssemblyDirectory + "\\UberJSON.json";
            if (System.IO.File.Exists(JsonFilePath))
            {
                var uber = UberJSONProcessor.ReadFile(JsonFilePath);
                var manager = new TranslateManager();
                for (int i = 0; i < uber.Length; i++)
                {
                    var dict = uber[i].GetValues();
                    manager.TranslateLines(ref dict);
                    uber[i].Clear();
                    uber[i].AddRange(dict);
                }
                System.IO.File.WriteAllText(JsonFilePath, JsonConvert.SerializeObject(uber, Formatting.Indented).ToString());

            }
        }
        static void ProcessTranslatorArgs(string[] args)
        {
            var langFromArg = args.FirstOrDefault(x => x.StartsWith("--langf") || x.StartsWith("--lf"));
            var langToArg = args.FirstOrDefault(x => x.StartsWith("--langt") || x.StartsWith("--lt"));
            var endpointArg = args.FirstOrDefault(x => x.StartsWith("--endpoint") || x.StartsWith("--e"));
            try
            {
                if (!string.IsNullOrWhiteSpace(langFromArg))
                {
                    var lang = langFromArg.Split('=')[1];
                    TranslateManager.LanguageFrom = lang;
                }
                if (!string.IsNullOrWhiteSpace(langToArg))
                {
                    var lang = langToArg.Split('=')[1];
                    TranslateManager.LanguageTo = lang;
                }
                if (!string.IsNullOrWhiteSpace(endpointArg))
                {
                    var endpoint = endpointArg.Split('=')[1];
                    TranslateManager.Endpoint = endpoint;
                }
            }
            catch
            {
                Console.WriteLine("[Translator] ERROR");
                AllowTranslate = false;
            }


        }

        static void ProcessVersion(string[] args)
        {
            var arg = args.FirstOrDefault(x => x.StartsWith("--version") || x.StartsWith("--v"));
            if (string.IsNullOrWhiteSpace(arg)) return;
            
            var version = arg.Split('=')[1];
            if (version.StartsWith("UE3")) 
                Version = UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_OLDEST_LOADABLE_PACKAGE;
            else
            {
                if (version.Contains(".")) version = $"UE{version.Replace(".", "_")}";
                version = $"VER_{version}"; Enum.TryParse(version, out Version);
            }
            BytecodeModifier.Version = Version;
        }

        private static bool _autoExit = false;
        static void ProcessArgs(string[] args)
        {
            foreach (var arg in args)
            {
                if (!arg.StartsWith("-") || (arg.Contains("=") || (arg.StartsWith("[")))) continue;
                var argument = arguments.Find(a => a.Key.Equals(arg, StringComparison.OrdinalIgnoreCase));
                if (argument != null)
                {
                    argument.Action();
                }
                else
                {
                    Console.WriteLine($"Unknown argument: {arg}");
                    ShowHelp(arguments);
                    return;
                }
            }
        }

        private static StringBuilder filelistBuilder = new StringBuilder();
        private static void AddToFileList(string path)
        {
            filelistBuilder.AppendLine($"\"{path}\"");
        }
        private static bool IsAsset(string file)
        {
            if (AllowedExtensionForAsset.Any(x => file.Trim('\"').EndsWith(x))) return true;
            return false;
        }
        private static string GetUnpackCsvPath(string file)
        {
            var csvFile = string.IsNullOrWhiteSpace(file) ? "" : Path.GetFileNameWithoutExtension(file)+".csv";
            var directoryName = string.IsNullOrWhiteSpace(file) ? "" : $"\\{Path.GetFileName(Path.GetDirectoryName(file))}\\";
            var unpackDirectory = EnvironmentHelper.CurrentAssemblyDirectory + "\\Unpack\\" + directoryName;
            Directory.CreateDirectory(unpackDirectory);
            return unpackDirectory + csvFile;
        }
        public static void ProcessProgram(string[] args)
        {
            var cmdArg = GetRunCommand(args);
            args = SplitArgs(args);
            ProcessArgs(args); ProcessVersion(args);
            if (args.Length > 0)
            {
                var onlyArgs = args.Where(x => !x.StartsWith("--")).ToArray();
                if (IsAsset(onlyArgs[0]))
                {
                    Console.WriteLine("[Extract mode]\n");
                    var assetFile = onlyArgs.First(x => !x.Contains(".csv") && !x.Contains(".json"));
                    var csvFile = onlyArgs.Count() >1 ? onlyArgs.FirstOrDefault(x => x.Contains(".csv"))
                        : Path.GetFileNameWithoutExtension(assetFile);
                    BytecodeExtractor.AllowTableExtract = AllowTable;
                    BytecodeExtractor.ExtractAllAndWriteUberJSON(assetFile);
                }
                else if (onlyArgs[0].Contains(".csv") || onlyArgs[0].Contains(".json"))
                {
                    var assetFile = onlyArgs.FirstOrDefault(x => IsAsset(x));
                    var csvFile = onlyArgs.FirstOrDefault(x => x.Contains(".csv"));
                    var uberJson = onlyArgs.FirstOrDefault(x => x.Contains(".json"));

                    if (string.IsNullOrWhiteSpace(assetFile))
                    {
                        Console.WriteLine("Drag & Drop UE file (.uasset|.umap) and press ENTER:");
                        assetFile = Console.ReadLine().Trim('\"');
                        if (!IsAsset(assetFile))
                        {
                            Console.WriteLine($"{Path.GetExtension(assetFile)} is invalid file type.");
                            Console.ReadLine(); Environment.Exit(1);
                        }
                    }
                    {
                        Console.WriteLine("[Replacement mode]");
                        var uberJSONCollection = csvFile != null ? JSON.UberJSONProcessor.Convert(csvFile) : JSON.UberJSONProcessor.ReadFile(uberJson);
                        var uber = uberJSONCollection.FirstOrDefault(x => Path.GetFileNameWithoutExtension(x.FileName) == Path.GetFileNameWithoutExtension(assetFile));
                        if (uber != null)
                        {
                            var values = uber.GetValues();
                            Kismet.BytecodeModifier.ModifyAsset(assetFile, values, AllowTable);
                            AddToFileList(assetFile);
                        }
                    }
                                  
                }
                else if (Directory.Exists(onlyArgs[0]))
                {
                    var files = Directory.GetFiles($@"{onlyArgs[0]}", "*", SearchOption.AllDirectories)
                        .Where(x => IsAsset(x)).ToArray();
                    var other = packFiles == true && onlyArgs.Length > 1 ? onlyArgs[1] : string.Empty; 
                    if (other != string.Empty)
                    {
                        Console.WriteLine("[Replacement mode]");
                        var uberJson = onlyArgs.FirstOrDefault(x => x.Contains(".json"));
                        if (uberJson != null)
                        {
                            var uberJSONCollection = JSON.UberJSONProcessor.Deserialize(uberJson);
                            foreach (var uber in uberJSONCollection)
                            {
                                var assetFile = files.FirstOrDefault(x => Path.GetFileName(x) == Path.GetFileName(uber.FileName));
                                var values = uber.GetValues();
                                Kismet.BytecodeModifier.ModifyAsset(assetFile, values, AllowTable);
                                AddToFileList(assetFile);
                            }
                        }
                        else
                        {
                            var csvFiles = Directory.GetFiles($@"{other}", "*", SearchOption.AllDirectories).Where(x => x.EndsWith(".csv"));
                            foreach (var csvFile in csvFiles)
                            {
                                var assetFile = files.FirstOrDefault(x => Path.GetFileNameWithoutExtension(x) == Path.GetFileNameWithoutExtension(csvFile));
                                var uberJSONCollection = JSON.UberJSONProcessor.Convert(csvFile);
                                var uber = uberJSONCollection.FirstOrDefault(x => x.FileName == Path.GetFileName(assetFile));
                                if (uber != null)
                                {
                                    var values = uber.GetValues();
                                    Kismet.BytecodeModifier.ModifyAsset(assetFile, values, AllowTable);
                                    AddToFileList(assetFile);
                                }
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("[Extract mode]\n");
                        Console.WriteLine("------ [UberJSON] ------");
                        Kismet.BytecodeExtractor.AllowTableExtract = AllowTable;
                        Kismet.BytecodeExtractor.ExtractAllAndWriteUberJSON(files);
                    }                      
                }
            }

            System.IO.File.WriteAllText(EnvironmentHelper.CurrentAssemblyDirectory+"\\mod_filelist.txt", filelistBuilder.ToString());
            #region Запускаем автоматический перевод
            if (AllowTranslate) ProcessTranslator();
            #endregion

            #region Запускаем командную строку
            if (!string.IsNullOrWhiteSpace(cmdArg)) RunTerminal(cmdArg);
            #endregion
        }

    }
}