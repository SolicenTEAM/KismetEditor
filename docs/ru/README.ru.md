# Solicen.KissE

[**English**](/README.md) | [**Русский**](./docs/ru/README.ru.md)

**KissE** (Kismet string Editor) — скрипт/инструмент для обработки и модификации `EX_StringConst` внутри Kismet-байткода [Unreal Engine](https://www.unrealengine.com/). Позволяет как получить все доступные для извлечения строки `EX_StringConst`, так и заменить их на `EX_UnicodeStringConst` путём изменения самой инструкции внутри байткода.

Для работы с файлами и байткодом Unreal Engine используется [UAssetAPI](https://github.com/atenfyr/UAssetAPI).

Ключевые возможности KissE:
- Извлечение `EX_StringConst` из байткода в UberJSON-файл.
- Замена `EX_StringConst` на указанные в UberJSON-файле значения.
- Корректировка новых смещений `EX_UnicodeStringConst` для корректной работы в игре.
- Извлечение и модификация строк `StrProperty` и `TextProperty`.
- Резервное извлечение и модификация других типов: `LocalizedSource`, `StringTable`, `DataTable` (с некоторым риском).

Сделано с ❤️ для **всех** переводчиков и разработчиков локализаций.

## Использование
* Или [скачайте](https://github.com/SolicenTEAM/KismetEditor/releases) и **перетащите** `(.uasset|.umap)` в командную строку.
* Или используйте `Kisse.exe <путь_к_файлу>`.
* Не используйте в **Windows Powershell**.

### Kis(met) s(tring) E(ditor)
> [!NOTE]
> Логика работы в CLI:
> #### Извлечение: (Из) Asset/Папки => (В) JSON/ИлиNull
> ```
> kisse <путь_к_ассету/папке> <json/null> <...аргументы>
> ```
> #### Замена: (Из) JSON/CSV => (В) Asset/Папку
> ```
> kisse <путь_к_json> <путь_к_ассету/папке> <...аргументы>
> ```

> [!TIP]
> Если некоторые строки `EX_StringConst` отсутствуют, попробуйте аргумент `-sc`. 
> * С ним будут извлечены все строки из всех `UFunction` в ассете.

### Виртуальные архивы (PAK / IoStore)

Чтение `.pak`, `.ucas` и `.utoc` без распаковки на диск:

```bash
KissE.exe "C:\Game\Content\Paks\Game-WindowsNoEditor.ucas" "C:\Output\strings.json" --virtual --version=4.27
```

Замена строк использует тот же провайдер:

```bash
KissE.exe "C:\Output\strings.json" "C:\Game\Content\Paks\Game-WindowsNoEditor.ucas" --virtual --version=4.27 --pack-folder=MyMod
```

Результат — отдельные legacy-ассеты в выходной папке. **Исходный UCAS не изменяется; обратная упаковка в IoStore в эту функцию не входит.**

### Основные аргументы:
| Аргумент | Сокращение | Описание |
|----------|-----------|----------|
| `--sconst` | `-sc` | Парсит все UFunction с ScriptBytecode и извлекает все строки типа EX_StringConst. |
| `--tprop` | `-tp`| Разрешить извлечение строк типа TextProperty. |
| `--lsource` | `-ls` | Извлечение резервных строк локализации (LocalizedSource). |
| `--dstable` | `-dst` | Извлечение строк из Data/String Table ассетов. |
| `--alltypes` | `-all` | Извлечение всех типов строк (включая StringTable, LocalizedSource и TextProperty). |
| `--no-filter` | `-nf` | Полностью отключает фильтрацию строк.
| `--no-backup` | `-nobak` | Отключает создание .bak резервных копий. |
| `-no-underscore` | `-un` | Пропускает строки, содержащие символ `_`. |
| `--namespace` | `-ns` | Включать имя/ключ::значение (например, "ENG::Gori"). |
| `--mapping` | `-map` | Указать .usmap рядом с .exe в качестве маппингов (например, -map="Gori_umap.usmap"). |
| `--mapping-auto` | `-ma` | Использовать .usmap файл, если он найден рядом. |
| `--patch-all-functions` | `-paf` | Применяет пайплайн замены байткода ко всем UFunction с ScriptBytecode (не только ExecuteUbergraph_*). Нужно для обработчиков виджетов и других функций, хранящих EX_StringConst вне уберграфа. |
| `--patch-assignments` | `-pa` | Также заменять EX_StringConst внутри AssignmentExpression в уберграфе (выключено по умолчанию, чтобы не трогать технические пути/ключи; включайте для игрового текста, заданного через ноды 'Set Text' / 'Print String'). |
| `--only-key` | `-tok` | Если ключ/имя совпадает, включать только это значение в вывод (например, --table:only:key=ENG). |
| `--pack-folder` | `-pf` | Перевести и упаковать ассеты в автоматически подготовленную папку (например, "ManicMiners_RUS"). |
| `--version` | `-v` | Установить версию движка для корректной обработки (например, -v=5.1). |
| `--run` | `-r` | Выполнить команду в терминале после завершения (например, --run=[CommandArgs]). |
| `--debug` | `-d` | Записывать дополнительные файлы для отладки: `Ubergraph.json`. |
| `--virtual` | `-vir` | Чтение PAK/UCAS/UTOC в памяти; обязательно для IoStore. |
| `--no-oodle-download` | `-noodle` | Запретить скачивание Oodle; использовать только встроенную или локальную DLL. |
| `--aes` | `-a` | Ключ AES-256: 64 hex-символа, допустим префикс 0x. |
| `--help` | `-h` | Показать справку. |

### Аргументы переводчика:
| Аргумент | Сокращение | Описание |
|----------|-----------|----------|
| `--endpoint` | `-e` | Эндпоинт сервиса перевода (например, -e=router (yandex, google, microsoft, router как OpenRouter)). |
| `--api-key` | `-api` | API-ключ для OpenRouter (например, --api=sk-or-v1-321313.....). |
| `--api-model` | `-model` | Модель для OpenRouter (например, -a:model=tngtech/deepseek-r1t2-chimera:free). |
| `--source-lang` | `-sl` | Исходный язык для перевода (например, --lang:from=en). |
| `--target-lang` | `-tl` | Целевой язык для перевода (например, --lang:to=ru). |

#### Реальный пример использования [CommandArgs]:
```
"A:\SteamLibrary\steamapps\common\Wigmund\RPG\Content\Paks\UI_Tooltip.csv" "A:\SteamLibrary\steamapps\common\Wigmund\RPG\Content\Paks\s_rus_NEW_P\RPG\Content\RPG\Blueprints\UI\Game\UI_Tooltip.uasset" [A:\SteamLibrary\steamapps\common\Wigmund\RPG\Content\Paks\UnrealPak-With-Compression.bat s_rus_NEW_P | start A:\SteamLibrary\steamapps\common\Wigmund\RPG\Binaries\Win64\RPG-Win64-Shipping.exe]
```
- Процесс полностью автоматизирован.
- Запускает процесс замены строк в ассете в указанной папке.
- Внутри [CommandArgs]: подключаюсь к UnrealPak.exe и передаю папку, которую нужно запаковать (ту, где находится ассет).
- После упаковки запускается игра для проверки работоспособности. Команды разделяются через '|'.
- Профит. Процесс полностью автоматизирован для тестирования работы игры и ассета после модификации.

## Компиляция
### Требования
- Visual Studio 2026

Для сборки IoStore DLL дополнительно нужны Rust (`x86_64-pc-windows-msvc`) и C++ Build Tools. Из корня репозитория:

```bash
git submodule update --init ThirdParty/retoc
```

```bash
cargo build --manifest-path Native/IoStoreBridge/Cargo.toml --release --locked
```

```bash
dotnet build KismetEditor/KismetEditor.csproj
```

При build/publish .NET-проект встраивает собранный мост. Если DLL ещё не собрана, выдаётся предупреждение; чтение PAK и обычных ассетов остаётся доступным. Rust нужен только для сборки, не для запуска программы. Retoc закреплён submodule-коммитом, его исходники не изменяются; транзитивные зависимости закреплены Cargo.lock обёртки.

Публикация автономного Windows x64 релиза (запускается из PowerShell; выходная папка должна быть новой или пустой):

```bash
powershell.exe -NoProfile -File scripts/publish-portable.ps1 -OodleLibraryPath "C:\Libraries\oo2core_9_win64.dll" -OutputDirectory artifacts/portable
```

Скрипт собирает мост со статическим MSVC runtime и публикует **только `KissE.exe`**, включая .NET, управляемые зависимости, мост, Oodle, её зависимость `vcruntime140.dll` и тексты MIT-лицензий. Redistributable x64 `vcruntime140.dll` находится автоматически в установленных Visual Studio C++ tools; `-VcRuntimeLibraryPath` позволяет явно выбрать разрешённую для распространения копию. Перед Oodle она загружается абсолютным путём из того же кэша по SHA-256. Для этой поддерживаемой комбинации DLL не нужны установленный .NET, отдельный Visual C++ redistributable или загрузка Oodle при запуске. `PortableRelease=true` делает отсутствие встроенных нативных библиотек ошибкой сборки, а не молчаливой публикацией урезанного EXE. Другая предоставленная сборка Oodle не должна требовать дополнительных невстроенных нативных зависимостей.

Встраивайте и распространяйте Oodle только при наличии соответствующих прав; в git DLL не добавляется. Developer-сборки через `dotnet build` сохраняются; `OodleLibraryPath` позволяет встроить Oodle и в них. `-p:EmbedIoStoreBridge=false` оставляет мост отдельной DLL, `-p:IoStoreBridgePath="C:\Libraries\kisse_iostore.dll"` выбирает другую сборку моста. Локальная адаптация `Native/oodle_loader` сохраняет закреплённый Oodle ABI, но требует явной инициализации абсолютным путём; submodule retoc не изменяется.

Регрессионные тесты без игровых данных:

```bash
dotnet test tests/KismetEditor.Tests/KismetEditor.Tests.csproj
```

```bash
cargo test --manifest-path Native/IoStoreBridge/Cargo.toml --locked
```

Проверка на игре: соберите `tests/IoStore.Smoke`, при необходимости положите/встройте Oodle для автономной работы и запустите `IoStore.Smoke.exe <архив> <версия UE> [папка round-trip] [usmap]`. Проверяются экспорты ассетов, виртуальное чтение legacy-файлов и повторное открытие архива; при указании выходной папки проверяется запись и повторное чтение отдельных ассетов.

## Вклад в проект
* Вы можете создать свой форк этого проекта и внести свой вклад в его развитие.
* Вы также можете внести свой вклад через вкладки [Issues](https://github.com/SolicenTEAM/KismetEditor/issues) и [Pull Request](https://github.com/SolicenTEAM/KismetEditor/pulls), предлагая свои изменения кода и дальнейшее развитие проекта.

## Благодарности
- [Ambi](https://github.com/JunkBeat) — за идею и его исследования.
- [@atenfyr](https://github.com/atenfyr) — за исследования и [UAssetAPI](https://github.com/atenfyr/UAssetAPI).
