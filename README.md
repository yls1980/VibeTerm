# VibeTerm

Терминальный менеджер для Windows: несколько независимых PowerShell-сессий в одном WPF-окне.

## Скачать и установить

### Готовая сборка

Когда в репозитории будет опубликован релиз, скачайте архив или `VibeTerm.exe` со страницы:

```text
https://github.com/yls1980/VibeTerm/releases
```

Затем распакуйте архив в удобную папку и запустите `VibeTerm.exe`.

### Из исходников

Если готового релиза ещё нет, приложение можно собрать локально:

```powershell
git clone https://github.com/yls1980/VibeTerm.git
cd VibeTerm
dotnet restore .\VibeTerm.sln
dotnet run --project .\VibeTerm\VibeTerm.csproj
```

Для создания publish-сборки:

```powershell
dotnet publish .\VibeTerm\VibeTerm.csproj -c Release -r win-x64 --self-contained false
.\VibeTerm\bin\Release\net7.0-windows\win-x64\publish\VibeTerm.exe
```

## Как пользоваться

1. Запустите `VibeTerm.exe` или `dotnet run --project .\VibeTerm\VibeTerm.csproj`.
2. Первый терминал открывается автоматически.
3. Добавляйте терминалы кнопкой `+` в правой панели или сочетанием `Ctrl+Shift+T`.
4. Переключайтесь между терминалами через список справа, `Ctrl+Tab` и `Ctrl+Shift+Tab`.
5. Настройки shell, стартовой папки, шрифта и логирования доступны через кнопку `Settings`.
6. Общие переменные окружения можно задать в панели `ENVIRONMENT` и применить кнопкой `Apply`.

## Возможности

- Несколько независимых терминалов на базе Windows ConPTY.
- Tiling-раскладка: все открытые терминалы видны одновременно в основной области.
- Правый сайдбар со списком терминалов, переименованием и закрытием.
- Сворачиваемая панель `ENVIRONMENT` для общих переменных окружения.
- Горячие клавиши для создания, закрытия, переключения терминалов, масштаба и clipboard-действий.
- Логи терминалов с маскированием чувствительных данных.
- Автозагрузка локальных xterm.js-ресурсов для последующих офлайн-запусков.

## Технологии

| Компонент | Выбор | Причина |
|---|---|---|
| UI | WPF (.NET 7) | Нативный Windows UI |
| Рендеринг терминала | WebView2 + xterm.js | VT-эмулятор с поддержкой интерактивных CLI |
| PTY | ConPTY | Официальный Windows Pseudo Console API |
| Сериализация | System.Text.Json | Встроенный JSON для настроек |
| Тесты | xUnit | Unit-тесты для ViewModel, логики сессий и маскирования |

## Требования

- Windows 10 версия 1809 (сборка 17763) или новее.
- WebView2 Runtime: обычно уже установлен в Windows 10/11; если нет, установите Microsoft Edge WebView2 Runtime.
- .NET 7 Runtime для запуска framework-dependent сборки.
- .NET 7 SDK для сборки из исходников.

## Сборка, запуск и тесты

```powershell
# Из корня репозитория
dotnet run --project .\VibeTerm\VibeTerm.csproj

dotnet test .\VibeTerm.sln

# Publish
dotnet publish .\VibeTerm\VibeTerm.csproj -c Release -r win-x64 --self-contained false
.\VibeTerm\bin\Release\net7.0-windows\win-x64\publish\VibeTerm.exe
```

В репозиторий не нужно добавлять `bin/`, `obj/`, `publish/`, локальные настройки IDE, логи и runtime-файлы приложения. Эти пути закрыты в `.gitignore`.

## Работа с терминалами

При запуске создаётся первый терминал. Новые терминалы добавляются кнопкой `+` в правом сайдбаре или `Ctrl+Shift+T`.

Терминалы располагаются в tiling-сетке:

- 1 терминал занимает всю рабочую область.
- 2 терминала делят область на две колонки.
- 3 терминала располагаются как две плитки сверху и одна широкая снизу.
- 4+ терминалов автоматически раскладываются по строкам и колонкам.

Выбранный терминал подсвечивается в сайдбаре и получает фокус для ввода.

## Переменные окружения

В правой панели есть сворачиваемый блок `ENVIRONMENT`.

В нём можно:

- добавить переменную кнопкой `+`;
- удалить выбранную переменную кнопкой `✕`;
- изменить `Name` и `Value` прямо в таблице;
- нажать `Apply`, чтобы применить текущий набор переменных ко всем открытым терминалам.

Переменные сохраняются в `%APPDATA%\VibeTerm\settings.json`.

Поведение:

- новые терминалы получают переменные через environment block при запуске процесса;
- уже открытые PowerShell-сессии получают изменения через команды `Set-Item Env:\...`;
- удалённые из таблицы переменные удаляются из уже открытых терминалов при `Apply`.

## Горячие клавиши

| Клавиши | Действие |
|---|---|
| `Ctrl+Shift+T` | Новый терминал |
| `Ctrl+W` | Закрыть активный терминал |
| `Ctrl+Tab` | Следующий терминал |
| `Ctrl+Shift+Tab` | Предыдущий терминал |
| `Ctrl++` | Увеличить шрифт |
| `Ctrl+-` | Уменьшить шрифт |
| `Ctrl+0` | Сбросить шрифт |
| `Ctrl+C` при выделении | Скопировать выделенное |
| `Ctrl+C` без выделения | Послать interrupt (`^C`) в процесс |
| `Ctrl+V`, `Ctrl+Shift+V`, `Shift+Insert` | Вставить текст, скриншот или файлы из буфера обмена |
| `Ctrl+A` | Выделить весь вывод терминала |

