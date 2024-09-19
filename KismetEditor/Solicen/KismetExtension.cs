// Выполнено Денисом Солиценом в 2024 году
// : https://github.com/DenisSolicen
// Техническая помощь с заполнителем строки ув. Ambi
// Основано на UAssetAPI и ресурсах Unreal Engine 4

using System;using System.Collections.Generic;using System.Linq;using System.Threading.Tasks;
using UAssetAPI.ExportTypes;using UAssetAPI.Kismet.Bytecode;using UAssetAPI.Kismet;
using UAssetAPI;using Newtonsoft.Json.Linq;using System.Text.RegularExpressions;

namespace Solicen.Kismet
{
    internal class KismetExtension
    {
        internal const int MAGIC_TES = 200519; // | 20 | 05 | 19 | To   End Script 
        internal const int MAGIC_FES = 060519; // | 06 | 05 | 19 | From End Script

        public int ModifiedInst = 0;

        public JObject JsonObject;
        public string Json => JsonObject.ToString();
        public KismetExtension(JObject json)
        {
            JsonObject = json; 
        }

        public static JArray GetUbergraphJson(UAsset asset)
        {
            var ubergraph = GetUbergraph(asset);
            if (ubergraph != null)
            {
                KismetSerializer.asset = asset;
                var JArray = KismetSerializer.SerializeScript(ubergraph);
                return JArray;
            }
            return null;
        }

        public static string FillString(string oldString)
        {
            string s = ""; // Заполнитель строки
            int all = (9 + 9 + (oldString.Length + 2))-7;
            Parallel.For(0, all, (i) =>
            {
                if (i < 7) s += "a";
                else s += "1";
            });
            return s;
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

        public static JArray ScriptBytecode(string json)
        {
            JObject jsonObject = JObject.Parse(json);
            JArray  exports    = (JArray)jsonObject["Exports"];
            JArray  bytecode   = new JArray();

            foreach (JObject obj in exports)
            {
                if (obj.ContainsKey("ScriptBytecode"))
                {
                    bytecode = (JArray)obj["ScriptBytecode"];
                }
            }
            return bytecode;
        }

        /// <summary>
        /// Заменяем напрямую смещения в JSON как текст, вместо изменения через NewToSoft.Json
        /// </summary>
        /// <param name="oldOffset">Старое смещение которое мы заменяем</param>
        /// <param name="newOffset">Новое смещение на которое мы заменяем</param>
        public void ReplaceOffset(int oldOffset, int newOffset)
        {
            var json = Json.ToString();
            json = json.Replace($"\"CodeOffset\": {oldOffset}", $"\"CodeOffset\": {newOffset}");
            JsonObject = JObject.Parse(json);
        }

        public void ReplaceAllInst(Dictionary<string, string> replacement)
        {
            foreach (var replace in replacement)
            {             
                var key   = replace.Key;    // ReplaceFrom
                var value = replace.Value;  // ReplaceTo

                // Подсчет количества копий инструкций
                int InstCount = Regex.Matches(Json, $"\"{key}\"").Count;

                // Непросредственно замена инструкции
                this.ReplaceInst(key, value);

                // Замена всего остального для работы
                var asset = UAsset.DeserializeJson(Json);
                var JObjectUbergraph = GetUbergraphJson(asset);
                var kismetObject = KismetObject.FromJson(JObjectUbergraph);
                var newToEnd = KismetObject.GetOffset(kismetObject, MAGIC_TES);
                var newFromEnd = KismetObject.GetOffset(kismetObject, MAGIC_FES);

                this.ReplaceOffset(MAGIC_TES, newFromEnd);
                this.ReplaceOffset(MAGIC_FES, newToEnd);

                for (int i = 0; i < InstCount-1; i++)
                {
                    ReplaceAllInst(new Dictionary<string, string>()
                    {
                        {replace.Key, replace.Value},
                    });
                }
            }
        }

        public void ReplaceInst(string replaceFrom, string replaceTo)
        {
            // Получаем массив "Exports"
            JArray exportsArray = (JArray)JsonObject["Exports"];
            if (exportsArray != null && exportsArray.Count > 0)
            {
                foreach (var export in exportsArray)
                {       
                    JObject exportObject = (JObject)export;
                    // Получаем массив "ScriptBytecode"
                    if ((JArray)exportObject["ScriptBytecode"] is JArray scriptBytecodeArray)
                    {
                        if (scriptBytecodeArray != null)
                        {
                            int index = 0;
                            // Проходим по каждому элементу массива ScriptBytecode
                            for (int i = 0; i < scriptBytecodeArray.Count; i++)
                            {
                                JObject script     = (JObject)scriptBytecodeArray[i];
                                JObject expression = (JObject)script["Expression"];

                                if (expression != null)
                                {
                                    try
                                    {
                                        var value = (string)expression["Value"];
                                        if (value == null) continue;
                                        if (value.ToString().Trim() == replaceFrom)
                                        {
                                            Console.WriteLine($"| Old: {replaceFrom} | New: {replaceTo}"); ModifiedInst++;
                                            var oldInst = script;
                                            var indexInst = scriptBytecodeArray.IndexOf(oldInst);

                                            // Удаляем старую инструкцию
                                            scriptBytecodeArray.Remove(oldInst);

                                            try
                                            {
                                                // Перезаписываем старые инструкции
                                                var _type = (string)expression["$type"];
                                                var _value = (string)expression["Value"];

                                                expression["$type"] = _type.Replace("EX_StringConst", "EX_UnicodeStringConst");
                                                expression["Value"] = _value.Replace(replaceFrom, replaceTo);
                                            }
                                            catch
                                            {
                                                // Если какого-то значения выше нет
                                                // то выбивает исключение и продолжает работу
                                            }

                                            // Две новые инструкции на замену старой
                                            var fillString = FillString(replaceFrom);
                                            var newInsts = JumpOffsetInstruction(fillString);
                                            foreach (JObject jobject in newInsts)
                                            {
                                                scriptBytecodeArray.Insert(indexInst, jobject);
                                            }

                                            JObject jumpInstruction = new JObject {
                                            { "$type", "UAssetAPI.Kismet.Bytecode.Expressions.EX_Jump, UAssetAPI" },
                                            { "CodeOffset", MAGIC_FES }};
  
                                            scriptBytecodeArray.Add(oldInst);
                                            scriptBytecodeArray.Add(jumpInstruction);
                                            break;

                                        }
                                    }
                                    catch
                                    {
                                        continue;
                                    }

                                }
                            }

                        }

                        exportObject["ScriptBytecode"] = scriptBytecodeArray;
                    }

                }
            }
            else
            {
                Console.WriteLine("Exports не найден.");
            }
        }

        public static KismetExpression[] GetUbergraph(UAsset asset)
        {
            var ubergraph = asset.Exports.FirstOrDefault(x => x.ObjectName.ToString().StartsWith("ExecuteUbergraph"));
            if (ubergraph != null)
            {
                if (ubergraph is StructExport structExport)
                {
                    KismetSerializer.asset = asset;
                    return structExport.ScriptBytecode;
                }
            }
            return null;

        }
    }
}
