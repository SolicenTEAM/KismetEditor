# Solicen.KismetEditor

[**Englsih**](/README.md) | [**Русский**](./docs/ru/README.ru.md)

Thanks `Ambi` for idea and his research. <br>
Made with ❤️ for **all** translators and translation developers.

This a script/tool to replace and extract (EX_StringConst) from the [Unreal Enigne](https://www.unrealengine.com/) kismet bytecode. 

## Using:
* Or [download](https://github.com/SolicenTEAM/KismetEditor/releases) and **drag & drop** `.uasset` and `.csv` to a command tool.
* Or use `KismetEditor.exe <file_path>`.

### KismetEditor
* You can simply **drag & drop** `.uasset` and `.csv` onto `KismetEditor.exe` to parse `.uasset` and replace strings into it. 
* Or use more advanced options with CMD.

#### Extract strings:
```cmd
KismetEditor.exe <file_path> <output_csv> --extract
```
#### Replace strings:
```cmd 
KismetEditor.exe <file_path> <input_csv> 
```
| Argument | Description |
|----------|-------------|
| --extract | extract strings from `kismet` to csv
| --version, --v | set specific unreal version: `--version=4.18`
| --help | Show help information.

## Contributions:
* You can create your own fork of this project and contribute to its development.
* You can also contribute via the [Issues](https://github.com/SolicenTEAM/KismetEditor/issues) and [Pull Request](https://github.com/SolicenTEAM/KismetEditor/pulls) tabs by suggesting your code changes. And further development of the project. 
