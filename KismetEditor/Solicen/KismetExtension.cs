using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UAssetAPI.ExportTypes;
using UAssetAPI.Kismet.Bytecode;
using UAssetAPI.Kismet;
using UAssetAPI;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
namespace Solicen.Kismet
{
    internal class KismetExtension
    {
        public JObject JsonObject;
        public string Json = "";
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
            string s = ""; int lenght = oldString.Length;

            // Заполнитель строки
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
                { "CodeOffset", 200519 }
                // 20 | 05 | 19 | To End Script 
            };
            JObject newInstruction = new JObject
            {
                { "$type", "UAssetAPI.Kismet.Bytecode.Expressions.EX_StringConst, UAssetAPI" },
                { "Value", $"{fillString}" }
            };

            JArray jsonArray = new JArray();
            jsonArray.Add(newInstruction);
            jsonArray.Add(jumpInstruction);

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

        public static JObject ReplaceOffset(string json, int oldOffset, int newOffset)
        {
            json = json.Replace($"\"CodeOffset\": {oldOffset}", $"\"CodeOffset\": {newOffset}");
            return JObject.Parse(json);
        }

        public static JObject ReplaceAllInst(JObject jsonObject, Dictionary<string, string> replacement)
        {
            foreach (var replace in replacement)
            {
                var key   = replace.Key;    // ReplaceFrom
                var value = replace.Value;  // ReplaceTo

                int copyInst = Regex.Matches(jsonObject.ToString(), $"\"{key}\"").Count;
                jsonObject = ReplaceInst(jsonObject, key, value);

                var asset = UAsset.DeserializeJson(jsonObject.ToString());
                var JObjectUbergraph = GetUbergraphJson(asset);
                var kismetObject = KismetObject.FromJson(JObjectUbergraph);
                var newToEnd = KismetObject.GetOffset(kismetObject, 200519);
                var newFromEnd = KismetObject.GetOffset(kismetObject, 60519);

                jsonObject = KismetExtension.ReplaceOffset(jsonObject.ToString(), 200519, newFromEnd);
                jsonObject = KismetExtension.ReplaceOffset(jsonObject.ToString(), 60519, newToEnd);

                for (int i = 0; i < copyInst-1; i++)
                {
                    jsonObject = ReplaceAllInst(jsonObject, new Dictionary<string, string>()
                    {
                        {replace.Key, replace.Value},
                    });
                }
            }
            return jsonObject;
        }

        public static JObject ReplaceInst(JObject jsonObject, string replaceFrom, string replaceTo)
        {
            Console.WriteLine($"Find: {replaceFrom} | Replace: {replaceTo}");

            // Получаем массив "Exports"
            JArray exportsArray = (JArray)jsonObject["Exports"];
            if (exportsArray != null && exportsArray.Count > 0)
            {
                bool instIs = false;
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
                                JObject script = (JObject)scriptBytecodeArray[i];
                                JObject expression = (JObject)script["Expression"];
                                if (expression != null)
                                {
                                    try
                                    {
                                        var value = (string)expression["Value"];
                                        if (value == null) continue;
                                        if (value.ToString().Trim() == replaceFrom)
                                        {
                                            Console.WriteLine($"Value: {value}");
                                            var oldInst = script;
                                            var indexInst = scriptBytecodeArray.IndexOf(oldInst);
                                            scriptBytecodeArray.Remove(oldInst);


                                            try
                                            {
                                                // Перезаписываем старые инструкции
                                                var _type = (string)expression["$type"];
                                                var _value = (string)expression["Value"];

                                                expression["$type"] = _type.Replace("EX_StringConst", "EX_UnicodeStringConst");
                                                expression["Value"] = _value.Replace(replaceFrom, replaceTo);
                                            }
                                            catch { }


                                            // Новые инструкции на замену старой
                                            var fillString = FillString(replaceFrom);
                                            var newInsts = JumpOffsetInstruction(fillString);
                                            foreach (JObject jobject in newInsts)
                                            {
                                                scriptBytecodeArray.Insert(indexInst, jobject);
                                            }
                                            JObject jumpInstruction = new JObject {
                                            { "$type", "UAssetAPI.Kismet.Bytecode.Expressions.EX_Jump, UAssetAPI" },
                                            { "CodeOffset", 60519 }
                                        };
                                            // 6 | 05 | 19 | FES = From End Script
                                            scriptBytecodeArray.Add(oldInst);
                                            scriptBytecodeArray.Add(jumpInstruction);
                                            instIs = true;
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

            
            return jsonObject;


        }

        public static KismetExpression[] GetUbergraph(UAsset asset)
        {
            var ubergraph = asset.Exports.FirstOrDefault(x => x.ObjectName.ToString().StartsWith("ExecuteUbergraph"));
            if (ubergraph != null)
            {
                Console.WriteLine(ubergraph.ObjectName);
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
