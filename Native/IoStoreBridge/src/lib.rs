mod packing;

use anyhow::{Context, Result, ensure};
use clap::ValueEnum;
use retoc::{
    Config, EIoChunkType, EIoStoreTocVersion, FIoChunkId, FIoChunkIdRaw, FPackageId,
    FileWriterTrait, Toc,
    asset_conversion::{FZenPackageContext, build_legacy},
    container_header::{EIoContainerHeaderVersion, StoreEntry},
    iostore::{self, ChunkInfo, IoStoreTrait, PackageInfo},
    logging::Log,
    ser::*,
    version::EngineVersion,
    zen::FPackageFileVersion,
};
use std::{
    collections::HashMap,
    ffi::{CStr, c_char},
    fs::File,
    io::{BufReader, Seek, SeekFrom},
    panic::{AssertUnwindSafe, catch_unwind},
    path::Path,
    ptr,
    sync::{Arc, Mutex},
};
use typed_path::Utf8UnixPath as UEPath;

#[repr(C)]
#[derive(Default)]
pub struct Buffer {
    data: *mut u8,
    len: usize,
}
impl Buffer {
    fn from_vec(data: Vec<u8>) -> Self {
        let mut data = data.into_boxed_slice();
        let result = Self {
            data: data.as_mut_ptr(),
            len: data.len(),
        };
        std::mem::forget(data);
        result
    }
}

#[repr(C)]
#[derive(Default)]
pub struct AssetBuffers {
    header: Buffer,
    exports: Buffer,
    bulk: Buffer,
    optional_bulk: Buffer,
    mapped_bulk: Buffer,
}

#[derive(Debug)]
struct OodleRequired;
impl std::fmt::Display for OodleRequired {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(
            f,
            "Oodle-compressed IoStore requires Oodle initialization from an absolute library path"
        )
    }
}
impl std::error::Error for OodleRequired {}

struct Archive {
    store: Box<dyn IoStoreTrait>,
    files: HashMap<String, (String, FIoChunkId)>,
    selected_name: String,
    version: FPackageFileVersion,
}

fn normalize(path: &str) -> String {
    let path = path.replace('\\', "/");
    let mut path = path.trim_start_matches('/');
    while let Some(rest) = path.strip_prefix("../") {
        path = rest;
    }
    path.to_lowercase()
}

fn open_archive(path: &Path, aes: &str, version: &str) -> Result<Archive> {
    let version = EngineVersion::from_str(version, true).map_err(|e| {
        anyhow::anyhow!("{e}. Specify an IoStore engine version with --version (4.25 through 5.7).")
    })?;
    let path = path.canonicalize().context("Archive not found")?;
    let directory = path.parent().context("Archive has no parent directory")?;
    let mut config = Config::default();
    for entry in std::fs::read_dir(directory)? {
        let entry = entry?;
        if entry.path().extension().and_then(|e| e.to_str()) != Some("utoc") {
            continue;
        }
        // Use the supplied key for each container's actual GUID, not just the zero GUID.
        let mut input = File::open(entry.path())?;
        input.seek(SeekFrom::Start(64))?;
        let guid = input.de()?;
        if !aes.is_empty() {
            config.aes_keys.insert(guid, aes.parse()?);
        }
    }
    let config = Arc::new(config);
    for entry in std::fs::read_dir(directory)? {
        let entry = entry?;
        if entry.path().extension().and_then(|e| e.to_str()) != Some("utoc") {
            continue;
        }
        let toc: Toc = BufReader::new(File::open(entry.path())?).de_ctx(config.clone())?;
        if toc
            .compression_methods
            .contains(&retoc::compression::CompressionMethod::Oodle)
            && !oodle_loader::is_initialized()
        {
            return Err(OodleRequired.into());
        }
    }
    let store = iostore::open(directory, config)?;
    let name = path
        .file_stem()
        .and_then(|n| n.to_str())
        .context("Invalid archive name")?;
    let selected = store
        .child_containers()
        .find(|c| c.container_name() == name)
        .context("Selected .utoc was not mounted (use a lowercase .utoc extension)")?;
    let mut files = HashMap::new();
    for chunk in selected.chunks() {
        if let Some(path) = chunk.path() {
            let key = normalize(&path);
            files.insert(key.clone(), (path.clone(), chunk.id()));
            if chunk.id().get_chunk_type() == EIoChunkType::ExportBundleData {
                let exports = UEPath::new(&path).with_extension("uexp").to_string();
                files.insert(normalize(&exports), (exports, chunk.id()));
            }
        }
    }
    ensure!(
        !files.is_empty(),
        "Container has no directory index / virtual files"
    );
    Ok(Archive {
        store,
        files,
        selected_name: name.to_owned(),
        version: version.package_file_version(),
    })
}

