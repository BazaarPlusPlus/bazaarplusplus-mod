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
}
