# Lingo

[English](README.md) | [Русский](README.ru.md)

Lingo is a desktop translator for Windows 10 and 11 featuring in-place photo OCR, dual-pane text translation, voice input, text-to-speech, and offline dictionary support.

---

## Features

- In-place photo and screen snip translation (OCR)
- Dual-pane text translation with voice input and text-to-speech
- Translation engines: Google, Yandex, Smart Auto, and offline SQLite dictionary
- Windows 11 Fluent dark theme interface
- Global hotkeys and system tray integration

---

## Hotkeys

| Shortcut | Action |
| :--- | :--- |
| `Ctrl + Alt + T` | Translate selected text on screen |
| `Ctrl + Alt + S` | Capture screen area for photo translation |
| `Ctrl + V` | Paste image from clipboard |
| `Esc` | Cancel snip or close window |

---

## Build and Run

Requirements: Windows 10/11, .NET 10 SDK.

```bash
git clone https://github.com/ydzeew-max/Lingo.git
cd lingo
dotnet build -c Debug
dotnet run
```

To build a standalone executable:
```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/
```

---

## License

MIT License. See [LICENSE](LICENSE) for details.
