# Solicen.KissE

[**Englsih**](/README.md) | [**Русский**](./docs/ru/README.ru.md)

KissE is a script/tool for processing and modifying (EX_String Const) inside the [Unreal Enigne](https://www.unrealengine.com/) Kismet bytecode. It allows you to both get all available for extraction strings (EX_StringConst) and replace them with (EX_UnicodeStringConst) by changing the instruction itself inside the bytecode.

Key features of kissE:
- Allows you to extract (EX_StringConst) bytecode as string to a CSV file.
- Ability to modify (EX_StringConst) with those specified replacement in the CSV file.
- After replacing (EX_StringConst), it adds new instructions to the bytecode and corrects their offsets for correct work.

<br>  Made with ❤️ for **all** translators and translation developers.

## Using
* Or [download](https://github.com/SolicenTEAM/KismetEditor/releases) and **drag & drop** `.uasset` and `.csv` to a command tool.
* Or use `Kisse.exe <file_path>`.

### Kis(met) s(tring) E(ditor)
* You can simply **drag & drop** `.uasset` and `.csv` onto `Kisse.exe` to replace (EX_StringConst). 
* Or use more advanced options with CMD.

#### Extract strings
```cmd
Kisse.exe <file_path> <output_csv> --extract
```
#### Replace strings
```cmd 
Kisse.exe <file_path> <input_csv> 
```
| Argument | Description |
|----------|-------------|
| --extract | extract strings from `kismet` to csv
| --version, --v | set specific unreal version: `--version=4.18`
| --help | Show help information.

## Compilation
### Requirements
- Visual Studio 2022 Preview

## Contributions
* You can create your own fork of this project and contribute to its development.
* You can also contribute via the [Issues](https://github.com/SolicenTEAM/KismetEditor/issues) and [Pull Request](https://github.com/SolicenTEAM/KismetEditor/pulls) tabs by suggesting your code changes. And further development of the project. 

## Thanks
- Thanks `Ambi` for idea and his research. 