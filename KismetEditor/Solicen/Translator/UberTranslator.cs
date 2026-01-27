using GTranslate;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UAssetAPI.ExportTypes;

namespace Solicen.Translator
{
    internal class UberTranslator
    {
        private GTranslate.Translators.GoogleTranslator2 GoogleV2 = new GTranslate.Translators.GoogleTranslator2();
        private GTranslate.Translators.MicrosoftTranslator Microsoft = new GTranslate.Translators.MicrosoftTranslator();
        private GTranslate.Translators.YandexTranslator Yandex = new GTranslate.Translators.YandexTranslator();
        private OpenRouterApiClient OpenRouterClient;

        public static string LanguageTo = "ru", LanguageFrom = "en", Endpoint = "yandex"; 
        public static string OpenRouterApiKey = "";
        public static string OpenRouterModel = "tngtech/deepseek-r1t2-chimera:free";

        public UberTranslator()
        {
            if (!string.IsNullOrEmpty(OpenRouterApiKey))
            {
                // Используем наш новый, надежный клиент
                OpenRouterClient = new OpenRouterApiClient(OpenRouterApiKey);
            }
        }

        public void TranslateLines(ref Dictionary<string, string> values, IProgress<Tuple<int, int>> progress = null, bool showWaringMsg = false, int delayBetweenMsg = 150)
        {
            int SegmentIndex = 1; Dictionary<string, string> result = new Dictionary<string, string>();
            int nullSegments = values.Where(s => string.IsNullOrWhiteSpace(s.Value)).ToArray().Length;

            if (Endpoint.ToLower() == "router")
            {
                if (OpenRouterClient == null)
                {
                    CLI.Console.WriteLine("[Error] DeepSeek API key is not configured. Please provide the API key.");
                    // Заполняем результат исходными значениями, чтобы не потерять данные
                    values = new Dictionary<string, string>(values);
                    return;
                }

                // Используем новую пакетную логику для DeepSeek
                var translatedBatch = TranslateBatchWithDeepSeekAsync(values, progress).GetAwaiter().GetResult();
                values = translatedBatch;
                return;
            }

            foreach (var entry in values)
            {   
                if (string.IsNullOrEmpty(entry.Value))
                {
                    var translatedValue =TranslateValue(entry.Key).Result; 
                    if (progress != null) progress.Report(new Tuple<int, int>(SegmentIndex, nullSegments));
                    if (!string.IsNullOrEmpty(translatedValue))
                    {
                        Console.WriteLine($"[{SegmentIndex}/{nullSegments}] : [{Endpoint[0].ToString().ToUpper()}] : '{entry.Key}' => '{translatedValue.Escape()}'");
                        result.Add(entry.Key, translatedValue.Escape());
                    }
                    else
                        result.Add(entry.Key, entry.Value);

                    Thread.Sleep(delayBetweenMsg); SegmentIndex++;
                    //await Task.Delay(delayBetweenMsg);
                }
                else
                {
                    result.Add(entry.Key, entry.Value);
                }
            }
            values = result;
        }

