# Solicen.KissE

[**Englsih**](/README.md) | [**Русский**](./docs/ru/README.ru.md)

KissE is a script/tool for processing and modifying `EX_StringConst` inside the [Unreal Enigne](https://www.unrealengine.com/) Kismet bytecode. It allows you to both get all available for extraction strings `EX_StringConst` and replace them with `EX_UnicodeStringConst` by changing the instruction itself inside the bytecode. 

[UAssetAPI](https://github.com/atenfyr/UAssetAPI) is used to work with Unreal Engine files and bytecode.

Key features of kissE:
- Allows you to extract `EX_StringConst` bytecode as string to a UberJSON file.
- Ability to modify `EX_StringConst` with those specified replacement in the UberJSON file.
- Corrects `EX_UnicodeStringConst` new offsets for correct work in the game.
- Extract and modify each `StrPropety` and `TextProperty` string.
- Fallback extract and modify any other type, as `LocalizedSource`, `StringTable`, `DataTable`, but with some risk.

Made with ❤️ for **all** translators and translation developers.

## Usage
* Or [download](https://github.com/SolicenTEAM/KismetEditor/releases) and **drag & drop** `(.uasset|.umap)` to a command tool.
* Or use `Kisse.exe <file_path>`.
* Do not use this in **Windows Powershell**.

### Kis(met) s(tring) E(ditor)
> [!NOTE]
> Logic of the work in CLI:
> #### Extract: (From) Asset/Folder => (To) JSON/OrNull
>  ```
> kisse <asset/folder_path> <json/null> <...args>
> ```
> #### Replace: (From) JSON/CSV => (To) Asset/Folder
> ```
> kisse <json_path> <asset/folder_path> <...args>
> ```

> [!TIP]
> If some `EX_StringConst` strings are missing, try the `--StringConst` argument. 
> * This will extract them from all `UFunction` in the asset.

### Main arguments:
| Argument | Short | Description |
|----------|-------|-------------|
| `--StringConst` | `-sc` | Parses all UFunction with ScriptBytecode and extracts all strings of type EX_StringConst.
| `--TextProperty` | | Allow to extract string with TextProperty type.
| `--LocalizedSource` | `-l` | Extract fallback localization strings (LocalizedSource).
| `--Table` | `-t` | Extract strings from Data/String Table assets.
| `--AllType`| `-all`| Extract all string types (includes StringTable and LocalizedSource and TextProperty)
| `-No-underscore` | `-n:un` | Skips strings that contain the `_` character.
| `--Include-name` | `-i:name` | Include name/key::value (e.g., "ENG::Gori")
  | `--Map` | `-m` | Specified .usmap nearby .exe as mappings for processing (e.g., --map="Gori_umap.usmap")
  | `--Map-nearby` | `-u:m` | Uses the usmap file if it finds it nearby.
  | `--NoBak` | `-bak` | Disables the creation of .bak backup files.
  | `--Patch-all-functions` | `-paf` | Iterate the bytecode-replacement pipeline over every UFunction with a ScriptBytecode (not just ExecuteUbergraph_*). Needed for widget event handlers and other functions that hold their (EX_StringConst) outside the ubergraph.
  | `--Patch-assignments` | `-pa` | Also replace (EX_StringConst) inside an AssignmentExpression in the ubergraph (off by default to avoid touching technical paths/keys; opt-in for game text hardcoded via 'Set Text' / 'Print String' Blueprint nodes).
  | `--Table-only-key` | `-t:o:k` | If key/name matches then include only this value to output (e.g., --table:only:key=ENG)
  | `--Pack-folder` | `-p:f` | Translate and pack assets into auto prepared folder (e.g., "ManicMiners_RUS")
  | `--Version` | `-v` | Set the engine version for correct processing (e.g., -v=5.1)
  | `--Run` | `-r` | Execute a command in the terminal after completion (e.g., --run=[CommandArgs])
| `--Debug` | `-d` | write additional files for debug: `Ubergraph.json`
| `--help` |  | Show help information.
### Translator arguments:
| Argument | Short | Description |
|----------|-------|-------------|
  | `--Endpoint` | `-e` | Translation service endpoint (e.g., -e=router (yandex, google, microsoft, router as OpenRouter))
  |  `--Api-key`| `-a` | Api-key for OpenRouter (e.g., --api=sk-or-v1-321313.....)
  | `--Api-model`| `-a:m` | Model for OpenRouter (e.g, -a:model=tngtech/deepseek-r1t2-chimera:free)
  | `--Language-source` | `-l:s` | Source language for translation (e.g., --lang:from=en)
  | `--Language-to` | `-l:t` | Target language for translation (e.g., --lang:to=ru)

#### Real example of [CommandArgs] usage:
```
"A:\SteamLibrary\steamapps\common\Wigmund\RPG\Content\Paks\UI_Tooltip.csv" "A:\SteamLibrary\steamapps\common\Wigmund\RPG\Content\Paks\s_rus_NEW_P\RPG\Content\RPG\Blueprints\UI\Game\UI_Tooltip.uasset" [A:\SteamLibrary\steamapps\common\Wigmund\RPG\Content\Paks\UnrealPak-With-Compression.bat s_rus_NEW_P | start A:\SteamLibrary\steamapps\common\Wigmund\RPG\Binaries\Win64\RPG-Win64-Shipping.exe]
```
- The process is fully automated here. 
- Start the process of replacing lines in the asset in a specific folder. 
- Inside [CommandArgs]: I connect to UnrealPak.exe and I hand over the folder that needs to be packed (the one with the asset)
- After packing, the game starts next to check its performance. The commands are separated via '|'
- Profit. The process is fully automated to test how the game and asset work after modification.

## Compilation
### Requirements
- Visual Studio 2026

## Contributions
* You can create your own fork of this project and contribute to its development.
* You can also contribute via the [Issues](https://github.com/SolicenTEAM/KismetEditor/issues) and [Pull Request](https://github.com/SolicenTEAM/KismetEditor/pulls) tabs by suggesting your code changes. And further development of the project. 

## Thanks
- [Ambi](https://github.com/JunkBeat) for idea and his research. 
- [@atenfyr](https://github.com/atenfyr) for research and [UAssetAPI](https://github.com/atenfyr/UAssetAPI).
