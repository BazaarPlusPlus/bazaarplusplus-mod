mod commands;

use commands::{
    bepinex::{install_bepinex, uninstall_bpp},
    config::{read_mod_config, write_config_value},
    detect::{detect_dotnet_runtime, detect_environment, verify_game_path},
    vdf::patch_launch_options,
};

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_opener::init())
        .invoke_handler(tauri::generate_handler![
            detect_environment,
            detect_dotnet_runtime,
            verify_game_path,
            install_bepinex,
            uninstall_bpp,
            patch_launch_options,
            read_mod_config,
            write_config_value,
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
