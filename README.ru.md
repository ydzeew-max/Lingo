# Lingo

[English](README.md) | [Русский](README.ru.md)

Lingo - настольный переводчик для Windows 10 и 11 с поддержкой визуального перевода фото (OCR), двухпанельного ввода, голосового набора, озвучки и офлайн-словаря.

---

## Возможности

- Визуальный перевод текста с фото и снимков экрана (OCR)
- Двухпанельный переводчик с голосовым вводом и озвучкой (TTS)
- Движки перевода: Google, Yandex, Smart Auto и локальная база SQLite
- Интерфейс в стиле Windows 11 Fluent Design
- Глобальные горячие клавиши и работа в системном трее

---

## Горячие клавиши

| Сочетание | Действие |
| :--- | :--- |
| `Ctrl + Alt + T` | Перевод выделенного на экране текста |
| `Ctrl + Alt + S` | Снимок области экрана для фотоперевода |
| `Ctrl + V` | Вставка изображения из буфера обмена |
| `Esc` | Отмена снимка экрана или закрытие окна |

---

## Сборка и запуск

Требования: Windows 10/11, .NET 10 SDK.

```bash
git clone https://github.com/ydzeew-max/Lingo.git
cd lingo
dotnet build -c Debug
dotnet run
```

Сборка единого файла (.exe):
```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/
```

---

## Лицензия

MIT License. Подробности в файле [LICENSE](LICENSE).
