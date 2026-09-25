using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using WpfColor = System.Windows.Media.Color;

namespace Lingo.Services
{
    public class OcrTextBlock
    {
        public string OriginalText { get; set; } = string.Empty;
        public string TranslatedText { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public WpfColor BackgroundColor { get; set; } = WpfColor.FromRgb(24, 24, 24);
        public WpfColor TextColor { get; set; } = WpfColor.FromRgb(245, 245, 245);
        public double FontSize { get; set; } = 13.0;
        public int LineCount { get; set; } = 1;
    }

    public class OcrVisualResult
    {
        public List<OcrTextBlock> Blocks { get; set; } = new();
        public Bitmap OriginalBitmap { get; set; } = null!;
        public string FullText { get; set; } = string.Empty;
        public string DetectedLanguage { get; set; } = "Русский";
        public string DetectedLanguageCode { get; set; } = "ru";
    }

    public class OcrService
    {
        private static readonly Dictionary<string, string> LanguageTagMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Автоопределение", "auto" },
            { "Auto", "auto" },
            { "Русский", "ru-RU" },
            { "Russian", "ru-RU" },
            { "Russian (Русский)", "ru-RU" },
            { "English", "en-US" },
            { "Deutsch (Немецкий)", "de-DE" },
            { "Deutsch", "de-DE" },
            { "German", "de-DE" },
            { "German (Deutsch)", "de-DE" },
            { "Français (Французский)", "fr-FR" },
            { "Français", "fr-FR" },
            { "French", "fr-FR" },
            { "French (Français)", "fr-FR" },
            { "Español (Испанский)", "es-ES" },
            { "Español", "es-ES" },
            { "Spanish", "es-ES" },
            { "Spanish (Español)", "es-ES" },
            { "Italiano (Итальянский)", "it-IT" },
            { "Italiano", "it-IT" },
            { "Italian", "it-IT" },
            { "Italian (Italiano)", "it-IT" },
            { "中文 (Китайский)", "zh-Hans-CN" },
            { "中文", "zh-Hans-CN" },
            { "Chinese", "zh-Hans-CN" },
            { "日本語 (Японский)", "ja-JP" },
            { "日本語", "ja-JP" },
            { "Japanese", "ja-JP" },
            { "한국어 (Корейский)", "ko-KR" },
            { "한국어", "ko-KR" },
            { "Korean", "ko-KR" },
            { "Português (Португальский)", "pt-PT" },
            { "Português", "pt-PT" },
            { "Portuguese", "pt-PT" },
            { "Türkçe (Турецкий)", "tr-TR" },
            { "Türkçe", "tr-TR" },
            { "Turkish", "tr-TR" },
            { "العربية (Арабский)", "ar-SA" },
            { "العربية", "ar-SA" },
            { "Arabic", "ar-SA" },
            { "Polski (Польский)", "pl-PL" },
            { "Polski", "pl-PL" },
            { "Polish", "pl-PL" },
            { "Українська (Украинский)", "uk-UA" },
            { "Українська", "uk-UA" },
            { "Ukrainian", "uk-UA" },
            { "Nederlands (Нидерландский)", "nl-NL" },
            { "Nederlands", "nl-NL" },
            { "Dutch", "nl-NL" }
        };

