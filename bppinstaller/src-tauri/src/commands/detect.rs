use serde::{Deserialize, Serialize};
use std::path::{Path, PathBuf};
use std::process::Command;

#[derive(Debug, Serialize, Deserialize)]
pub struct EnvironmentInfo {
    pub steam_path: Option<String>,
    pub game_path: Option<String>,
    pub dotnet_version: Option<String>,
    pub dotnet_ok: bool,
    pub bepinex_installed: bool,
}

#[tauri::command]
pub fn detect_environment() -> Result<EnvironmentInfo, String> {
    let steam_path = get_steam_path();
    let game_path = steam_path.as_ref().and_then(|path| get_game_path(path));
    let (dotnet_version, dotnet_ok) = detect_dotnet();
    let bepinex_installed = game_path
        .as_ref()
        .map(|path| path.join("BepInEx/core/BepInEx.Core.dll").exists())
        .unwrap_or(false);

    Ok(EnvironmentInfo {
        steam_path: steam_path.map(|path| path.to_string_lossy().into_owned()),
        game_path: game_path.map(|path| path.to_string_lossy().into_owned()),
        dotnet_version,
        dotnet_ok,
        bepinex_installed,
    })
}

pub fn find_game_in_library_vdf(vdf_content: &str, app_id: &str) -> Option<String> {
    let parsed = keyvalues_parser::Parser::new()
        .literal_special_chars(true)
        .parse(vdf_content)
        .ok()?;
    let libraries = parsed.value.get_obj()?;

    for values in libraries.values() {
        for value in values {
            let folder = value.get_obj()?;
            let library_path = folder
                .get("path")
                .and_then(|entries| entries.first())
                .and_then(|entry| entry.get_str())?;
            let has_game = folder
                .get("apps")
                .and_then(|entries| entries.first())
                .and_then(|entry| entry.get_obj())
                .map(|apps| apps.contains_key(app_id))
                .unwrap_or(false);

            if has_game {
                return Some(library_path.to_string());
            }
        }
    }

    None
}

pub fn parse_dotnet_runtimes(output: &str) -> Option<String> {
    output
        .lines()
        .filter(|line| line.starts_with("Microsoft.NETCore.App "))
        .filter_map(|line| line.split_whitespace().nth(1))
        .filter(|version| {
            version
                .split('.')
                .next()
                .and_then(|major| major.parse::<u32>().ok())
                .map(|major| major >= 6)
                .unwrap_or(false)
        })
        .map(str::to_string)
        .max()
}

fn get_steam_path() -> Option<PathBuf> {
    #[cfg(target_os = "macos")]
    {
        let path = dirs::home_dir()?.join("Library/Application Support/Steam");
        if path.exists() {
            return Some(path);
        }
    }

    #[cfg(target_os = "windows")]
    {
        use winreg::RegKey;
        use winreg::enums::HKEY_CURRENT_USER;

        let hkcu = RegKey::predef(HKEY_CURRENT_USER);
        if let Ok(key) = hkcu.open_subkey(r"Software\Valve\Steam") {
            if let Ok(path) = key.get_value::<String, _>("SteamPath") {
                let path = PathBuf::from(path);
                if path.exists() {
                    return Some(path);
                }
            }
        }
    }

    None
}

fn get_game_path(_steam_path: &Path) -> Option<PathBuf> {
    let library_vdf = std::fs::read_to_string(_steam_path.join("steamapps/libraryfolders.vdf")).ok()?;
    let library_root = find_game_in_library_vdf(&library_vdf, "2138550")?;
    let candidate = PathBuf::from(library_root).join("steamapps/common/The Bazaar");
    candidate.exists().then_some(candidate)
}

