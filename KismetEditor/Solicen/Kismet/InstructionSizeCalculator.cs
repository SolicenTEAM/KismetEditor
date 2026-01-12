using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Solicen.Kismet
{
    /// <summary>
    /// Отвечает за вычисление размера инструкций Kismet в байтах на основе их JSON-представления.
    /// </summary>
    internal static class InstructionSizeCalculator
    {
        static bool DebugOutput = false;

        /// <summary>
        /// Вычисляет размер инструкции Kismet в байтах.
        /// </summary>
        /// <param name="expression">JObject, представляющий инструкцию (поле Expression из уберграфа).</param>
        /// <returns>Размер инструкции в байтах.</returns>
        public static int GetSize(JObject expression)
        {
            if (expression == null) return 0;
            string jsonString = expression.ToString();
            if (DebugOutput)
            {
                Console.WriteLine("-----------------------------------------------");
                Console.WriteLine(jsonString);
                Console.WriteLine("-----------------------------------------------");
            }        
            int totalSize = 0;

            // Regex для поиска строковых констант и захвата их значения.
            // Он ищет "$type": "...StringConst..." и затем ближайшее поле "Value": "..."
            var stringConstRegex = new Regex(@"""(?:(\$type|Inst))"":\s*""[^""]*?(StringConst|UnicodeStringConst)[^""]*?"".*?""Value"":\s*""(.*?)(?<!\\)""", RegexOptions.Singleline);

            // Regex для поиска всех остальных инструкций.
            var otherInstRegex = new Regex(@"(?:(?:\$type)|(?:Inst))"":\s*""(?:UAssetAPI\.Kismet\.Bytecode\.Expressions\.EX_|EX_)?(\w+)");

            var stringConstInst = stringConstRegex.Matches(jsonString).Cast<Match>();
            var otherInst = otherInstRegex.Matches(jsonString).Cast<Match>();

            if (otherInst.Cast<Match>().Any(x => x.Value.Contains("UAssetAPI")))
            {
                otherInst = otherInst.Cast<Match>().Where(x => x.Value.Contains("UAssetAPI") && x.Value.Contains("Kismet"));
            }


            // 2. Теперь обрабатываем все остальные инструкции.
            foreach (Match match in otherInst)
            {
                if (DebugOutput) Console.WriteLine(match.Value);
                string instName = match.Groups[1].Value;

                // Пропускаем строковые константы, так как мы их уже посчитали.
                if (instName.Contains("StringConst"))
                {
                    // 1. Сначала обрабатываем строковые константы, так как их размер зависит от содержимого.
                    foreach (Match strMatch in stringConstInst)
                    {
                        string type = strMatch.Groups[2].Value;
                        string value = strMatch.Groups[3].Value;

                        if (type == "StringConst")
                        {
                            totalSize += 1 + value.Length + 1; // Opcode + ASCII-строка + null-терминатор
                        }
                        else if (type == "UnicodeStringConst")
                        {
                            totalSize += 1 + (value.Length + 1) * 2; // Opcode + UTF-16 строка + null-терминатор
                        }
                    }
                }

                switch ("EX_" + instName)
                {
                    // Инструкции с фиксированным размером
                    case "EX_Context":
                    case "EX_ClassContext":
                    case "EX_Context_FailSilent":
                        totalSize += 1 + 4 + 8; break; // Opcode + SkipOffset + RValuePointer
                    case "EX_Let":
                    case "EX_LetValueOnPersistentFrame":
                        totalSize += 1 + 8; break; // Opcode + PropertyPointer
                    case "EX_LocalVariable":
                    case "EX_InstanceVariable":
                    case "EX_DefaultVariable":
                    case "EX_LocalOutVariable":
                        totalSize += 1 + 8; break; // Opcode + KismetPropertyPointer
                    case "EX_VirtualFunction":
                    case "EX_LocalVirtualFunction":
                        totalSize += 1 + 12 + 1; break; // Opcode + FName + EndOfScript-байт для параметров
                    case "EX_CallMath":
                    case "EX_FinalFunction":
                        totalSize += 1 + 8 + 1; break; // Opcode + StackNode + EndOfScript-байт для параметров
                    case "EX_Jump":
                    case "EX_JumpIfNot":
                        totalSize += 1 + 4; break; // Opcode + CodeOffset
                    case "EX_NameConst":
                        totalSize += 1 + 12; break; // Opcode + FName
                    case "EX_ObjectConst":
                        totalSize += 1 + 8; break; // Opcode + FPackageIndex
                    case "EX_Int64Const":
                    case "EX_UInt64Const":
                    case "EX_DoubleConst":
                        totalSize += 1 + 8; break; // Opcode + 8-байтовое значение
                    case "EX_IntConst":
                    case "EX_FloatConst":
                    case "EX_SkipOffsetConst":
                        totalSize += 1 + 4; break; // Opcode + 4-байтовое значение
                    case "EX_ByteConst":
                    case "EX_IntConstByte":
                        totalSize += 1 + 1; break; // Opcode + 1-байтовое значение
                    default:
                        totalSize += 0; break; // Для всех остальных инструкций считаем как минимум 1 байт за Opcode
                }
            }
            if (DebugOutput)
            {
                Console.WriteLine("-----------------------------------------------");
                Console.WriteLine($"Count: [{otherInst.Count()}] Size: [{totalSize}]");
                Console.WriteLine("-----------------------------------------------\n");
            }

            return totalSize;
        }
    }
}