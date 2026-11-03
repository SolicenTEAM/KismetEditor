use anyhow::{Context, Result, bail, ensure};
use clap::ValueEnum;
use retoc::{
    EIoStoreTocVersion, FPackageId, FSHAHash, UEPath,
    container_header::EIoContainerHeaderVersion,
    iostore,
    iostore_writer::IoStoreWriter,
    legacy_asset::FSerializedAssetBundle,
    logging::Log,
    script_objects::ZenScriptObjects,
    shader_library,
    version::EngineVersion,
    zen::FZenPackageHeader,
    zen_asset_conversion::{self, ConvertedZenAssetBundle},
};
use std::{
    collections::{HashMap, HashSet},
    fs::{self, File, OpenOptions},
    io::{BufWriter, Cursor, Write},
    path::{Path, PathBuf},
    sync::{Arc, RwLock},
};

fn pak_version(version: &str) -> Result<repak::Version> {
    let version = version
        .strip_prefix("UE")
        .context("Specify --version, e.g. 4.27")?;
    let (major, minor) = version.split_once('_').context("Invalid engine version")?;
    let version = (major.parse::<u32>()?, minor.parse::<u32>()?);
    Ok(match version {
        (4, 0..=2) => repak::Version::V2,
        (4, 3..=15) => repak::Version::V3,
        (4, 16..=19) => repak::Version::V4,
        (4, 20) => repak::Version::V5,
        (4, 21) => repak::Version::V7,
        (4, 22) => repak::Version::V8A,
        (4, 23..=24) => repak::Version::V8B,
        (4, 25) => repak::Version::V9,
        (4, 26..=27) | (5, 0..=7) => repak::Version::V11,
        _ => bail!("Unsupported packing engine version UE{major}_{minor}"),
    })
}

fn reject_link(path: &Path) -> Result<()> {
    let metadata = fs::symlink_metadata(path)?;
    ensure!(
        !metadata.is_symlink(),
        "Links are not allowed in packing inputs: {}",
        path.display()
    );
    #[cfg(windows)]
    {
        use std::os::windows::fs::MetadataExt;
        ensure!(
            metadata.file_attributes() & 0x400 == 0,
            "Reparse points are not allowed: {}",
            path.display()
        );
    }
    Ok(())
}

fn validate_files(root: &Path, manifest: &str) -> Result<Vec<String>> {
    reject_link(root)?;
    ensure!(root.is_dir(), "Packing input must be a directory");
    let mut files: Vec<String> = serde_json::from_str(manifest)?;
    ensure!(!files.is_empty(), "Packing input is empty");
    let mut seen = HashSet::new();
    for file in &files {
        ensure!(
            !file.is_empty() && !file.contains(['\\', ':']) && !file.starts_with('/'),
            "Unsafe packing path: {file}"
        );
        let mut path = root.to_path_buf();
        for part in file.split('/') {
            ensure!(
                !part.is_empty() && part != "." && part != "..",
                "Unsafe packing path: {file}"
            );
            path.push(part);
            reject_link(&path)?;
        }
        ensure!(path.is_file(), "Packing file is missing: {file}");
        ensure!(
            seen.insert(file.to_lowercase()),
            "Duplicate packing path: {file}"
        );
    }
    files.sort();
    Ok(files)
}

fn write_pak(root: &Path, files: &[String], output: &Path, version: repak::Version) -> Result<()> {
    let file = OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(output)?;
    let mut pak =
        repak::PakBuilder::new().writer(BufWriter::new(file), version, "../../../".into(), None);
    for path in files {
        pak.write_file(path, false, fs::read(root.join(path))?)?;
    }
    let mut file = pak.write_index()?;
    file.flush()?;
    file.get_ref().sync_all()?;
    drop(file);
    let mut file = File::open(output)?;
    let reader = repak::PakBuilder::new().reader(&mut file)?;
    ensure!(
        reader.files().len() == files.len(),
        "PAK verification: file count mismatch"
    );
    for path in files {
        ensure!(
            reader.get(path, &mut file)? == fs::read(root.join(path))?,
            "PAK verification failed: {path}"
        );
    }
    Ok(())
}

