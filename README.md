# Solicen.KissE

[**Englsih**](/README.md) | [**Русский**](./docs/ru/README.ru.md)

KissE is a script/tool for processing and modifying (EX_StringConst) inside the [Unreal Enigne](https://www.unrealengine.com/) Kismet bytecode. It allows you to both get all available for extraction strings (EX_StringConst) and replace them with (EX_UnicodeStringConst) by changing the instruction itself inside the bytecode. 

[UAssetAPI](https://github.com/atenfyr/UAssetAPI) is used to work with Unreal Engine files and bytecode.

Key features of kissE:
- Allows you to extract (EX_StringConst) bytecode as string to a UberJSON file.
- Ability to modify (EX_StringConst) with those specified replacement in the UberJSON file.
- Corrects (EX_UnicodeStringConst) new offsets for correct work in the game.

Made with ❤️ for **all** translators and translation developers.

## Usage
* Or [download](https://github.com/SolicenTEAM/KismetEditor/releases) and **drag & drop** `(.uasset|.umap)` to a command tool.
* Or use `Kisse.exe <file_path>`.

### Kis(met) s(tring) E(ditor)
* You can simply **drag & drop** `(.uasset|.umap)` and `.json` onto `Kisse.exe` to replace (EX_StringConst). 
* Or use more advanced options with CMD.

### For single file:
#### Extract strings from asset (.uasset|.umap)
```cmd
Kisse.exe <file_path> <output_json or null>
```
#### Replace strings in asset (.uasset|.umap)
```cmd 
Kisse.exe <input_json> <asset_path>  
```
### For whole Directory:
#### Extract strings from each asset (.uasset|.umap) in directory
- The extracted strings will be inside each `.csv` in the `Unpack` folder.
```cmd
Kisse.exe <directory_path_with_assets>
```


#### Replace strings in each asset (.uasset|.umap) in directory
```cmd
Kisse.exe <input_json> <directory_path_with_assets>
```
| Argument | Description |
|----------|-------------|
| `--include:name` `-i:name` | Include name/key::value (e.g., "ENG::Gori").
  | `--map` `-m` | Add specified .usmap nearby .exe as mappings for processing (e.g., --map="Gori_umap.usmap").
  | `--nobak` | Disables the creation of .bak backup files.
  | `--all` | Extract all string types (includes StringTable and LocalizedSource).
  | `--table` | Extract strings from Data/String Table assets.
  | `--localized` `-l` | Extract fallback localization strings (LocalizedSource). [RISKY]
  | `--underscore` `-u` | Allow extracting strings that contain the '_' character.
  | `--table:only:key` | If key/name matches then include only this value to output (e.g., --table:only:key=ENG).
  | `--pack:folder` `-p:f` | Translate and pack assets into auto prepared folder (e.g., "ManicMiners_RUS")
  | `--version` `-v` | Set the engine version for correct processing (e.g., -v=5.1)
  | `--lang:from` `-l:f` | Set the source language for translation (e.g., --lang:from=en)
  | `--lang:to` `-l:t` | Set the target language for translation (e.g., --lang:to=ru)
  | `--endpoint` `-e` | Set the translation service endpoint (e.g., -e=yandex)
  | `--run` `-r` | Execute a command in the terminal after completion (e.g., --run=[CommandArgs])
| --debug | write additional files for debug: `Ubergraph.json`
| --help | Show help information.

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
- Visual Studio 2022 Preview

## Contributions
* You can create your own fork of this project and contribute to its development.
* You can also contribute via the [Issues](https://github.com/SolicenTEAM/KismetEditor/issues) and [Pull Request](https://github.com/SolicenTEAM/KismetEditor/pulls) tabs by suggesting your code changes. And further development of the project. 

## Thanks
- [Ambi](https://github.com/JunkBeat) for idea and his research. 
- [@atenfyr](https://github.com/atenfyr) for research and [UAssetAPI](https://github.com/atenfyr/UAssetAPI).
