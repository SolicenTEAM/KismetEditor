﻿using Newtonsoft.Json.Linq;
using Solicen.Kismet;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UAssetAPI;
using UAssetAPI.Kismet;
using UAssetAPI.Kismet.Bytecode;
using UAssetAPI.PropertyTypes.Structs;

namespace KismetEditor.Solicen
{
    /// <summary>
    /// Новый процессор байт-кода, основанный на улучшенной версии оригинальной логики замены.
    /// </summary>
    internal class KismetProcessor
    {
        public static bool DebugMode = true;
        public static int ModifiedInstCount = 0;
        public static UAsset asset;


        internal const int MAGIC_TES = 200519; // | 20 | 05 | 19 | To   End Script 
        internal const int MAGIC_FES = 060519; // | 06 | 05 | 19 | From End Script

        public static void ReplaceAll(JObject assetJsonObject, Dictionary<string, string> replacement, UAsset _asset)
        {
            asset = _asset;
            Console.WriteLine("\n[KismetProcessor] Запуск нового процесса замены...");
            foreach (var entry in replacement)
            {
                string replaceFrom = entry.Key;
                string replaceTo = entry.Value;
                Console.WriteLine($"\n--- Обработка строки: '{replaceFrom.Escape()}' ---");

                // Заменяем все вхождения этой строки, пока они находятся
                while (ReplaceSingle(assetJsonObject, replaceFrom, replaceTo))
                {
                    Console.WriteLine($"[INFO] Вхождение '{replaceFrom.Escape()}' было заменено. Поиск следующего...");
                }
                Console.WriteLine($"--- Завершена обработка строки: '{replaceFrom.Escape()}' ---");
            }
            Console.WriteLine("\n[KismetProcessor] Процесс замены полностью завершен.");
        }

        private static bool ReplaceSingle(JObject assetJsonObject, string replaceFrom, string replaceTo)
        {
            // Шаг 1: Получаем "живой" уберграф для поиска и модификации
            var liveUbergraph = KismetExtension.GetUbergraph(assetJsonObject);
            var bytecode = KismetExtension.GetUbergraph(asset);

            if (liveUbergraph == null)
            {
                Console.WriteLine("[ERROR] Не удалось найти уберграф в JObject.");
                return false;
            }

            // Шаг 2: Рекурсивно ищем и сразу заменяем первое найденное вхождение
            bool replaced = FindAndReplaceRecursively(liveUbergraph, liveUbergraph, bytecode, replaceFrom, replaceTo);

            if (replaced)
            {
                // Шаги 6-8: Если замена произошла, пересчитываем смещения
                Console.WriteLine("[DEBUG] Пересчет и патчинг смещений...");
                // Для пересчета нам снова нужен сериализованный ассет
                asset = UAsset.DeserializeJson(assetJsonObject.ToString());
                var newUbergraph = KismetExtension.GetUbergraphJson(asset);

                // Для получения карты смещений нам нужен полный список инструкций
                var newInstructionMap = MapParser.CreateMap(newUbergraph, asset, false);
                //var debugMap = MapParser.CreateMap(newUbergraph, asset, true);
                var newToEnd = OffsetCalculator.GetOffset(newInstructionMap, MAGIC_TES);
                var newFromEnd = OffsetCalculator.GetOffset(newInstructionMap, MAGIC_FES);

                if (newToEnd <= 0 || newFromEnd <= 0)
                {
                    Console.WriteLine($"[ERROR] Не удалось вычислить смещения (ToEnd: {newToEnd}, FromEnd: {newFromEnd}). Процесс может быть нестабилен.");
                }
                else
                {
                    Console.WriteLine($"[DEBUG] Вычислены новые смещения: ToEnd -> {newToEnd}, FromEnd -> {newFromEnd}.");
                }

                // Заменяем магические числа на реальные смещения в "живом" JObject
                ReplaceMagicNumber(liveUbergraph, MAGIC_TES, newFromEnd);
                ReplaceMagicNumber(liveUbergraph, MAGIC_FES, newToEnd);

                return true; // Замена была успешной
            }

            return false; // Больше вхождений не найдено
        }

        public static JArray JumpOffsetInstruction(string fillString)
        {
            // Создание новой инструкции
            JObject jumpInstruction = new JObject
            {
                { "$type", "UAssetAPI.Kismet.Bytecode.Expressions.EX_Jump, UAssetAPI" },
                { "CodeOffset", MAGIC_TES }
            };

            // Инструкция с заполнителем
            JObject newInstruction = new JObject
            {
                { "$type", "UAssetAPI.Kismet.Bytecode.Expressions.EX_StringConst, UAssetAPI" },
                { "Value", $"{fillString}" }
            };

            JArray jsonArray = new JArray();
            jsonArray.Add(newInstruction); jsonArray.Add(jumpInstruction);
            return jsonArray;
        }