// The selected container wins for its chunks; siblings provide imports/global script objects.
// Retoc's composite store handles sibling priority. Explicit reads below use the selected chunk's container.
struct ReadView<'a> {
    inner: &'a dyn IoStoreTrait,
    selected: &'a dyn IoStoreTrait,
    skip_bulk: bool,
    max_bytes: u64,
}
impl ReadView<'_> {
    fn is_hidden(&self, id: FIoChunkId) -> bool {
        self.skip_bulk
            && matches!(
                id.get_chunk_type(),
                EIoChunkType::BulkData
                    | EIoChunkType::OptionalBulkData
                    | EIoChunkType::MemoryMappedBulkData
            )
    }
    fn source(&self, id: FIoChunkId) -> &dyn IoStoreTrait {
        if self.selected.has_chunk_id(id) {
            self.selected
        } else {
            self.inner
        }
    }
}
impl IoStoreTrait for ReadView<'_> {
    fn container_name(&self) -> &str {
        self.inner.container_name()
    }
    fn container_file_version(&self) -> Option<EIoStoreTocVersion> {
        self.inner.container_file_version()
    }
    fn container_header_version(&self) -> Option<EIoContainerHeaderVersion> {
        self.inner.container_header_version()
    }
    fn print_info(&self, depth: usize) {
        self.inner.print_info(depth);
    }
    fn read(&self, id: FIoChunkId) -> Result<Vec<u8>> {
        ensure!(!self.is_hidden(id), "Bulk data is disabled");
        let source = self.source(id);
        if self.max_bytes > 0 {
            let id = id.with_version(
                self.container_file_version()
                    .context("Missing TOC version")?,
            );
            if let Some(chunk) = source.chunks().find(|c| c.id() == id) {
                ensure!(
                    chunk.size() <= self.max_bytes,
                    "Chunk exceeds the {} byte asset limit",
                    self.max_bytes
                );
            }
        }
        source.read(id)
    }
    fn read_raw(&self, id: FIoChunkIdRaw) -> Result<Vec<u8>> {
        self.inner.read_raw(id)
    }
    fn has_chunk_id(&self, id: FIoChunkId) -> bool {
        !self.is_hidden(id) && self.source(id).has_chunk_id(id)
    }
    fn has_chunk_id_raw(&self, id: FIoChunkIdRaw) -> bool {
        self.inner.has_chunk_id_raw(id)
    }
    fn chunks(&self) -> Box<dyn Iterator<Item = ChunkInfo<'_>> + Send + '_> {
        self.inner.chunks()
    }
    fn chunks_all(&self) -> Box<dyn Iterator<Item = ChunkInfo<'_>> + Send + '_> {
        self.inner.chunks_all()
    }
    fn packages(&self) -> Box<dyn Iterator<Item = PackageInfo<'_>> + Send + '_> {
        self.inner.packages()
    }
    fn packages_all(&self) -> Box<dyn Iterator<Item = PackageInfo<'_>> + Send + '_> {
        self.inner.packages_all()
    }
    fn child_containers(&self) -> Box<dyn Iterator<Item = &dyn IoStoreTrait> + '_> {
        self.inner.child_containers()
    }
    fn chunk_path(&self, id: FIoChunkId) -> Option<String> {
        self.source(id).chunk_path(id)
    }
    fn package_store_entry(&self, id: FPackageId) -> Option<StoreEntry> {
        self.selected
            .package_store_entry(id)
            .or_else(|| self.inner.package_store_entry(id))
    }
    fn lookup_package_redirect(&self, id: FPackageId) -> Option<FPackageId> {
        self.inner.lookup_package_redirect(id)
    }
}

