﻿using Newtonsoft.Json.Linq;
using Solicen.Kismet;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UAssetAPI;
using UAssetAPI.Kismet;
using UAssetAPI.Kismet.Bytecode;
using UAssetAPI.PropertyTypes.Structs;

namespace Solicen
{
    /// <summary>
    /// Новый процессор байт-кода, основанный на улучшенной версии оригинальной логики замены.
    /// </summary>
    internal class KismetProcessor
    {
        public static bool DebugMode = true;
        public static int ModifiedCount = 0;
        public static UAsset asset;

        internal const int MAGIC_TES = 200519; // | 20 | 05 | 19 | To   End Script 
        internal const int MAGIC_FES = 060519; // | 06 | 05 | 19 | From End Script

        public static void ReplaceAllInStrProperties(Dictionary<string, string> replacement, UAsset _asset)
        {
            asset = _asset;
            //Console.WriteLine("\n[INF] Starting a new replacement process...");
            foreach (var entry in replacement)
            {
                string replaceFrom = entry.Key; string replaceTo = entry.Value;
                //Console.WriteLine($"\n--- String processing: '{replaceFrom.Escape()}' ---");
                var replaced = MapParser.ReplaceStrProperty(asset, replaceFrom, replaceTo);
                if (replaced != 0) 
                {
                    //Console.WriteLine($"[INF] Occurrence of '{replaceFrom.Escape()}' has been replaced. Search for the next one...");
                    ModifiedCount += replaced;
                } 
                //Console.WriteLine($"--- Line processing completed: '{replaceFrom.Escape()}' ---");
            }
        }

        public static void ReplaceAllInStringTable(Dictionary<string, string> replacement, UAsset _asset)
        {
            asset = _asset;
            //Console.WriteLine("\n[INF] Starting a new replacement process...");
            foreach (var entry in replacement)
            {
                string replaceFrom = entry.Key; string replaceTo = entry.Value;
                //Console.WriteLine($"\n--- String processing: '{replaceFrom.Escape()}' ---");
                var replaced = MapParser.ReplaceStringTableEntry(asset, entry.Key, entry.Value);
                if (replaced != 0)
                {
                    //Console.WriteLine($"[INF] Occurrence of '{replaceFrom.Escape()}' has been replaced. Search for the next one...");
                    ModifiedCount += replaced;
                }
                //Console.WriteLine($"--- Line processing completed: '{replaceFrom.Escape()}' ---");
            }
        }

        public static void ReplaceAllInUbergraph(JObject assetJsonObject, Dictionary<string, string> replacement, UAsset _asset)
        {
            asset = _asset;
            //Console.WriteLine("\n[INF] Starting a new replacement process...");
            foreach (var entry in replacement)
            {
                string replaceFrom = entry.Key; string replaceTo = entry.Value;
                //Solicen.CLI.Console.StartProgress($"[INF] Replace process for: {replaceFrom.Escape()}");
                //Console.WriteLine($"\n--- String processing: '{replaceFrom.Escape()}' ---");

                // Заменяем все вхождения этой строки, пока они находятся
                while (ReplaceSingle(assetJsonObject, replaceFrom, replaceTo))
                {
                    //Console.WriteLine($"[INF] Occurrence of '{replaceFrom.Escape()}' has been replaced. Search for the next one...");
                }
                //Solicen.CLI.Console.StopProgress($"[INF] Replace processing completed: {replaceFrom.Escape()}");
                //Console.WriteLine($"--- Line processing completed: '{replaceFrom.Escape()}' ---");
            }
            //Console.WriteLine("\n[INF] Replacement process is fully completed.");
        }

