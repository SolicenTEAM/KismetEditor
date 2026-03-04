using Solicen.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;
namespace Solicen.Kismet
{
    /// <summary>
    /// Данный класс основан на изучении работы method2 в UE4localizationsTool. 
    /// <br></br>Является по большей степени последней инстанцией, угадайкой. 
    /// <br></br> - Небезопасен при обычном использовании.
    /// <br></br> - В том числе основан на том как UE хранит строки в ассете.
    /// </summary>
    internal class ReverseE
    {
        private static byte[] GetBytes(byte[] source, int index, int count)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (index < 0 || index >= source.Length) throw new ArgumentOutOfRangeException(nameof(index));
            if (count < 0 || index + count > source.Length) throw new ArgumentOutOfRangeException(nameof(count));

            byte[] result = new byte[count];
            Array.Copy(source, index, result, 0, count);
            return result;
        }

        public static int GetInt32(byte[] source, ref int index, int count = 4)
        {
            if (source[index + count + 1] == 0x00) return 0;
            if (count == 1) return GetBytes(source, index, count)[0];
            return BitConverter.ToInt32(GetBytes(source, index, count), 0);
        }

        public static int GetSize(string text)
        {
            bool ascii = text.All(c => c < 128);
            return ascii ? text.Length + 1 : -(text.Length + 1);
        }

        public static byte[] GetUnrealByteString(string text)
        {
            var result = new List<byte>();
            var bytes = Encoding.Default.GetBytes(text);
            var size = BitConverter.GetBytes(GetSize(text));

            result.AddRange(size); result.AddRange(bytes);
            return result.ToArray();
        }

        private static string ToHex(byte[] input)
        {
            return String.Concat(input.Select(b => b.ToString("X2")));
        }

        public static int GetUnicodeSize(string text)
        {
            return text.Length / 2;
        }



        /// <summary>
        /// Находит строки в стандартном хранении для FText
        /// <br></br>А именно: [4 байта][Значение]
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static LObject[] GetAllFText(string path)
        {
            var UM = new UMemory(File.ReadAllBytes(path));
            var texts = new Dictionary<string, LObject>();
            while (!UM.EndOfFile())
            {
                string Text = string.Empty;
                bool IsUnicode = false;      
                int pos = UM.GetPosition();
                if (UM.ReadByte() == 0x00) continue;
                UM.SetPosition(pos);
                if (UM.ReadByte() == 0x29 && UM.ReadByte() == 0x01)
                {
                    // 0x1F = ASCII // 0x34 = Unicode
                    var codeByte = UM.ReadByte();
                    if (codeByte == 0x1F) IsUnicode = false;
                    else if (codeByte == 0x34) IsUnicode = true;
                    else continue;
                    if (IsUnicode) Text = Encoding.Unicode.GetString(UM.GetByteString(Encoding.Unicode)).TrimEnd('\0');
                    else Text = Encoding.ASCII.GetString(UM.GetByteString(Encoding.ASCII)).TrimEnd('\0');
                    if (Text.IsGood())
                    {
                        Console.WriteLine($"\t[{Text.Length}]:[{Text.Escape()}]");
                        texts.TryAdd(Text, new LObject(0, Text.Escape(), "", 0, 0));
                    }
                }
            }
            return texts.Values.ToArray().OrderBy(line => line.Value).ToArray();
        }

        public static LObject[] GetALlValues(string path)
        {
            var UM = new UMemory(File.ReadAllBytes(path));
            var texts = new Dictionary<string, LObject>();
            while (!UM.EndOfFile())
            {
                bool IsUnicode = false;
                string Text = string.Empty;
                int pos = UM.GetPosition();
                int StringSize = 0;
                if (UM.ReadByte() == 0x00)
                {
                    continue;
                }
                // Возращаем изначальную позицию
                UM.SetPosition(pos);
                if (UM.ReadByte() == 0x29 && UM.ReadByte() == 0x01)
                {
                    // 0x1F = ASCII // 0x34 = Unicode
                    var codeByte = UM.ReadByte();
                    if (codeByte == 0x1F) IsUnicode = false;
                    else if (codeByte == 0x34) IsUnicode = true;
                    else continue;
                    if (IsUnicode) Text = Encoding.Unicode.GetString(UM.GetByteString(Encoding.Unicode)).TrimEnd('\0');
                    else       Text = Encoding.ASCII.GetString(UM.GetByteString(Encoding.ASCII)).TrimEnd('\0');

                    Console.WriteLine($"\t[{Text.Length}]:[{Text.Escape()}]");
                    if (Text.IsGood())
                    {
                        texts.TryAdd(Text, new LObject(0, Text.Escape(), "", 0, 0));
                    }
                }
                else
                {
                    UM.SetPosition(pos+1);
                }
                continue;


                if (StringSize <= 12000)
                {
                    if (StringSize > 0)
                    {
                        Text = Encoding.Default.GetString(UM.GetByteString(Encoding.ASCII)).TrimEnd('\0');
                        if (Text.Length > 0)
                        {
                            Console.WriteLine($"\t[{Text.Length}]:[{Text.Escape()}]");
                            if (Text != string.Empty)
                            {
                                if (Text.IsGood() && !MapParser.IsNotAllowedString(Text))
                                {
  
                                    texts.TryAdd(Text, new LObject(0, Text.Escape(), "", 0, 0));
                                    UM.ReadByte();
                                }
                            }

                        }
                    }
                    else
                    {
                        continue;
                        StringSize = -(StringSize) - 1;
                        if (StringSize == -1 || StringSize == 0 || StringSize == 255) continue;
                        Text = Encoding.Unicode.GetString(UM.GetByteString(Encoding.Unicode)).TrimEnd('\0');
                        if (StringSize == Text.Length)
                        {
                            if (Text != string.Empty)
                            {
                                if (Text.IsGood() && !MapParser.IsNotAllowedString(Text))
                                {
                                    Console.WriteLine($"\t[{Text.Length}]:[{Text.Escape()}]");
                                    //Console.WriteLine($"\t{ToHex(GetUnrealByteString(Text))}");
                                    texts.TryAdd(Text, new LObject(0, Text.Escape(), "", 0, 0));
                                }
                            }
                        }
                    }
                }


            }
            return texts.Values.ToArray().OrderBy(line => line.Value).ToArray();
        }


    }
}
