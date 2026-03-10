use std::path::{Path, PathBuf};

pub fn inject_launch_options(vdf_content: &str, args: &str) -> Result<String, String> {
    let marker = "\"2138550\"";
    let launch_options_key = "\"LaunchOptions\"";

    let app_start = vdf_content
        .find(marker)
        .ok_or_else(|| "App ID 2138550 not found in localconfig.vdf".to_string())?;
    let brace_start = vdf_content[app_start..]
        .find('{')
        .map(|offset| app_start + offset)
        .ok_or_else(|| "Malformed VDF: no opening brace for 2138550".to_string())?;

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

    let brace_end =
        brace_end.ok_or_else(|| "Malformed VDF: unmatched brace for 2138550".to_string())?;
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
            .ok_or_else(|| "Malformed VDF: no closing brace for 2138550".to_string())?;
        format!("{}{}\n{}", &block[..insert_pos], new_line, &block[insert_pos..])
    };

    Ok(format!(
        "{}{}{}",
        &vdf_content[..brace_start],
        new_block,
        &vdf_content[brace_end + 1..]
    ))
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

#[tauri::command]
pub fn patch_launch_options(
    _app: tauri::AppHandle,
    _steam_path: String,
    _game_path: String,
) -> Result<(), String> {
    use tauri::Emitter;

    #[cfg(target_os = "macos")]
    let args = "DYLD_INSERT_LIBRARIES=./doorstop_libs/libdoorstop.dylib";
    #[cfg(target_os = "windows")]
    let args = "--doorstop-enable true --doorstop-target BepInEx/core/BepInEx.Preloader.dll";
    #[cfg(not(any(target_os = "macos", target_os = "windows")))]
    let args = "";

    _app.emit("bppinstaller://log", "Locating localconfig.vdf files...")
        .ok();
    let localconfigs = find_localconfig_paths(Path::new(&_steam_path));
    if localconfigs.is_empty() {
        return Err("Could not find any localconfig.vdf under Steam/userdata".to_string());
    }

    for localconfig in localconfigs {
        let content = std::fs::read_to_string(&localconfig).map_err(|err| err.to_string())?;
        let backup = localconfig.with_extension("vdf.bak");
        std::fs::copy(&localconfig, &backup).map_err(|err| err.to_string())?;
        _app.emit(
            "bppinstaller://log",
            format!("Backed up {}", localconfig.display()),
        )
        .ok();

        let patched = inject_launch_options(&content, args)?;
        let tmp = localconfig.with_extension("vdf.tmp");
        std::fs::write(&tmp, patched).map_err(|err| err.to_string())?;
        std::fs::rename(&tmp, &localconfig).map_err(|err| err.to_string())?;
        _app.emit(
            "bppinstaller://log",
            format!("Updated {}", localconfig.display()),
        )
        .ok();
    }

    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

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
                    "2138550"
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
}