fn detect_dotnet() -> (Option<String>, bool) {
    #[cfg(target_os = "windows")]
    let candidates = {
        let mut candidates = vec!["dotnet".to_string()];
        if let Ok(program_files) = std::env::var("PROGRAMFILES") {
            candidates.push(format!(r"{}\dotnet\dotnet.exe", program_files));
        }
        candidates
    };

    #[cfg(not(target_os = "windows"))]
    let candidates = vec!["dotnet".to_string()];

    for candidate in candidates {
        let Ok(output) = Command::new(&candidate).arg("--list-runtimes").output() else {
            continue;
        };
        let stdout = String::from_utf8_lossy(&output.stdout);
        if let Some(version) = parse_dotnet_runtimes(&stdout) {
            return (Some(version), true);
        }
    }

    (None, false)
}

/// Returns the game version string if found, or None if the path is invalid.
#[tauri::command]
pub fn verify_game_path(path: String) -> Option<String> {
    let base = PathBuf::from(&path);

    #[cfg(target_os = "macos")]
    {
        let app = base.join("TheBazaar.app");
        if !app.exists() {
            return None;
        }
        let plist = std::fs::read_to_string(app.join("Contents/Info.plist")).ok()?;
        Some(read_plist_string(&plist, "CFBundleShortVersionString").unwrap_or_default())
    }

    #[cfg(target_os = "windows")]
    {
        base.join("TheBazaar.exe").exists().then(|| "windows".to_string())
    }

    #[cfg(not(any(target_os = "macos", target_os = "windows")))]
    {
        base.join("TheBazaar").exists().then(|| String::new())
    }
}

/// Reads a string value from an Apple XML plist by key name.
fn read_plist_string(plist: &str, key: &str) -> Option<String> {
    let key_tag = format!("<key>{key}</key>");
    let after_key = plist.split_once(&key_tag)?.1;
    let value = after_key
        .split_once("<string>")?
        .1
        .split_once("</string>")?
        .0;
    Some(value.trim().to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_find_game_in_library_folders() {
        let vdf_content = r#"
"libraryfolders"
{
    "0"
    {
        "path"    "/home/user/.steam/steam"
        "apps"
        {
            "2138550"    "1"
            "730"        "1"
        }
    }
}"#;

        let result = find_game_in_library_vdf(vdf_content, "2138550");
        assert_eq!(result, Some("/home/user/.steam/steam".to_string()));
    }

    #[test]
    fn test_find_game_missing() {
        let vdf_content = r#"
"libraryfolders"
{
    "0"
    {
        "path"    "/some/path"
        "apps"
        {
            "730"    "1"
        }
    }
}"#;

        let result = find_game_in_library_vdf(vdf_content, "2138550");
        assert_eq!(result, None);
    }

    #[test]
    fn test_find_game_skips_non_library_entries() {
        let vdf_content = r#"
"libraryfolders"
{
    "contentstatsid" "1234567890"
    "0"
    {
        "path"    "/steam/library"
        "apps"
        {
            "2138550"    "1"
        }
    }
}"#;

        let result = find_game_in_library_vdf(vdf_content, "2138550");
        assert_eq!(result, Some("/steam/library".to_string()));
    }

    #[test]
    fn test_parse_dotnet_runtimes_found() {
        let output = "Microsoft.NETCore.App 6.0.25 [/usr/share/dotnet/shared/Microsoft.NETCore.App]\nMicrosoft.NETCore.App 8.0.1 [/usr/share/dotnet/shared/Microsoft.NETCore.App]";
        let result = parse_dotnet_runtimes(output);
        assert_eq!(result.as_deref(), Some("8.0.1"));
    }

    #[test]
    fn test_parse_dotnet_runtimes_too_old() {
        let output = "Microsoft.NETCore.App 5.0.0 [/usr/share/dotnet]";
        let result = parse_dotnet_runtimes(output);
        assert_eq!(result, None);
    }

    #[test]
    fn test_parse_dotnet_runtimes_empty_output() {
        let result = parse_dotnet_runtimes("");
        assert_eq!(result, None);
    }
}
