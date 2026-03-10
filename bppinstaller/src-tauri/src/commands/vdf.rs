use std::path::{Path, PathBuf};

const THE_BAZAAR_APP_ID: &str = "1617400";

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

pub fn inject_launch_options(vdf_content: &str, args: &str) -> Result<String, String> {
    let marker = format!("\"{THE_BAZAAR_APP_ID}\"");
    let launch_options_key = "\"LaunchOptions\"";

    let app_start = vdf_content
        .find(&marker)
        .ok_or_else(|| format!("App ID {THE_BAZAAR_APP_ID} not found in localconfig.vdf"))?;
    let brace_start = vdf_content[app_start..]
        .find('{')
        .map(|offset| app_start + offset)
        .ok_or_else(|| format!("Malformed VDF: no opening brace for {THE_BAZAAR_APP_ID}"))?;

    let mut depth = 0usize;
    let mut brace_end = None;
    for (offset, ch) in vdf_content[brace_start..].char_indices() {
        match ch {
            '{' => depth += 1,
            '}' => {
                depth -= 1;
                if depth == 0 {
                    brace_end = Some(brace_start + offset);
                    break;
                }
            }
            _ => {}
        }
    }

    let brace_end = brace_end
        .ok_or_else(|| format!("Malformed VDF: unmatched brace for {THE_BAZAAR_APP_ID}"))?;
    let block = &vdf_content[brace_start..=brace_end];
    let new_line = format!("\t\t\t\t\t\"LaunchOptions\"\t\t\"{}\"", args);

    let new_block = if let Some(lo_pos) = block.find(launch_options_key) {
        let line_end = block[lo_pos..]
            .find('\n')
            .map(|offset| lo_pos + offset)
            .unwrap_or(block.len() - 1);
        format!("{}{}{}", &block[..lo_pos], new_line, &block[line_end..])
    } else {
        let insert_pos = block
            .rfind('}')
            .ok_or_else(|| format!("Malformed VDF: no closing brace for {THE_BAZAAR_APP_ID}"))?;
        format!("{}{}\n{}", &block[..insert_pos], new_line, &block[insert_pos..])
    };

    Ok(format!(
        "{}{}{}",
        &vdf_content[..brace_start],
        new_block,
        &vdf_content[brace_end + 1..]
    ))
}

pub fn clear_launch_options(vdf_content: &str) -> Result<String, String> {
    let marker = format!("\"{THE_BAZAAR_APP_ID}\"");
    let launch_options_key = "\"LaunchOptions\"";

    let app_start = vdf_content
        .find(&marker)
        .ok_or_else(|| format!("App ID {THE_BAZAAR_APP_ID} not found in localconfig.vdf"))?;
    let brace_start = vdf_content[app_start..]
        .find('{')
        .map(|offset| app_start + offset)
        .ok_or_else(|| format!("Malformed VDF: no opening brace for {THE_BAZAAR_APP_ID}"))?;

    let mut depth = 0usize;
    let mut brace_end = None;
    for (offset, ch) in vdf_content[brace_start..].char_indices() {
        match ch {
            '{' => depth += 1,
            '}' => {
                depth -= 1;
                if depth == 0 {
                    brace_end = Some(brace_start + offset);
                    break;
                }
            }
            _ => {}
        }
    }

    let brace_end = brace_end
        .ok_or_else(|| format!("Malformed VDF: unmatched brace for {THE_BAZAAR_APP_ID}"))?;
    let block = &vdf_content[brace_start..=brace_end];
    let Some(lo_pos) = block.find(launch_options_key) else {
        return Ok(vdf_content.to_string());
    };

    let line_start = block[..lo_pos].rfind('\n').map(|index| index + 1).unwrap_or(0);
    let line_end = block[lo_pos..]
        .find('\n')
        .map(|offset| lo_pos + offset + 1)
        .unwrap_or(block.len());
    let new_block = format!("{}{}", &block[..line_start], &block[line_end..]);

    Ok(format!(
        "{}{}{}",
        &vdf_content[..brace_start],
        new_block,
        &vdf_content[brace_end + 1..]
    ))
}

