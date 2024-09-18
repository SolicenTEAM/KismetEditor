using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Solicen.Kismet
{
    /// <summary>
    /// Kismet (Lite) Object
    /// </summary>
    internal class KismetObject
    {
        public int StatementIndex = 0;
        public string Value;
        public string Instruction;

        public KismetObject(int statementIndex, string value, string instruction)
        {
            StatementIndex = statementIndex;
            Value = value;
            Instruction = instruction;
        }

        public static int GetOffset(KismetObject[] kismets, int magic)
        {
            var _kismets = kismets.ToList();
            var item = kismets.FirstOrDefault(x => x.Value == magic.ToString());
            if (item != null)
            {
                int index = _kismets.IndexOf(item); 
                switch (magic)
                {
                    case 200519:
                        return kismets[index + 1].StatementIndex;
                    case 60519:
                        return kismets[index - 1].StatementIndex;
                    default:
                        return -1;

                }
            }
            return 0;
        }

        public static string[] ToCSV(JArray JArray)
        {
            HashSet<string> values = new HashSet<string>();
            var kismet = FromJson(JArray, true).Select(x => x.Value).ToArray();
            values.Add("OriginalText | Translation");
            for (int i = 0; i < kismet.Length; i++)
            {
                values.Add($"{kismet[i]} | ");
            }
            return values.ToArray();
        }

        public static KismetObject[] FromJson(JArray JArray, bool onlyString = false)
        {
            List<KismetObject> kismets = new List<KismetObject>();
            foreach (JObject jsonObject in JArray)
            {
                int statementIndex = (int)jsonObject["StatementIndex"];
                string value = "", instruction = "";
                try
                {
                    JObject variableObject = (JObject)jsonObject["Variable"];
                    if (variableObject != null)
                        instruction = (string)variableObject["Variable Name"];

                    JObject expressionObject = (JObject)jsonObject["Expression"];
                    if (expressionObject != null)
                    {
                        value = (string)expressionObject["Value"];
                        if (onlyString)
                        {
                            if (value != null)
                            {
                                if (value.Contains("_") || value == "None" || value.Trim().Length == 1)
                                {
                                    value = null;
                                    instruction = null;
                                }
                            }
                            try
                            {
                                int v = int.Parse(value);
                                value = null;
                                instruction = null;

                            }
                            catch { }
                        }
                    }


                }
                catch {}
                if (!onlyString)
                {
                    try
                    {
                        int offset = (int)jsonObject["Offset"];
                        if (offset == 200519 || offset == 60519)
                        {
                            value = offset.ToString(); instruction = "JumpOffset";
                        }

                    }
                    catch {}
                }

                if (string.IsNullOrWhiteSpace(value)) continue;
                if (string.IsNullOrWhiteSpace(instruction)) continue;

                //if (debug) Console.WriteLine($"StatementIndex: {statementIndex} | Value: {value} | Inst: {instruction}");
                kismets.Add(new KismetObject(statementIndex, value, instruction));
                
            }

            return kismets.ToArray();
        }

    }
}
