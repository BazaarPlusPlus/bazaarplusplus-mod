use serde::Serialize;
use std::collections::HashMap;
use std::fs;
use std::path::PathBuf;

const CFG_RELATIVE: &str = "BepInEx/config/BazaarPlusPlus.cfg";

#[derive(Serialize)]
pub struct ModConfigReadResult {
    config_exists: bool,
    values: HashMap<String, String>,
}

fn default_config() -> HashMap<String, String> {
    let mut map = HashMap::new();
    map.insert("StreamerMode.EnableNameOverride".to_string(), "false".to_string());
    map.insert("EnchantPreview.AlwaysShow".to_string(), "true".to_string());
    map.insert("CombatStatusBar.Enabled".to_string(), "false".to_string());
    map.insert("CombatStatusBar.Visible".to_string(), "true".to_string());
    map.insert("CombatStatusBar.SpeedMultiplier".to_string(), "1".to_string());
    map
}

fn build_cfg_content(values: &HashMap<String, String>) -> String {
    let name_override = values.get("StreamerMode.EnableNameOverride").map_or("false", |s| s);
    let enchant_preview_always_show = values.get("EnchantPreview.AlwaysShow").map_or("true", |s| s);
    let combat_bar = values.get("CombatStatusBar.Enabled").map_or("false", |s| s);
    let combat_bar_visible = values.get("CombatStatusBar.Visible").map_or("true", |s| s);
    let speed = values.get("CombatStatusBar.SpeedMultiplier").map_or("1", |s| s);

    format!(
         "[StreamerMode]\n\
         \n\
         ## Whether to set the in-game display name to Anonymous\n\
         # Setting type: Boolean\n\
         # Default value: false\n\
         EnableNameOverride = {name_override}\n\
         \n\
         [EnchantPreview]\n\
         \n\
         ## Whether to always show enchant preview text in item tooltips. If disabled, hold Ctrl to show it.\n\
         # Setting type: Boolean\n\
         # Default value: true\n\
         AlwaysShow = {enchant_preview_always_show}\n\
         \n\
         [CombatStatusBar]\n\
         \n\
         ## Whether to show the combat status bar with elapsed time and speed controls\n\
         # Setting type: Boolean\n\
         # Default value: false\n\
         Enabled = {combat_bar}\n\
         \n\
         ## Whether the combat status bar is currently visible when enabled. Toggled in game with F6.\n\
         # Setting type: Boolean\n\
         # Default value: true\n\
         Visible = {combat_bar_visible}\n\
         \n\
         ## Default combat playback speed multiplier. Supported values: 0.25, 0.50, 1.00, 2.00, 3.00, 4.00, 5.00\n\
         # Setting type: Single\n\
         # Default value: 1\n\
         SpeedMultiplier = {speed}\n"
    )
}

#[tauri::command]
pub async fn read_mod_config(game_path: String) -> Result<ModConfigReadResult, String> {
    let cfg_path = PathBuf::from(&game_path).join(CFG_RELATIVE);

    if !cfg_path.exists() {
        return Ok(ModConfigReadResult {
            config_exists: false,
            values: default_config(),
        });
    }

    let content = fs::read_to_string(&cfg_path).map_err(|e| e.to_string())?;
    let mut config = default_config();
    let mut current_section = String::new();

    for line in content.lines() {
        let trimmed = line.trim();
        if trimmed.starts_with('[') && trimmed.ends_with(']') {
            current_section = trimmed[1..trimmed.len() - 1].to_string();
        } else if !current_section.is_empty()
            && !trimmed.starts_with('#')
            && !trimmed.is_empty()
        {
            if let Some(eq_pos) = trimmed.find('=') {
                let k = trimmed[..eq_pos].trim().to_string();
                let v = trimmed[eq_pos + 1..].trim().to_string();
                config.insert(format!("{}.{}", current_section, k), v);
            }
        }
    }

    Ok(ModConfigReadResult {
        config_exists: true,
        values: config,
    })
}

