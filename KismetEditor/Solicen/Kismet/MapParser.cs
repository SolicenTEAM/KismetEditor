﻿using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UAssetAPI;
using UAssetAPI.Kismet;
using UAssetAPI.Kismet.Bytecode;
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

        public static string[] ParseAsCSV(JArray jArray)
        {
            var kismetValues = new HashSet<string>(CreateMap(jArray, null, true).Select(x => x.Value));

            HashSet<string> csvLines = new HashSet<string>();
            foreach (var value in kismetValues)
            {
                Console.WriteLine($" - {value.Escape()}");
                csvLines.Add($"{value.Escape()}|");
            }
            return csvLines.ToArray();
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
                bool isLocalized = (obj.TryGetValue("LocalizedSource", out var localToken));
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
                        // Проверяем, не является ли эта строка частью данных для локализации (.locres)
                        if (token.Parent is JProperty parentProperty)
                        {
                            if (parentProperty.Name == "LocalizedSource" || parentProperty.Name == "LocalizedKey" || parentProperty.Name == "LocalizedNamespace")
                            {
                                return; // Пропускаем эту строку
                            }
                        }

                        string value = valueToken.ToString();
                        if (token.Ancestors().Any(a => a is JProperty p && p.Name == "AssignmentExpression")) return;
                        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length < 2 || value.Contains("_") || value == "None" || int.TryParse(value, out _) || value.IsGUID()) return;
                        kismets.Add(new LObject(statementIndex, value, "StringConst", offset, 0)); 
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