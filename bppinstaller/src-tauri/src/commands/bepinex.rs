use std::io::{Cursor, Read};
use std::path::Path;
use tauri::Manager;

use crate::commands::vdf::clear_launch_options_for_steam;

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

pub const BPP_VERSION_URL: &str = "";
pub const BPP_DOWNLOAD_URL: &str = "";

#[derive(Debug, serde::Serialize, serde::Deserialize)]
pub struct UpdateInfo {
    pub current_version: Option<String>,
    pub latest_version: Option<String>,
    pub update_available: bool,
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

pub fn parse_latest_version_response(response: &str) -> Option<String> {
    let version = response.trim();
    (!version.is_empty()).then(|| version.to_string())
}

fn remove_path_if_exists(path: &Path) -> Result<(), String> {
    if !path.exists() {
        return Ok(());
    }

    if path.is_dir() {
        std::fs::remove_dir_all(path)
            .map_err(|err| format!("Cannot remove {}: {err}", path.display()))
    } else {
        std::fs::remove_file(path).map_err(|err| format!("Cannot remove {}: {err}", path.display()))
    }
}

fn uninstall_payload(game_path: &Path) -> Result<(), String> {
    remove_path_if_exists(&game_path.join("BepInEx"))?;

    #[cfg(target_os = "macos")]
    {
        remove_path_if_exists(&game_path.join("run_bepinex.sh"))?;
        remove_path_if_exists(&game_path.join("libdoorstop.dylib"))?;
    }

    #[cfg(target_os = "windows")]
    {
        remove_path_if_exists(&game_path.join("doorstop_config.ini"))?;
        remove_path_if_exists(&game_path.join("winhttp.dll"))?;
    }

    Ok(())
}

#[tauri::command]
pub fn check_bpp_update(current_version: Option<String>) -> Result<UpdateInfo, String> {
    if BPP_VERSION_URL.is_empty() {
        return Ok(UpdateInfo {
            current_version,
            latest_version: None,
            update_available: false,
        });
    }

    let response = reqwest::blocking::get(BPP_VERSION_URL)
        .map_err(|err| format!("Cannot fetch latest BazaarPlusPlus version: {err}"))?;
    let text = response
        .text()
        .map_err(|err| format!("Cannot read latest BazaarPlusPlus version response: {err}"))?;
    let latest_version = parse_latest_version_response(&text);
    let update_available = match (&current_version, &latest_version) {
        (Some(current), Some(latest)) => current != latest,
        _ => false,
    };

    Ok(UpdateInfo {
        current_version,
        latest_version,
        update_available,
    })
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

#[tauri::command]
pub fn update_bpp(_app: tauri::AppHandle, game_path: String) -> Result<(), String> {
    if BPP_DOWNLOAD_URL.is_empty() {
        return Err("BPP_DOWNLOAD_URL is not configured".to_string());
    }

    let response = reqwest::blocking::get(BPP_DOWNLOAD_URL)
        .map_err(|err| format!("Cannot download BazaarPlusPlus update zip: {err}"))?;
    let zip_bytes = response
        .bytes()
        .map_err(|err| format!("Cannot read BazaarPlusPlus update zip: {err}"))?;
    let extracted = extract_zip(zip_bytes.as_ref(), Path::new(&game_path))?;
    debug_log!("Updated BazaarPlusPlus with {} extracted files.", extracted.len());
    Ok(())
}

#[tauri::command]
pub fn uninstall_bpp(_app: tauri::AppHandle, steam_path: String, game_path: String) -> Result<(), String> {
    let game_path = Path::new(&game_path);
    uninstall_payload(game_path)?;

    #[cfg(target_os = "macos")]
    {
        clear_launch_options_for_steam(Path::new(&steam_path))?;
    }

    debug_log!("Uninstalled BazaarPlusPlus payload from {}", game_path.display());
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Write;

    fn make_test_zip() -> Vec<u8> {
        let buffer = Cursor::new(Vec::new());
        let mut zip = zip::ZipWriter::new(buffer);
        let options = zip::write::SimpleFileOptions::default();

        zip.add_directory("BepInEx/", options).unwrap();
        zip.start_file("BepInEx/core/BepInEx.Core.dll", options)
            .unwrap();
        zip.write_all(b"fake dll content").unwrap();

        zip.finish().unwrap().into_inner()
    }

    #[test]
    fn test_extract_zip_creates_files() {
        let zip_bytes = make_test_zip();
        let tmp = tempfile::tempdir().unwrap();

        let extracted = extract_zip(&zip_bytes, tmp.path()).unwrap();
        assert!(!extracted.is_empty());
        assert!(tmp.path().join("BepInEx/core/BepInEx.Core.dll").exists());
    }

    #[test]
    fn test_bundled_zip_relative_path_matches_supported_targets() {
        assert_eq!(bundled_zip_relative_path(), "BepInExSource/BepInEx.zip");
    }

    #[test]
    fn test_parse_latest_version_response_trims_text() {
        let version = parse_latest_version_response(" 1.2.3 \n");
        assert_eq!(version.as_deref(), Some("1.2.3"));
    }

    #[test]
    fn test_uninstall_payload_removes_platform_files() {
        let tmp = tempfile::tempdir().unwrap();
        std::fs::create_dir_all(tmp.path().join("BepInEx/plugins")).unwrap();
        std::fs::write(tmp.path().join("BepInEx/plugins/BazaarPlusPlus.dll"), b"dll").unwrap();

        #[cfg(target_os = "macos")]
        {
            std::fs::write(tmp.path().join("run_bepinex.sh"), b"#!/bin/sh\n").unwrap();
            std::fs::write(tmp.path().join("libdoorstop.dylib"), b"dylib").unwrap();
        }

        #[cfg(target_os = "windows")]
        {
            std::fs::write(tmp.path().join("doorstop_config.ini"), b"cfg").unwrap();
            std::fs::write(tmp.path().join("winhttp.dll"), b"dll").unwrap();
        }

        uninstall_payload(tmp.path()).unwrap();

        assert!(!tmp.path().join("BepInEx").exists());
        #[cfg(target_os = "macos")]
        {
            assert!(!tmp.path().join("run_bepinex.sh").exists());
            assert!(!tmp.path().join("libdoorstop.dylib").exists());
        }
        #[cfg(target_os = "windows")]
        {
            assert!(!tmp.path().join("doorstop_config.ini").exists());
            assert!(!tmp.path().join("winhttp.dll").exists());
        }
    }
}
