use serde::{Deserialize, Serialize};
use std::path::{Path, PathBuf};
use std::process::Command;

#[cfg(target_os = "windows")]
use std::os::windows::process::CommandExt;

#[cfg(target_os = "windows")]
const CREATE_NO_WINDOW: u32 = 0x08000000;

#[derive(Debug, Serialize, Deserialize)]
pub struct EnvironmentInfo {
    pub steam_path: Option<String>,
    pub game_path: Option<String>,
    pub dotnet_version: Option<String>,
    pub dotnet_ok: bool,
    pub bepinex_installed: bool,
    pub bpp_version: Option<String>,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct DotnetInfo {
    pub dotnet_version: Option<String>,
    pub dotnet_ok: bool,
}

#[tauri::command]
pub fn detect_environment() -> Result<EnvironmentInfo, String> {
    let steam_path = get_steam_path();
    let game_path = steam_path.as_ref().and_then(|path| get_game_path(path));
    let bpp_version = game_path
        .as_ref()
        .and_then(|path| read_installed_bpp_version(path));
    let bepinex_installed = bpp_version.is_some();

    Ok(EnvironmentInfo {
        steam_path: steam_path.map(|path| path.to_string_lossy().into_owned()),
        game_path: game_path.map(|path| path.to_string_lossy().into_owned()),
        dotnet_version: None,
        dotnet_ok: false,
        bepinex_installed,
        bpp_version,
    })
}

#[tauri::command]
pub async fn detect_dotnet_runtime() -> Result<DotnetInfo, String> {
    tauri::async_runtime::spawn_blocking(|| {
        let (dotnet_version, dotnet_ok) = detect_dotnet();
        DotnetInfo {
            dotnet_version,
            dotnet_ok,
        }
    })
    .await
    .map_err(|err| format!("failed to detect .NET runtime: {err}"))
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
        .filter(|version| is_supported_dotnet_version(version))
        .map(str::to_string)
        .max_by(|a, b| parse_version_tuple(a).cmp(&parse_version_tuple(b)))
}

fn parse_version_tuple(v: &str) -> (u32, u32, u32) {
    let mut parts = v.split('.').filter_map(|p| p.parse::<u32>().ok());
    (parts.next().unwrap_or(0), parts.next().unwrap_or(0), parts.next().unwrap_or(0))
}

fn is_supported_dotnet_version(version: &str) -> bool {
    version
        .split('.')
        .next()
        .and_then(|major| major.parse::<u32>().ok())
        .map(|major| major >= 6)
        .unwrap_or(false)
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

fn get_game_path(steam_path: &Path) -> Option<PathBuf> {
    let library_vdf = std::fs::read_to_string(steam_path.join("steamapps/libraryfolders.vdf")).ok()?;
    let library_root = find_game_in_library_vdf(&library_vdf, "1617400")?;
    let candidate = PathBuf::from(library_root).join("steamapps/common/The Bazaar");
    candidate.exists().then_some(candidate)
}

fn read_installed_bpp_version(game_path: &Path) -> Option<String> {
    let version_path = game_path.join("BepInEx/plugins/BazaarPlusPlus.version");
    let version = std::fs::read_to_string(version_path).ok()?;
    let version = version.trim();
    (!version.is_empty()).then(|| version.to_string())
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
    let candidates = {
        let mut candidates = vec![
            "dotnet".to_string(),
            "/usr/local/bin/dotnet".to_string(),
            "/usr/local/share/dotnet/dotnet".to_string(),
            "/opt/homebrew/bin/dotnet".to_string(),
        ];
        if let Some(home) = dirs::home_dir() {
            candidates.push(home.join(".dotnet/dotnet").to_string_lossy().into_owned());
        }
        candidates
    };

    for candidate in candidates {
        let mut command = Command::new(&candidate);
        command.arg("--list-runtimes");

        #[cfg(target_os = "windows")]
        command.creation_flags(CREATE_NO_WINDOW);

        let Ok(output) = command.output() else {
            continue;
        };
        let stdout = String::from_utf8_lossy(&output.stdout);
        if let Some(version) = parse_dotnet_runtimes(&stdout)
            .filter(|version| is_supported_dotnet_version(version))
        {
            return (Some(version), true);
        }
    }

    (None, false)
}


/// Returns true if the game installation is found at the given path.
#[tauri::command]
pub fn verify_game_path(path: String) -> bool {
    let base = PathBuf::from(&path);

    #[cfg(target_os = "macos")]
    return base.join("TheBazaar.app").exists();

    #[cfg(target_os = "windows")]
    return base.join("TheBazaar.exe").exists();

    #[cfg(not(any(target_os = "macos", target_os = "windows")))]
    return base.join("TheBazaar").exists();
}

#[cfg(test)]
mod tests {
    use super::*;

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

    #[test]
    fn test_is_supported_dotnet_version_requires_major_6_or_higher() {
        assert!(!is_supported_dotnet_version("5.0.17"));
        assert!(is_supported_dotnet_version("6.0.0"));
        assert!(is_supported_dotnet_version("8.0.1"));
    }

    #[test]
    fn test_read_installed_bpp_version_trims_contents() {
        let temp_root = std::env::temp_dir().join(format!(
            "bppinstaller-version-test-{}-{}",
            std::process::id(),
            std::time::SystemTime::now()
                .duration_since(std::time::UNIX_EPOCH)
                .expect("system time before epoch")
                .as_nanos()
        ));
        let plugins_dir = temp_root.join("BepInEx/plugins");
        std::fs::create_dir_all(&plugins_dir).expect("create plugins dir");
        std::fs::write(
            plugins_dir.join("BazaarPlusPlus.version"),
            "1.2.3+2026-03-10 12:34:56\n",
        )
        .expect("write version file");

        let version = read_installed_bpp_version(&temp_root);

        std::fs::remove_dir_all(&temp_root).expect("cleanup temp dir");

        assert_eq!(version.as_deref(), Some("1.2.3+2026-03-10 12:34:56"));
    }

}