fn write_iostore(
    root: &Path,
    files: &[String],
    output: &Path,
    version: EngineVersion,
) -> Result<()> {
    let log = Log::no_log();
    let header_version = version.container_header_version();
    let toc_version = version.toc_version();
    let mount = UEPath::new("../../../");
    let file_lookup: HashMap<String, &str> = files
        .iter()
        .map(|p| (p.to_lowercase(), p.as_str()))
        .collect();
    let assets: Vec<_> = files
        .iter()
        .filter(|p| {
            matches!(
                UEPath::new(p)
                    .extension()
                    .unwrap_or_default()
                    .to_ascii_lowercase()
                    .as_str(),
                "uasset" | "umap"
            )
        })
        .collect();
    ensure!(
        !assets.is_empty(),
        "IoStore requires cooked .uasset/.umap packages with .uexp files"
    );
    let mut consumed: HashSet<String> = HashSet::new();
    for path in &assets {
        let exports = UEPath::new(path).with_extension("uexp").to_string();
        ensure!(
            file_lookup.contains_key(&exports.to_lowercase()),
            "Cooked package is missing its .uexp: {path}"
        );
    }
    let mut writer = IoStoreWriter::new(output, toc_version, Some(header_version), mount.into())?;
    let mut shader_maps: HashMap<String, Vec<FSHAHash>> = HashMap::new();
    for path in files.iter().filter(|p| {
        UEPath::new(p)
            .extension()
            .is_some_and(|e| e.eq_ignore_ascii_case("ushaderbytecode"))
    }) {
        let ue = UEPath::new(path);
        let metadata = shader_library::get_shader_asset_info_filename_from_library_filename(
            ue.file_name().unwrap(),
        )?;
        let metadata = ue.parent().unwrap_or(UEPath::new("")).join(metadata);
        if let Some(actual) = file_lookup.get(&metadata.as_str().to_lowercase()) {
            shader_library::read_shader_asset_info(
                &fs::read(root.join(actual))?,
                &mut shader_maps,
            )?;
            consumed.insert(actual.to_string());
        }
        shader_library::write_io_store_library(
            &mut writer,
            &fs::read(root.join(path))?,
            &mount.join(ue),
            &log,
        )?;
        consumed.insert(path.clone());
    }
    let script_objects = if toc_version > EIoStoreTocVersion::PerfectHash {
        files
            .iter()
            .find(|p| UEPath::new(p).file_name() == Some("scriptobjects.bin"))
            .map(|p| ZenScriptObjects::deserialize_new(&mut Cursor::new(fs::read(root.join(p))?)))
            .transpose()?
            .map(Arc::new)
    } else {
        None
    };
    let cells = retoc::build_verse_cell_store(&vec![]);
    let needs_fixup = header_version <= EIoContainerHeaderVersion::Initial;
    let mut converted: HashMap<FPackageId, Arc<RwLock<ConvertedZenAssetBundle>>> = HashMap::new();
    let mut order = Vec::new();
    let mut package_ids = HashSet::new();
    for path in &assets {
        let ue = UEPath::new(path);
        let mut read = |extension: &str| -> Result<Option<Vec<u8>>> {
            let companion = ue.with_extension(extension).to_string();
            let Some(actual) = file_lookup.get(&companion.to_lowercase()) else {
                return Ok(None);
            };
            consumed.insert(actual.to_string());
            Ok(Some(fs::read(root.join(actual))?))
        };
        let bundle = FSerializedAssetBundle {
            asset_file_buffer: fs::read(root.join(path))?,
            exports_file_buffer: read("uexp")?.unwrap(),
            bulk_data_buffer: read("ubulk")?,
            optional_bulk_data_buffer: read("uptnl")?,
            memory_mapped_bulk_data_buffer: read("m.ubulk")?,
        };
        consumed.insert((*path).clone());
        let mut asset = zen_asset_conversion::build_zen_asset(
            bundle,
            &shader_maps,
            &mount.join(ue),
            Some(version.package_file_version()),
            header_version,
            needs_fixup,
            script_objects.clone(),
            Some(cells.clone()),
            &log,
        )
        .with_context(|| format!("Converting {path}"))?;
        ensure!(
            package_ids.insert(asset.package_id),
            "Duplicate IoStore package ID for {path}"
        );
        if needs_fixup {
            asset.write_and_release_bulk_data(&mut writer)?;
            order.push(asset.package_id);
            converted.insert(asset.package_id, Arc::new(RwLock::new(asset)));
        } else {
            asset.write(&mut writer)?;
        }
    }
    for id in &order {
        let mut asset = converted[id]
            .write()
            .map_err(|_| anyhow::anyhow!("Package lock poisoned"))?;
        if needs_fixup {
            asset.fixup_legacy_external_arcs(&converted, &log)?;
        }
        asset.write_package_data(&mut writer)?;
    }
    for path in files {
        if !consumed.contains(path)
            && matches!(
                UEPath::new(path)
                    .extension()
                    .unwrap_or_default()
                    .to_ascii_lowercase()
                    .as_str(),
                "uexp" | "ubulk" | "uptnl"
            )
        {
            bail!("Orphaned package sidecar: {path}");
        }
    }
    writer.finalize()?;
    let extra: Vec<_> = files
        .iter()
        .filter(|p| !consumed.contains(*p))
        .cloned()
        .collect();
    write_pak(
        root,
        &extra,
        &output.with_extension("pak"),
        pak_version(&format!("{version:?}"))?,
    )?;
    // Reopen the actual container and read every chunk before publishing it.
    let store = iostore::open(output, Arc::new(retoc::Config::default()))?;
    ensure!(
        store.packages().count() == assets.len(),
        "IoStore verification: package count mismatch"
    );
    for chunk in store.chunks() {
        let data = store.read(chunk.id())?;
        ensure!(
            data.len() as u64 == chunk.size(),
            "IoStore verification: chunk size mismatch"
        );
    }
    for package in store.packages() {
        let id = retoc::FIoChunkId::from_package_id(
            package.id(),
            0,
            retoc::EIoChunkType::ExportBundleData,
        );
        FZenPackageHeader::deserialize(
            &mut Cursor::new(store.read(id)?),
            store.package_store_entry(package.id()),
            toc_version,
            header_version,
            Some(version.package_file_version()),
        )?;
    }
    Ok(())
}

