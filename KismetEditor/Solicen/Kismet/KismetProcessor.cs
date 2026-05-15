﻿using Newtonsoft.Json.Linq;
using Solicen.Kismet;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.Kismet;
using UAssetAPI.Kismet.Bytecode;
using UAssetAPI.Kismet.Bytecode.Expressions;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.Unversioned;

namespace Solicen
{
    /// <summary>
    /// Новый процессор байт-кода, основанный на улучшенной версии оригинальной логики замены.
    /// </summary>
    internal class KismetProcessor
    {
        public static bool DebugMode = true;
        public static int ModifiedCount = 0;
        public static UAsset Asset;

        internal const int MAGIC_TES = 200519; // | 20 | 05 | 19 | To   End Script 
        internal const int MAGIC_FES = 060519; // | 06 | 05 | 19 | From End Script

        public static void ReplaceAllInTextProperties(Dictionary<string, string> replacement, UAsset _asset)
        {
            Asset = _asset;
            foreach (var entry in replacement)
            {
                string replaceFrom = entry.Key; string replaceTo = entry.Value;
                var replaced = MapParser.ReplaceTextProperty(Asset, replaceFrom, replaceTo);
                if (replaced != 0) 
                {
                    ModifiedCount += replaced;
                } 
            }
        }

        public static void ReplaceAllInStrProperties(Dictionary<string, string> replacement, UAsset _asset)
        {
            Asset = _asset;
            foreach (var entry in replacement)
            {
                string replaceFrom = entry.Key; string replaceTo = entry.Value;
                var replaced = MapParser.ReplaceStrProperty(Asset, replaceFrom, replaceTo);
                if (replaced != 0)
                {
                    ModifiedCount += replaced;
                }
            }
        }

        public static void ReplaceAllInStringTable(Dictionary<string, string> replacement, UAsset _asset)
        {
            Asset = _asset;
            foreach (var entry in replacement)
            {
                string replaceFrom = entry.Key; string replaceTo = entry.Value;
                var replaced = MapParser.ReplaceStringTableEntry(Asset, entry.Key, entry.Value);
                if (replaced != 0)
                {
                    ModifiedCount += replaced;
                }
            }
        }

        public static void ReplaceAllInDataTable(Dictionary<string, string> replacement, UAsset _asset)
        {
            Asset = _asset;
            foreach (var entry in replacement)
            {
                string replaceFrom = entry.Key; string replaceTo = entry.Value;
                var replaced = MapParser.ReplaceDataTableEntry(Asset, entry.Key, entry.Value);
                if (replaced != 0)
                {
                    ModifiedCount += replaced;
                }
            }
        }

        /// <summary>
        /// Like <see cref="ReplaceAllInUbergraph"/> but iterates the replacement pipeline over
        /// every UFunction with a ScriptBytecode (not just ExecuteUbergraph_*). Required for
        /// EX_StringConst literals living in widget event handlers and similar functions that
        /// the ubergraph-only pipeline ignores.
        /// </summary>
        public static void ReplaceAllInAllFunctions(Dictionary<string, string> replacement, ref UAsset _Asset)
        {
            Asset = _Asset;
            var assetJsonObject = JObject.Parse(Asset.SerializeJson());
            var tempUsmap = Asset.Mappings != null ? Asset.Mappings : new Usmap();

            foreach (var entry in replacement)
            {
                string from = entry.Key;
                string to = entry.Value;
                bool anyThisRound = true;
                while (anyThisRound)
                {
                    anyThisRound = false;
                    // Re-fetch each iteration: Asset is re-deserialized after each successful
                    // replace, invalidating the cached KismetExpression[] arrays.
                    var allFns = KismetExtension.GetAllScriptBytecode(assetJsonObject, Asset);
                    foreach (var fn in allFns)
                    {
                        if (ReplaceSingleInFunction(assetJsonObject, fn.name, fn.jsonBytecode, fn.exprBytecode, from, to))
                        {
                            anyThisRound = true;
                            break; // Restart to re-fetch — Asset has been re-deserialized.
                        }
                    }
                }
            }

            _Asset = UAsset.DeserializeJson(assetJsonObject.ToString());
            if (tempUsmap.FilePath != null)
            {
                _Asset.Mappings = tempUsmap;
            }
        }

