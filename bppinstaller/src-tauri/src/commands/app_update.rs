use std::sync::Mutex;

use tauri::{AppHandle, State, Url};
use tauri_plugin_updater::{Update, UpdaterExt};

const UPDATER_ENDPOINTS: Option<&str> = option_env!("BPP_UPDATER_ENDPOINTS");
const UPDATER_ENDPOINT: Option<&str> = option_env!("BPP_UPDATER_ENDPOINT");
const UPDATER_PUBKEY: Option<&str> = option_env!("BPP_UPDATER_PUBKEY");

pub struct PendingUpdate(pub Mutex<Option<Update>>);

#[derive(Clone, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct AppUpdateMetadata {
    version: String,
    current_version: String,
    body: Option<String>,
    date: Option<String>,
}

fn updater_endpoints() -> Result<Option<Vec<Url>>, String> {
    let Some(raw) = UPDATER_ENDPOINTS.or(UPDATER_ENDPOINT) else {
        return Ok(None);
    };

    let endpoints = raw
        .split(',')
        .map(str::trim)
        .filter(|entry| !entry.is_empty())
        .map(|entry| {
            Url::parse(entry).map_err(|err| format!("Invalid updater endpoint URL `{entry}`: {err}"))
        })
        .collect::<Result<Vec<_>, _>>()?;

    if endpoints.is_empty() {
        return Err(
            "Updater is not configured. BPP_UPDATER_ENDPOINTS did not contain any valid URL."
                .to_string(),
        );
    }

    Ok(Some(endpoints))
}

fn updater_pubkey() -> Option<&'static str> {
    let pubkey = UPDATER_PUBKEY?;

    let pubkey = pubkey.trim();
    if pubkey.is_empty() {
        return None;
    }

    Some(pubkey)
}

#[tauri::command]
pub async fn fetch_app_update(
    app: AppHandle,
    pending_update: State<'_, PendingUpdate>,
) -> Result<Option<AppUpdateMetadata>, String> {
    let mut builder = app.updater_builder();

    if let Some(pubkey) = updater_pubkey() {
        builder = builder.pubkey(pubkey);
    }

    if let Some(endpoints) = updater_endpoints()? {
        builder = builder
            .endpoints(endpoints)
            .map_err(|err| format!("Cannot configure updater endpoints: {err}"))?;
    }

    let update = builder
        .build()
        .map_err(|err| format!("Cannot build updater: {err}"))?
        .check()
        .await
        .map_err(|err| format!("Cannot check for updates: {err}"))?;

    let metadata = update.as_ref().map(|update| AppUpdateMetadata {
        version: update.version.clone(),
        current_version: update.current_version.clone(),
        body: update.body.clone(),
        date: update.date.map(|date| date.to_string()),
    });

    *pending_update.0.lock().unwrap() = update;

    Ok(metadata)
}

#[tauri::command]
pub async fn install_app_update(
    app: AppHandle,
    pending_update: State<'_, PendingUpdate>,
) -> Result<(), String> {
    let Some(update) = pending_update.0.lock().unwrap().take() else {
        return Err("No pending update. Check for updates first.".to_string());
    };

    update
        .download_and_install(|_, _| {}, || {})
        .await
        .map_err(|err| format!("Cannot install update: {err}"))?;

    app.restart();
}
