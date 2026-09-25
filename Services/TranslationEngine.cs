using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Lingo.Services
{
    public class TranslationResult
    {
        public string Text { get; set; } = string.Empty;
        public bool IsOfflineFallback { get; set; }
        public string UsedEngine { get; set; } = "Google";
    }

    public class TranslationEngine
    {
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(6)
        };

        private static readonly string YandexSessionId = Guid.NewGuid().ToString("N");
        private readonly OfflineDictionaryService _offlineService = new();

        private static readonly Dictionary<string, string> LangCodeMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "English", "en" },
            { "Russian", "ru" },
            { "Russian (Русский)", "ru" },
            { "Русский", "ru" },
            { "German", "de" },
            { "German (Deutsch)", "de" },
            { "Deutsch", "de" },
            { "French", "fr" },
            { "French (Français)", "fr" },
            { "Français", "fr" },
            { "Spanish", "es" },
            { "Spanish (Español)", "es" },
            { "Español", "es" },
            { "Italian", "it" },
            { "Italian (Italiano)", "it" },
            { "Italiano", "it" },
            { "Chinese", "zh" },
            { "Chinese (中文)", "zh" },
            { "中文", "zh" },
            { "Japanese", "ja" },
            { "Japanese (日本語)", "ja" },
            { "日本語", "ja" },
            { "Korean", "ko" },
            { "Korean (한국어)", "ko" },
            { "한국어", "ko" },
            { "Portuguese", "pt" },
            { "Portuguese (Português)", "pt" },
            { "Português", "pt" },
            { "Turkish", "tr" },
            { "Turkish (Türkçe)", "tr" },
            { "Türkçe", "tr" },
            { "Arabic", "ar" },
            { "Arabic (العربية)", "ar" },
            { "العربية", "ar" },
            { "Polish", "pl" },
            { "Polski", "pl" },
            { "Ukrainian", "uk" },
            { "Українська", "uk" },
            { "Dutch", "nl" },
            { "Nederlands", "nl" }
        };

        static TranslationEngine()
        {
            if (!HttpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36"
                );
            }
        }

        public async Task<string> TranslateAsync(string text, string sourceLang, string targetLang, string? apiProvider = null, CancellationToken cancellationToken = default)
        {
            var res = await TranslateDetailedAsync(text, sourceLang, targetLang, apiProvider, cancellationToken);
            return res.Text;
        }

        public async Task<TranslationResult> TranslateDetailedAsync(string text, string sourceLang, string targetLang, string? apiProvider = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new TranslationResult();

            string srcCode = GetLangCode(sourceLang);
            string tgtCode = GetLangCode(targetLang);

            if (!string.IsNullOrEmpty(srcCode) && !string.IsNullOrEmpty(tgtCode) && srcCode.Equals(tgtCode, StringComparison.OrdinalIgnoreCase))
            {
                return new TranslationResult
                {
                    Text = text,
                    UsedEngine = "Original"
                };
            }

            apiProvider ??= App.Settings.CurrentSettings.TranslationApi ?? "Google";

            // 1. Direct Offline Mode
            if (apiProvider.Equals("offline", StringComparison.OrdinalIgnoreCase))
            {
                string offlineTr = _offlineService.Translate(text, srcCode, tgtCode);
                return new TranslationResult
                {
                    Text = offlineTr,
                    IsOfflineFallback = false,
                    UsedEngine = "Offline"
                };
            }

            // 2. Online Modes with Automatic Offline Fallback on network failure
            try
            {
                switch (apiProvider.ToLowerInvariant())
                {
                    case "yandex":
                    {
                        string yandexRes = await TranslateViaYandexAsync(text, srcCode, tgtCode, cancellationToken);
                        if (!string.IsNullOrWhiteSpace(yandexRes))
                        {
                            return new TranslationResult { Text = yandexRes, UsedEngine = "Yandex" };
                        }

                        string googleRes = await TranslateViaGoogleAsync(text, srcCode, tgtCode, cancellationToken);
                        if (!string.IsNullOrWhiteSpace(googleRes))
                        {
                            return new TranslationResult { Text = googleRes, UsedEngine = "Google" };
                        }
                        break;
                    }

                    case "auto":
                    case "google":
                    default:
                    {
                        string googleRes = await TranslateViaGoogleAsync(text, srcCode, tgtCode, cancellationToken);
                        if (!string.IsNullOrWhiteSpace(googleRes))
                        {
                            return new TranslationResult { Text = googleRes, UsedEngine = "Google" };
                        }

                        string yandexRes = await TranslateViaYandexAsync(text, srcCode, tgtCode, cancellationToken);
                        if (!string.IsNullOrWhiteSpace(yandexRes))
                        {
                            return new TranslationResult { Text = yandexRes, UsedEngine = "Yandex" };
                        }
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return new TranslationResult();
            }
            catch { }

            // 3. Network / API failed: seamless fallback to Offline Dictionary
            string fallbackTr = _offlineService.Translate(text, srcCode, tgtCode);
            return new TranslationResult
            {
                Text = fallbackTr,
                IsOfflineFallback = true,
                UsedEngine = "Offline"
            };
        }

        public async Task<string> TranslateViaGoogleAsync(string text, string srcCode, string tgtCode, CancellationToken ct = default)
        {
            try
            {
                string gSrc = srcCode == "zh" ? "zh-CN" : srcCode;
                string gTgt = tgtCode == "zh" ? "zh-CN" : tgtCode;

                string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={gSrc}&tl={gTgt}&dt=t&q={Uri.EscapeDataString(text)}";

                using var response = await HttpClient.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode)
                    return string.Empty;

                string json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                {
                    var sentences = root[0];
                    if (sentences.ValueKind == JsonValueKind.Array)
                    {
                        var sb = new System.Text.StringBuilder();
                        foreach (var item in sentences.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.Array && item.GetArrayLength() > 0)
                            {
                                var seg = item[0].GetString();
                                if (!string.IsNullOrEmpty(seg))
                                    sb.Append(seg);
                            }
                        }
                        return sb.ToString();
                    }
                }
            }
            catch { }

            return string.Empty;
        }

        public async Task<string> TranslateViaYandexAsync(string text, string srcCode, string tgtCode, CancellationToken ct = default)
        {
            try
            {
                string url = $"https://translate.yandex.net/api/v1/tr.json/translate?id={YandexSessionId}-0-0&srv=android";

                var formContent = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("text", text),
                    new KeyValuePair<string, string>("lang", $"{srcCode}-{tgtCode}")
                });

                using var response = await HttpClient.PostAsync(url, formContent, ct);
                if (!response.IsSuccessStatusCode)
                    return string.Empty;

                string json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("text", out var textArray) &&
                    textArray.ValueKind == JsonValueKind.Array &&
                    textArray.GetArrayLength() > 0)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var item in textArray.EnumerateArray())
                    {
                        string? seg = item.GetString();
                        if (!string.IsNullOrEmpty(seg))
                        {
                            if (sb.Length > 0) sb.AppendLine();
                            sb.Append(seg);
                        }
                    }
                    return sb.ToString();
                }
            }
            catch { }

            return string.Empty;
        }

        public string GetLangCode(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "en";

            int idx = name.IndexOf(" [");
            if (idx > 0)
                name = name.Substring(0, idx);

            idx = name.IndexOf(" (");
            if (idx > 0)
            {
                string shortName = name.Substring(0, idx).Trim();
                if (LangCodeMap.TryGetValue(shortName, out var shortCode))
                    return shortCode;
            }

            if (LangCodeMap.TryGetValue(name.Trim(), out var code))
                return code;

            return "en";
        }
    }
}
