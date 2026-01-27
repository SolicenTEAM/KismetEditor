using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Solicen.Kismet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Solicen.JSON
{
    class KismetString
    {
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string KeyValue = null;

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
        /// Сохраняет файл по указанному пути.
        /// </summary>
        /// <param name="uber"></param>
        /// <param name="filePath"></param>
        public static void SaveFile(this UberJSON[] uber, string filePath)
        {
            var json = JsonConvert.SerializeObject(uber, Formatting.Indented);
            File.WriteAllText(filePath, json.ToString());
        }


        public static Dictionary<string, string> GetAllValues(this UberJSON[] uber)
        {
            Dictionary<string, string> allValues = new Dictionary<string, string>();
            for (int u = 0; u < uber.Length; u++)
            {
                for (int i = 0; i < uber[u].Values.Count; i++)
                {
                    allValues.TryAdd(uber[u].Values[i].Original, uber[u].Values[i].NewValue);
                }
            }
            return allValues;
        }

        public static void ReplaceAll(this UberJSON[] uber, Dictionary<string,string> keys)
        {
            foreach (var key in keys)
            {
                var u = uber.Where(x => x.Values.Any(c => c.Original == key.Key)).ToList();
                if (u.Count > 0)
                {
                    for (int q = 0; q < u.Count(); q++)
                    {
                        int index = uber.ToList().IndexOf(u[q]);
                        uber[index].Replace(key.Key, key.Value);
                    }
                }
            }
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

        public static bool ContainsFile(this UberJSON[] uber, string fileName)
        {
            if (uber.Any(x => x.FileName == fileName)) return true;
            return false;
        }
        public static bool ContainsKey(this List<KismetString> kismets, string key)
        {
            if (kismets.Any(x => x.Original == key)) return true;
            return false;
        }
        public static bool ContainsKey(this KismetString[] kismets, string key)
        {
            if (kismets.Any(x => x.Original == key)) return true;
            return false;
        }
        public static KismetString Get(this List<KismetString> kismets, string key)
        {
            return kismets.FirstOrDefault(x => x.Original == key);
        }
        public static KismetString Get(this KismetString[] kismets, string key)
        {
            return kismets.FirstOrDefault(x => x.Original == key);
        }

        /// <summary>
        /// Метод для слияния двух UberJSON в один.
        /// </summary>
        /// <param name="uberJSON">Текущий uberJSON</param>
        /// <param name="otherJSON">Сливаемый uberJSON</param>
        /// <returns></returns>
        public static UberJSON[] Merge(this UberJSON[] uberJSON, UberJSON[] otherJSON)
        {
            if (uberJSON == null) return otherJSON;
            for (int i = 0; i < uberJSON.Length; i++)
            {
                var c_values = uberJSON[i].Values;
                var o_values = otherJSON.FirstOrDefault(x => x.FileName == uberJSON[i].FileName);
                if (o_values != null)
                {
                    foreach (var value in o_values.Values)
                    {
                        if (c_values.ContainsKey(value.Original))
                        {
                            var val = c_values.Get(value.Original);
                            var index = c_values.IndexOf(val);
                            if (!string.IsNullOrWhiteSpace(value.NewValue))
                            {
                                if (val.Original != value.NewValue)
                                    c_values[index].NewValue = value.NewValue;
                            }     
                            if (!string.IsNullOrWhiteSpace(value.KeyValue))
                            {
                                if (string.IsNullOrWhiteSpace(val.KeyValue))
                                    c_values[index].KeyValue = value.KeyValue;
                            }
                        }
                        else
                            c_values.Add(value);
                    }
                }
                c_values = c_values.Where(x => !MapParser.IsCodePart(x.Original)).ToList();
                uberJSON[i].Clear();
                uberJSON[i].Values.AddRange(c_values);
            }     
            var newUber = otherJSON.Where(x => !uberJSON.ContainsFile(x.FileName));
            if (newUber!=null && newUber.Count() > 0)
            {
                // Если есть любые новые блоки строк
                var _tempUber = new List<UberJSON>(); _tempUber.AddRange(uberJSON);
                _tempUber.AddRange(newUber); uberJSON = _tempUber.ToArray();
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
            var json = File.ReadAllText(filePath);
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
                    if (!string.IsNullOrWhiteSpace(value.Original)) 
                        values.Add(value.Original, value.NewValue);
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
        public void Add(string original, string newValue = "") => Values.Add(new KismetString { Original = original, NewValue = newValue });
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
        public void Clear() => Values.Clear(); 
        public void Replace(string key, string value)
        {
            var items = Values.Where(x => x.Original == key);
            if (items != null && items.Count() > 0)
            {
                foreach (var item in items)
                {
                    int index = Values.IndexOf(item);
                    if (Values[index].NewValue != value)
                        Values[index].NewValue = value;
                }       
            }
        }
    }
}