        private static bool ReplaceSingleInFunction(JObject assetJsonObject, string functionName, JArray liveBytecode, KismetExpression[] exprBytecode, string replaceFrom, string replaceTo)
        {
            if (liveBytecode == null)
            {
                return false;
            }

            var displacement = new DisplacementInfo();
            bool replaced = FindAndReplaceRecursively(liveBytecode, liveBytecode, exprBytecode, replaceFrom, replaceTo, displacement);
            if (!replaced)
            {
                return false;
            }

            // Re-deserialize the asset so the new bytecode array reflects the placeholder + jump
            // and the appended modified statement + return jump.
            Asset = UAsset.DeserializeJson(assetJsonObject.ToString());

            var newFn = Asset.Exports.OfType<StructExport>()
                .FirstOrDefault(e => e.ObjectName.ToString() == functionName);
            if (newFn == null || newFn.ScriptBytecode == null)
            {
                Console.WriteLine($"[WRN] Function '{functionName}' not found in asset after re-deserialize; skipping offset recalculation.");
                return true;
            }

            // Compute MAGIC_TES / MAGIC_FES targets via KismetExpression.Visit(), which mirrors
            // ExpressionSerializer.WriteExpression byte-for-byte and reports the offsets that
            // StructExport.Write actually emits.
            int idxTes = -1;
            int idxFes = -1;
            uint[] topLevelOffsets = new uint[newFn.ScriptBytecode.Length];
            uint runningOffset = 0;
            for (int i = 0; i < newFn.ScriptBytecode.Length; i++)
            {
                topLevelOffsets[i] = runningOffset;
                var ex = newFn.ScriptBytecode[i];
                if (ex is EX_Jump jx)
                {
                    if (jx.CodeOffset == MAGIC_TES && idxTes < 0)
                    {
                        idxTes = i;
                    }
                    else if (jx.CodeOffset == MAGIC_FES && idxFes < 0)
                    {
                        idxFes = i;
                    }
                }
                uint after = runningOffset;
                ex.Visit(Asset, ref after, (_, _) => { });
                runningOffset = after;
            }

            int newToEnd = idxTes >= 0 && idxTes + 2 < topLevelOffsets.Length ? (int)topLevelOffsets[idxTes + 2] : 0;
            int newFromEnd = idxFes >= 1 ? (int)topLevelOffsets[idxFes - 1] : 0;

            if (newToEnd <= 0 || newFromEnd <= 0)
            {
                Console.WriteLine($"[WRN] Offsets could not be calculated for '{functionName}' (ToEnd: {newToEnd}, FromEnd: {newFromEnd}). The process may be unstable.");
            }

            ReplaceMagicNumber(liveBytecode, MAGIC_TES, newFromEnd);
            ReplaceMagicNumber(liveBytecode, MAGIC_FES, newToEnd);

            ShiftDisplacedStatementInternalOffsets(liveBytecode, newFn.ScriptBytecode, topLevelOffsets, idxTes, displacement);

            return true;
        }

        public static void ReplaceAllInUbergraph(Dictionary<string, string> replacement, ref UAsset _Asset)
        {
            Asset = _Asset;

            // Сохраняем то, что нельзя потерять
            var assetJsonObject = JObject.Parse(Asset.SerializeJson());
            var tempUsmap = Asset.Mappings != null ? Asset.Mappings : new Usmap();
     
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

            _Asset = UAsset.DeserializeJson(assetJsonObject.ToString());
            if (tempUsmap.FilePath != null) _Asset.Mappings = tempUsmap;

            //Console.WriteLine("\n[INF] Replacement process is fully completed.");
        }

