"""Verify a portable EXE against a real IoStore archive without adjacent DLLs."""
import argparse
import collections
import concurrent.futures
import ctypes
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import time


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def records(data):
    return collections.Counter(
        (asset["FileName"], value["Original"], value.get("KeyValue"), value["NewValue"])
        for asset in data for value in asset["Values"]
    )


def module_paths(process):
    psapi = ctypes.WinDLL("psapi", use_last_error=True)
    psapi.EnumProcessModulesEx.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_ulong,
                                          ctypes.POINTER(ctypes.c_ulong), ctypes.c_ulong]
    psapi.GetModuleFileNameExW.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_wchar_p, ctypes.c_ulong]
    modules = (ctypes.c_void_p * 2048)()
    needed = ctypes.c_ulong()
    handle = ctypes.c_void_p(int(process._handle))
    if not psapi.EnumProcessModulesEx(handle, modules, ctypes.sizeof(modules), ctypes.byref(needed), 3):
        return set()
    paths = set()
    for module in modules[:min(len(modules), needed.value // ctypes.sizeof(ctypes.c_void_p))]:
        buffer = ctypes.create_unicode_buffer(32768)
        if psapi.GetModuleFileNameExW(handle, module, buffer, len(buffer)):
            # MSIX hosts may expose logical paths that resolve to a package-local cache.
            paths.add(str(Path(buffer.value).resolve()).casefold())
    return paths


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("exe", type=Path)
    parser.add_argument("archive", type=Path)
    parser.add_argument("baseline", type=Path)
    parser.add_argument("bridge", type=Path)
    parser.add_argument("oodle", type=Path)
    parser.add_argument("vc_runtime", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    application = output / "application"
    application.mkdir()
    exe = application / "KissE.exe"
    shutil.copy2(args.exe, exe)
    empty_runtime = output / "no-installed-dotnet"
    empty_runtime.mkdir()
    bundle_cache = output / "dotnet-cache"
    env = dict(os.environ, DOTNET_ROOT=str(empty_runtime), DOTNET_ROOT_X64=str(empty_runtime),
               DOTNET_MULTILEVEL_LOOKUP="0", DOTNET_BUNDLE_EXTRACT_BASE_DIR=str(bundle_cache),
               COREHOST_TRACE="1")
    cache = Path(os.environ["LOCALAPPDATA"]) / "KissE/native/win-x64"
    libraries = [cache / sha256(args.bridge) / "kisse_iostore.dll",
                 cache / sha256(args.oodle) / "oo2core_9_win64.dll",
                 cache / sha256(args.vc_runtime) / "vcruntime140.dll"]
    cache_existed = {str(path): path.exists() for path in libraries}
    archive = args.archive.resolve()
    source_files = sorted(archive.parent.glob("*.utoc")) + sorted(archive.parent.glob("*.ucas"))
    before = {str(path): sha256(path) for path in source_files}
    baseline = json.loads(args.baseline.read_text(encoding="utf-8-sig"))
    expected = records(baseline)

    loaded_modules = set()

    def run(name, arguments):
        trace = output / (name + ".host.log")
        with subprocess.Popen([str(exe), *arguments], cwd=application,
                              env=dict(env, COREHOST_TRACEFILE=str(trace)), stdin=subprocess.PIPE,
                              stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
                              encoding="utf-8", errors="replace") as process:
            with concurrent.futures.ThreadPoolExecutor(max_workers=1) as executor:
                future = executor.submit(process.communicate, "", timeout=180)
                deadline = time.monotonic() + 180
                while not future.done():
                    loaded_modules.update(module_paths(process))
                    if time.monotonic() > deadline:
                        process.kill()
                        raise TimeoutError("CLI did not finish within 180 seconds")
                    time.sleep(0.05)
                stdout, stderr = future.result()
            code = process.returncode
        (output / (name + ".stdout.log")).write_text(stdout, encoding="utf-8")
        (output / (name + ".stderr.log")).write_text(stderr, encoding="utf-8")
        assert code == 0, (name, code, stderr, stdout[-3000:])
        assert [path.name for path in application.iterdir()] == ["KissE.exe"], "Files leaked beside EXE"
        return trace.read_text(encoding="utf-8-sig", errors="replace")

    help_trace = run("help", ["--help"])
    assert "Executing as a self-contained app" in help_trace, "Application depends on an installed runtime"
    embedded_runtime = "HOSTPOLICY_EMBEDDED = true" in help_trace and "CoreCLR path = ''" in help_trace
    assert embedded_runtime or ("coreclr.dll" in help_trace.lower() and str(bundle_cache).lower() in help_trace.lower()), \
        "Runtime was neither embedded nor loaded from the isolated bundle cache"
    snapshots = []
    for number in (1, 2):
        destination = output / f"JudgeSim-{number}.json"
        run(f"extract-{number}", [str(archive), str(destination), "--virtual", "--version=4.27",
                                 "--alltypes", "--no-oodle-download"])
        actual = json.loads(destination.read_text(encoding="utf-8-sig"))
        assert records(actual) == expected, "Strings, translations or keys changed"
        assert len(actual) == len(baseline)
        assert all(sha256(path) == path.parent.name for path in libraries), "Cached DLL hash mismatch"
        snapshots.append({str(path): path.stat().st_mtime_ns for path in libraries})
    assert snapshots[0] == snapshots[1], "Second launch rewrote matching cached DLLs"
    (output / "loaded-modules.json").write_text(json.dumps(sorted(loaded_modules), indent=2), encoding="utf-8")
    missing = [str(path.resolve()) for path in libraries if str(path.resolve()).casefold() not in loaded_modules]
    assert not missing, "Native DLLs not observed at cache paths: " + repr(missing)
    assert before == {str(path): sha256(path) for path in source_files}, "Source archives changed"
    report = dict(status="passed", exe=str(exe), exe_bytes=exe.stat().st_size,
                  assets=len(baseline), strings=sum(expected.values()), extra_files_beside_exe=[],
                  installed_runtime_lookup_disabled=True, bundled_coreclr_loaded=True,
                  native_cache=str(cache), cache_existed_before=cache_existed,
                  native_dlls_loaded_from_cache=True,
                  native_dlls={str(path): sha256(path) for path in libraries},
                  cached_dlls_unchanged_on_second_launch=True, oodle_download_disabled=True,
                  matches_filtered_baseline=True, source_archives_unchanged=True)
    (output / "verification.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
