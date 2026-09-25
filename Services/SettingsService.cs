using System;
using System.IO;
using System.Text.Json;
using Lingo.Models;

namespace Lingo.Services
{
    public class SettingsService
    {
        private static readonly string FolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Lingo"
        );
        private static readonly string FilePath = Path.Combine(FolderPath, "settings.json");

        public AppSettings CurrentSettings { get; private set; }

        public SettingsService()
        {
            CurrentSettings = Load();
        }

        public AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null) return settings;
                }
            }
            catch { }

            return new AppSettings();
        }

        public void Save(AppSettings settings)
        {
            try
            {
                CurrentSettings = settings;
                Directory.CreateDirectory(FolderPath);
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch { }
        }
    }
}
