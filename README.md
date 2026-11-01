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
> If some `EX_StringConst` strings are missing, try the `-sc` argument. 
> * This will extract them from all `UFunction` in the asset.

### Virtual archives (PAK / IoStore)

Read `.pak`, `.ucas` or `.utoc` assets without unpacking the archive:

```bash
KissE.exe "C:\Game\Content\Paks\Game-WindowsNoEditor.ucas" "C:\Output\strings.json" --virtual --version=4.27 --alltypes
```

IoStore requires `--virtual`, the matching `.utoc` and `.ucas`, and the game's `global.utoc/global.ucas` for UE4 script imports. Keep sibling containers together for dependency resolution. Only files from the selected container are exposed. Use the actual game engine version, `--mapping=Game.usmap` for unversioned properties when needed, and `--aes=<64 hex characters>` for encrypted containers.

The IoStore backend uses [retoc v0.1.5](https://github.com/trumank/retoc/releases/tag/v0.1.5) (MIT) to convert Zen packages to legacy `.uasset/.uexp` **in memory**, then parses them with the existing UAssetAPI. No temporary PAK or unpacked asset tree is created. Compatibility is limited to the formats supported by that retoc revision (engine versions 4.25–5.7); game-specific formats and assets may require mappings or remain unsupported.

The portable release contains the IoStore bridge and Oodle inside **one self-contained `KissE.exe`**; neither .NET nor separate DLLs need to be installed. Native libraries are extracted on demand to `%LOCALAPPDATA%\KissE\native\win-x64\<SHA256>\`, never next to the EXE. Matching files are reused; different library versions occupy separate directories. Extraction is atomic and protected by a cross-process lock. Only the cache must be writable. PAK and loose-asset operations do not extract these libraries. The bundled .NET runtime stays inside the EXE where supported; native runtime files that need extraction use .NET's own service cache (normally `%TEMP%\.net\KissE\...`, configurable via `DOTNET_BUNDLE_EXTRACT_BASE_DIR`).

For developer builds without embedded Oodle, the fallback order is an external `oo2core_9_win64.dll` (or `oodle-data-shared.dll`) next to the executable, then an on-demand download from the [pinned OodleUE 2.9.16 release](https://github.com/WorkingRobot/OodleUE/releases/tag/2026-06-04-1357). External files are read without modification; copies and verified downloads are installed only in the service cache. Downloads retain SHA-256 verification, timeout and size limits. `--no-oodle-download` forbids network acquisition; the portable release works with this flag even on its first run. The bridge explicitly initializes Oodle with its absolute cache path; it never searches or downloads libraries relative to the process executable.

Replacement uses the same virtual provider:

```bash
KissE.exe "C:\Output\strings.json" "C:\Game\Content\Paks\Game-WindowsNoEditor.ucas" --virtual --version=4.27 --pack-folder=MyMod
```

Modified assets are written as separate legacy files under the output folder. **The source UCAS is never modified; repacking IoStore is not included.**

### JSON keys and UE string filtering

`KeyValue` is written only when it contains a non-whitespace key; missing/empty keys in older JSON remain readable. `Original` and `NewValue` are unchanged, including an empty `NewValue` ready for translation.

The default filter removes standard UMG placeholders (`Text Block`, `Rich Text Block`, `Editable Text` and multiline/tagged variants), generated widget names such as `TextBlock_123`, and explicit UE identifiers, enum literals and object references. These checks compare normalized text but never rewrite the saved original. Ordinary labels such as `Button`, `Image`, `Text`, and sentences merely mentioning a text block are not rejected by these UE-specific rules.

The same rules clean old placeholders when extraction merges into an existing JSON; keys and translations of retained entries are preserved. Use `--no-filter` to bypass the new UE-specific filtering, including merge cleanup.

### Main arguments:
| Argument | Short | Description |
|----------|-------|-------------|
| `--sconst` | `-sc` | Extract strings EX_StringConst from all UFunction with ScriptBytecode.
| `--tprop` | `-tp` | Extract fallback localization strings with TextProperty type.
| `--lsource` | `-ls` | Extract fallback localization strings with LocalizedSource.
| `--dstable` | `-dst` | Extract fallback localization strings from Data/String Table assets.
| `--alltypes`| `-all`| Extract strings from all possible types (includes Table and LocalizedSource and TextProperty).
| `--no-filter` | `-nf` | Disables string filter function while processing.
| `--no-backup` | `-nobak` | Disables the creation of .bak backup files.
| `-no-underscore` | `-un` | Excludes strings that contain the '_' character.
| `--all-directories` | `-alldir` | Disables filter with specifed directory names for analyze and asset processing.
| `--namespace` | `-ns` | Include name/key::value (e.g., "ENG::Gori") in output JSON
  | `--mapping` | `-map` | Specified .usmap nearby .exe as mappings for processing (e.g., -map="Gori_umap.usmap")
  | `--mapping-auto` | `-ma` | Uses the usmap file if it finds it nearby.
  | `--patch-all-functions` | `-paf` | Iterate the bytecode-replacement pipeline over every UFunction with a ScriptBytecode (not just ExecuteUbergraph_*). Needed for widget event handlers and other functions that hold their (EX_StringConst) outside the ubergraph.
  | `--patch-assignments` | `-pa` | Also replace (EX_StringConst) inside an AssignmentExpression in the ubergraph (off by default to avoid touching technical paths/keys; opt-in for game text hardcoded via 'Set Text' / 'Print String' Blueprint nodes).
  | `--only-key` | `-tok` | If key/name matches then include only this value to output (e.g., -tok=ENG)
  | `--pack-folder` | `-pf` | Translate and pack assets into auto prepared folder (e.g., "ManicMiners_RUS")
  | `--version` | `-v` | Set the engine version for correct processing (e.g., -v=5.1)
  | `--run` | `-r` | Execute a command in the terminal after completion (e.g., --run=[CommandArgs])
| `--debug` | `-d` | Write additional files for debug: `Ubergraph.json`
| `--virtual` | `-vir` | Read PAK/UCAS/UTOC archives in memory; required for IoStore. |
| `--no-oodle-download` | `-noodle` | Disable Oodle downloads; require an embedded or local DLL. |
| `--aes` | `-a` | AES-256 key: 64 hex characters, optionally prefixed with 0x. |
| `--help` | `-h` | Show help information.
### Translator arguments:
| Argument | Short | Description |
|----------|-------|-------------|
  | `--endpoint` | `-e` | Translation service endpoint (e.g., -e=router (yandex, google, microsoft, router as OpenRouter))
  |  `--api-key`| `-api` | Key for OpenRouter api (e.g., -api=sk...)
  | `--api-model`| `-model` | Model for OpenRouter (e.g, -Model=tngtech/deepseek-r1t2-chimera:free)
  | `--source-lang` | `-sl` | Source language for translation (e.g., --sl=en)
  | `--target-lang` | `-tl` | Target language for translation (e.g., --tl=ru)

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

For IoStore, also install Rust (`x86_64-pc-windows-msvc`) and the Visual Studio C++ build tools. From the repository root:

```bash
git submodule update --init ThirdParty/retoc
```

```bash
cargo build --manifest-path Native/IoStoreBridge/Cargo.toml --release --locked
```

```bash
dotnet build KismetEditor/KismetEditor.csproj
```

The .NET build/publish embeds the built bridge when present. If it has not been built, a warning is emitted and PAK/loose-asset support remains available. Rust is only required to build the bridge, not to run KissE. The submodule stays pinned; its upstream code is not modified. The bridge's Cargo.lock pins transitive dependencies.

Publish the autonomous Windows x64 release (run from PowerShell; choose a new or empty output directory):

```bash
powershell.exe -NoProfile -File scripts/publish-portable.ps1 -OodleLibraryPath "C:\Libraries\oo2core_9_win64.dll" -OutputDirectory artifacts/portable
```

The script builds the bridge with a static MSVC runtime and publishes **only `KissE.exe`**, including .NET, managed dependencies, the bridge, Oodle, Oodle's `vcruntime140.dll` dependency and MIT license texts. The redistributable x64 `vcruntime140.dll` is located in the installed Visual Studio C++ tools; `-VcRuntimeLibraryPath` can select an authorized redistributable copy explicitly. It is preloaded by absolute path from the same content-addressed cache before Oodle. No installed .NET, Visual C++ redistributable, or runtime Oodle download is needed for this supported DLL combination. `PortableRelease=true` makes missing embedded native libraries a build error rather than silently producing a reduced release. A different supplied Oodle build must not require additional unbundled native dependencies.

Only embed/distribute Oodle if you have the corresponding rights; it is not committed to this repository. Developer builds remain available with `dotnet build`; `OodleLibraryPath` optionally embeds Oodle there too. `-p:EmbedIoStoreBridge=false` retains the external bridge DLL layout, and `-p:IoStoreBridgePath="C:\Libraries\kisse_iostore.dll"` selects another built bridge. The local `Native/oodle_loader` adaptation retains the pinned Oodle ABI but requires explicit-path initialization; the retoc submodule is unchanged.

Archive regression tests (no game files required):

```bash
dotnet test tests/KismetEditor.Tests/KismetEditor.Tests.csproj
```

```bash
cargo test --manifest-path Native/IoStoreBridge/Cargo.toml --locked
```

Optional real-game smoke test: build `tests/IoStore.Smoke`, optionally supply/embed Oodle for offline use, then run `IoStore.Smoke.exe <archive> <engine version> [round-trip output directory] [usmap]`. It verifies package exports, legacy virtual file reads and reopening; optional round-trip output writes only to the supplied directory.

## Contributions
* You can create your own fork of this project and contribute to its development.
* You can also contribute via the [Issues](https://github.com/SolicenTEAM/KismetEditor/issues) and [Pull Request](https://github.com/SolicenTEAM/KismetEditor/pulls) tabs by suggesting your code changes. And further development of the project. 

## Thanks
- [Ambi](https://github.com/JunkBeat) for idea and his research. 
- [@atenfyr](https://github.com/atenfyr) for research and [UAssetAPI](https://github.com/atenfyr/UAssetAPI).