        private static bool ReplaceSingle(JObject assetJsonObject, string replaceFrom, string replaceTo)
        {
            // Шаг 1: Получаем "живой" уберграф для поиска и модификации
            var liveUbergraph = KismetExtension.GetUbergraph(assetJsonObject);
            var bytecode = KismetExtension.GetUbergraph(Asset);

            if (liveUbergraph == null)
            {
                Console.WriteLine("[ERR] Couldn't find the ubergraph in JObject.");
                return false;
            }

            // Шаг 2: Рекурсивно ищем и сразу заменяем первое найденное вхождение
            var displacement = new DisplacementInfo();
            bool replaced = FindAndReplaceRecursively(liveUbergraph, liveUbergraph, bytecode, replaceFrom, replaceTo, displacement);
            if (replaced)
            {
                // Re-deserialize so the new ScriptBytecode reflects the placeholder + jump
                // and the appended modified statement + return jump.
                Asset = UAsset.DeserializeJson(assetJsonObject.ToString());

                // Compute MAGIC_TES / MAGIC_FES targets via KismetExpression.Visit(),
                // which mirrors ExpressionSerializer.WriteExpression byte-for-byte. The
                // SerializeScript + StatementIndex pipeline can drift from the actual
                // Write() output on some bytecode shapes, leaving magic-numbers patched
                // mid-instruction.
                var newBytecode = KismetExtension.GetUbergraph(Asset);
                int idxTes = -1;
                int idxFes = -1;
                var topLevelOffsets = new uint[newBytecode.Length];
                uint runningOffset = 0;
                for (int i = 0; i < newBytecode.Length; i++)
                {
                    topLevelOffsets[i] = runningOffset;
                    var ex = newBytecode[i];
                    if (ex is EX_Jump jx)
                    {
                        if (jx.CodeOffset == MAGIC_TES && idxTes < 0)
                        {
                            idxTes = i;
                        }
                        else if (jx.CodeOffset == MAGIC_FES && idxFes < 0)
                        {
                            idxFes = i;
                        }
                    }
                    uint after = runningOffset;
                    ex.Visit(Asset, ref after, (_, _) => { });
                    runningOffset = after;
                }

                int newToEnd = idxTes >= 0 && idxTes + 2 < topLevelOffsets.Length ? (int)topLevelOffsets[idxTes + 2] : 0;
                int newFromEnd = idxFes >= 1 ? (int)topLevelOffsets[idxFes - 1] : 0;

                if (newToEnd <= 0 || newFromEnd <= 0)
                {
                    Console.WriteLine($"[WRN] Offsets could not be calculated (ToEnd: {newToEnd}, FromEnd: {newFromEnd}). The process may be unstable.");
                }

                // Заменяем магические числа на реальные смещения в "живом" JObject
                ReplaceMagicNumber(liveUbergraph, MAGIC_TES, newFromEnd);
                ReplaceMagicNumber(liveUbergraph, MAGIC_FES, newToEnd);

                ShiftDisplacedStatementInternalOffsets(liveUbergraph, newBytecode, topLevelOffsets, idxTes, displacement);

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


        private static bool FindAndReplaceRecursively(JToken token, JArray scriptBytecodeArray, KismetExpression[] ubergraph, string replaceFrom, string replaceTo, DisplacementInfo displacement)
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
                        if (!CLI.CLIHandler.Config.PatchAssignments
                            && token.Ancestors().Any(ancestor => ancestor is JProperty prop && prop.Name == "AssignmentExpression"))
                        {
                            //Console.WriteLine("[INF] Filtered: located in the 'AssignmentExpression'.");
                            return false; // skipped unless --patch-assignments
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

                        // Capture position + size of the string we are about to replace, relative to the
                        // start of the typed statement. Used later by ShiftDisplacedStatementInternalOffsets
                        // to apply both the global displacement shift AND the in-statement size shift.
                        // Walks the TYPED statement pre-modification.
                        if (originalIndex >= 0 && originalIndex < ubergraph.Length)
                        {
                            uint w = 0;
                            var localDisplacement = displacement;
                            ubergraph[originalIndex].Visit(Asset, ref w, (e, off) =>
                            {
                                if (localDisplacement.RelPos != uint.MaxValue)
                                {
                                    return;
                                }
                                if (e is EX_StringConst sc && sc.Value == replaceFrom)
                                {
                                    localDisplacement.RelPos = off;
                                    localDisplacement.OldSize = 1 + (sc.Value?.Length ?? 0) + 1;
                                }
                                else if (e is EX_UnicodeStringConst usc && usc.Value == replaceFrom)
                                {
                                    localDisplacement.RelPos = off;
                                    localDisplacement.OldSize = 1 + 2 * ((usc.Value?.Length ?? 0) + 1);
                                }
                            });
                            // Modification below always swaps to EX_UnicodeStringConst.
                            displacement.NewSize = 1 + 2 * ((replaceTo?.Length ?? 0) + 1);
                        }

                        scriptBytecodeArray.RemoveAt(originalIndex);
                        var size = InstructionSearchSize.GetSize(Asset, statement, ubergraph);
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
                            if (stringConstToModify.ContainsKey("$type")) stringConstToModify["$type"] = stringConstToModify["$type"].ToString().Replace("EX_StringConst", "EX_UnicodeStringConst");
                            if (stringConstToModify.ContainsKey("Inst")) stringConstToModify["Inst"] = stringConstToModify["Inst"].ToString().Replace("EX_StringConst", "EX_UnicodeStringConst");
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
                    if (FindAndReplaceRecursively(property.Value, scriptBytecodeArray, ubergraph, replaceFrom, replaceTo, displacement))
                    {
                        return true;
                    }
                }
            }
            else if (token is JArray array)
            {
                foreach (var item in array)
                {
                    if (FindAndReplaceRecursively(item, scriptBytecodeArray, ubergraph, replaceFrom, replaceTo, displacement))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Names of opcode fields that hold absolute byte positions inside the same bytecode buffer
        /// (and therefore become stale when a statement is physically displaced).
        /// </summary>
        private static readonly string[] AbsoluteOffsetFieldNames =
        {
            "CodeOffset",     // EX_Jump, EX_JumpIfNot, EX_Skip
            "EndGotoOffset",  // EX_SwitchValue
            "NextOffset",     // FKismetSwitchCase (inside EX_SwitchValue.Cases)
            "PushingAddress", // EX_PushExecutionFlow
        };

        /// <summary>
        /// Carries the string-position and size info captured during FindAndReplaceRecursively
        /// to ShiftDisplacedStatementInternalOffsets. Allows the shifter to distinguish
        /// "before the replaced string" (shift by relocDelta only) from "after the replaced
        /// string" (shift by relocDelta + internalDelta) when computing the final shift for
        /// each internal absolute offset.
        ///
        /// Passed explicitly through the call chain rather than via static state, so that
        /// nested invocations of FindAndReplaceRecursively (recursive walks, nested
        /// replacement attempts) don't accidentally clobber each other's data.
        /// </summary>
        private class DisplacementInfo
        {
            public uint RelPos = uint.MaxValue;
            public int OldSize = 0;
            public int NewSize = 0;
        }

        /// <summary>
        /// After FindAndReplaceRecursively has displaced the modified statement to the end of the
        /// bytecode (and inserted EX_Jump+placeholder at its original position), the statement's
        /// internal absolute offsets (e.g. EX_SwitchValue.EndGotoOffset, EX_Jump.CodeOffset inside
        /// the statement) still point at byte positions from the OLD layout. For offsets whose
        /// target falls inside the statement's original byte range, those bytes have moved by
        /// relocDelta = newPos - oldPos. This walks the displaced statement's JSON subtree and
        /// adds relocDelta to any absolute-offset field whose value is in the original range.
        ///
        /// Without this fix, default branches of EX_SwitchValue and similar in-statement jumps
        /// land mid-instruction in the patched bytecode, causing "undefined opcode" / "infinite
        /// script recursion" crashes at runtime on the affected branches.
        /// </summary>
        private static void ShiftDisplacedStatementInternalOffsets(JArray liveBytecode, KismetExpression[] newBytecode, uint[] topLevelOffsets, int idxTes, DisplacementInfo displacement)
        {
            if (idxTes < 0 || idxTes + 2 > topLevelOffsets.Length - 1)
            {
                return;
            }
            if (liveBytecode == null || liveBytecode.Count < 2)
            {
                return;
            }
            if (newBytecode == null || newBytecode.Length < 2)
            {
                return;
            }

            int dispJsonIdx = liveBytecode.Count - 2;     // displaced statement is just before the return jump
            int dispExprIdx = newBytecode.Length - 2;

            if (dispJsonIdx <= idxTes + 1 || dispExprIdx <= idxTes + 1)
            {
                return;
            }
            if (dispJsonIdx >= liveBytecode.Count)
            {
                return;
            }
            if (!(liveBytecode[dispJsonIdx] is JObject dispJson))
            {
                return;
            }

            uint oldStmtPos = topLevelOffsets[idxTes];       // position of the placeholder == original position of statement
            uint oldStmtEnd = topLevelOffsets[idxTes + 2];   // start of the next original statement
            uint newStmtPos = topLevelOffsets[dispExprIdx];  // position of the displaced statement at the end of bytecode
            long relocDelta = (long)newStmtPos - (long)oldStmtPos;

            if (relocDelta == 0 && displacement.RelPos == uint.MaxValue)
            {
                return;
            }

            // Compute the absolute byte range of the OLD string (the one we replaced) inside the
            // OLD bytecode. Internal jump targets that point AFTER this range need an additional
            // shift of (newStringSize - oldStringSize) because everything after the string was
            // physically moved within the (now displaced) statement by that delta.
            int internalDelta = displacement.NewSize - displacement.OldSize;
            long absOldStringEnd = (displacement.RelPos == uint.MaxValue)
                ? long.MaxValue
                : (long)oldStmtPos + displacement.RelPos + displacement.OldSize;

            ShiftInternalOffsetsRecursive(dispJson, oldStmtPos, oldStmtEnd, relocDelta,
                absOldStringEnd, internalDelta);
        }

        private static void ShiftInternalOffsetsRecursive(JToken token, uint oldStart, uint oldEnd, long relocDelta,
            long absStringEnd, int internalDelta)
        {
            if (token is JObject obj)
            {
                foreach (var fieldName in AbsoluteOffsetFieldNames)
                {
                    if (obj.TryGetValue(fieldName, out JToken offsetToken) && offsetToken.Type == JTokenType.Integer)
                    {
                        long target = offsetToken.Value<long>();
                        if (target >= oldStart && target < oldEnd)
                        {
                            long extra = (target >= absStringEnd) ? internalDelta : 0L;
                            obj[fieldName] = target + relocDelta + extra;
                        }
                    }
                }
                foreach (var prop in obj.Properties())
                {
                    ShiftInternalOffsetsRecursive(prop.Value, oldStart, oldEnd, relocDelta,
                        absStringEnd, internalDelta);
                }
            }
            else if (token is JArray array)
            {
                foreach (var item in array)
                {
                    ShiftInternalOffsetsRecursive(item, oldStart, oldEnd, relocDelta,
                        absStringEnd, internalDelta);
                }
            }
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