pub fn pack(root: &Path, manifest: &str, output: &Path, version: &str) -> Result<()> {
    let files = validate_files(root, manifest)?;
    let pak = pak_version(version)?;
    let extension = output
        .extension()
        .and_then(|e| e.to_str())
        .unwrap_or_default()
        .to_ascii_lowercase();
    ensure!(
        extension == "pak" || extension == "utoc",
        "--pack requires a .pak or .utoc output"
    );
    let outputs: Vec<PathBuf> = if extension == "utoc" {
        ["utoc", "ucas", "pak"]
            .into_iter()
            .map(|e| output.with_extension(e))
            .collect()
    } else {
        vec![output.to_path_buf()]
    };
    for path in &outputs {
        ensure!(
            !path.try_exists()?,
            "Refusing to overwrite {}",
            path.display()
        );
    }
    if extension == "pak" {
        write_pak(root, &files, output, pak)
    } else {
        if files.iter().any(|p| {
            UEPath::new(p)
                .extension()
                .is_some_and(|e| e.eq_ignore_ascii_case("ushaderbytecode"))
        }) && !oodle_loader::is_initialized()
        {
            return Err(super::OodleRequired.into());
        }
        let version = EngineVersion::from_str(version, true)
            .map_err(|e| anyhow::anyhow!("{e}. IoStore packing supports UE4.25 through UE5.7."))?;
        write_iostore(root, &files, output, version)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    fn temporary() -> PathBuf {
        let path = std::env::temp_dir().join(format!(
            "KissE-pack-{}-{}",
            std::process::id(),
            std::time::SystemTime::now()
                .duration_since(std::time::UNIX_EPOCH)
                .unwrap()
                .as_nanos()
        ));
        fs::create_dir_all(&path).unwrap();
        path
    }
    #[test]
    fn pak_round_trip_uses_engine_appropriate_versions() -> Result<()> {
        let dir = temporary();
        let root = dir.join("input");
        fs::create_dir_all(root.join("Example/Content"))?;
        fs::write(root.join("Example/Content/Text.txt"), "Привет")?;
        for version in ["UE4_18", "UE4_22", "UE4_25", "UE4_27", "UE5_3"] {
            let path = dir.join(format!("{version}.pak"));
            pack(&root, r#"["Example/Content/Text.txt"]"#, &path, version)?;
            let mut file = File::open(path)?;
            let pak = repak::PakBuilder::new().reader(&mut file)?;
            assert_eq!(pak.version(), pak_version(version)?);
        }
        fs::remove_dir_all(dir)?;
        Ok(())
    }
    #[test]
    fn rejects_unsafe_duplicate_and_missing_files_before_creating_output() -> Result<()> {
        let root = temporary();
        fs::write(root.join("one.txt"), "one")?;
        let output = root.with_extension("pak");
        for manifest in [
            r#"["../one.txt"]"#,
            r#"["/one.txt"]"#,
            r#"["one.txt","one.txt"]"#,
            r#"["missing"]"#,
            "[]",
        ] {
            assert!(pack(&root, manifest, &output, "UE4_27").is_err());
            assert!(!output.exists());
        }
        fs::remove_dir_all(root)?;
        Ok(())
    }
    #[test]
    fn synthetic_cooked_package_and_bulk_round_trip_through_iostore() -> Result<()> {
        use retoc::legacy_asset::{EPackageFlags, FLegacyPackageHeader};
        let directory = temporary();
        let root = directory.join("input");
        fs::create_dir_all(root.join("Example/Content"))?;
        let mut package = FLegacyPackageHeader::default();
        package.summary.versioning_info.legacy_file_version = -7;
        package.summary.versioning_info.package_file_version =
            EngineVersion::UE4_27.package_file_version();
        package.summary.package_name = "/Game/Empty".into();
        package.summary.package_flags =
            EPackageFlags::Cooked as u32 | EPackageFlags::FilterEditorOnly as u32;
        package.name_map.store("None");
        let mut header = Cursor::new(Vec::new());
        package.serialize(&mut header, None, &Log::no_log())?;
        fs::write(
            root.join("Example/Content/Empty.uasset"),
            header.into_inner(),
        )?;
        fs::write(
            root.join("Example/Content/Empty.uexp"),
            0x9E2A83C1u32.to_le_bytes(),
        )?;
        fs::write(root.join("Example/Content/Empty.ubulk"), b"bulk bytes")?;
        fs::write(root.join("Example/Content/Empty.uptnl"), b"optional bytes")?;
        fs::write(root.join("Example/Content/Empty.m.ubulk"), b"mapped bytes")?;
        fs::write(root.join("Example/Content/extra.txt"), b"companion pak")?;
        let files: Vec<String> = ["uasset", "uexp", "ubulk", "uptnl", "m.ubulk"]
            .iter()
            .map(|ext| format!("Example/Content/Empty.{ext}"))
            .chain(["Example/Content/extra.txt".into()])
            .collect();
        let output = directory.join("mod_P.utoc");
        pack(&root, &serde_json::to_string(&files)?, &output, "UE4_27")?;
        let store = iostore::open(&output, Arc::new(retoc::Config::default()))?;
        let package = store.packages().next().context("No package written")?.id();
        for (kind, expected) in [
            (retoc::EIoChunkType::BulkData, b"bulk bytes".as_slice()),
            (
                retoc::EIoChunkType::OptionalBulkData,
                b"optional bytes".as_slice(),
            ),
            (
                retoc::EIoChunkType::MemoryMappedBulkData,
                b"mapped bytes".as_slice(),
            ),
        ] {
            assert_eq!(
                store.read(retoc::FIoChunkId::from_package_id(package, 0, kind))?,
                expected
            );
        }
        drop(store);
        fs::remove_dir_all(directory)?;
        Ok(())
    }

    #[test]
    fn rejects_existing_outputs_and_unsupported_versions() -> Result<()> {
        let dir = temporary();
        fs::write(dir.join("one.txt"), "one")?;
        let output = dir.join("mod.pak");
        fs::write(&output, "keep")?;
        assert!(pack(&dir, r#"["one.txt"]"#, &output, "UE4_27").is_err());
        assert_eq!(fs::read_to_string(&output)?, "keep");
        assert!(pak_version("UE6_0").is_err());
        fs::remove_dir_all(dir)?;
        Ok(())
    }
}