        private static bool FindAndReplaceRecursively(JToken token, JArray scriptBytecodeArray, KismetExpression[] ubergraph, string replaceFrom, string replaceTo)
        {
            if (token is JObject obj)
            {
                // Проверяем, является ли этот объект строковой константой
                bool isStringConst = (obj.TryGetValue("$type", out var typeToken) && typeToken.ToString().Contains("StringConst")) ||
                                     (obj.TryGetValue("Inst", out var instToken) && instToken.ToString().Contains("StringConst"));

                if (isStringConst)
                {
                    JToken valueToken = obj["Value"] ?? obj["RawValue"];
                    if (valueToken?.ToString() == replaceFrom)
                    {
                        Console.WriteLine($"[DEBUG] Найден кандидат со строкой '{replaceFrom}'. Применяем фильтры...");
                        // Применяем фильтры
                        if (token.Ancestors().Any(ancestor => ancestor is JProperty prop && prop.Name == "AssignmentExpression"))
                        {
                            Console.WriteLine("[DEBUG] Отфильтровано: находится в 'AssignmentExpression'.");
                            return false; // Продолжаем поиск
                        }

                        // Шаг 1-3: Находим корневой Statement в "живом" уберграфе
                        JToken current = token;
                        while (current != null && current.Parent != scriptBytecodeArray)
                        {
                            current = current.Parent;
                        }
                        var statement = current as JObject;
                        if (statement == null)
                        {
                            Console.WriteLine("[ERROR] Не удалось найти родительский Statement.");
                            return false; // Критическая ошибка, но может, в другом месте найдется
                        }
                        Console.WriteLine($"[DEBUG] Найден родительский Statement для модификации.");

                        // Шаг 4: Запоминаем, удаляем и вставляем заполнитель
                        int originalIndex = scriptBytecodeArray.IndexOf(statement);
                        if (originalIndex == -1) return false; // Не смогли найти индекс, что-то пошло не так

                        scriptBytecodeArray.RemoveAt(originalIndex);

                        Console.WriteLine($"[DEBUG] Шаг 1: Подсчет размера всех инструкций.");
                        var size = InstructionSizeCalculator.GetSize(asset, statement, ubergraph);
                        Console.WriteLine($"[DEBUG] Шаг 2: Подсчет размера заполнителя.");
                        var fillString = KismetExtension.FillBySize(size);

                        foreach (var inst in JumpOffsetInstruction(fillString))
                        {
                            scriptBytecodeArray.Insert(originalIndex, inst);
                        }
                        Console.WriteLine($"[DEBUG] Шаг 3: Завершен подсчет размера заполнителя: {fillString.Length}.");
                        Console.WriteLine("[DEBUG] Шаг 4: Старая инструкция удалена, заполнитель и прыжок (TES) вставлены.");

                        // Шаг 5: Вставляем измененную инструкцию и прыжок-возврат в конец
                        var stringConstToModify = statement.SelectTokens("$..*").OfType<JObject>().FirstOrDefault(o => (o["Value"] ?? o["RawValue"])?.ToString() == replaceFrom);
                        if (stringConstToModify != null)
                        {
                            if (stringConstToModify.ContainsKey("$type")) stringConstToModify["$type"] = stringConstToModify["$type"].ToString().Replace("StringConst", "UnicodeStringConst");
                            if (stringConstToModify.ContainsKey("Inst")) stringConstToModify["Inst"] = stringConstToModify["Inst"].ToString().Replace("StringConst", "UnicodeStringConst");
                            if (stringConstToModify.ContainsKey("Value")) stringConstToModify["Value"] = replaceTo;
                            if (stringConstToModify.ContainsKey("RawValue")) stringConstToModify["RawValue"] = replaceTo;
                        }

                        var returnJump = new JObject {
                                            { "$type", "UAssetAPI.Kismet.Bytecode.Expressions.EX_Jump, UAssetAPI" },
                                            { "CodeOffset", MAGIC_FES }};

                        scriptBytecodeArray.Add(statement);
                        scriptBytecodeArray.Add(returnJump);
                        Console.WriteLine("[DEBUG] Шаг 5: Измененная инструкция и прыжок (FES) добавлены в конец.");
                        ModifiedInstCount++;

                        return true; // Замена произведена, выходим из рекурсии
                    }
                }

                foreach (var property in obj.Properties())
                {
                    if (FindAndReplaceRecursively(property.Value, scriptBytecodeArray, ubergraph, replaceFrom, replaceTo)) return true;
                }
            }
            else if (token is JArray array)
            {
                foreach (var item in array)
                {
                    if (FindAndReplaceRecursively(item, scriptBytecodeArray, ubergraph, replaceFrom, replaceTo)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Находит и заменяет первое вхождение "магического числа" в CodeOffset.
        /// </summary>
        private static void ReplaceMagicNumber(JArray ubergraph, int magicToFind, int newOffset)
        {
            if (newOffset == 0) return;

            var jumpToReplace = ubergraph
                .SelectMany(statement => statement.SelectTokens("$..*"))
                .OfType<JObject>()
                .FirstOrDefault(obj => obj.TryGetValue("CodeOffset", out var offsetToken) && offsetToken.Value<int>() == magicToFind);

            if (jumpToReplace != null)
            {
                jumpToReplace["CodeOffset"] = newOffset;
                Console.WriteLine($"[DEBUG] Патчинг: Магическое число {magicToFind} заменено на смещение {newOffset}.");
            } else
            {
                Console.WriteLine($"[WARNING] Не удалось найти инструкцию с магическим числом {magicToFind} для замены.");
            }
        }
    }
}