mod commands;

use commands::{
    app_update::{fetch_app_update, install_app_update, PendingUpdate},
    bepinex::{install_bepinex, uninstall_bpp},
    config::{read_mod_config, write_config_value},
    detect::{detect_dotnet_runtime, detect_environment, verify_game_path},
    vdf::patch_launch_options,
};
use std::sync::Mutex;

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_updater::Builder::new().build())
        .manage(PendingUpdate(Mutex::new(None)))
        .invoke_handler(tauri::generate_handler![
            detect_environment,
            detect_dotnet_runtime,
            verify_game_path,
            install_bepinex,
            uninstall_bpp,
            patch_launch_options,
            read_mod_config,
            write_config_value,
            fetch_app_update,
            install_app_update,
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
