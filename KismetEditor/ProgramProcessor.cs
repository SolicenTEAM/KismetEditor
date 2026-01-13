using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Solicen.Kismet
{
    class ProgramProcessor
    {
        public static bool DebugMode = false;
        static bool isExtract = true;
        static bool packFiles = false;
        public static UAssetAPI.UnrealTypes.EngineVersion Version = UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_18;

        static List<Argument> arguments = new List<Argument>
        {
            new Argument("--pack", "[Directory] pack translate from csv to files in directory", () => packFiles=true),
            new Argument("--debug", "[File] write additional files for debug",() => DebugMode = true),
            new Argument("--version", "[Utility] set specific unreal version: --version=4.18",null),
            new Argument("--help", "Show help information", () => ShowHelp(arguments))
        };

        class Argument
        {
            public string Name { get; }
            public string Description { get; }
            public Action Action { get; }

            public Argument(string name, string description, Action action)
            {
                Name = name;
                Description = description;
                Action = action;
            }
        }

        static void ShowHelp(System.Collections.Generic.List<Argument> arguments)
        {
            Console.WriteLine("Available arguments:");
            foreach (var argument in arguments)
            {
                Console.WriteLine($"{argument.Name}: {argument.Description}");
            }
        }

        #region Расширенное управление терминалом
        static string GetRunCommand(string[] args) => Regex.Match(string.Join(" ", args), @"[[].*[]]").Value.Trim('[', ']');
        static void RunTerminal(string anyCommand) => System.Diagnostics.Process.Start("CMD.exe", "/c " + anyCommand);
        static string[] SplitArgs(string[] args)
        {
            if (GetRunCommand(args) != string.Empty)
                return string.Join(" ", args).Replace(GetRunCommand(args), "").Split(' ').Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            else
                return string.Join(" ", args).Split(' ').Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        }
        #endregion

        

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
                if (!arg.StartsWith("--") || (arg.Contains("=") || (arg.StartsWith("[")))) continue;
                var argument = arguments.Find(a => a.Name.Equals(arg, StringComparison.OrdinalIgnoreCase));
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

        private static string CreateDirectoryAndGetUnpackCsvPath(string file)
        {
            var csvFile = Path.GetFileNameWithoutExtension(file);
            var directoryName = Path.GetFileName(Path.GetDirectoryName(file));
            var unpackDirectory = EnvironmentHelper.CurrentAssemblyDirectory + "\\Unpack\\" + $"\\{directoryName}\\";
            Directory.CreateDirectory(unpackDirectory);

            return unpackDirectory + csvFile + ".csv";
        }
        public static void ProcessProgram(string[] args)
        {
            args = SplitArgs(args);
            ProcessArgs(args); ProcessVersion(args);
            if (args.Length > 0)
            {
                var onlyStr = args.Where(x => !x.StartsWith("--") || !x.StartsWith("[")).ToArray();
                if (onlyStr[0].Contains(".uasset") || args[0].Contains(".umap"))
                {
                    var kismetFile = onlyStr.First(x => !x.Contains(".csv"));
                    var csvFile = onlyStr.Count() >1 ? onlyStr.FirstOrDefault(x => x.Contains(".csv"))
                        : Path.GetFileNameWithoutExtension(kismetFile);

                    Solicen.Kismet.BytecodeModifier.ExtractAndWriteCSV(kismetFile, csvFile);
                }
                else if (onlyStr[0].Contains(".csv"))
                {
                    var kismetFile = onlyStr.First(x => !x.Contains(".csv"));
                    var csvFile = onlyStr.First(x => x.Contains(".csv"));

                    if (string.IsNullOrWhiteSpace(kismetFile))
                    {
                        Console.WriteLine("Drag & Drop UE file (.uasset|.umap)");
                        kismetFile = Console.ReadLine();
                        var extension = Path.GetExtension(csvFile);
                        if (extension != ".uasset" || extension != ".umap")
                        {
                            Console.WriteLine($"{Path.GetExtension(kismetFile)} is invalid file type.");
                            Console.ReadLine(); Environment.Exit(1);
                        }
                    }

                    if (Path.GetExtension(csvFile) != ".csv")
                    {
                        Console.WriteLine($"{Path.GetExtension(csvFile)} is invalid file type.");
                        Console.ReadLine(); Environment.Exit(1);
                    }

                    // Если аргумент содержит .csv файл - запускаем замену строк
                    if (Path.GetExtension(csvFile) == ".csv")
                    {
                        var csv = Kismet.BytecodeModifier.TranslateFromCSV(csvFile);
                        Kismet.BytecodeModifier.ModifyAsset(kismetFile, csv);
                    }
                }
                else if (Directory.Exists(onlyStr[0]))
                {
                    var files = Directory.GetFiles($@"{onlyStr[0]}", "*", SearchOption.AllDirectories)
                        .Where(x => x.EndsWith(".uasset") || x.EndsWith(".umap")).ToArray();
                    var otherDirectory = packFiles == true && onlyStr.Length > 1 ? onlyStr[1] : string.Empty; 
                    if (otherDirectory != string.Empty)
                    {
                        var csvFiles = Directory.GetFiles($@"{otherDirectory}", "*", SearchOption.AllDirectories)
                        .Where(x => x.EndsWith(".csv"));
                        foreach (var csv in csvFiles)
                        {
                            var asset = files.FirstOrDefault(x => Path.GetFileNameWithoutExtension(x) == Path.GetFileNameWithoutExtension(csv));
                            var FromCSV = Kismet.BytecodeModifier.TranslateFromCSV(csv);
                            Kismet.BytecodeModifier.ModifyAsset(asset,FromCSV);
                        }
                    }
                    else
                    {
                        foreach (var file in files)
                        {
                            var kismetFile = file;
                            var info = new FileInfo(kismetFile);
                            Console.WriteLine($"...{info.Name}");

                            try
                            {
                                Solicen.Kismet.BytecodeModifier.ExtractAndWriteCSV(kismetFile, CreateDirectoryAndGetUnpackCsvPath(file));
                            }
                            catch (Exception ex)
                            {

                            }
                        }
                    }
                    
                   
                }
            }
            #region Запускаем командную строку
            var cmdArg = GetRunCommand(args);
            if (!string.IsNullOrWhiteSpace(cmdArg)) RunTerminal(cmdArg);
            #endregion

            if (DebugMode) Console.ReadLine();
        }

    }
}