        public async Task<OcrVisualResult> RecognizeVisualBlocksAsync(Bitmap sourceBitmap, string? preferredLang = null)
        {
            var result = new OcrVisualResult
            {
                OriginalBitmap = (Bitmap)sourceBitmap.Clone()
            };

            if (sourceBitmap == null || sourceBitmap.Width < 2 || sourceBitmap.Height < 2)
                return result;

            try
            {
                // Standardize and normalize resolution for optimal OCR text recognition
                double scale = 1.0;
                Bitmap processBitmap;

                double maxDim = Math.Max(sourceBitmap.Width, sourceBitmap.Height);
                double minDim = Math.Min(sourceBitmap.Width, sourceBitmap.Height);

                if (minDim < 500 || maxDim < 1400)
                {
                    scale = Math.Min(3.0, Math.Max(1.5, 1600.0 / maxDim));
                }

                int targetW = (int)Math.Round(sourceBitmap.Width * scale);
                int targetH = (int)Math.Round(sourceBitmap.Height * scale);

                processBitmap = new Bitmap(targetW, targetH, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(processBitmap))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.CompositingQuality = CompositingQuality.HighQuality;

                    // Clean contrast boost (+24%)
                    using var attr = new ImageAttributes();
                    float c = 1.24f;
                    float t = (1.0f - c) / 2.0f;
                    attr.SetColorMatrix(new ColorMatrix(new float[][]
                    {
                        new float[] {c, 0, 0, 0, 0},
                        new float[] {0, c, 0, 0, 0},
                        new float[] {0, 0, c, 0, 0},
                        new float[] {0, 0, 0, 1, 0},
                        new float[] {t, t, t, 0, 1}
                    }));

                    g.DrawImage(sourceBitmap, new Rectangle(0, 0, targetW, targetH), 0, 0, sourceBitmap.Width, sourceBitmap.Height, GraphicsUnit.Pixel, attr);
                }

                // Recognize with smart language selection
                var (bestOcrResult, detectedLanguage, detectedCode) = await SmartAutoDetectAndRecognizeAsync(processBitmap, preferredLang);

                // Fallback Pass: Invert bitmap for dark mode screenshots if text count was poor
                if (bestOcrResult == null || bestOcrResult.Lines == null || bestOcrResult.Lines.Count == 0)
                {
                    using var inverted = InvertBitmap(processBitmap);
                    var (invResult, invLang, invCode) = await SmartAutoDetectAndRecognizeAsync(inverted, preferredLang);
                    if (invResult != null && invResult.Lines != null && invResult.Lines.Count > 0)
                    {
                        bestOcrResult = invResult;
                        detectedLanguage = invLang;
                        detectedCode = invCode;
                    }
                }

                processBitmap.Dispose();

                result.DetectedLanguage = detectedLanguage;
                result.DetectedLanguageCode = detectedCode;

                if (bestOcrResult == null || bestOcrResult.Lines == null || bestOcrResult.Lines.Count == 0)
                    return result;

                // 1. Extract raw line bounding boxes from OCR
                var rawLines = new List<RawOcrLine>();

                foreach (var line in bestOcrResult.Lines)
                {
                    string text = line.Text?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    double minX = double.MaxValue, minY = double.MaxValue;
                    double maxX = double.MinValue, maxY = double.MinValue;

                    if (line.Words != null && line.Words.Count > 0)
                    {
                        foreach (var word in line.Words)
                        {
                            var r = word.BoundingRect;
                            minX = Math.Min(minX, r.X);
                            minY = Math.Min(minY, r.Y);
                            maxX = Math.Max(maxX, r.X + r.Width);
                            maxY = Math.Max(maxY, r.Y + r.Height);
                        }
                    }
                    else
                    {
                        continue;
                    }

                    double origX = minX / scale;
                    double origY = minY / scale;
                    double origW = (maxX - minX) / scale;
                    double origH = (maxY - minY) / scale;

                    if (origW < 3 || origH < 3)
                        continue;

                    rawLines.Add(new RawOcrLine
                    {
                        Text = text,
                        X = origX,
                        Y = origY,
                        Width = origW,
                        Height = origH
                    });
                }

                // Calculate median text height among multi-character words
                var multiCharLines = rawLines.Where(l => l.Text.Length >= 3 && l.Text.Count(char.IsLetterOrDigit) >= 2).ToList();
                double medianTextH = multiCharLines.Count > 0 
                    ? multiCharLines.OrderBy(l => l.Height).ElementAt(multiCharLines.Count / 2).Height 
                    : 16.0;

                // 2. Strict Filtering of Graphic False Positives & Noise:
                // Prevents icons/logos (e.g. Blender logo detected as 'O' or 'О') from being recognized as text
                var validLines = new List<RawOcrLine>();
                foreach (var line in rawLines)
                {
                    int letterOrDigitCount = line.Text.Count(char.IsLetterOrDigit);

                    // A: Discard lines containing only punctuation or symbols without any letters or digits
                    if (letterOrDigitCount == 0)
                        continue;

                    // B: Filter graphic icons, logos, and badges misrecognized as single characters
                    if (letterOrDigitCount <= 1 || line.Text.Length <= 1)
                    {
                        char singleChar = line.Text.Trim()[0];
                        bool isCommonIconChar = singleChar is 'O' or 'О' or '0' or 'o' or 'C' or 'с' or 'Q' or 'D' or 'I' or 'l' or '|' or '®' or '©';

                        // Check square/circular aspect ratio
                        double aspect = line.Width / Math.Max(1.0, line.Height);
                        bool isSquareAspect = aspect >= 0.68 && aspect <= 1.45;

                        // Check if height is abnormally large compared to normal text lines
                        bool isAbnormallyTall = line.Height >= 20.0 || (multiCharLines.Count > 0 && line.Height > medianTextH * 1.45);

                        // If it's a square/circular graphic or abnormally tall icon char, discard it
                        if ((isSquareAspect && line.Height >= 14.0) || (isCommonIconChar && (isSquareAspect || isAbnormallyTall)))
                        {
                            continue;
                        }

                        // Isolated 1-char speck check: distance to any other text
                        double minDistance = double.MaxValue;
                        foreach (var other in rawLines)
                        {
                            if (ReferenceEquals(line, other)) continue;
                            if (other.Text.Length < 3 && other.Text.Count(char.IsLetterOrDigit) < 2) continue;

                            double dx = Math.Max(0, Math.Max(line.X - (other.X + other.Width), other.X - (line.X + line.Width)));
                            double dy = Math.Max(0, Math.Max(line.Y - (other.Y + other.Height), other.Y - (line.Y + line.Height)));
                            double dist = Math.Sqrt(dx * dx + dy * dy);
                            if (dist < minDistance) minDistance = dist;
                        }

                        if (minDistance > 45.0)
                            continue;
                    }

                    // C: Discard 2-char noise that is far from any real text block
                    if (line.Text.Length <= 2 && letterOrDigitCount <= 2)
                    {
                        double minDistance = double.MaxValue;
                        foreach (var other in rawLines)
                        {
                            if (ReferenceEquals(line, other)) continue;
                            if (other.Text.Length < 3 && other.Text.Count(char.IsLetterOrDigit) < 2) continue;

                            double dx = Math.Max(0, Math.Max(line.X - (other.X + other.Width), other.X - (line.X + line.Width)));
                            double dy = Math.Max(0, Math.Max(line.Y - (other.Y + other.Height), other.Y - (line.Y + line.Height)));
                            double dist = Math.Sqrt(dx * dx + dy * dy);
                            if (dist < minDistance) minDistance = dist;
                        }

                        if (minDistance > 45.0)
                            continue;
                    }

                    validLines.Add(line);
                }

                // 3. Line-by-Line Mapping (1 строка = 1 плашка):
                // Strictly preserves original line positions, indents, and bullet points!
                validLines.Sort((a, b) => a.Y.CompareTo(b.Y));

                var blocks = new List<OcrTextBlock>();
                var fullTextSb = new StringBuilder();

                foreach (var line in validLines)
                {
                    fullTextSb.AppendLine(line.Text);

                    // Padding to cleanly cover font completely without excessive vertical bleed
                    double padX = 2.0;
                    double padY = 0.5;

                    double origX = Math.Max(0, line.X - padX);
                    double origY = Math.Max(0, line.Y - padY);
                    double origW = Math.Min(sourceBitmap.Width - origX, line.Width + padX * 2);
                    double origH = Math.Min(sourceBitmap.Height - origY, line.Height + padY * 2);

                    blocks.Add(new OcrTextBlock
                    {
                        OriginalText = line.Text,
                        X = origX,
                        Y = origY,
                        Width = origW,
                        Height = origH,
                        FontSize = Math.Max(10.0, origH * 0.76),
                        LineCount = 1
                    });
                }

                result.FullText = fullTextSb.ToString().Trim();

                // 4. Prevent adjacent lines from colliding vertically
                blocks.Sort((a, b) => a.Y.CompareTo(b.Y));

                for (int i = 0; i < blocks.Count - 1; i++)
                {
                    var a = blocks[i];
                    var b = blocks[i + 1];

                    double overlapX = Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X));
                    if (overlapX > 4.0 && (a.Y + a.Height) > b.Y)
                    {
                        double splitY = (a.Y + a.Height + b.Y) / 2.0;
                        a.Height = Math.Max(4.0, splitY - a.Y);
                        double oldBottomB = b.Y + b.Height;
                        b.Y = splitY;
                        b.Height = Math.Max(4.0, oldBottomB - b.Y);
                    }
                }

