using Solicen.Translator;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Solicen.CLI
{
    /// <summary>
    /// Обрабатывает аргументы командной строки, настраивает конфигурацию и выполняет связанные действия.
    /// </summary>
    public static class Argumentor
    {
        #region Расширенное управление терминалом
        public static void RunTerminal(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return;
            System.Diagnostics.Process.Start("CMD.exe", "/c " + command);
        }

        public static string[] SplitArgs(string[] args)
        {
            if (args == null) return Array.Empty<string>();
            // Новый, более надежный подход. Мы не объединяем аргументы в одну строку,
            // а обрабатываем их как есть, чтобы сохранить пути с пробелами.
            var processedArgs = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(args[i])) continue;
                // Специальная обработка для --run=[...], чтобы захватить всю команду.
                var runMatch = Regex.Match(args[i], @"^--run\s*=?\s*\[(.*)", RegexOptions.IgnoreCase);
                if (runMatch.Success)
                {
                    var commandBuilder = new StringBuilder(runMatch.Groups[1].Value);
                    // Если команда не заканчивается в этом же аргументе, ищем ']' в следующих.
                    while (!args[i].EndsWith("]") && i + 1 < args.Length)
                    {
                        i++;
                        commandBuilder.Append(" ").Append(args[i]);
                    }
                    string finalCommand = commandBuilder.ToString().TrimEnd(']');
                    processedArgs.Add($"--run={finalCommand}");
                }
                else
                {
                    processedArgs.Add(args[i]);
                }
            }
            return processedArgs.ToArray();
        }
        #endregion

        #region Helpers

        private static string CleanValue(string raw)
        {
            if (raw == null) return null;
            // Trim whitespace and surrounding quotes
            var v = raw.Trim();
            // Remove outer double/single quotes if present
            if ((v.StartsWith("\"") && v.EndsWith("\"") && v.Length >= 2) ||
                (v.StartsWith("'") && v.EndsWith("'") && v.Length >= 2))
            {
                v = v.Substring(1, v.Length - 2);
            }
            // Also trim remaining quotes inside (e.g., "C:\path" with inner)
            v = v.Trim().Trim('"').Trim('\'').Trim();
            return v;
        }

        private static Argument FindClosestArgument(string unknown, List<Argument> definedArguments)
        {
            if (string.IsNullOrWhiteSpace(unknown) || definedArguments == null || definedArguments.Count == 0) return null;
            string keyPart = Argument.GetKeyPart(unknown).Trim();
            if (string.IsNullOrEmpty(keyPart)) return null;

            // Normalize: remove leading dashes for distance calc but keep for display
            string normalizedUnknown = keyPart.TrimStart('-').ToLowerInvariant();

            Argument best = null;
            int bestDist = int.MaxValue;

            foreach (var arg in definedArguments)
            {
                // Compare against Key, ShortKey and Aliases
                var candidates = new List<string> { arg.Key, arg.ShortKey };
                if (arg.Aliases != null) candidates.AddRange(arg.Aliases);
                foreach (var candidate in candidates)
                {
                    if (string.IsNullOrEmpty(candidate)) continue;
                    string normalizedCandidate = candidate.TrimStart('-').ToLowerInvariant();
                    int dist = LevenshteinDistance(normalizedUnknown, normalizedCandidate);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = arg;
                    }
                }
            }

            // Only suggest if reasonably close (heuristic) - strict to avoid noisy suggestions
            if (best != null)
            {
                // Very strict: distance 1-2 is strong, distance 3 only for longer keys
                if (bestDist <= 2) return best;
                if (bestDist == 3 && normalizedUnknown.Length >= 4 && best.Key.Length >= 4) return best;

                // If unknown is substring of candidate or vice versa, allow distance 3 as well
                string bestKeyNorm = best.Key.TrimStart('-').ToLowerInvariant();
                string bestShortNorm = best.ShortKey?.TrimStart('-').ToLowerInvariant() ?? "";
                if (normalizedUnknown.Length >= 3 && bestDist <= 3 &&
                    (bestKeyNorm.Contains(normalizedUnknown) || normalizedUnknown.Contains(bestKeyNorm) ||
                     (!string.IsNullOrEmpty(bestShortNorm) && (bestShortNorm.Contains(normalizedUnknown) || normalizedUnknown.Contains(bestShortNorm)))))
                {
                    return best;
                }
            }
            return null;
        }

        private static int LevenshteinDistance(string s, string t)
        {
            if (string.IsNullOrEmpty(s)) return t?.Length ?? 0;
            if (string.IsNullOrEmpty(t)) return s.Length;
            int n = s.Length, m = t.Length;
            var d = new int[n + 1, m + 1];
            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;
            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = s[i - 1] == t[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            }
            return d[n, m];
        }

        private static void PrintValueHint(Argument argument)
        {
            // Контекстные подсказки по формату значения
            switch (argument.Key)
            {
                case "--mapping":
                    CLI.Console.WriteLine($"[DarkGray]  Example: [Cyan]{argument.Key}=Gori_usmap.usmap[DarkGray] or [Cyan]{argument.ShortKey}=Gori[DarkGray] or [Cyan]{argument.ShortKey} \"C:\\Full\\Path\\Gori.usmap\"[White]");
                    CLI.Console.WriteLine($"[DarkGray]  Tip: file must be next to KismetEditor.exe or specify full path. Use [Cyan]--mapping-auto[DarkGray] to pick any .usmap nearby.[White]");
                    break;
                case "--version":
                    CLI.Console.WriteLine($"[DarkGray]  Example: [Cyan]{argument.Key}=4.18[DarkGray], [Cyan]-v=5.1[DarkGray], [Cyan]-v=UE4_27[DarkGray] or [Cyan]-v=UE5_3[White]");
                    break;
                case "--pack-folder":
                    CLI.Console.WriteLine($"[DarkGray]  Example: [Cyan]{argument.Key}=MyGame_RUS[DarkGray] or [Cyan]{argument.ShortKey} \"C:\\Output\\MyMod\"[White]");
                    break;
                case "--only-key":
                    CLI.Console.WriteLine($"[DarkGray]  Example: [Cyan]{argument.Key}=ENG[DarkGray] or [Cyan]{argument.ShortKey}=DIAG[White]");
                    break;
                case "--api-key":
                    CLI.Console.WriteLine($"[DarkGray]  Example: [Cyan]{argument.Key}=sk-or-v1-...[White]");
                    break;
                case "--api-Model":
                    CLI.Console.WriteLine($"[DarkGray]  Example: [Cyan]{argument.Key}=tngtech/deepseek-r1t2-chimera:free[White]");
                    break;
                case "--source-lang":
                case "--target-lang":
                    CLI.Console.WriteLine($"[DarkGray]  Example: [Cyan]{argument.Key}=en[DarkGray], [Cyan]ru[DarkGray], [Cyan]ja[White]");
                    break;
                case "--endpoint":
                    CLI.Console.WriteLine($"[DarkGray]  Example: [Cyan]{argument.Key}=yandex[DarkGray] ([Cyan]yandex[DarkGray], [Cyan]google[DarkGray], [Cyan]microsoft[DarkGray], [Cyan]router[DarkGray])[White]");
                    break;
                case "--run":
                    CLI.Console.WriteLine($"[DarkGray]  Example: [Cyan]{argument.Key}=[echo done && pause][DarkGray] or [Cyan]{argument.ShortKey}=\"UnrealPak.exe ...\"[White]");
                    break;
                default:
                    CLI.Console.WriteLine($"[DarkGray]  Example: [Cyan]{argument.Key}=<value>[White]");
                    break;
            }
        }

        private static void ValidateMappingResult(string originalValue)
        {
            // Вызывается после попытки установки mapping
            var path = Solicen.Kismet.AssetLoader.MappingsPath;
            if (string.IsNullOrEmpty(path))
            {
                CLI.Console.WriteLine($"[Red][ERR] [White]Mapping file not found for value '[Yellow]{originalValue}[White]'");
                try
                {
                    string exeDir = EnvironmentHelper.AssemblyDirectory;
                    var usmapsNearby = Directory.Exists(exeDir) ? Directory.GetFiles(exeDir, "*.usmap") : Array.Empty<string>();
                    if (usmapsNearby.Length == 0)
                    {
                        CLI.Console.WriteLine($"[DarkGray]  No [White].usmap[DarkGray] files found next to [White]{exeDir}[DarkGray]. Place your .usmap file next to KismetEditor.exe or specify full path.[White]");
                    }
                    else
                    {
                        CLI.Console.WriteLine($"[DarkGray]  Available .usmap files nearby ({usmapsNearby.Length}):[White]");
                        foreach (var f in usmapsNearby.Take(10))
                            CLI.Console.WriteLine($"[DarkGray]    - [White]{Path.GetFileName(f)} [DarkGray]({f})[White]");
                        if (usmapsNearby.Length > 10)
                            CLI.Console.WriteLine($"[DarkGray]    ... and {usmapsNearby.Length - 10} more[White]");
                        CLI.Console.WriteLine($"[DarkGray]  Try: [Cyan]--mapping={Path.GetFileName(usmapsNearby[0])}[DarkGray] or [Cyan]--mapping-auto[White]");
                    }
                }
                catch (Exception ex)
                {
                    CLI.Console.WriteLine($"[DarkGray]  (Failed to list .usmap files: {ex.Message})[White]");
                }
                CLI.Console.WriteLine($"[DarkGray]  Tip: Use [Cyan]--mapping-auto[DarkGray] ([Cyan]-ma[DarkGray]) to auto-pick any .usmap nearby.\n[White]");
            }
            else
            {
                if (File.Exists(path))
                    CLI.Console.WriteLine($"[DarkGray][INF] [White]Using mappings: [Cyan]{path}[White]");
                else
                    CLI.Console.WriteLine($"[Yellow][WARN] [White]Mappings path set to '[Yellow]{path}[White]' but file does not exist (may be relative name).");
            }
        }

        #endregion

        /// <summary>
        /// Разбирает массив аргументов командной строки, выполняет действия и возвращает оставшиеся аргументы (пути к файлам).
        /// </summary>
        public static string[] Process(string[] args, List<Argument> definedArguments)
        {
            var remainingArgs = new List<string>();
            if (definedArguments == null) definedArguments = new List<Argument>();
            if (args == null || args.Length == 0)
            {
                return remainingArgs.ToArray();
            }

            bool hasAnyError = false;
            bool helpShown = false;

            for (int i = 0; i < args.Length; i++)
            {
                string raw = args[i];
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string arg = raw.Trim();
                if (string.IsNullOrEmpty(arg)) continue;

                // Позиционный аргумент (путь) не начинается с '-'
                if (!arg.StartsWith("-"))
                {
                    // Это путь - валидируем существование
                    string cleaned = CleanValue(arg);
                    if (string.IsNullOrWhiteSpace(cleaned))
                        continue;

                    bool isFile = File.Exists(cleaned);
                    bool isDir = Directory.Exists(cleaned);

                    if (!isFile && !isDir)
                    {
                        // Специальная обработка: .json/.csv могут быть ВЫХОДНЫМИ файлами для Extract (создаются), не требуем их существования
                        string extForCheck = null;
                        try { extForCheck = Path.GetExtension(cleaned)?.ToLowerInvariant(); } catch { }
                        bool isJsonCsvOutput = extForCheck == ".json" || extForCheck == ".csv";
                        if (isJsonCsvOutput)
                        {
                            // Для выходных JSON/CSV не выдаем ошибку Path not found — они будут созданы.
                            // Но проверим валидность имени файла: нет запрещенных символов, расширение корректно
                            bool hasInvalidChars = cleaned.IndexOfAny(Path.GetInvalidPathChars()) >= 0;
                            if (hasInvalidChars)
                            {
                                CLI.Console.WriteLine($"[Red][ERR] [White]Output path contains invalid characters: '[Yellow]{cleaned}[White]'");
                                hasAnyError = true;
                            }
                            else
                            {
                                // Легитимный выходной путь — не считаем ошибкой, просто добавляем
                                // Инфо для отладки можно вывести в DebugMode
                                // CLI.Console.WriteLine($"[DarkGray][INF] Output file will be created: '[White]{cleaned}[White]'");
                            }
                            remainingArgs.Add(cleaned);
                            continue;
                        }

                        CLI.Console.WriteLine($"[Red][ERR] [White]Path not found: '[Yellow]{cleaned}[White]'");
                        string parent = null;
                        try { parent = Path.GetDirectoryName(Path.GetFullPath(cleaned)); } catch { try { parent = Path.GetDirectoryName(cleaned); } catch { } }

                        if (!string.IsNullOrWhiteSpace(parent))
                        {
                            if (!Directory.Exists(parent))
                            {
                                CLI.Console.WriteLine($"[DarkGray]  Parent directory does not exist: '[White]{parent}[DarkGray]'");
                                CLI.Console.WriteLine($"[DarkGray]  Check for typos, missing drive letter, or incorrect relative path. Current exe dir: [White]{EnvironmentHelper.AssemblyDirectory}[White]");
                            }
                            else
                            {
                                CLI.Console.WriteLine($"[DarkGray]  Parent directory exists, but file/folder '[White]{Path.GetFileName(cleaned)}[DarkGray]' not found inside it.");
                                try
                                {
                                    var entries = Directory.GetFileSystemEntries(parent);
                                    if (entries.Length > 0 && entries.Length <= 12)
                                    {
                                        CLI.Console.WriteLine($"[DarkGray]  Contents of '[White]{parent}[DarkGray]':");
                                        foreach (var f in entries.Take(12))
                                            CLI.Console.WriteLine($"[DarkGray]    - [White]{Path.GetFileName(f)}");
                                    }
                                    else if (entries.Length > 12)
                                    {
                                        CLI.Console.WriteLine($"[DarkGray]  Directory contains {entries.Length} entries. Verify filename spelling (showing first 12):");
                                        foreach (var f in entries.Take(12))
                                            CLI.Console.WriteLine($"[DarkGray]    - [White]{Path.GetFileName(f)}");
                                    }
                                    else
                                    {
                                        CLI.Console.WriteLine($"[DarkGray]  Directory '[White]{parent}[DarkGray]' is empty.[White]");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    CLI.Console.WriteLine($"[DarkGray]  (Cannot list directory: {ex.Message})[White]");
                                }
                            }
                        }
                        else
                        {
                            CLI.Console.WriteLine($"[DarkGray]  File is expected to be in current directory: [White]{EnvironmentHelper.AssemblyDirectory}");
                            CLI.Console.WriteLine($"[DarkGray]  If path contains spaces, wrap it in quotes: [Cyan]\"C:\\My Folder\\file.uasset\"[White]");
                        }

                        // Эвристика: похоже на аргумент без дефисов? e.g., "mapping=Gori.usmap"
                        if (cleaned.Contains("=") && !cleaned.Contains("\\") && !cleaned.Contains("/") && !cleaned.StartsWith("-"))
                        {
                            string guessedKey = cleaned.Split('=')[0].Trim();
                            var suggestion = FindClosestArgument("--" + guessedKey, definedArguments);
                            if (suggestion != null)
                                CLI.Console.WriteLine($"[DarkGray]  Looks like an argument with '=' but missing leading dashes. Did you mean '[Cyan]{suggestion.Key}={cleaned.Split('=', 2)[1]}[White]' ? [White]");
                            else
                                CLI.Console.WriteLine($"[DarkGray]  Did you forget leading dashes? Example: [Cyan]--{cleaned}[White]");
                        }

                        // Также подсказка если расширение похоже на опечатку
                        string ext = null;
                        try { ext = Path.GetExtension(cleaned); } catch { }
                        if (!string.IsNullOrEmpty(ext) && ext.Length >= 2)
                        {
                            var knownExts = new[] { ".uasset", ".umap", ".uexp", ".json", ".csv", ".usmap" };
                            if (!knownExts.Contains(ext.ToLowerInvariant()))
                            {
                                var closestExt = knownExts.OrderBy(k => LevenshteinDistance(ext.ToLowerInvariant(), k)).First();
                                if (LevenshteinDistance(ext.ToLowerInvariant(), closestExt) <= 2)
                                    CLI.Console.WriteLine($"[DarkGray]  Unknown extension '[Yellow]{ext}[DarkGray]'. Did you mean '[Cyan]{closestExt}[DarkGray]' ?[White]");
                            }
                        }

                        hasAnyError = true;
                        remainingArgs.Add(cleaned);
                        continue;
                    }

                    // Файл/папка существует — проверяем расширение для файлов
                    if (isFile)
                    {
                        string ext = Path.GetExtension(cleaned).ToLowerInvariant();
                        var allowedAsset = new[] { ".uasset", ".umap" };
                        var allowedUexp = new[] { ".uexp" };
                        var allowedData = new[] { ".json", ".csv" };
                        var allAllowed = allowedAsset.Concat(allowedUexp).Concat(allowedData).Concat(new[] { ".usmap" }).ToArray();
                        if (!allAllowed.Contains(ext))
                        {
                            CLI.Console.WriteLine($"[Yellow][WARN] [White]File '[Yellow]{cleaned}[White]' has unusual extension '[Yellow]{ext}[White]'. Expected .uasset/.umap/.uexp/.json/.csv");
                            CLI.Console.WriteLine($"[DarkGray]  The file will still be processed as positional argument, but may be ignored later. Check for typos in extension.[White]");
                        }
                        if (ext == ".uexp")
                        {
                            bool hasUexpFlag = definedArguments.Any(a => a.Key == "--uexp");
                            // Inform if --uexp not enabled but file is .uexp — will be filtered unless -xp
                            // Не делаем ошибку, просто инфо
                        }
                    }

                    remainingArgs.Add(cleaned);
                    continue;
                }

                // Аргумент начинается с '-' — ищем точное совпадение (строгое)
                var argument = definedArguments.Find(a => a.Matches(arg));

                if (argument == null)
                {
                    // Неизвестный аргумент — показываем детальную ошибку + подсказку
                    CLI.Console.WriteLine($"[Red][ERR] [White]Unknown argument: '[Yellow]{arg}[White]'");

                    // Попытка найти похожий аргумент для подсказки (Did you mean?)
                    var suggestion = FindClosestArgument(arg, definedArguments);
                    if (suggestion != null)
                    {
                        CLI.Console.WriteLine($"[DarkGray]  Did you mean '[Cyan]{suggestion.Key}[DarkGray]' ([Cyan]{suggestion.ShortKey}[DarkGray]) ? [DarkGray]{suggestion.Description}[White]");
                        // Дополнительно: если unknown похож на mapping, подсказать разницу -map vs -ma
                        string norm = arg.TrimStart('-').ToLower();
                        if (norm == "m" || norm == "ma" || norm.StartsWith("map"))
                        {
                            CLI.Console.WriteLine($"[DarkGray]  Note: [Cyan]--mapping -map/-m[DarkGray] needs a value (usmap), while [Cyan]--mapping-auto -ma[DarkGray] is a flag without value.[White]");
                        }
                    }
                    else
                    {
                        CLI.Console.WriteLine($"[DarkGray]  Use [White]--help[DarkGray] or [White]-h[DarkGray] to see all available arguments.[White]");
                    }

                    // Специальные подсказки для частых опечаток
                    string keyPartRaw = Argument.GetKeyPart(arg).ToLowerInvariant();
                    if (keyPartRaw == "--map" || keyPartRaw == "-maps" || keyPartRaw == "--maps")
                        CLI.Console.WriteLine($"[DarkGray]  Hint: correct is [Cyan]--mapping[DarkGray] ([Cyan]-map[DarkGray], [Cyan]-m[DarkGray]) or [Cyan]--mapping-auto[DarkGray] ([Cyan]-ma[DarkGray])[White]");
                    if (keyPartRaw == "--sc" || keyPartRaw == "-sconst")
                        CLI.Console.WriteLine($"[DarkGray]  Hint: correct is [Cyan]--sconst -sc[White]");

                    hasAnyError = true;
                    continue;
                }

                // Найден известный аргумент
                string keyPart = Argument.GetKeyPart(arg);
                string valuePartRaw = Argument.GetValuePart(arg);
                bool hasEquals = arg.Contains("=");

                if (argument.HasValue)
                {
                    string value = null;

                    if (hasEquals)
                    {
                        // Значение после '='
                        value = CleanValue(valuePartRaw);
                        // valuePartRaw может быть null или "" если было "--mapping=" без значения
                        if (string.IsNullOrWhiteSpace(value))
                        {
                            CLI.Console.WriteLine($"[Red][ERR] [White]Argument '[Yellow]{keyPart}[White]' requires a non-empty value, but empty value was provided ('{arg}').");
                            CLI.Console.WriteLine($"[DarkGray]  Description: [White]{argument.Description}");
                            PrintValueHint(argument);
                            hasAnyError = true;
                            continue;
                        }
                    }
                    else
                    {
                        // Значение в следующем токене, если он не флаг
                        if (i + 1 < args.Length && !args[i + 1].TrimStart().StartsWith("-"))
                        {
                            value = CleanValue(args[i + 1]);
                            if (string.IsNullOrWhiteSpace(value))
                            {
                                CLI.Console.WriteLine($"[Red][ERR] [White]Argument '[Yellow]{keyPart}[White]' requires a value but next token is empty/whitespace.");
                                CLI.Console.WriteLine($"[DarkGray]  Description: [White]{argument.Description}");
                                PrintValueHint(argument);
                                hasAnyError = true;
                                continue;
                            }
                            i++; // потребляем следующий токен как значение
                        }
                        else
                        {
                            CLI.Console.WriteLine($"[Red][ERR] [White]Missing value for argument '[Yellow]{keyPart}[White]' ([Cyan]{argument.Key}[DarkGray], [Cyan]{argument.ShortKey}[DarkGray])");
                            CLI.Console.WriteLine($"[DarkGray]  Description: [White]{argument.Description}");
                            PrintValueHint(argument);
                            if (i + 1 < args.Length && args[i + 1].TrimStart().StartsWith("-"))
                                CLI.Console.WriteLine($"[DarkGray]  Next token '[Yellow]{args[i + 1]}[DarkGray]' looks like another argument, not a value. Use '[Cyan]{keyPart}=value[DarkGray]' syntax.[White]");
                            else
                                CLI.Console.WriteLine($"[DarkGray]  Provide value as '[Cyan]{keyPart}=value[DarkGray]' or '[Cyan]{keyPart} value[DarkGray]'.[White]");
                            hasAnyError = true;
                            continue;
                        }
                    }

                    // Значение получено — пробуем применить
                    try
                    {
                        argument.ActionWithValue?.Invoke(value);
                    }
                    catch (Exception ex)
                    {
                        CLI.Console.WriteLine($"[Red][ERR] [White]Failed to apply argument '[Yellow]{keyPart}[White]' with value '[Yellow]{value}[White]': [White]{ex.Message}");
                        if (Solicen.CLI.CLIHandler.Config.DebugMode && ex.StackTrace != null)
                            CLI.Console.WriteLine($"[DarkGray]{ex.StackTrace}[White]");
                        hasAnyError = true;
                        continue;
                    }

                    // Пост-валидация для специфичных аргументов cо значением
                    if (argument.Key == "--mapping")
                    {
                        ValidateMappingResult(value);
                        if (string.IsNullOrEmpty(Solicen.Kismet.AssetLoader.MappingsPath))
                            hasAnyError = true;
                    }
                    else if (argument.Key == "--version")
                    {
                        // ProcessVersion уже установила версию, но если парсинг не удался — предупредим
                        var ver = Solicen.CLI.CLIHandler.Config.Version;
                        // Если версия осталась дефолтной и value явно не совпадает с дефолтом — возможно парсинг не сработал
                        // Эвристика: value должен содержать цифры
                        if (ver == UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_18 && !value.Contains("4.18") && !value.ToUpper().Contains("UE4_18"))
                        {
                            // Check if parsing actually produced 0 / unknown
                            // Enum.TryParse for invalid will leave ver as 0 (VER_Unknown ~0?) — проверим
                            bool looksLikeVersion = Regex.IsMatch(value, @"^\d+(\.\d+)?$") || Regex.IsMatch(value, @"^UE\d.*", RegexOptions.IgnoreCase) || Regex.IsMatch(value, @"^VER_.*", RegexOptions.IgnoreCase);
                            if (looksLikeVersion)
                            {
                                // Попробуем повторно распарсить для проверки
                                string testVer = value.Trim();
                                if (testVer.Contains(".")) testVer = $"UE{testVer.Replace(".", "_")}";
                                UAssetAPI.UnrealTypes.EngineVersion testEngine;
                                if (!Enum.TryParse($"VER_{testVer}", true, out testEngine) && !Enum.TryParse(testVer, true, out testEngine))
                                {
                                    CLI.Console.WriteLine($"[Yellow][WARN] [White]Version '[Yellow]{value}[White]' could not be parsed. Using default [Cyan]{ver}[White]. Expected e.g. [Cyan]4.18[DarkGray], [Cyan]5.1[DarkGray], [Cyan]UE4_18[White].");
                                }
                            }
                        }
                        else
                        {
                            CLI.Console.WriteLine($"[DarkGray][INF] [White]Engine version set to: [Cyan]{ver}[White] (from '[Yellow]{value}[White]')");
                        }
                    }
                }
                else
                {
                    // Флаг без значения
                    if (hasEquals)
                    {
                        string after = valuePartRaw ?? "";
                        CLI.Console.WriteLine($"[Yellow][WARN] [White]Flag '[Yellow]{keyPart}[White]' does not take a value, but '[DarkGray]{after}[White]' was provided after '='. Value will be ignored.");
                        CLI.Console.WriteLine($"[DarkGray]  Flag description: [White]{argument.Description}");
                        CLI.Console.WriteLine($"[DarkGray]  Use flag as '[Cyan]{keyPart}[White]' without '='. If you need to pass a value, check [Cyan]--help[White] for arguments that require values.[White]");
                        hasAnyError = true;
                    }

                    try
                    {
                        argument.ActionWithoutValue?.Invoke();
                        if (argument.Key == "--help")
                            helpShown = true;

                        // Пост-валидация для флагов
                        if (argument.Key == "--mapping-auto")
                        {
                            if (string.IsNullOrEmpty(Solicen.Kismet.AssetLoader.MappingsPath))
                            {
                                CLI.Console.WriteLine($"[Yellow][WARN] [White]--mapping-auto did not find any .usmap file nearby ([White]{EnvironmentHelper.AssemblyDirectory}[White]). Place a .usmap next to exe or use [Cyan]--mapping=path[White].");
                                hasAnyError = true;
                            }
                            else
                            {
                                CLI.Console.WriteLine($"[DarkGray][INF] [White]Auto mappings: [Cyan]{Solicen.Kismet.AssetLoader.MappingsPath}[White]");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        CLI.Console.WriteLine($"[Red][ERR] [White]Failed to apply flag '[Yellow]{keyPart}[White]': {ex.Message}");
                        hasAnyError = true;
                    }
                }
            }

            // Итоговая валидация: подсказать --help если были ошибки и хелп не показан
            if (hasAnyError && !helpShown)
            {
                CLI.Console.WriteLine($"[DarkGray]Tip: Use [White]--help[DarkGray] ([White]-h[DarkGray]) to see usage and examples.\n[White]");
            }

            return remainingArgs.ToArray();
        }

        /// <summary>
        /// Отображает справочную информацию по всем доступным аргументам.
        /// </summary>
        public static void ShowHelp(List<Argument> definedArguments)
        {
            ShowHelp(definedArguments, false);
        }

        /// <summary>
        /// Отображает справочную информацию. waitForInput = true добавляет паузу (ReadLine) для интерактивного режима.
        /// </summary>
        public static void ShowHelp(List<Argument> definedArguments, bool waitForInput)
        {
            if (definedArguments == null) definedArguments = new List<Argument>();

            CLI.Console.WriteLine("[White]Usage:[DarkGray]");
            CLI.Console.WriteLine("  [White]Extract [DarkGray](Asset/Folder -> JSON):[White]");
            CLI.Console.WriteLine("    [Cyan]KissE.exe [White]<asset_or_folder> [White][options][White]");
            CLI.Console.WriteLine("    [DarkGray]Example: [Cyan]KissE.exe \"C:\\Game\\Content\\Paks\\MyAsset.uasset\" -sc[White]");
            CLI.Console.WriteLine("    [DarkGray]Example: [Cyan]KissE.exe \"C:\\Game\\Content\" --alltypes --all-directories[White]");
            CLI.Console.WriteLine("");
            CLI.Console.WriteLine("  [White]Pack [DarkGray](JSON/CSV -> Asset/Folder):[White]");
            CLI.Console.WriteLine("    [Cyan]KissE.exe [White]<json_or_csv> <asset_or_folder> [White][options][White]");
            CLI.Console.WriteLine("    [DarkGray]Example: [Cyan]KissE.exe \"C:\\My.json\" \"C:\\Game\\Content\\Paks\" -m=Gori.usmap[White]");
            CLI.Console.WriteLine("    [DarkGray]Example: [Cyan]KissE.exe \"C:\\My.csv\" \"C:\\Game\\Content\\Paks\\Asset.uasset\"[White]");
            CLI.Console.WriteLine("");
            CLI.Console.WriteLine("[Yellow]Note: [White]Drag & Drop [DarkGray]is also supported — just drop file/folder onto exe.[White]");
            CLI.Console.Separator(64);

            CLI.Console.WriteLine("[White]Available arguments:[White]");
            // Группировка для читаемости
            var groups = new Dictionary<string, List<Argument>>
            {
                ["Extraction"] = definedArguments.Where(a => new[] { "--sconst","--tprop","--lsource","--dstable","--alltypes","--all-directories","--uexp","--namespace","--only-key" }.Contains(a.Key)).ToList(),
                ["Filtering"] = definedArguments.Where(a => new[] { "--no-filter","--no-backup","--no-underscore" }.Contains(a.Key)).ToList(),
                ["Patch / Advanced"] = definedArguments.Where(a => new[] { "--patch-all-functions","--patch-assignments" }.Contains(a.Key)).ToList(),
                ["Mappings & Version"] = definedArguments.Where(a => new[] { "--mapping","--mapping-auto","--version","--pack-folder" }.Contains(a.Key)).ToList(),
                ["Translator"] = definedArguments.Where(a => new[] { "--translate","--api-key","--api-Model","--source-lang","--target-lang","--endpoint" }.Contains(a.Key)).ToList(),
                ["Other"] = new List<Argument>()
            };
            var groupedKeys = new HashSet<string>(groups.Values.SelectMany(v => v).Select(a => a.Key));
            groups["Other"] = definedArguments.Where(a => !groupedKeys.Contains(a.Key)).ToList();

            foreach (var grp in groups)
            {
                if (grp.Value.Count == 0) continue;
                CLI.Console.WriteLine($"[DarkGray]  {grp.Key}:[White]");
                foreach (var argument in grp.Value)
                {
                    string keys = argument.GetDisplayKeys();
                    // Выравнивание: 28 символа под ключи (учитывая алиасы)
                    string displayKeys = keys.Length > 28 ? keys : keys.PadRight(28);
                    CLI.Console.WriteLine($"    [Cyan]{displayKeys}[White]{argument.Description}");
                    if (argument.HasValue)
                        CLI.Console.WriteLine($"    [DarkGray]{"",-28} Usage: {argument.Key}=<value> or {argument.ShortKey} <value>[White]");
                }
            }

            CLI.Console.Separator(64);
            CLI.Console.WriteLine("[White]Examples:[DarkGray]");
            CLI.Console.WriteLine("  [Cyan]KissE.exe \"C:\\Game\\MyAsset.uasset\" --sconst[White]");
            CLI.Console.WriteLine("  [Cyan]KissE.exe \"C:\\Game\\Content\" --mapping=Gori.usmap --all-directories[White]");
            CLI.Console.WriteLine("  [Cyan]KissE.exe \"C:\\out.json\" \"C:\\Game\\Content\" --mapping-auto[White]");
            CLI.Console.WriteLine("  [Cyan]KissE.exe \"C:\\out.json\" \"C:\\Game\\Content\\Asset.uasset\" -v=5.1[White]");
            CLI.Console.WriteLine("  [Cyan]KissE.exe \"C:\\out.json\" \"C:\\Game\\Paks\" --run=[UnrealPak.exe ... | start Game.exe][White]");
            CLI.Console.Separator(64);
            CLI.Console.WriteLine("[DarkGray]For detailed docs see [White]https://github.com/SolicenTEAM/KismetEditor");
            CLI.Console.WriteLine("[DarkGray]Issues: [White]https://github.com/SolicenTEAM/KismetEditor/issues");
            CLI.Console.WriteLine("[DarkGray]Tip: Paths with spaces must be quoted: [Cyan]\"C:\\My Folder\\file.uasset\"[White]");
            CLI.Console.Separator(64);

            if (waitForInput)
            {
                CLI.Console.WriteLine("[DarkGray]Press ENTER to continue...[White]");
                try { System.Console.ReadLine(); } catch { }
            }
        }
    }
}
