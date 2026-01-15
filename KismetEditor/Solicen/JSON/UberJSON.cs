using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;

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
                    catch (Exception ex) { Console.WriteLine($"[{key}] An element with this key has already been added."); }
                }
                catch
                {
                    uberJSON.Add(new KismetString(l, ""));
                }
            }
            return new UberJSON[] { uberJSON };
        }

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
