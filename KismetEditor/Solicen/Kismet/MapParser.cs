﻿using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UAssetAPI;
using UAssetAPI.Kismet;
using UAssetAPI.Kismet.Bytecode;
using UAssetAPI.UnrealTypes;
using UAssetAPI.PropertyTypes.Objects;
using Newtonsoft.Json;
using System.Xml.Linq;
namespace Solicen.Kismet
{
    /// <summary>
    /// Отвечает за парсинг JArray уберграфа и создание
    /// детальной "карты" инструкций в виде массива KismetObject.
    /// </summary>
    internal static class MapParser
    {
        // Нам нужен доступ к ассету для работы сериализатора
        private static UAsset uAsset;

        public static bool AllowLocalizedSource = false;
        private static bool IsLocalizedSource(string name) => AllowLocalizedSource ? false : name == "LocalizedSource";

        public static bool AllowUnderscore = false;
        private static bool IsContainsUnderscore(string str) => AllowUnderscore ? false : str.Contains("_"); 

        /// <summary>
        /// Создает карту инструкций из JArray уберграфа.
        /// </summary>
        public static LObject[] CreateMap(JArray jArray, UAsset asset, bool onlyString = false)
        {
            uAsset = asset;
            List<LObject> kismets = new List<LObject>();
            if (jArray != null)
            {
                foreach (var token in jArray)
                {
                    ParseRecursively(token, kismets, onlyString);
                }
            }
            return kismets.ToArray();
        }

        // Метод для работы с StrProperty обьектами и иными
        public static string[] ParseAsCSV(LObject[] objects)
        {
            HashSet<string> csvLines = new HashSet<string>();
            foreach (var value in objects)
            {
                Console.WriteLine($" - {value.Value.Escape()}");
                csvLines.Add($"{value.Value.Escape()}");
            }
            return csvLines.ToArray();
        }

        public static string[] ParseUbergraph(JArray jArray)
        {
            HashSet<string> kismetValues = new HashSet<string>(CreateMap(jArray, null, true).Select(x => x.Value));
            HashSet<string> csvLines = new HashSet<string>();
            if (kismetValues.Count > 0)
            {
                Console.WriteLine("------- [Ubergraph] -------");
                foreach (var value in kismetValues)
                {
                    Console.WriteLine($" - {value.Escape()}");
                    csvLines.Add($"{value.Escape()}");
                }
            }
            return csvLines.ToArray();
        }

        /// <summary>
        /// Извлекает все значения из StrProperty во всех экспортах ассета.
        /// </summary>
        /// <param name="asset">Ассет для сканирования.</param>
        /// <returns>Массив LObject с найденными строками.</returns>
        public static LObject[] ExtractStrProperties(UAsset asset)
        {
            var results = new List<LObject>();
            if (asset == null) return results.ToArray();

            for (int i = 0; i < asset.Exports.Count; i++)
            {
                if (asset.Exports[i] is UAssetAPI.ExportTypes.NormalExport normalExport)
                {
                    foreach (var prop in normalExport.Data)
                    {
                        if (prop is StrPropertyData strProp && strProp.Value != null)
                        {
                            string value = strProp.Value.Value;
                            if (string.IsNullOrWhiteSpace(value) || value.Trim().Length < 2 || value.Contains("_") || value == "None" || int.TryParse(value, out _) || value.IsGUID()) continue;
                            results.Add(new LObject(i, value, "StrProperty", 0, 0));
                        }
                    }
                }
            }
            return results.ToArray();
        }

        /// <summary>
        /// Извлекает все значения из StringTable.
        /// </summary>
        /// <param name="asset">Ассет StringTable для сканирования.</param>
        /// <returns>Массив LObject с найденными строками.</returns>
        public static LObject[] ExtractStringTableEntries(UAsset asset)
        {
            var results = new List<LObject>();
            if (asset == null) return results.ToArray();

            for (int i = 0; i < asset.Exports.Count; i++)
            {
                if (asset.Exports[i] is UAssetAPI.ExportTypes.StringTableExport stringTableExport)
                {
                    foreach (var entry in stringTableExport.Table)
                    {
                        // entry.Key - это ключ локализации, entry.Value - это исходный текст.
                        string value = entry.Value.Value;
                        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length < 2 || value.Contains("_") || value == "None" || int.TryParse(value, out _) || value.IsGUID()) continue;
                        
                        // В LObject мы сохраняем исходный текст как Value, а ключ локализации как Instruction, чтобы использовать его при замене.
                        results.Add(new LObject(i, value, entry.Key.Value, 0, 0));
                    }
                }
            }
            return results.ToArray();
        }