#[cfg(target_os = "macos")]
fn launch_options_args(game_path: &Path) -> String {
    format!("\"{}\" %command%", game_path.join("run_bepinex.sh").display())
}

#[cfg(target_os = "windows")]
fn launch_options_args(_game_path: &Path) -> String {
    String::new()
}

#[cfg(not(any(target_os = "macos", target_os = "windows")))]
fn launch_options_args(_game_path: &Path) -> String {
    String::new()
}

#[cfg(target_os = "macos")]
fn ensure_launcher_executable(script_path: &Path) -> Result<(), String> {
    use std::os::unix::fs::PermissionsExt;

    let metadata = std::fs::metadata(script_path)
        .map_err(|err| format!("Cannot access {}: {err}", script_path.display()))?;
    let mut permissions = metadata.permissions();
    permissions.set_mode(permissions.mode() | 0o111);
    std::fs::set_permissions(script_path, permissions)
        .map_err(|err| format!("Cannot set executable permission on {}: {err}", script_path.display()))
}

#[cfg(not(target_os = "macos"))]
fn ensure_launcher_executable(_script_path: &Path) -> Result<(), String> {
    Ok(())
}

pub fn find_localconfig_paths(steam_path: &Path) -> Vec<PathBuf> {
    let Ok(entries) = std::fs::read_dir(steam_path.join("userdata")) else {
        return Vec::new();
    };

    let mut paths = entries
        .filter_map(|entry| entry.ok())
        .filter_map(|entry| {
            let user_name = entry.file_name();
            let user_name = user_name.to_str()?;
            if !user_name.chars().all(|ch| ch.is_ascii_digit()) {
                return None;
            }

            let localconfig = entry.path().join("config/localconfig.vdf");
            localconfig.exists().then_some(localconfig)
        })
        .collect::<Vec<_>>();
    paths.sort();
    paths
}

pub fn clear_launch_options_for_steam(steam_path: &Path) -> Result<(), String> {
    let localconfigs = find_localconfig_paths(steam_path);
    if localconfigs.is_empty() {
        return Ok(());
    }

    for localconfig in localconfigs {
        let content = std::fs::read_to_string(&localconfig).map_err(|err| err.to_string())?;
        let backup = localconfig.with_extension("vdf.bak");
        std::fs::copy(&localconfig, &backup).map_err(|err| err.to_string())?;
        let cleared = clear_launch_options(&content)?;
        let tmp = localconfig.with_extension("vdf.tmp");
        std::fs::write(&tmp, cleared).map_err(|err| err.to_string())?;
        std::fs::rename(&tmp, &localconfig).map_err(|err| err.to_string())?;
    }

    Ok(())
}