        private static bool ReplaceSingle(JObject assetJsonObject, string replaceFrom, string replaceTo)
        {
            // Шаг 1: Получаем "живой" уберграф для поиска и модификации
            var liveUbergraph = KismetExtension.GetUbergraph(assetJsonObject);
            var bytecode = KismetExtension.GetUbergraph(asset);

            if (liveUbergraph == null)
            {
                Console.WriteLine("[ERR] Couldn't find the ubergraph in JObject.");
                return false;
            }

            // Шаг 2: Рекурсивно ищем и сразу заменяем первое найденное вхождение
            bool replaced = FindAndReplaceRecursively(liveUbergraph, liveUbergraph, bytecode, replaceFrom, replaceTo);

            if (replaced)
            {
                // Шаги 6-8: Если замена произошла, пересчитываем смещения
                //Console.WriteLine("[INF] Recalculation and patching of offsets...");
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
                    Console.WriteLine($"[WRN] Offsets could not be calculated (ToEnd: {newToEnd}, FromEnd: {newFromEnd}). The process may be unstable.");
                }
                else
                {
                    //Console.WriteLine($"[INF] New offsets have been calculated (ToEnd -> {newToEnd}, FromEnd -> {newFromEnd})");
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
            // Инструкция прыжка
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
                        //Console.WriteLine($"[INF] A candidate with the string '{replaceFrom.Escape()}' was found. Apply filters...");
                        // Применяем фильтры
                        if (token.Ancestors().Any(ancestor => ancestor is JProperty prop && prop.Name == "AssignmentExpression"))
                        {
                            //Console.WriteLine("[INF] Filtered: located in the 'AssignmentExpression'.");
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
                            Console.WriteLine("[ERR] The parent Statement could not be found.");
                            return false; // Критическая ошибка, но может, в другом месте найдется
                        }
                        //Console.WriteLine($"[INF] The parent Statement was found for modification.");

                        // Шаг 4: Запоминаем, удаляем и вставляем заполнитель
                        int originalIndex = scriptBytecodeArray.IndexOf(statement);
                        if (originalIndex == -1) return false; // Не смогли найти индекс, что-то пошло не так

                        scriptBytecodeArray.RemoveAt(originalIndex);       
                        var size = InstructionSearchSize.GetSize(asset, statement, ubergraph);
                        /*
                        Console.WriteLine($"[INF] Step 1: Calculate the size of all instructions.");
                        Console.WriteLine($"[INF] Step 2: Calculate the placeholder size.");
                        */
                        var placeholder = KismetExtension.PlaceholderBySize(size);


                        foreach (var inst in JumpOffsetInstruction(placeholder))
                        {
                            scriptBytecodeArray.Insert(originalIndex, inst);
                        }
                        /*
                        Console.WriteLine($"[INF] Step 3: The placeholder size calculation is completed: {placeholder.Length}.");
                        Console.WriteLine("[INF] Step 4: The old instruction deleted, the placeholder and the jump (TES) are inserted.");
                        */

                        // Шаг 5: Вставляем измененную инструкцию и прыжок-возврат в конец
                        var stringConstToModify = statement.SelectTokens("$..*").OfType<JObject>().FirstOrDefault(o => (o["Value"] ?? o["RawValue"])?.ToString() == replaceFrom);
                        if (stringConstToModify != null)
                        {
                            if (stringConstToModify.ContainsKey("$type")) stringConstToModify["$type"] = stringConstToModify["$type"].ToString().Replace("StringConst", "UnicodeStringConst");
                            if (stringConstToModify.ContainsKey("Inst")) stringConstToModify["Inst"] = stringConstToModify["Inst"].ToString().Replace("StringConst", "UnicodeStringConst");
                            if (stringConstToModify.ContainsKey("Value")) stringConstToModify["Value"] = replaceTo;
                            if (stringConstToModify.ContainsKey("RawValue")) stringConstToModify["RawValue"] = replaceTo;
                        }

                        var returnJump = new JObject {{ "$type", "UAssetAPI.Kismet.Bytecode.Expressions.EX_Jump, UAssetAPI" },{ "CodeOffset", MAGIC_FES }};

                        scriptBytecodeArray.Add(statement);
                        scriptBytecodeArray.Add(returnJump);
                        //Console.WriteLine("[INF] Step 5: The modified instruction and the jump (FES) added to the end.");
                        ModifiedCount++;

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
                //Console.WriteLine($"[INF] Patch: The magic number {magicToFind} has been replaced with the offset {newOffset}.");
            } else
            {
                Console.WriteLine($"[ERR] Couldn't find instructions with the magic number {magicToFind} to replace.");
            }
        }
    }
}