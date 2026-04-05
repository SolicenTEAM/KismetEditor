using Solicen.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace KissEGUI
{
    class DDS
    {
        public int HeaderSize, Flags;
        public int Height, Width;
        public int LinearSize, MipCount;
        public int PixelFormat, FOURCC;
        public int Depth = 0;
        public string CompressFormat = string.Empty;
        public byte[] CFormat => Encoding.ASCII.GetBytes(CompressFormat);
        public byte[] Texture = Array.Empty<byte>();
    }
    internal class TexConv
    {
        string _tempDirectory = Path.Combine(Path.GetTempPath(), "TextureInjectorTemp");
        public string ConvertPngToDds(string pngPath, string format = "DXT1", string filter = "LINEAR")
        {
            string outputDds = Path.Combine(_tempDirectory, $"{Guid.NewGuid()}.dds");
            Directory.CreateDirectory(_tempDirectory);
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = Environment.CurrentDirectory+ "\\texconv.exe",
                Arguments = $"\"{pngPath}\" -f {format} -o \"{_tempDirectory}\" -if {filter} -y",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (Process process = Process.Start(startInfo))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                    throw new Exception($"texconv error: {error}");

                Console.WriteLine("texconv output: " + output);
            }

            // texconv создает файл с тем же именем, но с расширением .dds в выходной папке
            string generatedFile = Path.Combine(_tempDirectory, Path.GetFileNameWithoutExtension(pngPath) + ".dds");

            // Если имя сгенерировалось иначе (например, с суффиксом), нужно найти файл
            if (!File.Exists(generatedFile))
            {
                var files = Directory.GetFiles(_tempDirectory, "*.dds");
                generatedFile = files.FirstOrDefault() ?? throw new Exception("DDS файл не был создан texconv");
            }

            // Переименовываем в наш уникальный GUID, чтобы не было конфликтов
            File.Move(generatedFile, outputDds);

            return outputDds;
        }

        public string TextureFormatFromUE(byte[] ueArray)
        {
            var UM = new UMemory(ueArray);
            while (!UM.EndOfFile())
            {
                while (true)
                {
                    var b = UM.ReadByte();
                    if (b == 0x08)
                    {
                        { UM.SetPosition(UM.GetPosition() - 1); break; }
                    }
                }
                UM.GetInt32();
                return UM.GetString(8).Replace("PF_", "").Trim('\0');
            }
            return string.Empty;
        }

        public DDS ParseDds(byte[] array)
        {
            DDS dds = new DDS();
            var UM = new UMemory(array);
            while (!UM.EndOfFile())
            {
                var head = UM.GetInt32();
                if (head == 542327876)
                {
                    dds.HeaderSize = UM.GetInt32();
                    dds.Flags = UM.GetInt32();
                    dds.Height = UM.GetInt32();
                    dds.Width = UM.GetInt32();
                    dds.LinearSize = UM.GetInt32();
                    dds.Depth = UM.GetInt32();
                    dds.MipCount = UM.GetInt32();

                    while (true)
                    {
                        var b = UM.ReadByte();
                        if (b != 0x00) 
                        { UM.SetPosition(UM.GetPosition() - 1); break; }
                    }

                    dds.PixelFormat = UM.GetInt32();
                    dds.FOURCC = UM.GetInt32();
                    dds.CompressFormat = UM.GetString(4);

                    while (true)
                    {
                        var b = UM.ReadByte();
                        if (b != 0x00)
                        { UM.SetPosition(UM.GetPosition() - 1); break; }
                    }

                    UM.GetInt32(); // Читаем ненужные данные
                    while (true)
                    {
                        var b = UM.ReadByte();
                        if (b != 0x00)
                        { UM.SetPosition(UM.GetPosition() - 1); break; }
                    }

                    int index = UM.GetPosition();
                    dds.Texture = UM.GetBytes(index, UM.Lenght - index);
                    return dds;
                }
                else
                {
                    break;
                }
            }

            return dds;
        }
    }
}
