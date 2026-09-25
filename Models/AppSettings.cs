using System;

namespace Lingo.Models
{
    public class AppSettings
    {
        public string HotkeyModifiers { get; set; } = "Ctrl + Alt";
        public string HotkeyKey { get; set; } = "T";
        public string SourceLanguage { get; set; } = "English";
        public string TargetLanguage { get; set; } = "Russian (Русский)";
        public string TranslationApi { get; set; } = "Google";
        public bool StartWithWindows { get; set; } = false;
        public bool MinimizeToTrayOnClose { get; set; } = false;
        public bool AlwaysOnTop { get; set; } = true;
    }
}
