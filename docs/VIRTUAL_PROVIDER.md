# What is a virtual provider?

This file briefly describes the function of the virtual provider and its work with the ucas/utoc files of Unreal Engine 4-5.

----------------------------------------------------------------

IoStore requires `--virtual`, the matching `.utoc` and `.ucas`, and the game's `global.utoc/global.ucas` for UE4 script imports. 

Keep sibling containers together for dependency resolution. Only files from the selected container are exposed. Use the actual game engine version, `--mapping=Game.usmap` for unversioned properties when needed, and `--aes=<64 hex characters>` for encrypted containers.

The IoStore backend uses [retoc v0.1.5](https://github.com/trumank/retoc/releases/tag/v0.1.5) (MIT) to convert Zen packages to legacy `.uasset/.uexp` **in memory**, then parses them with the existing UAssetAPI. No temporary PAK or unpacked asset tree is created. Compatibility is limited to the formats supported by that retoc revision (engine versions 4.25–5.7); game-specific formats and assets may require mappings or remain unsupported.

The portable release contains the IoStore bridge and Oodle inside **one self-contained `KissE.exe`**; neither .NET nor separate DLLs need to be installed. Native libraries are extracted on demand to `%LOCALAPPDATA%\KissE\native\win-x64\<SHA256>\`, never next to the EXE. Matching files are reused; different library versions occupy separate directories. Extraction is atomic and protected by a cross-process lock. Only the cache must be writable. PAK and loose-asset operations do not extract these libraries. The bundled .NET runtime stays inside the EXE where supported; native runtime files that need extraction use .NET's own service cache (normally `%TEMP%\.net\KissE\...`, configurable via `DOTNET_BUNDLE_EXTRACT_BASE_DIR`).

For developer builds without embedded Oodle, the fallback order is an external `oo2core_9_win64.dll` (or `oodle-data-shared.dll`) next to the executable, then an on-demand download from the [pinned OodleUE 2.9.16 release](https://github.com/WorkingRobot/OodleUE/releases/tag/2026-06-04-1357). External files are read without modification; copies and verified downloads are installed only in the service cache. Downloads retain SHA-256 verification, timeout and size limits. `--no-oodle-download` forbids network acquisition; the portable release works with this flag even on its first run. The bridge explicitly initializes Oodle with its absolute cache path; it never searches or downloads libraries relative to the process executable.