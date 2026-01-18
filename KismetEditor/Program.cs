using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
 
namespace Solicen.KismetEditor
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            var vers = $"{FileVersionInfo.GetVersionInfo(AppContext.BaseDirectory+"\\KissE.exe").FileVersion}";
            CLI.Console.Separator(64);
            CLI.Console.WriteLine($"[DarkGray][CLI] [White]KismetEditor {vers} - by Solicen (Denis Solicen)        [DarkGray]|");
            CLI.Console.WriteLine("[DarkGray][URL] [White]https://github.com/SolicenTEAM/KismetEditor              [DarkGray]|");            
            CLI.Console.Separator(64);
            CLI.Console.WriteLine("[Yellow]Toss a coin: [Blue]https://boosty.to/denissolicen/donate             [DarkGray]|");
            CLI.Console.Separator(64);

            Thread.Sleep(200);
            Stopwatch stopwatch = new Stopwatch(); stopwatch.Start();
            TimeSpan timeTaken = new TimeSpan();

            var backgroundThread = new Thread(() =>
            {
                Solicen.CLI.CLIHandler.ProcessProgram(args);
                stopwatch.Stop(); timeTaken = stopwatch.Elapsed;
            });

            backgroundThread.Start(); backgroundThread.Join(); GC.Collect();
            Console.WriteLine($"Operation completed in: {(int)timeTaken.TotalSeconds} seconds.");
        }
    }
}
