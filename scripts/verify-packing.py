"""Exercise folder packing and translated JudgeSim mods through the actual KissE CLI."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("exe", type=Path)
    parser.add_argument("archive", type=Path)
    parser.add_argument("baseline", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    exe = args.exe.resolve()
    application_files = set(exe.parent.iterdir())
    archive = args.archive.resolve()
    sources = list(archive.parent.glob("*.utoc")) + list(archive.parent.glob("*.ucas"))
    original_hashes = {str(p): digest(p) for p in sources}
    no_runtime = output / "no-dotnet"
    no_runtime.mkdir()
    env = dict(os.environ, DOTNET_ROOT=str(no_runtime), DOTNET_ROOT_X64=str(no_runtime), DOTNET_MULTILEVEL_LOOKUP="0")

    def run(name, arguments, success=True):
        result = subprocess.run([str(exe), *map(str, arguments)], cwd=output, input="", capture_output=True,
                                encoding="utf-8", errors="replace", timeout=180, env=env)
        (output / (name + ".stdout.log")).write_text(result.stdout, encoding="utf-8")
        (output / (name + ".stderr.log")).write_text(result.stderr, encoding="utf-8")
        assert (result.returncode == 0) == success, (name, result.returncode, result.stdout[-5000:], result.stderr)

    fixture = output / "fixture"
    (fixture / "Example/Content").mkdir(parents=True)
    (fixture / "Example/Content/test.txt").write_text("PAK fixture: Привет", encoding="utf-8")
    fixture_pak = output / "fixture_P.pak"
    run("folder-pak", [fixture, "--pack=" + str(fixture_pak), "--version=4.27"])
    hash_pak = digest(fixture_pak)
    run("no-overwrite", [fixture, "--pack=" + str(fixture_pak), "--version=4.27"], False)
    assert digest(fixture_pak) == hash_pak
    run("missing-version", [fixture, "--pack=" + str(output / "missing.pak")], False)
    assert not (output / "missing.pak").exists()

    baseline = json.loads(args.baseline.read_text(encoding="utf-8-sig"))
    selected = []
    translations = ["Киссе: сохранение игры", "Киссе: сохранение свободной игры", "Киссе: доказательства отсутствуют", "Киссе: строка байткода проверена"]
    for name, translation in zip(["SaveGameAnim.uasset", "SaveGameAnimFreeMode.uasset", "MainStringTable1.uasset", "Doorway001.uasset"], translations):
        asset = next(a for a in baseline if a["FileName"] == name)
        value = dict(asset["Values"][0], NewValue=translation)
        selected.append(dict(FileName=name, Values=[value]))
    source_json = output / "translation.json"
    source_json.write_text(json.dumps(selected, ensure_ascii=False, indent=2), encoding="utf-8")
    flags = ["--virtual", "--version=4.27", "--alltypes", "--no-oodle-download", "--patch-all-functions", "--patch-assignments"]
    prepared = output / "prepared"
    for extension in ("pak", "utoc"):
        name = "translated_" + extension + "_P"
        directory = output / name
        directory.mkdir()
        target = directory / (name + "." + extension)
        command = [source_json, archive, *flags, "--pack=" + str(target)]
        if extension == "pak":
            command.append("--pack-folder=" + str(prepared))
        run("replace-" + extension, command)
        if extension == "utoc":
            # Mod containers use the game's global script imports. Copy, never modify, them.
            for p in archive.parent.glob("global.*"):
                if p.suffix in (".utoc", ".ucas"):
                    shutil.copy2(p, directory / p.name)
        strings = output / (extension + "-strings.json")
        run("read-" + extension, [target, strings, *flags])
        actual = json.loads(strings.read_text(encoding="utf-8-sig"))
        texts = {v["Original"] for a in actual for v in a["Values"]}
        assert all(t in texts for t in translations), (extension, "Translated strings missing", texts)
        assert len(actual) == len(selected), (extension, len(actual))
    target = output / "from-folder_P.utoc"
    run("folder-iostore", [prepared, "--pack=" + str(target), "--version=4.27", "--no-oodle-download"])
    for p in archive.parent.glob("global.*"):
        if p.suffix in (".utoc", ".ucas"):
            shutil.copy2(p, output / p.name)
    strings = output / "folder-strings.json"
    run("read-folder-iostore", [target, strings, *flags])
    texts = {v["Original"] for a in json.loads(strings.read_text(encoding="utf-8-sig")) for v in a["Values"]}
    assert all(t in texts for t in translations)
    prepared_hashes = {str(p): digest(p) for p in prepared.rglob('*') if p.is_file()}
    revised = [dict(FileName=a['FileName'], Values=[dict(a['Values'][0], Original=t, NewValue=t + ' / повтор')])
               for a, t in zip(selected, translations)]
    revised_json = output / 'revised.json'
    revised_json.write_text(json.dumps(revised, ensure_ascii=False), encoding='utf-8')
    for name, source, virtual in [('loose', prepared, []),
                                  ('pak-source', output / 'translated_pak_P/translated_pak_P.pak', ['--virtual'])]:
        target = output / (name + '_P.pak')
        run('replace-' + name, [revised_json, source, '--version=4.27', '--alltypes', '--patch-all-functions', '--patch-assignments', *virtual, '--pack=' + str(target)])
        result = output / (name + '-strings.json')
        run('read-' + name, [target, result, *flags])
        texts = {v['Original'] for a in json.loads(result.read_text(encoding='utf-8-sig')) for v in a['Values']}
        assert all(t + ' / повтор' in texts for t in translations), name
    csv = output / 'SaveGameAnim.csv'
    csv.write_text(translations[0] + '|Киссе: CSV проверен\n', encoding='utf-8')
    single = next(prepared.rglob('SaveGameAnim.uasset'))
    single_pak = output / 'single-csv_P.pak'
    run('replace-single-csv', [csv, single, '--version=4.27', '--alltypes', '--pack=' + str(single_pak)])
    csv_result = output / 'csv-strings.json'
    run('read-single-csv', [single_pak, csv_result, *flags])
    texts = {v['Original'] for a in json.loads(csv_result.read_text(encoding='utf-8-sig')) for v in a['Values']}
    assert 'Киссе: CSV проверен' in texts
    assert prepared_hashes == {str(p): digest(p) for p in prepared.rglob('*') if p.is_file()}, 'Loose sources changed'
    invalid = output / 'invalid/Example/Content'
    invalid.mkdir(parents=True)
    (invalid / 'Broken.uasset').write_bytes(b'not a package')
    (invalid / 'Broken.uexp').write_bytes(b'not exports')
    target = output / 'invalid_P.utoc'
    run('failed-conversion', [output / 'invalid', '--pack=' + str(target), '--version=4.27'], False)
    assert all(not target.with_suffix(ext).exists() for ext in ('.utoc', '.ucas', '.pak'))
    assert not list(output.glob('.kisse-pack-*')), 'Working directory leaked'
    assert original_hashes == {str(p): digest(p) for p in sources}, "Source archives changed"
    assert application_files == set(exe.parent.iterdir()), "Files leaked beside EXE"
    report = dict(status="passed", folder_pak=True, translated_pak=True, translated_iostore=True,
                  folder_iostore=True, translated_assets=len(selected), verified_translations=translations,
                  source_archives_unchanged=True, loose_sources_unchanged=True,
                  loose_replacement=True, pak_source_replacement=True, single_asset_csv_replacement=True,
                  no_partial_output_on_error=True, no_files_beside_exe=True, no_overwrite=True,
                  exe=str(exe), exe_bytes=exe.stat().st_size)
    (output / "verification.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(report, ensure_ascii=True, indent=2))


if __name__ == "__main__":
    main()