#[tauri::command]
pub fn patch_launch_options(
    _app: tauri::AppHandle,
    _steam_path: String,
    _game_path: String,
) -> Result<(), String> {
    let game_path = PathBuf::from(&_game_path);
    let args = launch_options_args(&game_path);

    if args.is_empty() {
        debug_log!("Skipping launch option patch for this platform.");
        return Ok(());
    }

    #[cfg(target_os = "macos")]
    {
        let script_path = game_path.join("run_bepinex.sh");
        ensure_launcher_executable(&script_path)?;
        debug_log!("Marked {} as executable.", script_path.display());
    }

    debug_log!("Locating localconfig.vdf files...");
    let localconfigs = find_localconfig_paths(Path::new(&_steam_path));
    if localconfigs.is_empty() {
        debug_error!("Could not find any localconfig.vdf under Steam/userdata");
        return Err("Could not find any localconfig.vdf under Steam/userdata".to_string());
    }

    for localconfig in localconfigs {
        let content = std::fs::read_to_string(&localconfig).map_err(|err| err.to_string())?;
        let backup = localconfig.with_extension("vdf.bak");
        std::fs::copy(&localconfig, &backup).map_err(|err| err.to_string())?;
        debug_log!("Backed up {}", localconfig.display());

        let patched = inject_launch_options(&content, &args)?;
        let tmp = localconfig.with_extension("vdf.tmp");
        std::fs::write(&tmp, patched).map_err(|err| err.to_string())?;
        std::fs::rename(&tmp, &localconfig).map_err(|err| err.to_string())?;
        debug_log!("Updated {}", localconfig.display());
    }

    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[cfg(target_os = "macos")]
    use std::os::unix::fs::PermissionsExt;

    fn fixture_vdf() -> &'static str {
        r#"
"UserLocalConfigStore"
{
    "Software"
    {
        "Valve"
        {
            "Steam"
            {
                "apps"
                {
                    "1617400"
                    {
                        "LastPlayed"    "1700000000"
                    }
                }
            }
        }
    }
}"#
    }

    #[test]
    fn test_inject_launch_options_inserts_when_missing() {
        let result = inject_launch_options(fixture_vdf(), "MY_ARGS").unwrap();
        assert!(result.contains("LaunchOptions"));
        assert!(result.contains("MY_ARGS"));
    }

    #[test]
    fn test_inject_launch_options_replaces_existing() {
        let vdf_with_lo = fixture_vdf().replace(
            "\"LastPlayed\"",
            "\"LaunchOptions\"\t\t\"OLD_ARGS\"\n\t\t\t\t\t\"LastPlayed\"",
        );

        let result = inject_launch_options(&vdf_with_lo, "NEW_ARGS").unwrap();
        assert!(result.contains("NEW_ARGS"));
        assert!(!result.contains("OLD_ARGS"));
    }

    #[test]
    fn test_clear_launch_options_removes_existing_line() {
        let vdf_with_lo = fixture_vdf().replace(
            "\"LastPlayed\"",
            "\"LaunchOptions\"\t\t\"OLD_ARGS\"\n\t\t\t\t\t\"LastPlayed\"",
        );
        let result = clear_launch_options(&vdf_with_lo).unwrap();
        assert!(!result.contains("LaunchOptions"));
        assert!(result.contains("LastPlayed"));
    }

    #[test]
    fn test_inject_returns_error_for_missing_app_id() {
        let vdf = r#""UserLocalConfigStore" { "apps" { "730" { } } }"#;
        let result = inject_launch_options(vdf, "args");
        assert!(result.is_err());
    }

    #[test]
    fn test_find_localconfig_paths_returns_only_numeric_userdata_entries() {
        let tmp = tempfile::tempdir().unwrap();
        let valid = tmp.path().join("userdata/123456/config");
        let invalid = tmp.path().join("userdata/not-a-user/config");

        std::fs::create_dir_all(&valid).unwrap();
        std::fs::create_dir_all(&invalid).unwrap();
        std::fs::write(valid.join("localconfig.vdf"), fixture_vdf()).unwrap();
        std::fs::write(invalid.join("localconfig.vdf"), fixture_vdf()).unwrap();

        let paths = find_localconfig_paths(tmp.path());
        assert_eq!(paths, vec![valid.join("localconfig.vdf")]);
    }

    #[cfg(target_os = "macos")]
    #[test]
    fn test_launch_options_args_uses_run_script_with_quoted_game_path() {
        let args = launch_options_args(Path::new("/Applications/The Bazaar"));
        assert_eq!(
            args,
            "\"/Applications/The Bazaar/run_bepinex.sh\" %command%"
        );
    }

    #[cfg(target_os = "macos")]
    #[test]
    fn test_ensure_launcher_executable_sets_execute_bits() {
        let tmp = tempfile::tempdir().unwrap();
        let script = tmp.path().join("run_bepinex.sh");
        std::fs::write(&script, "#!/bin/sh\n").unwrap();
        std::fs::set_permissions(&script, std::fs::Permissions::from_mode(0o644)).unwrap();

        ensure_launcher_executable(&script).unwrap();

        let mode = std::fs::metadata(&script).unwrap().permissions().mode();
        assert_eq!(mode & 0o111, 0o111);
    }

    #[cfg(target_os = "windows")]
    #[test]
    fn test_launch_options_args_is_empty_on_windows() {
        let args = launch_options_args(Path::new("C:\\Games\\The Bazaar"));
        assert!(args.is_empty());
    }
}
