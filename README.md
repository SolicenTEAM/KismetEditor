# Solicen.KissE

[**Englsih**](/README.md) | [**Русский**](./docs/ru/README.ru.md)

KissE is a script/tool for processing and modifying (EX_StringConst) inside the [Unreal Enigne](https://www.unrealengine.com/) Kismet bytecode. It allows you to both get all available for extraction strings (EX_StringConst) and replace them with (EX_UnicodeStringConst) by changing the instruction itself inside the bytecode. 

[UAssetAPI](https://github.com/atenfyr/UAssetAPI) is used to work with Unreal Engine files and bytecode.

Key features of kissE:
- Allows you to extract (EX_StringConst) bytecode as string to a CSV file.
- Ability to modify (EX_StringConst) with those specified replacement in the CSV file.
- Corrects (EX_UnicodeStringConst) new offsets for correct work in the game.

Made with ❤️ for **all** translators and translation developers.

## Usage
* Or [download](https://github.com/SolicenTEAM/KismetEditor/releases) and **drag & drop** `(.uasset|.umap)` and `.csv` to a command tool.
* Or use `Kisse.exe <file_path>`.

### Kis(met) s(tring) E(ditor)
* You can simply **drag & drop** `(.uasset|.umap)` and `.csv` onto `Kisse.exe` to replace (EX_StringConst). 
* Or use more advanced options with CMD.

### For single file:
#### Extract strings from asset (.uasset|.umap)
```cmd
Kisse.exe <file_path> <output_csv or null>
```
#### Replace strings in asset (.uasset|.umap)
```cmd 
Kisse.exe <input_csv> <asset_path>  
```
### For whole Directory:
#### Extract strings from each asset (.uasset|.umap) in directory
```cmd
Kisse.exe <directory_path_with_assets>
```
#### Replace strings in each asset (.uasset|.umap) in directory
```cmd
Kisse.exe <directory_path_with_csv's> <directory_path_with_assets> --pack
```
| Argument | Description |
|----------|-------------|
| --pack | pack translate from csv to each asset in directory
| --debug | write additional files for debug: `Ubergraph.json`
| --version | set specific unreal version: `--version=4.18`
| --help | Show help information.

## Compilation
### Requirements
- Visual Studio 2022 Preview

## Contributions
* You can create your own fork of this project and contribute to its development.
* You can also contribute via the [Issues](https://github.com/SolicenTEAM/KismetEditor/issues) and [Pull Request](https://github.com/SolicenTEAM/KismetEditor/pulls) tabs by suggesting your code changes. And further development of the project. 

## Thanks
- [Ambi](https://github.com/JunkBeat) for idea and his research. 
- [@atenfyr](https://github.com/atenfyr) for research and [UAssetAPI](https://github.com/atenfyr/UAssetAPI).
