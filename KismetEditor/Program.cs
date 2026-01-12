using Solicen.Kismet;
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