        private async Task<Dictionary<string, string>> TranslateBatchWithDeepSeekAsync(Dictionary<string, string> values, IProgress<Tuple<int, int>> progress)
        {
            const string separator = "|||";
            const int maxSegmentsPerRequest = 150;
            var result = new Dictionary<string, string>(values);
            var toTranslate = values.Where(kvp => string.IsNullOrWhiteSpace(kvp.Value)).ToList();
            if (toTranslate.Count == 0) return result;

            int totalTranslated = 0;

            // Разбиваем список на батчи по 150 сегментов
            for (int i = 0; i < toTranslate.Count; i += maxSegmentsPerRequest)
            {
                var chunk = toTranslate.Skip(i).Take(maxSegmentsPerRequest).ToList();
                if (chunk.Count == 0) continue;

                // 1. Объединяем строки текущего батча в одну
                var combinedText = string.Join(separator, chunk.Select(kvp => kvp.Key.Replace(separator, "")));

                // 2. Формируем промпт для модели
                var systemPrompt = $"You are an expert language translator. Translate the following text from '{LanguageFrom}' to '{LanguageTo}'. " +
                    $"The texts are separated by '{separator}'. Maintain the exact same separation in your output. Keep the same punctuation. " +
                    $"Provide only the translated text in a literary style, without any additional explanations or context. ";

                var userPrompt = $"Please translate this:\n\n{combinedText}";
                var request = new OpenRouterRequest
                {
                    Model = $"{OpenRouterModel}",
                    Messages = new List<OpenRouterMessage>
                    {
                        new OpenRouterMessage { Role = "system", Content = systemPrompt },
                        new OpenRouterMessage { Role = "user", Content = userPrompt }
                    }
                };

                CLI.Console.StartProgress($"Translating batch {i / maxSegmentsPerRequest + 1} ({chunk.Count} segments) with OpenRouter...");
                var response = await OpenRouterClient.ChatAsync(request);
                CLI.Console.StopProgress();

                if (response != null && response.Choices.Any())
                {
                    var translatedCombinedText = response.Choices.First().Message?.Content;
                    string[] translatedSegments = translatedCombinedText.Split(new[] { separator }, StringSplitOptions.None);

                    if (translatedSegments.Length == chunk.Count)
                    {
                        // 3. Сопоставляем переводы с оригиналами
                        for (int j = 0; j < chunk.Count; j++)
                        {
                            var originalKey = chunk[j].Key;
                            var translatedValue = translatedSegments[j].Trim();
                            result[originalKey] = translatedValue;
                            totalTranslated++;
                            progress?.Report(new Tuple<int, int>(totalTranslated, toTranslate.Count));
                            CLI.Console.WriteLine($"[{totalTranslated}/{toTranslate.Count}] : [O] : '{originalKey.Escape()}' => '{translatedValue.Escape()}'");
                        }
                    }
                    else
                    {
                        CLI.Console.WriteLine($"[Red][Error] [White]OpenRouter returned a different number of segments ({translatedSegments.Length}) than expected ({chunk.Count}). Batch failed.");
                    }
                }
                else
                {
                    CLI.Console.WriteLine($"[Red][Error] [White]Failed to get a response from OpenRouter. Error: {OpenRouterClient.LastError}");
                }

                // Небольшая задержка между запросами, чтобы не превышать лимиты API
                if (i + maxSegmentsPerRequest < toTranslate.Count) await Task.Delay(3000);
            }
            return result;
        }

        public async Task<string> TranslateValue(string SourceText)
        {
            if (SourceText == "") return string.Empty;
            LanguageTo = LanguageTo.ToLower();
            var from = LanguageFrom.ToLower() != "auto" ? LanguageFrom : string.Empty;
            switch (Endpoint)
            {
                case "router":
                    if (OpenRouterClient == null) return "[Error: OpenRouter API key not configured]";
                    var request = new OpenRouterRequest
                    {
                        Model = $"{OpenRouterModel}",
                        Messages = new List<OpenRouterMessage>
                        {
                            new OpenRouterMessage { Role = "system", Content = $"Translate the following text from '{LanguageFrom}' to '{LanguageTo}'. Provide only the translated text." },
                            new OpenRouterMessage { Role = "user", Content = SourceText }
                        }
                    };
                    var response = await OpenRouterClient.ChatAsync(request);
                    return response?.Choices?.FirstOrDefault()?.Message?.Content.Trim() ?? string.Empty;

                case "google":
                    if (from == string.Empty)
                    {
                        var gResult = await GoogleV2.TranslateAsync(SourceText, LanguageTo);
                        return gResult.Translation.ToString();
                    }
                    else
                    {
                        var gResult = await GoogleV2.TranslateAsync(SourceText, LanguageTo, from);
                        return gResult.Translation;
                    }
                case "yandex":
                    if (from == string.Empty)
                    {
                        var yResult = await Yandex.TranslateAsync(SourceText, LanguageTo);
                        return yResult.Translation;
                    }
                    else
                    {
                        var yResult = await Yandex.TranslateAsync(SourceText, LanguageTo, from);
                        return yResult.Translation;
                    }
                case "microsoft":
                    if (from == string.Empty)
                    {
                        var mResult = await Microsoft.TranslateAsync(SourceText, LanguageTo);
                        return mResult.Translation;
                    }
                    else
                    {
                        var mResult = await Microsoft.TranslateAsync(SourceText, LanguageTo, from);
                        return mResult.Translation;
                    } 
                default:
                    return string.Empty;
            }
        }
    }
}
