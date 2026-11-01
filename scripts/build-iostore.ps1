param()
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $hostInfo = & rustc -vV
    if ($LASTEXITCODE -ne 0 -or $hostInfo -notcontains "host: x86_64-pc-windows-msvc") {
        throw "Install the x86_64-pc-windows-msvc Rust toolchain and Visual Studio C++ Build Tools."
    }
    & git submodule update --init ThirdParty/retoc
    if ($LASTEXITCODE -ne 0) { throw "Could not initialize the retoc submodule." }
    & cargo build --manifest-path Native/IoStoreBridge/Cargo.toml --release --locked
    if ($LASTEXITCODE -ne 0) { throw "IoStore bridge build failed." }
    Write-Host "IoStore bridge ready. Build KismetEditor to embed it, or use scripts/publish-portable.ps1 for a self-contained EXE."
}
finally {
    Pop-Location
}
