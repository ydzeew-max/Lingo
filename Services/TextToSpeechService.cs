using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Speech.Synthesis;
using System.Threading.Tasks;

namespace Lingo.Services
{
    public class TextToSpeechService : IDisposable
    {
        private readonly SpeechSynthesizer _synthesizer;
        private string? _currentSpeakingTag;
        private bool _disposed;

        public event EventHandler<string?>? SpeechStarted;
        public event EventHandler<string?>? SpeechFinished;

        public bool IsSpeaking => _synthesizer.State == SynthesizerState.Speaking;
        public string? CurrentSpeakingTag => _currentSpeakingTag;

        private static readonly Dictionary<string, string> LangToCultureMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "ru", "ru-RU" },
            { "Русский", "ru-RU" },
            { "Russian", "ru-RU" },
            { "en", "en-US" },
            { "English", "en-US" },
            { "de", "de-DE" },
            { "Deutsch", "de-DE" },
            { "German", "de-DE" },
            { "fr", "fr-FR" },
            { "Français", "fr-FR" },
            { "French", "fr-FR" },
            { "es", "es-ES" },
            { "Español", "es-ES" },
            { "Spanish", "es-ES" },
            { "it", "it-IT" },
            { "Italiano", "it-IT" },
            { "Italian", "it-IT" },
            { "zh", "zh-CN" },
            { "中文", "zh-CN" },
            { "Chinese", "zh-CN" },
            { "ja", "ja-JP" },
            { "日本語", "ja-JP" },
            { "Japanese", "ja-JP" },
            { "ko", "ko-KR" },
            { "한국어", "ko-KR" },
            { "Korean", "ko-KR" },
            { "pt", "pt-PT" },
            { "Português", "pt-PT" },
            { "Portuguese", "pt-PT" },
            { "tr", "tr-TR" },
            { "Türkçe", "tr-TR" },
            { "Turkish", "tr-TR" },
            { "pl", "pl-PL" },
            { "Polski", "pl-PL" },
            { "Polish", "pl-PL" },
            { "uk", "uk-UA" },
            { "Українська", "uk-UA" },
            { "Ukrainian", "uk-UA" },
            { "nl", "nl-NL" },
            { "Nederlands", "nl-NL" },
            { "Dutch", "nl-NL" }
        };

        public TextToSpeechService()
        {
            _synthesizer = new SpeechSynthesizer();
            _synthesizer.Rate = 0; // Normal natural rate
            _synthesizer.Volume = 100;

            _synthesizer.SpeakCompleted += (s, e) =>
            {
                string? prevTag = _currentSpeakingTag;
                _currentSpeakingTag = null;
                SpeechFinished?.Invoke(this, prevTag);
            };
        }

        public void ToggleSpeak(string text, string languageNameOrCode, string tag = "")
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            // If already speaking this exact element, stop it (toggle)
            if (IsSpeaking && _currentSpeakingTag == tag)
            {
                Stop();
                return;
            }

            // Stop previous speech if active
            Stop();

            SelectBestVoice(languageNameOrCode);

            _currentSpeakingTag = tag;
            SpeechStarted?.Invoke(this, tag);

            try
            {
                _synthesizer.SpeakAsync(text);
            }
            catch
            {
                _currentSpeakingTag = null;
                SpeechFinished?.Invoke(this, tag);
            }
        }

        public void Stop()
        {
            try
            {
                if (IsSpeaking)
                {
                    _synthesizer.SpeakAsyncCancelAll();
                }
            }
            catch { }

            string? prevTag = _currentSpeakingTag;
            _currentSpeakingTag = null;
            SpeechFinished?.Invoke(this, prevTag);
        }

        private void SelectBestVoice(string langNameOrCode)
        {
            string culture = "ru-RU";
            if (!string.IsNullOrWhiteSpace(langNameOrCode))
            {
                string clean = CleanLanguageName(langNameOrCode);
                if (LangToCultureMap.TryGetValue(clean, out var mapped))
                {
                    culture = mapped;
                }
                else if (LangToCultureMap.TryGetValue(langNameOrCode.Trim(), out var direct))
                {
                    culture = direct;
                }
            }

            string twoLetter = culture.Split('-')[0].ToLowerInvariant();

            try
            {
                var installedVoices = _synthesizer.GetInstalledVoices()
                    .Where(v => v.Enabled)
                    .Select(v => v.VoiceInfo)
                    .ToList();

                // 1. Exact culture match (e.g. ru-RU, en-US)
                var exact = installedVoices.FirstOrDefault(v => v.Culture.Name.Equals(culture, StringComparison.OrdinalIgnoreCase));
                if (exact != null)
                {
                    _synthesizer.SelectVoice(exact.Name);
                    return;
                }

                // 2. Language two-letter prefix match (e.g. ru, en)
                var prefixMatch = installedVoices.FirstOrDefault(v => v.Culture.TwoLetterISOLanguageName.Equals(twoLetter, StringComparison.OrdinalIgnoreCase));
                if (prefixMatch != null)
                {
                    _synthesizer.SelectVoice(prefixMatch.Name);
                    return;
                }

                // 3. Name contains language hint
                var nameMatch = installedVoices.FirstOrDefault(v => v.Name.IndexOf(twoLetter, StringComparison.OrdinalIgnoreCase) >= 0);
                if (nameMatch != null)
                {
                    _synthesizer.SelectVoice(nameMatch.Name);
                    return;
                }
            }
            catch { }
        }

        private static string CleanLanguageName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            int idx = name.IndexOf(" (");
            if (idx > 0) return name.Substring(0, idx).Trim();
            idx = name.IndexOf(" [");
            if (idx > 0) return name.Substring(0, idx).Trim();
            return name.Trim();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Stop();
                _synthesizer.Dispose();
            }
        }
    }
}
