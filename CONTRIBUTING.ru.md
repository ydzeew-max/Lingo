# Участие в разработке Lingo

[English](CONTRIBUTING.md) | [Русский](CONTRIBUTING.ru.md)

Спасибо за интерес к проекту Lingo.

---

## Начало работы

1. Требования: Windows 10/11, .NET 10 SDK.
2. Клонирование репозитория:
   ```bash
   git clone https://github.com/ydzeew-max/Lingo.git
   cd lingo
   ```
3. Сборка и запуск:
   ```bash
   dotnet build -c Debug
   dotnet run
   ```

---

## Порядок внесения изменений

1. Создайте ветку для доработок:
   ```bash
   git checkout -b feature/your-feature
   ```
2. Соблюдайте стили Windows 11 Fluent Design из `App.xaml`.
3. Убедитесь, что команда `dotnet build` завершается с 0 ошибок и предупреждений.
4. Зафиксируйте изменения через commit и создайте Pull Request.