#[derive(Default)]
struct MemoryWriter(Mutex<HashMap<String, Vec<u8>>>);
impl FileWriterTrait for MemoryWriter {
    fn write_file(&self, path: String, _allow_compress: bool, data: Vec<u8>) -> Result<()> {
        self.0
            .lock()
            .map_err(|_| anyhow::anyhow!("Memory writer lock poisoned"))?
            .insert(path, data);
        Ok(())
    }
}
impl Archive {
    fn selected(&self) -> &dyn IoStoreTrait {
        self.store
            .child_containers()
            .find(|c| c.container_name() == self.selected_name)
            .unwrap()
    }
    fn read_asset(&self, path: &str, skip_bulk: bool, max_bytes: u64) -> Result<AssetBuffers> {
        let (_, id) = self
            .files
            .get(&normalize(path))
            .context("Virtual asset not found")?;
        ensure!(
            id.get_chunk_type() == EIoChunkType::ExportBundleData,
            "Entry is not an asset package"
        );
        let package = id.get_package_id();
        let view = ReadView {
            inner: self.store.as_ref(),
            selected: self.selected(),
            skip_bulk,
            max_bytes,
        };
        let log = Log::no_log();
        let context = FZenPackageContext::create(&view, Some(self.version), &log, None);
        let writer = MemoryWriter::default();
        build_legacy(&context, package, UEPath::new("asset.uasset"), &writer)?;
        let mut files = writer
            .0
            .into_inner()
            .map_err(|_| anyhow::anyhow!("Memory writer lock poisoned"))?;
        let header = files
            .remove("asset.uasset")
            .context("Conversion produced no header")?;
        let exports = files
            .remove("asset.uexp")
            .context("Conversion produced no exports")?;
        ensure!(
            max_bytes == 0 || (header.len() as u64 + exports.len() as u64) <= max_bytes,
            "Converted asset exceeds the {max_bytes} byte limit"
        );
        Ok(AssetBuffers {
            header: Buffer::from_vec(header),
            exports: Buffer::from_vec(exports),
            bulk: Buffer::from_vec(files.remove("asset.ubulk").unwrap_or_default()),
            optional_bulk: Buffer::from_vec(files.remove("asset.uptnl").unwrap_or_default()),
            mapped_bulk: Buffer::from_vec(files.remove("asset.m.ubulk").unwrap_or_default()),
        })
    }
}