#[tauri::command]
pub async fn write_config_value(
    game_path: String,
    section: String,
    key: String,
    value: String,
) -> Result<(), String> {
    let cfg_path = PathBuf::from(&game_path).join(CFG_RELATIVE);

    // File doesn't exist: build full default content, override this key, write once
    if !cfg_path.exists() {
        if let Some(parent) = cfg_path.parent() {
            fs::create_dir_all(parent).map_err(|e| e.to_string())?;
        }
        let mut defaults = default_config();
        defaults.insert(format!("{}.{}", section, key), value);
        fs::write(&cfg_path, build_cfg_content(&defaults)).map_err(|e| e.to_string())?;
        return Ok(());
    }

    let content = fs::read_to_string(&cfg_path).map_err(|e| e.to_string())?;
    let mut lines: Vec<String> = content.lines().map(|l| l.to_string()).collect();

    let section_header = format!("[{}]", section);
    let mut in_section = false;
    let mut replaced = false;

    for line in &mut lines {
        let trimmed = line.trim();
        if trimmed.starts_with('[') && trimmed.ends_with(']') {
            in_section = trimmed == section_header;
        } else if in_section && !trimmed.starts_with('#') && !trimmed.is_empty() {
            if let Some(eq_pos) = trimmed.find('=') {
                let existing_key = trimmed[..eq_pos].trim();
                if existing_key == key {
                    *line = format!("{} = {}", key, value);
                    replaced = true;
                    break;
                }
            }
        }
    }

    if !replaced {
        // Find the section and append the key before the next section (or at end)
        let mut found_section = false;
        let mut insert_pos = lines.len();

        for (i, line) in lines.iter().enumerate() {
            let trimmed = line.trim();
            if trimmed == section_header {
                found_section = true;
            } else if found_section && trimmed.starts_with('[') {
                insert_pos = i;
                break;
            }
        }

        if found_section {
            lines.insert(insert_pos, format!("{} = {}", key, value));
        } else {
            lines.push(String::new());
            lines.push(section_header);
            lines.push(format!("{} = {}", key, value));
        }
    }

    fs::write(&cfg_path, lines.join("\n")).map_err(|e| e.to_string())?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn read_mod_config_reports_missing_config_file() {
        let tmp = tempfile::tempdir().unwrap();

        let result =
            tauri::async_runtime::block_on(read_mod_config(tmp.path().display().to_string()))
                .unwrap();

        assert!(!result.config_exists);
        assert_eq!(
            result
                .values
                .get("EnchantPreview.AlwaysShow")
                .map(String::as_str),
            Some("true")
        );
        assert_eq!(
            result
                .values
                .get("CombatStatusBar.Enabled")
                .map(String::as_str),
            Some("false")
        );
        assert_eq!(
            result
                .values
                .get("CombatStatusBar.SpeedMultiplier")
                .map(String::as_str),
            Some("1")
        );
        assert_eq!(
            result
                .values
                .get("CombatStatusBar.Visible")
                .map(String::as_str),
            Some("true")
        );
    }

    #[test]
    fn read_mod_config_reports_existing_config_file() {
        let tmp = tempfile::tempdir().unwrap();
        let cfg_path = tmp.path().join(CFG_RELATIVE);
        fs::create_dir_all(cfg_path.parent().unwrap()).unwrap();
        fs::write(
            &cfg_path,
            "[EnchantPreview]\nAlwaysShow = false\n[CombatStatusBar]\nSpeedMultiplier = 2.00\nEnabled = false\nVisible = false\n",
        )
        .unwrap();

        let result =
            tauri::async_runtime::block_on(read_mod_config(tmp.path().display().to_string()))
                .unwrap();

        assert!(result.config_exists);
        assert_eq!(
            result
                .values
                .get("EnchantPreview.AlwaysShow")
                .map(String::as_str),
            Some("false")
        );
        assert_eq!(
            result
                .values
                .get("CombatStatusBar.SpeedMultiplier")
                .map(String::as_str),
            Some("2.00")
        );
        assert_eq!(
            result
                .values
                .get("CombatStatusBar.Enabled")
                .map(String::as_str),
            Some("false")
        );
        assert_eq!(
            result
                .values
                .get("CombatStatusBar.Visible")
                .map(String::as_str),
            Some("false")
        );
    }
}
