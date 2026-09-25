using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lingo.Services
{
    public class OfflineDictionaryService
    {
        private static readonly Dictionary<string, Dictionary<string, string>> CachedDictionaries = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Dictionary<(char, int), List<string>>> CachedBuckets = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object LockObj = new();
        private static bool _isInitialized = false;

        // Keyboard layout mappings for QWERTY <-> ЙЦУКЕН (Russian/Ukrainian/English typos)
        private static readonly Dictionary<char, char> EnToRuLayout = new();
        private static readonly Dictionary<char, char> RuToEnLayout = new();

        static OfflineDictionaryService()
        {
            string enChars = "`qwertyuiop[]asdfghjkl;'zxcvbnm,./~QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>?";
            string ruChars = "ёйцукенгшщзхъфывапролджэячсмитьбю.ЁЙЦУКЕНГШЩЗХЪФЫВАПРОЛДЖЭЯЧСМИТЬБЮ,";

            for (int i = 0; i < Math.Min(enChars.Length, ruChars.Length); i++)
            {
                EnToRuLayout[enChars[i]] = ruChars[i];
                RuToEnLayout[ruChars[i]] = enChars[i];
            }
        }

        public OfflineDictionaryService()
        {
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            if (_isInitialized) return;
            lock (LockObj)
            {
                if (_isInitialized) return;
                LoadAllLocalDictionaries();
                _isInitialized = true;
            }
        }

        private static List<string> GetDictionaryDirectories()
        {
            var list = new List<string>();

            // 1. Next to the .exe (Bin/Dictionaries)
            string appDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dictionaries");
            if (Directory.Exists(appDir))
                list.Add(appDir);

            // 2. Base directory direct (in case placed alongside .exe directly)
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (Directory.Exists(baseDir) && !list.Contains(baseDir))
                list.Add(baseDir);

            // 3. Current working directory / Dictionaries
            string currentDir = Path.Combine(Directory.GetCurrentDirectory(), "Dictionaries");
            if (Directory.Exists(currentDir) && !list.Contains(currentDir))
                list.Add(currentDir);

            return list;
        }

        private void LoadAllLocalDictionaries()
        {
            // 1. Embedded dictionaries inside the single-file executable
            LoadEmbeddedDictionaries();

            // 2. External dictionaries from disk (if present)
            foreach (var dir in GetDictionaryDirectories())
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;

                    foreach (var file in Directory.GetFiles(dir, "*.json"))
                    {
                        string fileName = Path.GetFileNameWithoutExtension(file);
                        if (fileName.Equals("words_raw", StringComparison.OrdinalIgnoreCase) ||
                            fileName.Equals("words", StringComparison.OrdinalIgnoreCase) ||
                            fileName.Equals("appsettings", StringComparison.OrdinalIgnoreCase))
                            continue;

                        LoadSingleDictionaryFile(file, fileName);
                    }
                }
                catch { }
            }
        }

        private static void LoadEmbeddedDictionaries()
        {
            try
            {
                var asm = typeof(OfflineDictionaryService).Assembly;
                foreach (var resourceName in asm.GetManifestResourceNames())
                {
                    if (resourceName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                        resourceName.Contains("Dictionaries."))
                    {
                        int dictIdx = resourceName.IndexOf("Dictionaries.");
                        string pairWithExt = resourceName.Substring(dictIdx + "Dictionaries.".Length);
                        string pairKey = Path.GetFileNameWithoutExtension(pairWithExt);

                        if (pairKey.Equals("words_raw", StringComparison.OrdinalIgnoreCase) ||
                            pairKey.Equals("words", StringComparison.OrdinalIgnoreCase) ||
                            pairKey.Equals("appsettings", StringComparison.OrdinalIgnoreCase))
                            continue;

                        using var stream = asm.GetManifestResourceStream(resourceName);
                        if (stream != null)
                        {
                            using var reader = new StreamReader(stream, Encoding.UTF8);
                            string json = reader.ReadToEnd();
                            var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                            if (loaded != null)
                            {
                                if (!CachedDictionaries.TryGetValue(pairKey, out var existing))
                                {
                                    existing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                                    CachedDictionaries[pairKey] = existing;
                                }

                                foreach (var kvp in loaded)
                                {
                                    string k = kvp.Key.Trim();
                                    string v = kvp.Value.Trim();
                                    if (!string.IsNullOrEmpty(k) && !string.IsNullOrEmpty(v))
                                    {
                                        existing[k] = v;
                                    }
                                }

                                BuildBucketsForDict(pairKey, existing);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static void LoadSingleDictionaryFile(string filePath, string pairKey)
        {
            try
            {
                string json = File.ReadAllText(filePath, Encoding.UTF8);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (loaded != null)
                {
                    if (!CachedDictionaries.TryGetValue(pairKey, out var existing))
                    {
                        existing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        CachedDictionaries[pairKey] = existing;
                    }

                    foreach (var kvp in loaded)
                    {
                        string k = kvp.Key.Trim();
                        string v = kvp.Value.Trim();
                        if (!string.IsNullOrEmpty(k) && !string.IsNullOrEmpty(v))
                        {
                            existing[k] = v;
                        }
                    }

                    BuildBucketsForDict(pairKey, existing);
                }
            }
            catch { }
        }

        private static void BuildBucketsForDict(string pairKey, Dictionary<string, string> dict)
        {
            var buckets = new Dictionary<(char, int), List<string>>();
            foreach (var key in dict.Keys)
            {
                if (string.IsNullOrEmpty(key) || key.Contains(' '))
                    continue;

                char firstChar = char.ToLowerInvariant(key[0]);
                int len = key.Length;
                var bucketKey = (firstChar, len);

                if (!buckets.TryGetValue(bucketKey, out var list))
                {
                    list = new List<string>();
                    buckets[bucketKey] = list;
                }
                list.Add(key);
            }

            CachedBuckets[pairKey] = buckets;
        }

        public string Translate(string text, string srcCode, string tgtCode)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            EnsureInitialized();

            srcCode = NormalizeCode(srcCode);
            tgtCode = NormalizeCode(tgtCode);

            if (srcCode.Equals(tgtCode, StringComparison.OrdinalIgnoreCase))
                return text;

            string pairKey = $"{srcCode}_{tgtCode}";
            var dict = GetOrLoadDictionary(srcCode, tgtCode);

            // If direct dictionary exists and has entries
            if (dict.Count > 0)
            {
                string translated = TranslateWithDict(text, pairKey, dict);
                if (!string.IsNullOrWhiteSpace(translated))
                    return translated;
            }

            // Universal Pivot Translation (via English or Russian)
            // e.g. German -> Japanese translates de -> en -> ja
            if (!srcCode.Equals("en", StringComparison.OrdinalIgnoreCase) && !tgtCode.Equals("en", StringComparison.OrdinalIgnoreCase))
            {
                string intermediateEn = Translate(text, srcCode, "en");
                if (!string.IsNullOrWhiteSpace(intermediateEn))
                {
                    string finalResult = Translate(intermediateEn, "en", tgtCode);
                    if (!string.IsNullOrWhiteSpace(finalResult))
                        return finalResult;
                }
            }

            if (!srcCode.Equals("ru", StringComparison.OrdinalIgnoreCase) && !tgtCode.Equals("ru", StringComparison.OrdinalIgnoreCase))
            {
                string intermediateRu = Translate(text, srcCode, "ru");
                if (!string.IsNullOrWhiteSpace(intermediateRu))
                {
                    string finalResult = Translate(intermediateRu, "ru", tgtCode);
                    if (!string.IsNullOrWhiteSpace(finalResult))
                        return finalResult;
                }
            }

            return text;
        }

        private string TranslateWithDict(string text, string pairKey, Dictionary<string, string> dict)
        {
            string trimmed = text.Trim();

            // 1. Direct full-text match O(1)
            if (dict.TryGetValue(trimmed, out var fullMatch))
            {
                return MatchCasing(text, fullMatch);
            }

            // 2. Keyboard layout converted full match (e.g. "ghbdtn" -> "привет")
            string layoutFixed = ConvertKeyboardLayout(trimmed);
            if (!layoutFixed.Equals(trimmed, StringComparison.OrdinalIgnoreCase) && dict.TryGetValue(layoutFixed, out var layoutMatch))
            {
                return MatchCasing(text, layoutMatch);
            }

            // 3. Full-text fuzzy / typo match (only for short queries)
            if (trimmed.Length <= 24)
            {
                string? fuzzyFull = FindFastFuzzyMatch(trimmed.ToLowerInvariant(), pairKey, dict, maxDistance: 2);
                if (fuzzyFull != null && dict.TryGetValue(fuzzyFull, out var fuzzyFullVal))
                {
                    return MatchCasing(text, fuzzyFullVal);
                }
            }

            // 4. Smart Greedy N-Gram Multi-word Phrase Matcher
            return TranslateWithGreedyPhraseMatcher(text, pairKey, dict);
        }

        private string TranslateWithGreedyPhraseMatcher(string text, string pairKey, Dictionary<string, string> dict)
        {
            // Tokenize by word groups and non-word separators (including unicode alphabets)
            var tokenMatches = Regex.Matches(text, @"[\p{L}\p{M}\p{N}]+|[^\p{L}\p{M}\p{N}]+");

            var tokens = new List<(string text, bool isWord)>();
            foreach (Match m in tokenMatches)
            {
                bool isWord = Regex.IsMatch(m.Value, @"^[\p{L}\p{M}\p{N}]+$");
                tokens.Add((m.Value, isWord));
            }

            var result = new StringBuilder();
            int i = 0;

            while (i < tokens.Count)
            {
                if (!tokens[i].isWord)
                {
                    result.Append(tokens[i].text);
                    i++;
                    continue;
                }

                bool matchedPhrase = false;
                for (int phraseLen = 5; phraseLen >= 2; phraseLen--)
                {
                    var wordIndices = new List<int>();
                    var phraseWords = new List<string>();

                    for (int j = i; j < tokens.Count && wordIndices.Count < phraseLen; j++)
                    {
                        if (tokens[j].isWord)
                        {
                            wordIndices.Add(j);
                            phraseWords.Add(tokens[j].text.ToLowerInvariant());
                        }
                    }

                    if (wordIndices.Count == phraseLen)
                    {
                        string phrase = string.Join(" ", phraseWords);

                        // O(1) exact phrase match
                        if (dict.TryGetValue(phrase, out var phraseTranslation))
                        {
                            string origPhraseFirst = tokens[i].text;
                            result.Append(MatchCasing(origPhraseFirst, phraseTranslation));
                            i = wordIndices[wordIndices.Count - 1] + 1;
                            matchedPhrase = true;
                            break;
                        }

                        // Try layout converted phrase (e.g. "rfr ltkf" -> "как дела")
                        string layoutPhrase = ConvertKeyboardLayout(phrase);
                        if (!layoutPhrase.Equals(phrase, StringComparison.OrdinalIgnoreCase) && dict.TryGetValue(layoutPhrase, out var layoutPhraseTr))
                        {
                            string origPhraseFirst = tokens[i].text;
                            result.Append(MatchCasing(origPhraseFirst, layoutPhraseTr));
                            i = wordIndices[wordIndices.Count - 1] + 1;
                            matchedPhrase = true;
                            break;
                        }
                    }
                }

                if (matchedPhrase)
                    continue;

                // Single word translation
                string currentWord = tokens[i].text;
                string translatedWord = TranslateSingleWord(currentWord, pairKey, dict);
                result.Append(translatedWord);
                i++;
            }

            return result.ToString();
        }

        private string TranslateSingleWord(string word, string pairKey, Dictionary<string, string> dict)
        {
            string lower = word.ToLowerInvariant();

            // 1. Exact match O(1)
            if (dict.TryGetValue(lower, out var exact))
            {
                return MatchCasing(word, exact);
            }

            // 2. Keyboard layout converted match (e.g. "ghbdtn" -> "привет" -> "hello")
            string layoutFixed = ConvertKeyboardLayout(lower);
            if (!layoutFixed.Equals(lower, StringComparison.OrdinalIgnoreCase))
            {
                if (dict.TryGetValue(layoutFixed, out var layoutVal))
                    return MatchCasing(word, layoutVal);

                string? layoutStem = TryStemmedMatch(layoutFixed, dict);
                if (layoutStem != null)
                    return MatchCasing(word, layoutStem);
            }

            // 3. Fast Stemming match (Russian, English, Ukrainian, German, French, etc.)
            string? stemmedMatch = TryStemmedMatch(lower, dict);
            if (stemmedMatch != null)
            {
                return MatchCasing(word, stemmedMatch);
            }

            // 4. Accent/Diacritics normalized match (e.g. "espanol" -> "español", "uber" -> "über")
            string normalizedDiacritics = RemoveDiacritics(lower);
            if (!normalizedDiacritics.Equals(lower, StringComparison.OrdinalIgnoreCase))
            {
                if (dict.TryGetValue(normalizedDiacritics, out var diaVal))
                    return MatchCasing(word, diaVal);
            }

            // 5. Ultra-Fast Indexed Damerau-Levenshtein Typo / T9 Search (< 0.01 ms)
            if (lower.Length >= 3 && lower.Length <= 24)
            {
                int maxDist = lower.Length <= 4 ? 1 : 2;
                string? fuzzyWord = FindFastFuzzyMatch(lower, pairKey, dict, maxDist);
                if (fuzzyWord != null && dict.TryGetValue(fuzzyWord, out var fuzzyVal))
                {
                    return MatchCasing(word, fuzzyVal);
                }
            }

            // 6. Cross-Pivot lookup for single word if not in direct dictionary
            string[] parts = pairKey.Split('_');
            if (parts.Length == 2)
            {
                string src = parts[0];
                string tgt = parts[1];
                if (!src.Equals("en", StringComparison.OrdinalIgnoreCase) && !tgt.Equals("en", StringComparison.OrdinalIgnoreCase))
                {
                    var srcEn = GetOrLoadDictionary(src, "en");
                    var enTgt = GetOrLoadDictionary("en", tgt);
                    if (srcEn.TryGetValue(lower, out var enMid) && enTgt.TryGetValue(enMid.ToLowerInvariant(), out var finalTgt))
                    {
                        return MatchCasing(word, finalTgt);
                    }
                }
                if (!src.Equals("ru", StringComparison.OrdinalIgnoreCase) && !tgt.Equals("ru", StringComparison.OrdinalIgnoreCase))
                {
                    var srcRu = GetOrLoadDictionary(src, "ru");
                    var ruTgt = GetOrLoadDictionary("ru", tgt);
                    if (srcRu.TryGetValue(lower, out var ruMid) && ruTgt.TryGetValue(ruMid.ToLowerInvariant(), out var finalTgt))
                    {
                        return MatchCasing(word, finalTgt);
                    }
                }
            }

            return word;
        }

        private static string ConvertKeyboardLayout(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var sb = new StringBuilder(text.Length);
            bool convertedAny = false;

            foreach (char c in text)
            {
                if (EnToRuLayout.TryGetValue(c, out var ruChar))
                {
                    sb.Append(ruChar);
                    convertedAny = true;
                }
                else if (RuToEnLayout.TryGetValue(c, out var enChar))
                {
                    sb.Append(enChar);
                    convertedAny = true;
                }
                else
                {
                    sb.Append(c);
                }
            }

            return convertedAny ? sb.ToString() : text;
        }

        private static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder(normalizedString.Length);

            foreach (var c in normalizedString)
            {
                var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }

        private string? TryStemmedMatch(string word, Dictionary<string, string> dict)
        {
            string[] endings = {
                // English
                "ing", "ed", "es", "ly", "er", "est", "s",
                // Russian / Ukrainian
                "ами", "ями", "ов", "ев", "ом", "ем", "ой", "ей", "ам", "ям", "ах", "ях",
                "ешь", "ете", "ем", "ут", "ют", "ат", "ят", "ить", "еть", "ать", "ять",
                "ал", "ила", "или", "ала", "ела", "ели", "ся", "сь",
                "ы", "и", "а", "я", "у", "ю", "е", "о", "ого", "его", "ому", "ему", "ых", "их", "ую", "юю",
                // German
                "ungen", "ung", "lich", "keit", "heit", "isch", "end", "en", "er", "es", "te",
                // French / Spanish / Italian / Portuguese
                "ment", "ando", "endo", "ción", "tion", "mente", "ados", "adas", "ades", "ant"
            };

            foreach (var end in endings)
            {
                if (word.EndsWith(end, StringComparison.OrdinalIgnoreCase) && word.Length - end.Length >= 3)
                {
                    string root = word.Substring(0, word.Length - end.Length);
                    if (dict.TryGetValue(root, out var val))
                        return val;
                }
            }
            return null;
        }

        private string? FindFastFuzzyMatch(string query, string pairKey, Dictionary<string, string> dict, int maxDistance)
        {
            if (string.IsNullOrEmpty(query)) return null;

            if (!CachedBuckets.TryGetValue(pairKey, out var buckets))
                return null;

            char firstChar = query[0];
            int qLen = query.Length;

            string? bestMatch = null;
            int minDistance = int.MaxValue;

            // Search words in length buckets: qLen - 1, qLen, qLen + 1
            int[] lens = { qLen - 1, qLen, qLen + 1 };

            foreach (var l in lens)
            {
                if (l < 1) continue;

                // Candidate bucket matching first char
                if (buckets.TryGetValue((firstChar, l), out var list))
                {
                    foreach (var candidate in list)
                    {
                        int dist = CalculateDamerauLevenshtein(query, candidate);
                        if (dist <= maxDistance && dist < minDistance)
                        {
                            minDistance = dist;
                            bestMatch = candidate;
                            if (dist == 1) return bestMatch; // Instant early exit on 1-typo
                        }
                    }
                }

                // If query has transposed first 2 characters (e.g. "ипрвет" -> "привет")
                if (query.Length >= 2 && query[0] != query[1])
                {
                    char secondChar = query[1];
                    if (buckets.TryGetValue((secondChar, l), out var altList))
                    {
                        foreach (var candidate in altList)
                        {
                            int dist = CalculateDamerauLevenshtein(query, candidate);
                            if (dist <= maxDistance && dist < minDistance)
                            {
                                minDistance = dist;
                                bestMatch = candidate;
                                if (dist == 1) return bestMatch;
                            }
                        }
                    }
                }
            }

            return bestMatch;
        }

        public static int CalculateDamerauLevenshtein(string s, string t)
        {
            if (string.IsNullOrEmpty(s)) return string.IsNullOrEmpty(t) ? 0 : t.Length;
            if (string.IsNullOrEmpty(t)) return s.Length;

            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;

                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost
                    );

                    if (i > 1 && j > 1 && s[i - 1] == t[j - 2] && s[i - 2] == t[j - 1])
                    {
                        d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + cost);
                    }
                }
            }

            return d[n, m];
        }

        private static string MatchCasing(string original, string translation)
        {
            if (string.IsNullOrEmpty(original) || string.IsNullOrEmpty(translation))
                return translation;

            // All uppercase (e.g. "HELLO" -> "ПРИВЕТ")
            if (original.Length > 1 && original.Equals(original.ToUpperInvariant(), StringComparison.Ordinal))
            {
                return translation.ToUpperInvariant();
            }

            // Title case (e.g. "Hello" -> "Привет")
            if (char.IsUpper(original[0]))
            {
                if (translation.Length == 1)
                    return translation.ToUpperInvariant();
                return char.ToUpperInvariant(translation[0]) + translation.Substring(1);
            }

            return translation;
        }

        private static string NormalizeCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "en";
            string clean = code.ToLowerInvariant().Trim();
            if (clean.Contains('-')) clean = clean.Split('-')[0];
            if (clean.Contains('_')) clean = clean.Split('_')[0];
            return clean;
        }

        private Dictionary<string, string> GetOrLoadDictionary(string src, string tgt)
        {
            string pairKey = $"{src}_{tgt}";
            if (CachedDictionaries.TryGetValue(pairKey, out var cached))
                return cached;

            lock (LockObj)
            {
                if (CachedDictionaries.TryGetValue(pairKey, out var again))
                    return again;

                foreach (var dir in GetDictionaryDirectories())
                {
                    string targetFile = Path.Combine(dir, $"{pairKey}.json");
                    if (File.Exists(targetFile))
                    {
                        LoadSingleDictionaryFile(targetFile, pairKey);
                        if (CachedDictionaries.TryGetValue(pairKey, out var newlyLoaded))
                            return newlyLoaded;
                    }
                }

                var empty = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                CachedDictionaries[pairKey] = empty;
                return empty;
            }
        }
    }
}

