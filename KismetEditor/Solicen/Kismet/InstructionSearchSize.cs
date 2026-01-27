using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using UAssetAPI.Kismet;
using UAssetAPI.Kismet.Bytecode;

namespace Solicen.Kismet
{
    /// <summary>
    /// Отвечает за поиск размера инструкций Kismet в байтах на основе сравнения Expression и их JSON-представления.
    /// </summary>
    internal static class InstructionSearchSize
    {
        static bool DebugOutput = false;

      
        /// <summary>
        /// Вычисляет размер инструкции Kismet в байтах.
        /// </summary>
        /// <param name="expression">JObject, представляющий инструкцию (поле Expression из уберграфа).</param>
        /// <returns>Размер инструкции в байтах.</returns>
        public static int GetSize(UAssetAPI.UAsset asset, JObject expression, KismetExpression[] ubergraph)
        {
            if (expression == null) return 0;
            int totalSize = 0; KismetExpression expAsset = null;
            var json = expression.ToString();
            var serializer = new KismetExpressionSerializer(asset);

            if (DebugOutput)
            {
                var context = ubergraph.FirstOrDefault(x => serializer.SerializeExpression(x).ToString().Contains("Initializing world..."));
                if (context != null)
                {
                    Console.WriteLine("[MY]-------------------------------------------");
                    Console.WriteLine(serializer.SerializeExpression(context).ToString());
                    Console.WriteLine("-----------------------------------------------");
                    Console.WriteLine();
                    Console.WriteLine("[JSON]-----------------------------------------");
                    Console.WriteLine(json);
                    Console.WriteLine("-----------------------------------------------");
                }
            }

            #region Нахождение KismetExpression идентичного JObject
            if (expression.ToString().Contains("$type")) // Ассет был сериализован
                expAsset = ubergraph.First(x => (serializer.SerializeExpression(x).ToString() == expression.ToString()));              
            else // Ассет не был сериализован можем найти так
                expAsset = ubergraph.First(x => x.ToString() == expression.ToString());
            if (expAsset == null) return 0; // Если ничего не найдено, отправляем 0 как ошибку.
            #endregion

            KismetSerializer.SerializeExpression(expAsset, ref totalSize, true);
            if (DebugOutput)
            {
                Console.WriteLine("-----------------------------------------------");
                Console.WriteLine($"Size: [{totalSize}]");
                Console.WriteLine("-----------------------------------------------\n");
            }
            return totalSize;
        }
    }
}