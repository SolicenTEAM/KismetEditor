param(
    [Parameter(Mandatory = $true)][string]$OodleLibraryPath,
    [string]$VcRuntimeLibraryPath,
    [string]$OutputDirectory = "artifacts/portable"
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
$oldFlags = $env:CARGO_TARGET_X86_64_PC_WINDOWS_MSVC_RUSTFLAGS
try {
    $oodle = (Resolve-Path -LiteralPath $OodleLibraryPath).Path
    if (!$VcRuntimeLibraryPath) {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
        if (!(Test-Path -LiteralPath $vswhere)) { throw "Supply -VcRuntimeLibraryPath with an x64 redistributable vcruntime140.dll." }
        $installation = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
        if ($LASTEXITCODE -ne 0 -or !$installation) { throw "Visual Studio C++ Build Tools not found." }
        $crt = Get-ChildItem -Path "$installation\VC\Redist\MSVC\*\x64\Microsoft.VC*.CRT\vcruntime140.dll" | Sort-Object FullName -Descending | Select-Object -First 1
        if (!$crt) { throw "Redistributable vcruntime140.dll not found; supply -VcRuntimeLibraryPath." }
        $VcRuntimeLibraryPath = $crt.FullName
    }
    $vcRuntime = (Resolve-Path -LiteralPath $VcRuntimeLibraryPath).Path
    $output = [IO.Path]::GetFullPath($OutputDirectory)
    if ((Test-Path -LiteralPath $output) -and @(Get-ChildItem -LiteralPath $output -Force).Count -ne 0) {
        throw "Choose a new or empty output directory: $output"
    }
    & git submodule update --init ThirdParty/retoc
    if ($LASTEXITCODE -ne 0) { throw "Could not initialize the retoc submodule." }
    # Do not depend on an independently installed Visual C++ runtime.
    $env:CARGO_TARGET_X86_64_PC_WINDOWS_MSVC_RUSTFLAGS = "-C target-feature=+crt-static"
    & cargo build --manifest-path Native/IoStoreBridge/Cargo.toml --release --locked --target x86_64-pc-windows-msvc
    if ($LASTEXITCODE -ne 0) { throw "IoStore bridge build failed." }
    & dotnet publish KismetEditor/KismetEditor.csproj -c Release -p:PortableRelease=true "-p:OodleLibraryPath=$oodle" "-p:VcRuntimeLibraryPath=$vcRuntime" -o $output
    if ($LASTEXITCODE -ne 0) { throw "Portable publish failed." }
    $files = @(Get-ChildItem -LiteralPath $output -Force)
    if ($files.Count -ne 1 -or $files[0].Name -ne "KissE.exe") {
        throw "Portable output must contain only KissE.exe; inspect $output"
    }
    Write-Host "Self-contained single-file EXE: $output\KissE.exe"
}
finally {
    $env:CARGO_TARGET_X86_64_PC_WINDOWS_MSVC_RUSTFLAGS = $oldFlags
    Pop-Location
}
