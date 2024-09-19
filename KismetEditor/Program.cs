using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KismetEditor
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("KismetEditor | Solicen");
            Console.WriteLine("———————————————————————————————————————————————");
            Thread.Sleep(200);
            Stopwatch stopwatch = new Stopwatch(); stopwatch.Start();
            TimeSpan timeTaken = new TimeSpan();

            string assetPath = "UI_Tooltip.uasset";

            //Solicen.Kismet.BytecodeModifier.WriteStringsFile(assetPath); // Для извлечения уникальных строк

            var replacement = Solicen.Kismet.BytecodeModifier.TranslateFromCSV("UI_Tooltip_strings.csv");
            Solicen.Kismet.BytecodeModifier.ModifyAsset(assetPath, replacement);

            stopwatch.Stop(); timeTaken = stopwatch.Elapsed; GC.Collect();
            Console.WriteLine($"Operation completed in: {(int)timeTaken.TotalSeconds} seconds.");
            Console.ReadLine();
        }
    }
}