                // 5. Estimate 100% opaque background color and contrasting text color for each line
                foreach (var block in blocks)
                {
                    var (bgColor, textColor) = EstimateBlockColors(
                        sourceBitmap, 
                        (int)block.X, 
                        (int)block.Y, 
                        (int)block.Width, 
                        (int)block.Height
                    );

                    block.BackgroundColor = bgColor;
                    block.TextColor = textColor;
                }

                result.Blocks = blocks;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Visual OCR error: {ex.Message}");
            }

            return result;
        }

        private async Task<(OcrResult? result, string langName, string langCode)> SmartAutoDetectAndRecognizeAsync(Bitmap bitmap, string? preferredLang)
        {
            // If user explicitly specified a language other than Auto, prioritize it directly!
            if (!string.IsNullOrWhiteSpace(preferredLang) && 
                !preferredLang.StartsWith("Авто", StringComparison.OrdinalIgnoreCase) && 
                !preferredLang.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                string prefTag = MapToLanguageTag(preferredLang);
                var directOcr = await RunOcrInternalAsync(bitmap, prefTag);
                if (directOcr != null && directOcr.Lines != null && directOcr.Lines.Count > 0)
                {
                    string code = prefTag.Split('-')[0];
                    return (directOcr, preferredLang, code);
                }
            }

            var candidateLanguages = new List<(string tag, string name, string code)>
            {
                ("ru-RU", "Русский", "ru"),
                ("en-US", "English", "en"),
                ("de-DE", "Deutsch (Немецкий)", "de"),
                ("fr-FR", "Français (Французский)", "fr"),
                ("es-ES", "Español (Испанский)", "es"),
                ("zh-Hans-CN", "中文 (Китайский)", "zh")
            };

            OcrResult? bestResult = null;
            double bestScore = -9999;
            string bestName = "Русский";
            string bestCode = "ru";

            foreach (var cand in candidateLanguages)
            {
                try
                {
                    var ocr = await RunOcrInternalAsync(bitmap, cand.tag);
                    if (ocr != null && ocr.Lines != null && ocr.Lines.Count > 0)
                    {
                        string allText = string.Join(" ", ocr.Lines.Select(l => l.Text ?? ""));
                        double score = EvaluateTextQuality(allText, cand.code);

                        if (!string.IsNullOrWhiteSpace(preferredLang) && 
                            (preferredLang.Equals(cand.name, StringComparison.OrdinalIgnoreCase) || preferredLang.Equals(cand.code, StringComparison.OrdinalIgnoreCase)))
                        {
                            score += 25;
                        }

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestResult = ocr;
                            bestName = cand.name;
                            bestCode = cand.code;
                        }
                    }
                }
                catch { }
            }

            if (bestResult == null)
            {
                var profileOcr = await RunOcrInternalAsync(bitmap, null);
                if (profileOcr != null && profileOcr.Lines != null && profileOcr.Lines.Count > 0)
                {
                    bestResult = profileOcr;
                }
            }

            return (bestResult, bestName, bestCode);
        }

        private static double EvaluateTextQuality(string text, string targetLangCode)
        {
            if (string.IsNullOrWhiteSpace(text))
                return -500;

            int cyrillicCount = 0;
            int latinCount = 0;
            int digitCount = 0;
            int symbolCount = 0;
            int wordsWithDigits = 0;

            string[] words = text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var word in words)
            {
                bool hasLetter = false;
                bool hasDigit = false;
                foreach (char c in word)
                {
                    if ((c >= 'а' && c <= 'я') || (c >= 'А' && c <= 'Я') || c == 'ё' || c == 'Ё')
                    {
                        cyrillicCount++;
                        hasLetter = true;
                    }
                    else if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
                    {
                        latinCount++;
                        hasLetter = true;
                    }
                    else if (char.IsDigit(c))
                    {
                        digitCount++;
                        hasDigit = true;
                    }
                    else if (!char.IsPunctuation(c))
                    {
                        symbolCount++;
                    }
                }

                if (hasLetter && hasDigit)
                {
                    wordsWithDigits++;
                }
            }

            int totalLetters = cyrillicCount + latinCount;
            if (totalLetters == 0)
                return -500;

            double score = (totalLetters * 12) - (symbolCount * 6) - (wordsWithDigits * 100) - (digitCount * 20);

            if (targetLangCode == "ru")
            {
                if (cyrillicCount > 0)
                    score += cyrillicCount * 30;
                if (latinCount > cyrillicCount)
                    score -= 200;
            }
            else if (targetLangCode == "en")
            {
                if (latinCount > 0)
                    score += latinCount * 20;
                if (cyrillicCount > 0)
                    score -= 200;
            }

            return score;
        }

        public async Task<string> RecognizeBitmapAsync(Bitmap bitmap, string? sourceLang = null)
        {
            var visual = await RecognizeVisualBlocksAsync(bitmap, sourceLang);
            return visual.FullText;
        }

        public async Task<string> RecognizeImageFileAsync(string filePath, string? sourceLang = null)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return string.Empty;

            try
            {
                using var bitmap = new Bitmap(filePath);
                return await RecognizeBitmapAsync(bitmap, sourceLang);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"File OCR error: {ex.Message}");
                return string.Empty;
            }
        }

        private static async Task<OcrResult?> RunOcrInternalAsync(Bitmap bitmap, string? sourceLang)
        {
            try
            {
                using var ms = new MemoryStream();
                bitmap.Save(ms, ImageFormat.Bmp);
                ms.Position = 0;

                using var randomAccessStream = ms.AsRandomAccessStream();
                var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
                var softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

                var engine = GetOcrEngine(sourceLang);
                if (engine == null)
                    return null;

                return await engine.RecognizeAsync(softwareBitmap);
            }
            catch
            {
                return null;
            }
        }

        private static Bitmap InvertBitmap(Bitmap original)
        {
            var inverted = new Bitmap(original.Width, original.Height);
            using (var g = Graphics.FromImage(inverted))
            {
                var colorMatrix = new ColorMatrix(new float[][]
                {
                    new float[] {-1, 0,  0,  0, 0},
                    new float[] { 0, -1, 0,  0, 0},
                    new float[] { 0,  0, -1, 0, 0},
                    new float[] { 0,  0,  0, 1, 0},
                    new float[] { 1,  1,  1, 0, 1}
                });

                using var attributes = new ImageAttributes();
                attributes.SetColorMatrix(colorMatrix);
                g.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height), 0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
            }
            return inverted;
        }

        private static (WpfColor bg, WpfColor text) EstimateBlockColors(Bitmap bmp, int x, int y, int w, int h)
        {
            try
            {
                x = Math.Max(0, Math.Min(bmp.Width - 1, x));
                y = Math.Max(0, Math.Min(bmp.Height - 1, y));
                w = Math.Max(1, Math.Min(bmp.Width - x, w));
                h = Math.Max(1, Math.Min(bmp.Height - y, h));

                long totalR = 0, totalG = 0, totalB = 0;
                int count = 0;

                // Sample just outside top and bottom edges (pure background)
                int sampleTopY = Math.Max(0, y - 1);
                int sampleBottomY = Math.Min(bmp.Height - 1, y + h);

                int stepX = Math.Max(1, w / 12);
                for (int px = x; px < x + w; px += stepX)
                {
                    var c1 = bmp.GetPixel(px, sampleTopY);
                    var c2 = bmp.GetPixel(px, sampleBottomY);
                    totalR += c1.R + c2.R;
                    totalG += c1.G + c2.G;
                    totalB += c1.B + c2.B;
                    count += 2;
                }

                // Sample just outside left and right edges
                int sampleLeftX = Math.Max(0, x - 1);
                int sampleRightX = Math.Min(bmp.Width - 1, x + w);
                int stepY = Math.Max(1, h / 6);
                for (int py = y; py < y + h; py += stepY)
                {
                    var c1 = bmp.GetPixel(sampleLeftX, py);
                    var c2 = bmp.GetPixel(sampleRightX, py);
                    totalR += c1.R + c2.R;
                    totalG += c1.G + c2.G;
                    totalB += c1.B + c2.B;
                    count += 2;
                }

                if (count > 0)
                {
                    byte avgR = (byte)(totalR / count);
                    byte avgG = (byte)(totalG / count);
                    byte avgB = (byte)(totalB / count);

                    double luminance = (0.299 * avgR + 0.587 * avgG + 0.114 * avgB) / 255.0;
                    WpfColor textColor = luminance > 0.52 ? WpfColor.FromRgb(15, 15, 15) : WpfColor.FromRgb(250, 250, 250);
                    // 100% opaque solid background color
                    WpfColor bgColor = WpfColor.FromRgb(avgR, avgG, avgB);

                    return (bgColor, textColor);
                }
            }
            catch { }

            return (WpfColor.FromRgb(24, 24, 24), WpfColor.FromRgb(250, 250, 250));
        }

        private static OcrEngine? GetOcrEngine(string? langName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(langName) || langName.Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    var prof = OcrEngine.TryCreateFromUserProfileLanguages();
                    if (prof != null) return prof;
                    var firstLang = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault();
                    if (firstLang != null) return OcrEngine.TryCreateFromLanguage(firstLang);
                    return null;
                }

                string tag = MapToLanguageTag(langName);

                var targetLang = new Language(tag);
                if (OcrEngine.IsLanguageSupported(targetLang))
                {
                    var engine = OcrEngine.TryCreateFromLanguage(targetLang);
                    if (engine != null) return engine;
                }

                string prefix = tag.Split('-')[0];
                var availableMatch = OcrEngine.AvailableRecognizerLanguages
                    .FirstOrDefault(l => l.LanguageTag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

                if (availableMatch != null)
                {
                    var engine = OcrEngine.TryCreateFromLanguage(availableMatch);
                    if (engine != null) return engine;
                }

                var profileEngine = OcrEngine.TryCreateFromUserProfileLanguages();
                if (profileEngine != null) return profileEngine;

                var first = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault();
                if (first != null)
                {
                    return OcrEngine.TryCreateFromLanguage(first);
                }
            }
            catch { }

            return null;
        }

        private static string MapToLanguageTag(string? langName)
        {
            if (string.IsNullOrWhiteSpace(langName))
                return "ru-RU";

            if (LanguageTagMap.TryGetValue(langName.Trim(), out var tag))
                return tag;

            int idx = langName.IndexOf(" (");
            if (idx > 0)
            {
                string shortName = langName.Substring(0, idx).Trim();
                if (LanguageTagMap.TryGetValue(shortName, out var shortTag))
                    return shortTag;
            }

            idx = langName.IndexOf(" [");
            if (idx > 0)
            {
                string shortName = langName.Substring(0, idx).Trim();
                if (LanguageTagMap.TryGetValue(shortName, out var shortTag))
                    return shortTag;
            }

            return "ru-RU";
        }

        private class RawOcrLine
        {
            public string Text { get; set; } = string.Empty;
            public double X { get; set; }
            public double Y { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
        }
    }
}