        /// <summary>
        /// Заменяет запись в StringTable по ключу.
        /// </summary>
        /// <param name="asset">Ассет StringTable для модификации.</param>
        /// <param name="exportIndex">Индекс экспорта StringTable.</param>
        /// <param name="key">Ключ строки, которую нужно заменить.</param>
        /// <param name="newValue">Новое строковое значение.</param>
        /// <returns>True, если замена прошла успешно.</returns>
        public static int ReplaceStringTableEntry(UAsset asset, string originalValue, string newValue)
        {
            if (asset == null || string.IsNullOrWhiteSpace(newValue)) return 0;
            int replacementCount = 0;
            foreach (var export in asset.Exports)
            {
                if (export is UAssetAPI.ExportTypes.StringTableExport stringTableExport)
                {
                    foreach(var entry in stringTableExport.Table)
                    {
                        if (entry.Value.Value == originalValue)
                        {
                            entry.Value.Value = newValue;
                            entry.Value.Encoding = System.Text.Encoding.Unicode;
                            replacementCount++;
                        }
                    }
                }
            }

            return replacementCount;
        }

        /// <summary>
        /// Заменяет все вхождения указанной строки в StrProperty по всему ассету.
        /// </summary>
        /// <param name="asset">Ассет для модификации.</param>
        /// <param name="originalValue">Оригинальное строковое значение для поиска.</param>
        /// <param name="newValue">Новое строковое значение для установки.</param>
        /// <returns>Количество произведенных замен.</returns>
        public static int ReplaceStrProperty(UAsset asset, string originalValue, string newValue)
        {
            if (asset == null || string.IsNullOrWhiteSpace(newValue)) return 0;
            int replacementCount = 0;
            foreach (var export in asset.Exports)
            {
                if (export is UAssetAPI.ExportTypes.NormalExport normalExport)
                {
                    // Мы не можем изменять коллекцию во время итерации, поэтому создаем копию для поиска
                    var propertiesToModify = normalExport.Data
                        .Where(p => p is StrPropertyData strProp && strProp.Value?.Value == originalValue)
                        .ToList();

                    foreach (var prop in propertiesToModify)
                    {
                        var strProp = (StrPropertyData)prop;

                        // Создаем новое свойство с тем же именем, но новым Unicode-значением
                        var newProp = new StrPropertyData(strProp.Name)
                        {
                            Value = new FString(newValue)
                        };

                        // Находим индекс старого свойства, удаляем его и вставляем на его место новое
                        int index = normalExport.Data.IndexOf(strProp);
                        if (index == -1) continue;

                        normalExport.Data.RemoveAt(index);
                        normalExport.Data.Insert(index, newProp);
                        replacementCount++;
                    }
                }
            }
            return replacementCount;
        }
        private static void ParseRecursively(JToken token, List<LObject> kismets, bool onlyString)
        {
            if (onlyString)
            {
                // Старая, проверенная логика для поиска только строк
                FindStringsOnlyRecursively(token, kismets);
            }
            else
            {
                // Мы не должны использовать рекурсию для полного парсинга,
                // так как каждая запись в JArray - это уже корневая инструкция.
                ParseStatement(token, kismets);
            }
        }

