using Solicen.Kismet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KismetEditor
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var vers = $"{FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion}";
            Console.WriteLine($"KismetEditor {vers} - by Solicen (Denis Solicen)         |");
            Console.WriteLine("[URL] https://github.com/SolicenTEAM/KismetEditor         |");
            Console.WriteLine("[INF] Toss a coin: https://boosty.to/denissolicen/donate  |");
            Console.WriteLine("———————————————————————————————————————————————————————————\n");
            Thread.Sleep(200);
            Stopwatch stopwatch = new Stopwatch(); stopwatch.Start();
            TimeSpan timeTaken = new TimeSpan();

            var backgroundThread = new Thread(() =>
            {
                ProgramProcessor.ProcessProgram(args);
                stopwatch.Stop(); timeTaken = stopwatch.Elapsed;
            });

            backgroundThread.Start(); backgroundThread.Join(); GC.Collect();
            Console.WriteLine($"Operation completed in: {(int)timeTaken.TotalSeconds} seconds.");
        }
    }
}