При вставке скриншота VibeTerm сохраняет его как JPG в текущем каталоге панели. Файлы, скопированные в Проводнике, копируются туда же с исходным расширением. Если имя занято, к копии добавляется номер. В строку ввода вставляются полные пути в кавычках. Ввод можно продолжить перед отправкой.

## Управление панелями через MCP

Каждая панель - отдельный PowerShell. Пока окно VibeTerm открыто, им можно управлять из редактора.

Приложение поднимает локальный программный интерфейс на `127.0.0.1`. Рядом есть программа `VibeTerm.Mcp`. Cursor запускает её как сервер MCP и через неё печатает в панели, читает вывод, открывает и закрывает панели. Инструменты `list_agents`, `ask_agent` и `ask_agents` задают вопрос агентам, которые уже работают в панелях. Сами агенты сервер не запускает.

MCP (Model Context Protocol) - способ, которым редактор вызывает внешние инструменты.

Подробности, список инструментов и пример подключения: [docs/panel-api.md](docs/panel-api.md).

Флажок `Enable local panel API for MCP` в настройках включает интерфейс со следующего запуска. По умолчанию он включён.

## Логи

Файлы логов находятся в:

```text
%APPDATA%\VibeTerm\logs\terminal_<id>_<timestamp>.log
```

Пример:

```text
[2026-05-01 15:30:12.123] [SESSIONCREATED] id=1 name="PowerShell 1" shell="pwsh.exe" cwd="C:\Users\User"
[2026-05-01 15:30:15.441] [USERINPUT] git status
[2026-05-01 15:30:15.812] [PROCESSOUTPUT] On branch main
[2026-05-01 15:40:00.001] [PROCESSEXITED] exitCode=0
```

Логи могут содержать чувствительные данные. Перед отправкой логов наружу их нужно просматривать.

## xterm.js и офлайн-режим

`terminal.html` сначала пытается загрузить локальные файлы:

- `Resources\xterm.css`
- `Resources\xterm.js`
- `Resources\xterm-addon-fit.js`

Если локальных файлов нет, используется CDN:

- `https://cdn.jsdelivr.net/npm/xterm@5.3.0/...`
- `https://cdn.jsdelivr.net/npm/xterm-addon-fit@0.8.0/...`

`Services\XtermAssets.cs` при запуске скачивает недостающие файлы в выходную папку `Resources`, поэтому после первого успешного онлайн-запуска терминал может работать без интернета.

WebView2 обслуживает ресурсы через виртуальный хост:

```text
https://app.local/terminal.html
```

## Структура проекта

```text
VibeTerm/
├── ConPTY/
│   ├── NativeMethods.cs
│   ├── PseudoConsole.cs
│   └── ConPtyProcess.cs
├── Converters/
│   └── BoolVisibilityConverters.cs
├── Logging/
│   ├── LogEventType.cs
│   ├── SensitiveMasker.cs
│   └── TerminalLoggingService.cs
├── Resources/
│   └── terminal.html
├── Services/
│   └── XtermAssets.cs
├── Settings/
│   ├── AppSettings.cs
│   └── SettingsService.cs
├── Api/
│   ├── PanelApiServer.cs
│   ├── PanelApiHost.cs
│   └── WpfPanelController.cs
├── Terminal/
│   ├── TerminalStatus.cs
│   ├── TerminalSession.cs
│   └── TerminalSessionManager.cs
├── ViewModels/
│   ├── EnvironmentVariableViewModel.cs
│   ├── MainViewModel.cs
│   ├── RelayCommand.cs
│   └── TerminalViewModel.cs
├── Views/
│   ├── SettingsWindow.xaml(.cs)
│   └── TerminalView.xaml(.cs)
├── App.xaml(.cs)
├── MainWindow.xaml(.cs)
└── VibeTerm.csproj

VibeTerm.Tests/
├── SensitiveMaskerTests.cs
├── TerminalSessionManagerTests.cs
├── TerminalViewModelModeTests.cs
└── VibeTerm.Tests.csproj

VibeTerm.Mcp/
├── Program.cs
├── PanelTools.cs
└── VibeTermApiClient.cs
```

## Известные ограничения

1. Приложение работает только на Windows.
2. Сессии терминалов не восстанавливаются после перезапуска.
3. Все WebView2-контролы используют общий user data folder: `%APPDATA%\VibeTerm\WebView2Cache`.
4. Переменные окружения из панели применяются к PowerShell-сессиям через PowerShell-команды; для других shell это поведение может потребовать отдельной адаптации.
5. Первый resize терминала может происходить с небольшой задержкой после инициализации xterm.js.
6. Программный интерфейс панелей принимает соединения только с этого компьютера. Если открыты два окна, файл адреса указывает на последнее. Чтение вывода возвращает текст текущего процесса без цветов, а не точную копию экрана.

## Следующие улучшения

- [ ] Drag-and-drop переупорядочивание терминалов в сайдбаре.
- [ ] Ручное управление tiling-сеткой и split-pane.
- [ ] Поиск по выводу терминала (`Ctrl+F`).
- [ ] Профили терминалов: shell, cwd, тема, env-наборы.
- [ ] Copy current directory в контекстном меню.
- [ ] Сохранение размеров и положения окна.
- [ ] Уведомление о завершении долгого процесса.
- [ ] Очистка старых логов через UI.
- [ ] Light-тема.

## Лицензия

Проект распространяется по лицензии MIT. См. `LICENSE`.
