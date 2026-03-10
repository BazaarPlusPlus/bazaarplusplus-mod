use std::io::{Cursor, Read};
use std::path::Path;
use tauri::Manager;

macro_rules! debug_log {
    ($($arg:tt)*) => {
        #[cfg(debug_assertions)]
        println!($($arg)*);
    };
}

macro_rules! debug_error {
    ($($arg:tt)*) => {
        #[cfg(debug_assertions)]
        eprintln!($($arg)*);
    };
}

pub fn bundled_zip_relative_path() -> &'static str {
    "BepInExSource/BepInEx.zip"
}

pub fn extract_zip(zip_bytes: &[u8], dest_dir: &Path) -> Result<Vec<String>, String> {
    let reader = Cursor::new(zip_bytes);
    let mut archive = zip::ZipArchive::new(reader).map_err(|err| err.to_string())?;
    let mut extracted = Vec::new();

    for index in 0..archive.len() {
        let mut file = archive.by_index(index).map_err(|err| err.to_string())?;
        let Some(relative_path) = file.enclosed_name().map(|path| path.to_path_buf()) else {
            return Err(format!("Zip entry has unsafe path: {}", file.name()));
        };
        let output_path = dest_dir.join(relative_path);

        if file.is_dir() {
            std::fs::create_dir_all(&output_path).map_err(|err| err.to_string())?;
            continue;
        }

        if let Some(parent) = output_path.parent() {
            std::fs::create_dir_all(parent).map_err(|err| err.to_string())?;
        }

        let mut contents = Vec::new();
        file.read_to_end(&mut contents)
            .map_err(|err| err.to_string())?;
        std::fs::write(&output_path, contents).map_err(|err| err.to_string())?;
        extracted.push(output_path.to_string_lossy().into_owned());
    }

    Ok(extracted)
}

#[tauri::command]
pub fn install_bepinex(app: tauri::AppHandle, game_path: String) -> Result<(), String> {
    debug_log!("Reading bundled BepInEx.zip...");
    let relative_zip_path = bundled_zip_relative_path();
    let resource_path = app
        .path()
        .resource_dir()
        .map_err(|err| err.to_string())?
        .join(relative_zip_path);
    let zip_bytes = std::fs::read(&resource_path).map_err(|err| {
        debug_error!("Cannot read bundled BepInEx.zip: {err}");
        format!("Cannot read bundled BepInEx.zip: {err}")
    })?;

    debug_log!("Extracting BepInEx...");
    let extracted = extract_zip(&zip_bytes, Path::new(&game_path))?;
    debug_log!("Extracted {} files.", extracted.len());

    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_bundled_zip_relative_path_matches_supported_targets() {
        assert_eq!(bundled_zip_relative_path(), "BepInExSource/BepInEx.zip");
    }
}
