using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KismetEditor
{
    internal class Program
    {
        static void Main(string[] args)
        {
            string assetPath = "UI_Tooltip.uasset";

            //Solicen.Kismet.BytecodeModifier.WriteStringsFile(assetPath); // Для извлечения уникальных строк

            var replacement = Solicen.Kismet.BytecodeModifier.TranslateFromCSV("UI_Tooltip_strings.csv");
            Solicen.Kismet.BytecodeModifier.ModifyAsset(assetPath, replacement);
        }
    }
}
