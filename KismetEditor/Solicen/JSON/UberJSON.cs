using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Solicen.JSON
{
    class KismetString
    {
        public string Original = "";
        public string NewValue = "";

        public KismetString(){ }
        public KismetString(string original, string newValue)
        {
            this.Original = original;this.NewValue = newValue;
        }
    }

    static class UberJSONProcessor
    {
        /// <summary>
        /// Сохраняет фйл по указанному пути.
        /// </summary>
        /// <param name="uber"></param>
        /// <param name="filePath"></param>
        public static void SaveFile(this UberJSON[] uber, string filePath)
        {
            var json = JsonConvert.SerializeObject(uber, Formatting.Indented);
            File.WriteAllText(filePath, json.ToString());
        }

        /// <summary>
        /// Конвертирует старый CSV формат в UberJSON.
        /// </summary>
        /// <param name="filePath">Путь до CSV файла.</param>
        /// <param name="separator">Разделитель значений.</param>
        /// <returns></returns>
        public static UberJSON[] Convert(string filePath, char separator = '|')
        {
            var uberJSON = new UberJSON(Path.GetFileName(filePath));
            var lines = System.IO.File.ReadAllLines(filePath);
            foreach (var l in lines)
            {
                if (string.IsNullOrWhiteSpace(l)) continue;
                if (l.Contains("OriginalText") && l.Contains("Translation") || l.StartsWith("//")) continue; 
                // Пропускаем строку заголовка и если строка начинается с символов комментирования - пропустить             
                try
                {
                    var values = l.Split(separator);
                    var key = values[0].Unescape();
                    var value = values[1].Unescape();
                    try { if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value)) uberJSON.Add(new KismetString(key, value)); }
                    catch { Console.WriteLine($"[{key}] An element with this key has already been added."); }
                }
                catch
                {
                    uberJSON.Add(new KismetString(l, ""));
                }
            }
            return new UberJSON[] { uberJSON };
        }

        /// <summary>
        /// Метод для слияния двух UberJSON в один.
        /// </summary>
        /// <param name="uberJSON">Текущий uberJSON</param>
        /// <param name="otherJSON">Сливаемый uberJSON</param>
        /// <returns></returns>
        public static UberJSON[] Merge(this UberJSON[] uberJSON, UberJSON[] otherJSON)
        {
            for (int i = 0; i < uberJSON.Length; i++)
            {
                var c_values = uberJSON[i].GetValues();
                var o_values = otherJSON.FirstOrDefault(x => x.FileName == uberJSON[i].FileName);
                if (o_values != null)
                {
                    foreach (var value in o_values.Values)
                    {
                        if (c_values.ContainsKey(value.Original))
                        {
                            if (!string.IsNullOrWhiteSpace(value.NewValue))
                            {
                                if (c_values[value.Original] != value.NewValue)
                                {
                                    c_values[value.Original] = value.NewValue;
                                }
                            }
                        }
                        else
                        {
                            c_values.TryAdd(value.Original, value.NewValue);
                        }
                    }
                }
                uberJSON[i].Clear();
                uberJSON[i].AddRange(c_values);
            }
            return uberJSON;
        }

        /// <summary>
        /// Получение данных из файла типа uberJSON.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public static UberJSON[] ReadFile(string filePath)
        {
            var json = System.IO.File.ReadAllText(filePath);
            return Deserialize(json);
        }
        public static UberJSON[] Deserialize(string json)
        {
            return JsonConvert.DeserializeObject<UberJSON[]>(json);
        }
        public static Dictionary<string, string> GetValues(this UberJSON uberJSON)
        {
            Dictionary<string, string> values = new Dictionary<string, string>();
            foreach (var value in uberJSON.Values)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(value.Original)) values.Add(value.Original, value.NewValue);
                }
                catch { }

            }
            return values;
        }

    }

    internal class UberJSON
    {
        public string FileName = "";
        public List<KismetString> Values = new List<KismetString>();
        public UberJSON(string fileName) { this.FileName = fileName; }

        public void Add(KismetString kismetString) => Values.Add(kismetString);
        public void Add(string str) => Values.Add(new KismetString { Original = str });
        /// <summary>
        /// Добавляет элементы указанной коллекции в конец списка.
        /// </summary>
        /// <param name="strings"></param>
        public void AddRange(string[] strings)
        {
            foreach (string str in strings) { Add(str); }
        }
        public void AddRange(Dictionary<string, string> keys)
        {
            foreach (var key in keys) { Add(new KismetString { Original = key.Key, NewValue = key.Value }); }
        }
        public void Clear() {  Values.Clear(); }
    }
}
