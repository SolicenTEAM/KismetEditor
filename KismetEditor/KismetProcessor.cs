using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Solicen.Kismet
{
    class KismetProcessor
    {
        static bool isExtract = false;
        public static UAssetAPI.UnrealTypes.EngineVersion Version = UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_18;

        static List<Argument> arguments = new List<Argument>
        {
            new Argument("--extract", "extract strings from `kismet` to csv.", () => isExtract = true),
            new Argument("--version", "set specific unreal version: --version=4.18",null),
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
                if (!arg.StartsWith("--") || (arg.Contains("="))) continue;
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

        public static void ProcessProgram(string[] args)
        {
            ProcessArgs(args); ProcessVersion(args);
            if (args.Length > 0)
            {
                if (args[0].Contains(".json") || args[0].Contains(".uasset") || args[0].Contains(".uexp"))
                {
                    var kismetFile = args[0]; var csvFile = args.Length>1 ? args[1] : "";
                    // Если аргумент --extract был активирован, извлечение строк из файла.

                    if (isExtract) // Для извлечения уникальных строк
                    {
                        Solicen.Kismet.BytecodeModifier.ExtractAndWriteCSV(kismetFile, csvFile); 
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(csvFile))
                        {
                            Console.WriteLine("Drag & Drop csv file.");
                            csvFile = Console.ReadLine();
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
                }
                else if (Directory.Exists(args[0]))
                {
                    var files = Directory.GetFiles($@"{args[0]}", "*", SearchOption.AllDirectories)
                        .Where(x => x.EndsWith(".uexp") || x.EndsWith(".uasset") || x.EndsWith(".json"));
                    foreach (var file in files)
                    {
                        var kismetFile = file;
                        var info = new FileInfo(kismetFile).Name;
                        Console.WriteLine($"...{info}");
                        try
                        {
                            Solicen.Kismet.BytecodeModifier.ExtractAndWriteCSV(kismetFile, "");
                        }
                        catch (Exception ex)
                        {
                            
                        }
                    }
                }
            }
        }

    }
}