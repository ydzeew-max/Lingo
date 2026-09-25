using System;
using System.Runtime.InteropServices;
using System.Speech.Recognition;
using System.Threading.Tasks;

namespace Lingo.Services
{
    public class VoiceInputService : IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const byte VK_LWIN = 0x5B;
        private const byte VK_H = 0x48;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private SpeechRecognitionEngine? _engine;
        private bool _isListening;
        private bool _disposed;

        public event EventHandler<string>? TextRecognized;
        public event EventHandler<bool>? ListeningStateChanged;

        public bool IsListening => _isListening;

        public VoiceInputService()
        {
            InitializeSapiEngineIfAvailable();
        }

        private void InitializeSapiEngineIfAvailable()
        {
            try
            {
                var recognizers = SpeechRecognitionEngine.InstalledRecognizers();
                if (recognizers.Count > 0)
                {
                    _engine = new SpeechRecognitionEngine(recognizers[0]);
                    _engine.LoadGrammar(new DictationGrammar());
                    _engine.SetInputToDefaultAudioDevice();

                    _engine.SpeechRecognized += (s, e) =>
                    {
                        if (e.Result != null && !string.IsNullOrWhiteSpace(e.Result.Text))
                        {
                            TextRecognized?.Invoke(this, e.Result.Text);
                        }
                    };

                    _engine.RecognizeCompleted += (s, e) =>
                    {
                        _isListening = false;
                        ListeningStateChanged?.Invoke(this, false);
                    };
                }
            }
            catch
            {
                _engine = null;
            }
        }

        public void TriggerVoiceInput()
        {
            // If SAPI engine is available with dictation grammar, start it
            if (_engine != null)
            {
                try
                {
                    if (!_isListening)
                    {
                        _isListening = true;
                        ListeningStateChanged?.Invoke(this, true);
                        _engine.RecognizeAsync(RecognizeMode.Multiple);
                        return;
                    }
                    else
                    {
                        _engine.RecognizeAsyncStop();
                        _isListening = false;
                        ListeningStateChanged?.Invoke(this, false);
                        return;
                    }
                }
                catch { }
            }

            // Universal modern Windows 10/11 AI Voice Typing (Win + H)
            // Triggers Microsoft's built-in neural dictation toolbar directly into the focused TextBox
            SimulateWinH();
            _isListening = true;
            ListeningStateChanged?.Invoke(this, true);
        }

        public void Stop()
        {
            try
            {
                if (_engine != null && _isListening)
                {
                    _engine.RecognizeAsyncStop();
                }
            }
            catch { }

            _isListening = false;
            ListeningStateChanged?.Invoke(this, false);
        }

        private static void SimulateWinH()
        {
            try
            {
                keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
                keybd_event(VK_H, 0, 0, UIntPtr.Zero);
                keybd_event(VK_H, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
            catch { }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Stop();
                _engine?.Dispose();
            }
        }
    }
}