unsafe fn text<'a>(value: *const c_char) -> Result<&'a str> {
    ensure!(!value.is_null(), "Null string argument");
    Ok(unsafe { CStr::from_ptr(value) }.to_str()?)
}
fn boundary(error: *mut Buffer, action: impl FnOnce() -> Result<()>) -> i32 {
    if !error.is_null() {
        unsafe {
            *error = Buffer::default();
        }
    }
    let result = catch_unwind(AssertUnwindSafe(action));
    let mut status = 1;
    let message = match result {
        Ok(Ok(())) => return 0,
        Ok(Err(error)) => {
            if error.is::<OodleRequired>() {
                status = 2;
            }
            format!("{error:#}")
        }
        Err(payload) => format!(
            "IoStore panic: {}",
            payload
                .downcast_ref::<String>()
                .map(String::as_str)
                .or_else(|| payload.downcast_ref::<&str>().copied())
                .unwrap_or("unknown error")
        ),
    };
    if !error.is_null() {
        unsafe {
            *error = Buffer::from_vec(message.into_bytes());
        }
    }
    status
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn kisse_io_pack(
    root: *const c_char,
    manifest: *const c_char,
    output: *const c_char,
    version: *const c_char,
    error: *mut Buffer,
) -> i32 {
    boundary(error, || {
        packing::pack(
            Path::new(unsafe { text(root)? }),
            unsafe { text(manifest)? },
            Path::new(unsafe { text(output)? }),
            unsafe { text(version)? },
        )
    })
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn kisse_io_set_oodle(path: *const c_char, error: *mut Buffer) -> i32 {
    boundary(error, || {
        oodle_loader::initialize(Path::new(unsafe { text(path)? }))?;
        Ok(())
    })
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn kisse_io_open(
    path: *const c_char,
    aes: *const c_char,
    version: *const c_char,
    output: *mut *mut std::ffi::c_void,
    error: *mut Buffer,
) -> i32 {
    boundary(error, || {
        ensure!(!output.is_null(), "Null output handle");
        unsafe {
            *output = ptr::null_mut();
        }
        let archive = open_archive(
            Path::new(unsafe { text(path)? }),
            unsafe { text(aes)? },
            unsafe { text(version)? },
        )?;
        unsafe {
            *output = Box::into_raw(Box::new(archive)).cast();
        }
        Ok(())
    })
}
#[unsafe(no_mangle)]
pub unsafe extern "C" fn kisse_io_files(
    handle: *mut std::ffi::c_void,
    output: *mut Buffer,
    error: *mut Buffer,
) -> i32 {
    boundary(error, || {
        ensure!(!handle.is_null() && !output.is_null(), "Null handle/output");
        let archive = unsafe { &*handle.cast::<Archive>() };
        let mut files: Vec<_> = archive
            .files
            .values()
            .map(|(path, _)| path.as_str())
            .collect();
        files.sort();
        unsafe {
            *output = Buffer::from_vec(serde_json::to_vec(&files)?);
        }
        Ok(())
    })
}
#[unsafe(no_mangle)]
pub unsafe extern "C" fn kisse_io_read_asset(
    handle: *mut std::ffi::c_void,
    path: *const c_char,
    skip_bulk: i32,
    max_bytes: u64,
    output: *mut AssetBuffers,
    error: *mut Buffer,
) -> i32 {
    boundary(error, || {
        ensure!(!handle.is_null() && !output.is_null(), "Null handle/output");
        let archive = unsafe { &*handle.cast::<Archive>() };
        unsafe {
            *output = archive.read_asset(text(path)?, skip_bulk != 0, max_bytes)?;
        }
        Ok(())
    })
}
#[unsafe(no_mangle)]
pub unsafe extern "C" fn kisse_io_read_file(
    handle: *mut std::ffi::c_void,
    path: *const c_char,
    max_bytes: u64,
    output: *mut Buffer,
    error: *mut Buffer,
) -> i32 {
    boundary(error, || {
        ensure!(!handle.is_null() && !output.is_null(), "Null handle/output");
        let archive = unsafe { &*handle.cast::<Archive>() };
        let (_, id) = archive
            .files
            .get(&normalize(unsafe { text(path)? }))
            .context("Virtual file not found")?;
        let view = ReadView {
            inner: archive.store.as_ref(),
            selected: archive.selected(),
            skip_bulk: false,
            max_bytes,
        };
        let data = view.read(*id)?;
        unsafe {
            *output = Buffer::from_vec(data);
        }
        Ok(())
    })
}
#[unsafe(no_mangle)]
pub unsafe extern "C" fn kisse_io_free(buffer: Buffer) {
    if !buffer.data.is_null() {
        unsafe {
            drop(Box::from_raw(ptr::slice_from_raw_parts_mut(
                buffer.data,
                buffer.len,
            )));
        }
    }
}
#[unsafe(no_mangle)]
pub unsafe extern "C" fn kisse_io_close(handle: *mut std::ffi::c_void) {
    if !handle.is_null() {
        let _ = catch_unwind(AssertUnwindSafe(|| unsafe {
            drop(Box::from_raw(handle.cast::<Archive>()));
        }));
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn paths_ignore_mount_prefix_and_separator_case() {
        assert_eq!(
            normalize("../../../Game/Content/A.uasset"),
            normalize("Game\\Content\\A.UASSET")
        );
        assert_eq!(normalize("/Game/Content/A.uasset"), "game/content/a.uasset");
    }
    #[test]
    fn buffers_round_trip_and_empty_buffers_can_be_freed() {
        for bytes in [vec![], vec![0, 1, 2, 255]] {
            let buffer = Buffer::from_vec(bytes.clone());
            assert_eq!(
                unsafe { std::slice::from_raw_parts(buffer.data, buffer.len) },
                bytes
            );
            unsafe {
                kisse_io_free(buffer);
            }
        }
    }
    #[test]
    fn null_handles_return_an_error_buffer() {
        let mut output = Buffer::default();
        let mut error = Buffer::default();
        assert_eq!(
            unsafe { kisse_io_files(ptr::null_mut(), &mut output, &mut error) },
            1
        );
        assert!(output.data.is_null());
        assert!(error.len > 0);
        unsafe {
            kisse_io_free(error);
            kisse_io_free(output);
            kisse_io_close(ptr::null_mut());
        }
    }
    #[test]
    fn missing_oodle_has_a_distinct_retry_status() {
        let mut error = Buffer::default();
        assert_eq!(boundary(&mut error, || Err(OodleRequired.into())), 2);
        unsafe {
            kisse_io_free(error);
        }
    }
    #[test]
    fn oodle_initialization_errors_stay_inside_abi_and_can_be_retried() {
        for path in [ptr::null(), c"oo2core_9_win64.dll".as_ptr()] {
            let mut error = Buffer::default();
            assert_eq!(unsafe { kisse_io_set_oodle(path, &mut error) }, 1);
            assert!(error.len > 0);
            assert!(!oodle_loader::is_initialized());
            unsafe { kisse_io_free(error) };
        }
    }
    #[test]
    fn panics_and_errors_do_not_cross_abi() {
        let mut error = Buffer::default();
        assert_eq!(boundary(&mut error, || anyhow::bail!("test error")), 1);
        unsafe {
            kisse_io_free(error);
        }
        let mut error = Buffer::default();
        assert_eq!(boundary(&mut error, || panic!("test panic")), 1);
        unsafe {
            kisse_io_free(error);
        }
    }
}
