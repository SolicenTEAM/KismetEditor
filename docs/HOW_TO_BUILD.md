# How to build project

This file briefly describes the necessary components for the assembly and testing of all program functions, including the import of third-party libraries for IoStore.

----------------------------------------------------------------

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