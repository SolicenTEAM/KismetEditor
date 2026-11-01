use std::sync::OnceLock;

type Result<T, E = Error> = std::result::Result<T, E>;

pub use oodle_lz::{CompressionLevel, Compressor};

mod oodle_lz {
    #[derive(Debug, Clone, Copy)]
    #[repr(i32)]
    pub enum Compressor {
        /// None = memcpy, pass through uncompressed bytes
        None = 3,

        /// Fast decompression and high compression ratios, amazing!
        Kraken = 8,
        /// Leviathan = Kraken's big brother with higher compression, slightly slower decompression.
        Leviathan = 13,
        /// Mermaid is between Kraken & Selkie - crazy fast, still decent compression.
        Mermaid = 9,
        /// Selkie is a super-fast relative of Mermaid.  For maximum decode speed.
        Selkie = 11,
        /// Hydra, the many-headed beast = Leviathan, Kraken, Mermaid, or Selkie (see $OodleLZ_About_Hydra)
        Hydra = 12,
    }

    #[derive(Debug, Clone, Copy)]
    #[repr(i32)]
    pub enum CompressionLevel {
        /// don't compress, just copy raw bytes
        None = 0,
        /// super fast mode, lower compression ratio
        SuperFast = 1,
        /// fastest LZ mode with still decent compression ratio
        VeryFast = 2,
        /// fast - good for daily use
        Fast = 3,
        /// standard medium speed LZ mode
        Normal = 4,

        /// optimal parse level 1 (faster optimal encoder)
        Optimal1 = 5,
        /// optimal parse level 2 (recommended baseline optimal encoder)
        Optimal2 = 6,
        /// optimal parse level 3 (slower optimal encoder)
        Optimal3 = 7,
        /// optimal parse level 4 (very slow optimal encoder)
        Optimal4 = 8,
        /// optimal parse level 5 (don't care about encode speed, maximum compression)
        Optimal5 = 9,

        /// faster than SuperFast, less compression
        HyperFast1 = -1,
        /// faster than HyperFast1, less compression
        HyperFast2 = -2,
        /// faster than HyperFast2, less compression
        HyperFast3 = -3,
        /// fastest, less compression
        HyperFast4 = -4,
    }

    pub type Compress = unsafe extern "system" fn(
        compressor: Compressor,
        rawBuf: *const u8,
        rawLen: usize,
        compBuf: *mut u8,
        level: CompressionLevel,
        pOptions: *const (),
        dictionaryBase: *const (),
        lrm: *const (),
        scratchMem: *mut u8,
        scratchSize: usize,
    ) -> isize;

    pub type Decompress = unsafe extern "system" fn(
        compBuf: *const u8,
        compBufSize: usize,
        rawBuf: *mut u8,
        rawLen: usize,
        fuzzSafe: u32,
        checkCRC: u32,
        verbosity: u32,
        decBufBase: u64,
        decBufSize: usize,
        fpCallback: u64,
        callbackUserData: u64,
        decoderMemory: *mut u8,
        decoderMemorySize: usize,
        threadPhase: u32,
    ) -> isize;

    pub type GetCompressedBufferSizeNeeded =
        unsafe extern "system" fn(compressor: Compressor, rawSize: usize) -> usize;

    pub type SetPrintf = unsafe extern "system" fn(printf: *const ());
}

#[derive(thiserror::Error, Debug)]
pub enum Error {
    #[error("Oodle compression failed")]
    CompressionFailed,
    #[error("Oodle must be initialized with an absolute library path")]
    NotInitialized,
    #[error("Oodle libloading error {0:?}")]
    LibLoading(#[from] libloading::Error),
}

pub struct Oodle {
    _library: libloading::Library,
    compress: oodle_lz::Compress,
    decompress: oodle_lz::Decompress,
    get_compressed_buffer_size_needed: oodle_lz::GetCompressedBufferSizeNeeded,
    set_printf: oodle_lz::SetPrintf,
}
impl Oodle {
    fn new(lib: libloading::Library) -> Result<Self> {
        unsafe {
            let res = Oodle {
                compress: *lib.get(b"OodleLZ_Compress")?,
                decompress: *lib.get(b"OodleLZ_Decompress")?,
                get_compressed_buffer_size_needed: *lib
                    .get(b"OodleLZ_GetCompressedBufferSizeNeeded")?,
                set_printf: *lib.get(b"OodleCore_Plugins_SetPrintf")?,
                _library: lib,
            };
            (res.set_printf)(std::ptr::null()); // silence oodle logging
            Ok(res)
        }
    }
    pub fn compress(
        &self,
        input: &[u8],
        compressor: Compressor,
        compression_level: CompressionLevel,
    ) -> Result<Vec<u8>> {
        unsafe {
            let buffer_size = self.get_compressed_buffer_size_needed(compressor, input.len());
            let mut buffer = vec![0; buffer_size];

            let len = (self.compress)(
                compressor,
                input.as_ptr(),
                input.len(),
                buffer.as_mut_ptr(),
                compression_level,
                std::ptr::null(),
                std::ptr::null(),
                std::ptr::null(),
                std::ptr::null_mut(),
                0,
            );

            if len == -1 {
                return Err(Error::CompressionFailed);
            }
            buffer.truncate(len as usize);

            Ok(buffer)
        }
    }
    pub fn decompress(&self, input: &[u8], output: &mut [u8]) -> isize {
        unsafe {
            (self.decompress)(
                input.as_ptr(),
                input.len(),
                output.as_mut_ptr(),
                output.len(),
                1,
                1,
                0,
                0,
                0,
                0,
                0,
                std::ptr::null_mut(),
                0,
                3,
            )
        }
    }
    fn get_compressed_buffer_size_needed(
        &self,
        compressor: oodle_lz::Compressor,
        raw_buffer: usize,
    ) -> usize {
        unsafe { (self.get_compressed_buffer_size_needed)(compressor, raw_buffer) }
    }
}

static OODLE: OnceLock<Oodle> = OnceLock::new();

pub fn is_initialized() -> bool {
    OODLE.get().is_some()
}

pub fn initialize(path: &std::path::Path) -> Result<()> {
    if !path.is_absolute() {
        return Err(Error::NotInitialized);
    }
    if is_initialized() {
        return Ok(());
    }
    let library = unsafe { libloading::Library::new(path)? };
    let oodle = Oodle::new(library)?;
    // Losing a race drops only our extra module reference, never the active instance.
    let _ = OODLE.set(oodle);
    Ok(())
}

pub fn oodle() -> Result<&'static Oodle> {
    OODLE.get().ok_or(Error::NotInitialized)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn relative_path_is_rejected_without_loading() {
        assert!(matches!(
            initialize(std::path::Path::new("oo2core_9_win64.dll")),
            Err(Error::NotInitialized)
        ));
        assert!(!is_initialized());
    }
    #[test]
    fn missing_library_does_not_poison_initialization() {
        let path = std::env::temp_dir().join("kisse-nonexistent-oodle-library.dll");
        assert!(initialize(&path).is_err());
        assert!(initialize(&path).is_err());
        assert!(!is_initialized());
        assert!(oodle().is_err());
    }
}
