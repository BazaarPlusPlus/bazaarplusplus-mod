mod commands;

use commands::{
    bepinex::install_bepinex,
    detect::{detect_environment, verify_game_path},
    vdf::patch_launch_options,
};

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_opener::init())
        .invoke_handler(tauri::generate_handler![
            detect_environment,
            verify_game_path,
            install_bepinex,
            patch_launch_options,
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
