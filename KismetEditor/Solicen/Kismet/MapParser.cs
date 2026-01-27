using Newtonsoft.Json;
﻿﻿﻿using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.Kismet;
using UAssetAPI.Kismet.Bytecode;
using UAssetAPI.Kismet.Bytecode.Expressions;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
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
        public static string SearchNameSpace = "";
        public static bool AllowLocalizedSource = false;
        public static bool AllowUnderscore = false;
        public static bool IncludeNameSpace = false;
        internal static bool IsLocalizedSource(string name) => name == "LocalizedSource";
        internal static bool IsContainsUnderscore(string str) => AllowUnderscore ? false : str.Contains("_");

        public static bool IsCodePart(string str)
        {
            if (Regex.Match(str, @"^\w[.]\w.*\d").Success == true) return true;
            if (str.Contains(" = ") || str.Contains("(, None, )")) return true;
            if (str == "][" || str == "NONE") return true;
            if (str.Contains(" Is Set ") || str.Contains("Is Not Set")) return true;
            return false;
        }
        public static bool IsNotAllowedString(string value)
        {
            return (
                string.IsNullOrWhiteSpace(value)
                || value.Trim().Length < 2
                || IsCodePart(value)
                || IsContainsUnderscore(value)
                || value == "None"
                || int.TryParse(value, out _)
                || value.IsGUID()
                || value.IsAllNumber()
                || value.IsAllDot()
                || value.IsUpperLower()
                || value.IsBoolean()
                || value.IsPath()
                || value.IsAllOne()
                || value.IsStringDigit());
        }

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

        public static void OutputInformation(string instInfo, LObject[] kismets)
        {
            if (kismets.Length > 0)
            {
                CLI.Console.Write($"  [DarkYellow][{instInfo}] ");
                CLI.Console.Separator(36, true, ConsoleColor.DarkYellow);
                foreach (var kismet in kismets)
                {
                    if (IsCodePart(kismet.Value)) continue;
                    CLI.Console.WriteLine($"  [DarkYellow]| [White]- {kismet.Value}");
                }
            }
        }

        public static LObject[] ParseUbergraph(JArray jArray)
        {
            HashSet<LObject> kismetValues = new HashSet<LObject>(CreateMap(jArray, null, true));
            return kismetValues.ToArray();
        }

        /// <summary>
        /// Извлекает все значения из StrProperty во всех экспортах ассета.
        /// </summary>
        /// <param name="asset">Ассет для сканирования.</param>
        /// <returns>Массив LObject с найденными строками.</returns>
        public static LObject[] ExtractStrProperties(UAsset asset)
        {            
            var results = new Dictionary<string,LObject>();
            if (asset == null) return Array.Empty<LObject>();

            for (int i = 0; i < asset.Exports.Count; i++)
            {
                if (asset.Exports[i] is UAssetAPI.ExportTypes.NormalExport normalExport)
                {
                    foreach (var prop in normalExport.Data)
                    {
                        if (prop is StrPropertyData strProp && strProp.Value != null)
                        {
                            string keyValue = BuildKeyValue("", strProp.Name?.Value.Value);
                            string value = strProp.Value.Value;
                            if (IsNotAllowedString(value) || value == "Visibility") continue;
                            results.TryAdd(value, new LObject(i, value.Escape(), "StrProperty", 0, 0));
                        }
                    }
                }
            }
            return results.Select(x => x.Value).ToArray();
        }

        /// <summary>
        /// Извлекает все значения из TextProperty во всех экспортах ассета.
        /// </summary>
        /// <param name="asset">Ассет для сканирования.</param>
        /// <returns>Массив LObject с найденными строками.</returns>
        public static LObject[] ExtractTextProperties(UAsset asset)
        {
            var results = new Dictionary<string, LObject>();
            if (asset == null) return Array.Empty<LObject>();

            for (int i = 0; i < asset.Exports.Count; i++)
            {
                if (asset.Exports[i] is UAssetAPI.ExportTypes.NormalExport normalExport)
                {
                    foreach (var prop in normalExport.Data)
                    {
                        if (prop is TextPropertyData textProp && textProp.CultureInvariantString != null)
                        {
                            var keyValue = BuildKeyValue(textProp.Namespace?.Value, textProp.Value?.Value);
                            string value = textProp.CultureInvariantString.Value;
                            if (IsNotAllowedString(value) || keyValue.Contains("UMG")) continue;
                            results.TryAdd(value, new LObject(i, value.Escape(), "TextProperty", 0, 0, keyValue));
                        }
                    }
                }
            }
            return results.Select(x => x.Value).ToArray();
        }

        public static string BuildKeyValue(string namespaceValue, string keyValue)
        {
            if (!string.IsNullOrWhiteSpace(namespaceValue))
                return $"{namespaceValue}::{keyValue}";
            else if (!string.IsNullOrWhiteSpace(keyValue))
                return $"{keyValue}";
            return string.Empty;
        }

        /// <summary>
        /// Извлекает все значения из StringTable.
        /// </summary>
        /// <param name="asset">Ассет StringTable для сканирования.</param>
        /// <returns>Массив LObject с найденными строками.</returns>
        public static LObject[] ExtractStringTableEntries(UAsset asset)
        {
            var results = new List<LObject>();
            if (asset == null) return Array.Empty<LObject>();

            for (int i = 0; i < asset.Exports.Count; i++)
            {
                if (asset.Exports[i] is UAssetAPI.ExportTypes.StringTableExport stringTableExport)
                {
                    foreach (var entry in stringTableExport.Table)
                    {
                        // entry.Key - это ключ локализации, entry.Value - это исходный текст.
                        var keyValue = BuildKeyValue(stringTableExport.Table.TableNamespace?.Value, entry.Key?.Value);
                        string value = entry.Value.Value;
                        if (IsNotAllowedString(value) || keyValue.Contains("UMG")) continue;
                        results.Add(new LObject(i, value.Escape(), "StringTable", 0, 0, keyValue));
                    }
                }
            }
            return results.ToArray();
        }

        /// <summary>
        /// Извлекает все строки из DataTable в ассете.
        /// </summary>
        /// <param name="asset">Ассет для сканирования.</param>
        /// <returns>Массив LObject с найденными строками.</returns>
        public static LObject[] ExtractDataTableEntries(UAsset asset)
        {
            var results = new Dictionary<string, LObject>();
            if (asset == null) return Array.Empty<LObject>();

            for (int i = 0; i < asset.Exports.Count; i++)
            {
                if (asset.Exports[i] is UAssetAPI.ExportTypes.DataTableExport dataTableExport)
                {
                    foreach (var row in dataTableExport.Table.Data)
                    {
                        // Имя строки (RowName) является ключом
                        var rowName = row.Name.ToString();
                       
                        // Ищем текстовые свойства внутри строки
                        foreach (var prop in row.Value)
                        {
                            if (prop.RawValue != null)
                            {
                                var keyValue = BuildKeyValue("", rowName);
                                string dValue = prop.RawValue.ToString().Escape();
                                string finalValue = dValue;
                                var key = string.Empty;
                                if (!string.IsNullOrEmpty(prop.Name.Value.Value))
                                {
                                    
                                    key = prop.Name.Value.Value;
                                    bool isAllowedNamespace = SearchNameSpace != "" && SearchNameSpace == key ? true : 
                                        SearchNameSpace == "" ? true : false;
                                    if (!isAllowedNamespace) continue;
                                }
                                if (IsNotAllowedString(dValue) || keyValue.Contains("UMG")) continue;
                                if (finalValue.Contains("::")) continue;

                                // Сохраняем имя строки (ключ) в поле Instruction
                                results.TryAdd(finalValue, new LObject(i, finalValue.Escape(), key, 0, 0, keyValue));
                            }
                        }
                    }
                }
            }
            return results.Values.ToArray();
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
        /// Заменяет запись в DataTable по ключу.
        /// </summary>
        /// <param name="asset">Ассет StringTable для модификации.</param>
        /// <param name="exportIndex">Индекс экспорта StringTable.</param>
        /// <param name="key">Ключ строки, которую нужно заменить.</param>
        /// <param name="newValue">Новое строковое значение.</param>
        /// <returns>True, если замена прошла успешно.</returns>
        public static int ReplaceDataTableEntry(UAsset asset, string originalValue, string newValue)
        {
            if (asset == null || string.IsNullOrWhiteSpace(newValue)) return 0;
            int replacementCount = 0;
            foreach (var export in asset.Exports)
            {
                if (export is UAssetAPI.ExportTypes.DataTableExport dataTableExport)
                {
                    foreach (var row in dataTableExport.Table.Data)
                    {
                        foreach (var prop in row.Value)
                        {
                            if (prop.RawValue != null)
                            {
                                prop.RawValue = newValue;
                            }
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
        /// <summary>
        /// Заменяет все вхождения указанной строки в StrProperty по всему ассету.
        /// </summary>
        /// <param name="asset">Ассет для модификации.</param>
        /// <param name="originalValue">Оригинальное строковое значение для поиска.</param>
        /// <param name="newValue">Новое строковое значение для установки.</param>
        /// <returns>Количество произведенных замен.</returns>
        public static int ReplaceTextProperty(UAsset asset, string originalValue, string newValue)
        {
            if (asset == null || string.IsNullOrWhiteSpace(newValue)) return 0;
            int replacementCount = 0;
            foreach (var export in asset.Exports)
            {
                if (export is UAssetAPI.ExportTypes.NormalExport normalExport)
                {
                    // Мы не можем изменять коллекцию во время итерации, поэтому создаем копию для поиска
                    var propertiesToModify = normalExport.Data
                        .Where(p => p is TextPropertyData textProp && textProp.CultureInvariantString?.Value == originalValue)
                        .ToList();

                    foreach (var prop in propertiesToModify)
                    {
                        var _textProp = (TextPropertyData)prop;

                        // Создаем новое свойство с тем же именем, но новым Unicode-значением
                        var newProp = _textProp;
                        _textProp.CultureInvariantString = new FString(newValue);
                        _textProp.CultureInvariantString.Encoding = System.Text.Encoding.Unicode;

                        // Находим индекс старого свойства, удаляем его и вставляем на его место новое
                        int index = normalExport.Data.IndexOf(_textProp);
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
                        var keyValue = "";
                        var valueName = "StringConst";
                        // Проверяем, не является ли эта строка частью данных для локализации (.locres)
                        if (token.Parent is JProperty parentProperty)
                        {
                            if (IsLocalizedSource(parentProperty.Name))
                            {
                                valueName = parentProperty.Name;
                                if (!AllowLocalizedSource) return;
                                else
                                {
                                    //keyValue = BuildKeyValue(localizedNamespace.ToString(), localizedKey.ToString());
                                }
                            }
                            if (parentProperty.Name == "LocalizedKey" || parentProperty.Name == "LocalizedNamespace")
                                    return;
                        }
                        string value = valueToken.ToString();
                        if (token.Ancestors().Any(a => a is JProperty p && p.Name == "AssignmentExpression")) return;
                        if (IsNotAllowedString(value)) return;
                        if (kismets.Any(x=>x.Value != value.Escape()))
                            kismets.Add(new LObject(statementIndex, value.Escape(), valueName, offset, 0)); 
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