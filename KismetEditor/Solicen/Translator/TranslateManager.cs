using GTranslate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Solicen.Translator
{
    internal class TranslateManager
    {
        private GTranslate.Translators.GoogleTranslator2 GoogleV2 = new GTranslate.Translators.GoogleTranslator2();
        private GTranslate.Translators.MicrosoftTranslator Microsoft = new GTranslate.Translators.MicrosoftTranslator();
        private GTranslate.Translators.YandexTranslator Yandex = new GTranslate.Translators.YandexTranslator();
        private GTranslate.Translators.BingTranslator Bing = new GTranslate.Translators.BingTranslator();

        public static string LanguageTo = "ru";
        public static string LanguageFrom = "en";
        public static string Endpoint = "Yandex";

        public async void TranslateLines(Dictionary<string, string> values, IProgress<Tuple<int, int>> progress = null, bool showWaringMsg = false, int delayBetweenMsg = 50)
        {
            int SegmentIndex = 1;
            int nullSegments = values.Where(s => string.IsNullOrWhiteSpace(s.Value)).ToArray().Length;
            Console.WriteLine($"[Translator] : Prepare to change : [{nullSegments}]");
            foreach (var value in values)
            {
                Console.WriteLine($"[Translator] : [{SegmentIndex}/{nullSegments}] : [{Endpoint[0].ToString().ToUpper()}] : {value.Key}");
                if (string.IsNullOrWhiteSpace(value.Value))
                {
                    var translated = await TranslateValue(value.Key);
                    SegmentIndex++;
                    if (progress != null) progress.Report(new Tuple<int, int>(SegmentIndex, nullSegments));
                    await Task.Delay(delayBetweenMsg);
                }
            }
        }

        public async Task<string> TranslateValue(string SourceText)
        {
            if (SourceText == "") return string.Empty;
            LanguageTo = LanguageTo.ToLower();
            var from = LanguageFrom.ToLower() != "auto" ? LanguageFrom : string.Empty;
            switch (Endpoint)
            {
                case "Google":
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
                case "Yandex":
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
                case "Microsoft":
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

                case "Bing":
                    if (from == string.Empty)
                    {
                        var bResult = await Microsoft.TranslateAsync(SourceText, LanguageTo);
                        return bResult.Translation;
                    }
                    else
                    {
                        var bResult = await Microsoft.TranslateAsync(SourceText, LanguageTo, from);
                        return bResult.Translation;
                    }
                default:
                    return string.Empty;
            }
        }
    }
}
