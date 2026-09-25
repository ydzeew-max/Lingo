using System.Threading.Tasks;

namespace Lingo.Services
{
    public class TranslationService
    {
        public Task<string> TranslateAsync(string text, string sourceLang, string targetLang)
        {
            string api = App.Settings.CurrentSettings.TranslationApi;
            return App.Translator.TranslateAsync(text, sourceLang, targetLang, api);
        }
    }
}