        /// <summary>
        /// Парсит один JObject (Statement) и преобразует его в LObject.
        /// </summary>
        private static void ParseStatement(JToken token, List<LObject> kismets)
        {
            if (!(token is JObject statement) || !statement.ContainsKey("StatementIndex")) return;

            int statementIndex = statement["StatementIndex"].Value<int>();
            int offset = statement["Offset"]?.Value<int>() ?? 0;
            var expression = statement["Expression"] as JObject;

            string instructionType;
            string value;

            // Приоритет №1: Найти Jump внутри всего выражения, так как он самый важный для смещений.
            var jumpToken = expression?.SelectTokens("$..*").OfType<JObject>().FirstOrDefault(o => o.ContainsKey("CodeOffset"));
            if (jumpToken != null)
            {
                instructionType = "Jump"; // Используем "Jump" для соответствия с GetOffset
                value = jumpToken["CodeOffset"].ToString();
            }
            else // Приоритет №2: Просто взять тип из корня Expression.
            {
                instructionType = statement["Inst"]?.Value<string>() ?? "None";
                value = expression?["Value"]?.ToString();
            }

            // Вычисляем размер инструкции, передавая корневой JObject выражения
            //int instructionSize = InstructionSizeCalculator.GetSize(statement);

            kismets.Add(new LObject(statementIndex, value, instructionType, offset, 0));
        }

        /// <summary>
        /// Находит корневой JObject инструкции (Statement), двигаясь вверх по дереву JSON от текущего токена.
        /// </summary>
        public static JObject FindRootStatement(JToken currentToken)
        {
            JToken node = currentToken;
            while (node != null)
            {
                // Statement - это JObject, который является прямым потомком корневого JArray
                // и содержит ключ "StatementIndex".
                if (node is JObject statement && statement.Parent is JArray && statement.ContainsKey("StatementIndex"))
                {
                    return statement;
                }
                if (node is JObject offset && offset.Parent is JArray && offset.ContainsKey("Offset"))
                {
                    return offset;
                }
                node = node.Parent;
            }
            JToken token = node == null ? currentToken : node;
            return (JObject)token;
        }

        /// <summary>
        /// Оригинальная рекурсивная логика для поиска только локализуемых строк.
        /// </summary>
        private static void FindStringsOnlyRecursively(JToken token, List<LObject> kismets)
        {
            if (token is JObject obj)
            {
                bool isStringConst = (obj.TryGetValue("$type", out var typeToken) && typeToken.ToString().Contains("StringConst")) ||
                                     (obj.TryGetValue("Inst", out var instToken) && instToken.ToString().Contains("StringConst"));

                var rootExpression = FindRootStatement(token) as JObject;
                int statementIndex = rootExpression["StatementIndex"]?.Value<int>() ?? 0;
                int offset = rootExpression["Offset"]?.Value<int>() ?? 0;

                // Находим корневой Expression, чтобы правильно посчитать размер всей инструкции
                if (isStringConst)
                {
                    JToken valueToken = obj["Value"] ?? obj["RawValue"];
                    if (valueToken != null && valueToken.Type == JTokenType.String)
                    {
                        var valueName = "StringConst";
                        // Проверяем, не является ли эта строка частью данных для локализации (.locres)
                        if (token.Parent is JProperty parentProperty)
                        {
                            bool isLocalized = IsLocalizedSource(parentProperty.Name);
                            if (IsLocalizedSource(parentProperty.Name))
                            {
                                if (!AllowLocalizedSource) return; // Пропускаем эту строку
                                else valueName = parentProperty.Name;
                            }
                            if (parentProperty.Name == "LocalizedKey" || parentProperty.Name == "LocalizedNamespace")
                                    return;
                        }
                        string value = valueToken.ToString();
                        if (token.Ancestors().Any(a => a is JProperty p && p.Name == "AssignmentExpression")) return;
                        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length < 2 || value.Contains("_") || value == "None" || int.TryParse(value, out _) || value.IsGUID()) return;
                        kismets.Add(new LObject(statementIndex, value, valueName, offset, 0)); 
                    }
                }

                foreach (var property in obj.Properties())
                {
                    FindStringsOnlyRecursively(property.Value, kismets);
                }
            }
            else if (token is JArray array)
            {
                foreach (var item in array)
                {
                    FindStringsOnlyRecursively(item, kismets);
                }
            }
        }
    }
}