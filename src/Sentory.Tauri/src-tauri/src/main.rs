use std::borrow::Cow;
use std::fs::{create_dir_all, OpenOptions};
use std::io::Write;
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::{Arc, Mutex as StdMutex};
use std::time::{SystemTime, UNIX_EPOCH};
use tauri::menu::{CheckMenuItem, Menu, MenuItem, PredefinedMenuItem};
use tauri::tray::{MouseButton, TrayIconBuilder, TrayIconEvent};
use tauri::async_runtime::Receiver;
use tauri::{AppHandle, Emitter, Manager, State, WindowEvent};
use tauri_plugin_shell::process::{CommandChild, CommandEvent};
use tauri_plugin_shell::ShellExt;
use tokio::sync::Mutex;
use tokio::time::{interval, Duration};

const MAXIMUM_GALLERY_ITEMS: u16 = 2_000;
const STARTUP_REGISTRY_PATH: &str = r"Software\Microsoft\Windows\CurrentVersion\Run";
const STARTUP_VALUE_NAME: &str = "Sentory";

#[derive(Default)]
struct AppLifecycleState {
    exiting: AtomicBool,
}

struct TrayMenuState {
    open: MenuItem<tauri::Wry>,
    pause: MenuItem<tauri::Wry>,
    startup: CheckMenuItem<tauri::Wry>,
    discord: CheckMenuItem<tauri::Wry>,
    repair: MenuItem<tauri::Wry>,
    open_data: MenuItem<tauri::Wry>,
    exit: MenuItem<tauri::Wry>,
    pause_label: StdMutex<String>,
    resume_label: StdMutex<String>,
    detecting_tooltip: StdMutex<String>,
    paused_tooltip: StdMutex<String>,
    detection_off_tooltip: StdMutex<String>,
    detection_enabled: AtomicBool,
}

#[derive(Clone, Default)]
struct EngineClient {
    process: Arc<Mutex<Option<EngineProcess>>>,
    next_request_id: Arc<AtomicU64>,
}

struct EngineProcess {
    child: CommandChild,
    receiver: Receiver<CommandEvent>,
}

enum EngineCallError {
    Restartable(String),
    Request(String),
}

impl EngineClient {
    async fn request(
        &self,
        app: &AppHandle,
        command: &str,
        payload: serde_json::Value,
    ) -> Result<serde_json::Value, String> {
        let request_id = self.next_request_id.fetch_add(1, Ordering::Relaxed) + 1;
        for attempt in 0..=1 {
            let mut process = self.process.lock().await;
            if process.is_none() {
                let _ = app.emit("engine-status", serde_json::json!({
                    "state": if attempt == 0 { "connecting" } else { "recovering" }
                }));
                *process = Some(spawn_engine(app)?);
            }

            let result = send_engine_request(
                process.as_mut().expect("엔진 프로세스가 초기화되어야 합니다."),
                request_id,
                command,
                &payload,
            )
            .await;
            match result {
                Ok(value) => {
                    let _ = app.emit("engine-status", serde_json::json!({ "state": "ready" }));
                    return Ok(value);
                }
                Err(EngineCallError::Request(message)) => return Err(message),
                Err(EngineCallError::Restartable(message)) => {
                    append_diagnostic(
                        "engine-restart",
                        &format!("attempt={} detail={message}", attempt + 1),
                    );
                    if let Some(failed) = process.take() {
                        let _ = failed.child.kill();
                    }
                    if attempt == 1 {
                        let _ = app.emit(
                            "engine-status",
                            serde_json::json!({ "state": "error", "message": message }),
                        );
                        return Err(format!("C# 엔진 연결을 복구하지 못했습니다: {message}"));
                    }
                }
            }
        }
        Err("C# 엔진 연결을 복구하지 못했습니다.".to_string())
    }

    async fn stop(&self) {
        let mut process = self.process.lock().await;
        let Some(mut running) = process.take() else {
            return;
        };
        let request_id = self.next_request_id.fetch_add(1, Ordering::Relaxed) + 1;
        if let Err(error) = send_engine_request(
            &mut running,
            request_id,
            "shutdown",
            &serde_json::Value::Null,
        )
        .await
        {
            let detail = match error {
                EngineCallError::Restartable(message) | EngineCallError::Request(message) => message,
            };
            append_diagnostic("engine-shutdown-failed", &detail);
            let _ = running.child.kill();
        }
    }
}

fn spawn_engine(app: &AppHandle) -> Result<EngineProcess, String> {
    let command = app
        .shell()
        .sidecar("sentory-engine")
        .map_err(|error| format!("C# 엔진 실행 파일을 찾지 못했습니다: {error}"))?;
    let (receiver, child) = command
        .arg("serve")
        .spawn()
        .map_err(|error| format!("C# 엔진을 실행하지 못했습니다: {error}"))?;
    append_diagnostic("engine-spawned", &format!("pid={}", child.pid()));
    Ok(EngineProcess { child, receiver })
}

async fn send_engine_request(
    process: &mut EngineProcess,
    request_id: u64,
    command: &str,
    payload: &serde_json::Value,
) -> Result<serde_json::Value, EngineCallError> {
    let mut request = serde_json::to_vec(&serde_json::json!({
        "id": request_id,
        "command": command,
        "payload": payload
    }))
    .map_err(|error| EngineCallError::Request(
        format!("C# 엔진 요청을 만들지 못했습니다: {error}")))?;
    request.push(b'\n');
    process
        .child
        .write(&request)
        .map_err(|error| EngineCallError::Restartable(
            format!("C# 엔진에 요청을 보내지 못했습니다: {error}")))?;

    while let Some(event) = process.receiver.recv().await {
        match event {
            CommandEvent::Stdout(line) => {
                let response = serde_json::from_slice::<serde_json::Value>(&line)
                    .map_err(|error| EngineCallError::Restartable(
                        format!("C# 엔진 응답을 읽지 못했습니다: {error}")))?;
                if response.get("id").and_then(serde_json::Value::as_u64) != Some(request_id) {
                    continue;
                }
                if response.get("ok").and_then(serde_json::Value::as_bool) == Some(true) {
                    return Ok(response.get("result").cloned().unwrap_or(serde_json::Value::Null));
                }
                let message = response
                    .get("error")
                    .and_then(serde_json::Value::as_str)
                    .unwrap_or("C# 엔진 요청이 실패했습니다.")
                    .to_string();
                return Err(EngineCallError::Request(message));
            }
            CommandEvent::Stderr(line) => append_diagnostic(
                "engine-stderr",
                String::from_utf8_lossy(&line).trim(),
            ),
            CommandEvent::Error(error) => {
                return Err(EngineCallError::Restartable(error));
            }
            CommandEvent::Terminated(payload) => {
                return Err(EngineCallError::Restartable(format!(
                    "C# 엔진이 종료되었습니다: {:?}",
                    payload.code
                )));
            }
            _ => {}
        }
    }
    Err(EngineCallError::Restartable(
        "C# 엔진 응답 채널이 닫혔습니다.".to_string(),
    ))
}

#[tauri::command]
async fn gallery_list(
    app: AppHandle,
    engine: State<'_, EngineClient>,
    limit: u16,
) -> Result<serde_json::Value, String> {
    let safe_limit = limit.clamp(1, MAXIMUM_GALLERY_ITEMS);
    append_diagnostic("gallery-request", &format!("limit={safe_limit}"));
    engine
        .request(&app, "gallery-list", serde_json::json!(safe_limit))
        .await
}

#[tauri::command]
async fn gallery_page(
    app: AppHandle,
    engine: State<'_, EngineClient>,
    request: serde_json::Value,
) -> Result<serde_json::Value, String> {
    append_diagnostic("gallery-page-request", &request.to_string());
    engine.request(&app, "gallery-page", request).await
}

#[tauri::command]
async fn gallery_item(
    app: AppHandle,
    engine: State<'_, EngineClient>,
    item_id: String,
) -> Result<serde_json::Value, String> {
    engine.request(&app, "gallery-item", serde_json::json!(item_id)).await
}

#[tauri::command]
async fn gallery_favorite(
    app: AppHandle,
    engine: State<'_, EngineClient>,
    item_id: String,
    is_favorite: bool,
) -> Result<serde_json::Value, String> {
    engine
        .request(
            &app,
            "gallery-favorite",
            serde_json::json!({ "itemId": item_id, "isFavorite": is_favorite }),
        )
        .await
}

#[tauri::command]
async fn gallery_delete(
    app: AppHandle,
    engine: State<'_, EngineClient>,
    item_ids: Vec<String>,
) -> Result<serde_json::Value, String> {
    engine.request(&app, "gallery-delete", serde_json::json!(item_ids)).await
}

#[tauri::command]
async fn gallery_open(
    app: AppHandle,
    engine: State<'_, EngineClient>,
    item_id: String,
) -> Result<(), String> {
    let detail = engine
        .request(&app, "gallery-item", serde_json::json!(item_id))
        .await?;
    let target = resolve_open_target(&detail)
        .ok_or_else(|| "열 수 있는 원본이 없습니다.".to_string())?;
    open::that_detached(target)
        .map_err(|error| format!("원본을 열지 못했습니다: {error}"))
}

#[tauri::command]
async fn gallery_copy(
    app: AppHandle,
    engine: State<'_, EngineClient>,
    item_id: String,
) -> Result<serde_json::Value, String> {
    let detail = engine
        .request(&app, "gallery-item", serde_json::json!(item_id))
        .await?;
    let clipboard_detail = detail.clone();
    tauri::async_runtime::spawn_blocking(move || copy_detail_to_clipboard(&clipboard_detail))
        .await
        .map_err(|error| format!("클립보드 작업을 기다리지 못했습니다: {error}"))??;
    engine
        .request(&app, "gallery-copy-record", serde_json::json!(item_id))
        .await
}

#[tauri::command]
async fn gallery_detail_target_open(
    app: AppHandle,
    engine: State<'_, EngineClient>,
    item_id: String,
    member_position: Option<i32>,
) -> Result<(), String> {
    let detail = engine
        .request(&app, "gallery-item", serde_json::json!(item_id))
        .await?;
    let (_, target) = resolve_detail_target(&detail, member_position)?;
    open::that_detached(target)
        .map_err(|error| format!("원본을 열지 못했습니다: {error}"))
}

#[tauri::command]
async fn gallery_detail_target_copy(
    app: AppHandle,
    engine: State<'_, EngineClient>,
    item_id: String,
    member_position: Option<i32>,
) -> Result<(), String> {
    let detail = engine
        .request(&app, "gallery-item", serde_json::json!(item_id))
        .await?;
    let clipboard_detail = detail.clone();
    tauri::async_runtime::spawn_blocking(move || {
        copy_detail_target_to_clipboard(&clipboard_detail, member_position)
    })
    .await
    .map_err(|error| format!("클립보드 작업을 기다리지 못했습니다: {error}"))?
}

#[tauri::command]
async fn settings_get(
    app: AppHandle,
    engine: State<'_, EngineClient>,
) -> Result<serde_json::Value, String> {
    engine.request(&app, "settings-get", serde_json::Value::Null).await
}

#[tauri::command]
async fn settings_update(
    app: AppHandle,
    engine: State<'_, EngineClient>,
    patch: serde_json::Value,
) -> Result<serde_json::Value, String> {
    engine.request(&app, "settings-update", patch).await
}

#[tauri::command]
async fn discord_repair(
    app: AppHandle,
    engine: State<'_, EngineClient>,
) -> Result<serde_json::Value, String> {
    engine.request(&app, "discord-repair", serde_json::Value::Null).await
}

#[tauri::command]
async fn runtime_pause_toggle(
    app: AppHandle,
    engine: State<'_, EngineClient>,
) -> Result<serde_json::Value, String> {
    let status = engine
        .request(&app, "runtime-pause-toggle", serde_json::Value::Null)
        .await?;
    update_pause_menu(&app, &status);
    let _ = app.emit("runtime-status", status.clone());
    Ok(status)
}

#[tauri::command]
async fn data_statistics(
    app: AppHandle,
    engine: State<'_, EngineClient>,
) -> Result<serde_json::Value, String> {
    engine.request(&app, "data-statistics", serde_json::Value::Null).await
}

#[tauri::command]
async fn data_cleanup_preview(
    app: AppHandle,
    engine: State<'_, EngineClient>,
) -> Result<serde_json::Value, String> {
    engine
        .request(&app, "data-cleanup-preview", serde_json::Value::Null)
        .await
}

#[tauri::command]
async fn data_cleanup(
    app: AppHandle,
    engine: State<'_, EngineClient>,
) -> Result<serde_json::Value, String> {
    engine.request(&app, "data-cleanup", serde_json::Value::Null).await
}

#[tauri::command]
async fn open_data_directory(
    app: AppHandle,
    engine: State<'_, EngineClient>,
) -> Result<(), String> {
    let path = engine
        .request(&app, "data-directory", serde_json::Value::Null)
        .await?
        .as_str()
        .ok_or_else(|| "데이터 폴더 경로를 읽지 못했습니다.".to_string())?
        .to_string();
    open::that_detached(path)
        .map_err(|error| format!("데이터 폴더를 열지 못했습니다: {error}"))
}

#[tauri::command]
async fn update_check(
    app: AppHandle,
    engine: State<'_, EngineClient>,
) -> Result<serde_json::Value, String> {
    engine.request(&app, "update-check", serde_json::Value::Null).await
}

#[tauri::command]
async fn startup_get() -> Result<bool, String> {
    read_startup_enabled()
}

#[tauri::command]
async fn startup_set(
    app: AppHandle,
    engine: State<'_, EngineClient>,
    enabled: bool,
) -> Result<serde_json::Value, String> {
    write_startup_enabled(enabled)?;
    let settings = engine
        .request(
            &app,
            "settings-update",
            serde_json::json!({ "startWithWindows": enabled }),
        )
        .await?;
    if let Some(tray) = app.try_state::<TrayMenuState>() {
        let _ = tray.startup.set_checked(enabled);
    }
    Ok(settings)
}

#[allow(clippy::too_many_arguments)]
#[tauri::command]
fn tray_configure(
    app: AppHandle,
    open_label: String,
    pause_label: String,
    resume_label: String,
    startup_label: String,
    discord_label: String,
    repair_label: String,
    open_data_label: String,
    exit_label: String,
    detecting_tooltip: String,
    paused_tooltip: String,
    detection_off_tooltip: String,
    paused: bool,
    detection_enabled: bool,
    startup_enabled: bool,
    discord_enabled: bool,
) -> Result<(), String> {
    let tray = app.state::<TrayMenuState>();
    tray.open.set_text(open_label).map_err(|error| error.to_string())?;
    tray.startup.set_text(startup_label).map_err(|error| error.to_string())?;
    tray.discord.set_text(discord_label).map_err(|error| error.to_string())?;
    tray.repair.set_text(repair_label).map_err(|error| error.to_string())?;
    tray.open_data.set_text(open_data_label).map_err(|error| error.to_string())?;
    tray.exit.set_text(exit_label).map_err(|error| error.to_string())?;
    *tray.pause_label.lock().map_err(|_| "트레이 문구 잠금을 사용할 수 없습니다.")? = pause_label;
    *tray.resume_label.lock().map_err(|_| "트레이 문구 잠금을 사용할 수 없습니다.")? = resume_label;
    *tray.detecting_tooltip.lock().map_err(|_| "트레이 문구 잠금을 사용할 수 없습니다.")? = detecting_tooltip;
    *tray.paused_tooltip.lock().map_err(|_| "트레이 문구 잠금을 사용할 수 없습니다.")? = paused_tooltip;
    *tray.detection_off_tooltip.lock().map_err(|_| "트레이 문구 잠금을 사용할 수 없습니다.")? = detection_off_tooltip;
    tray.detection_enabled.store(detection_enabled, Ordering::Release);
    tray.startup.set_checked(startup_enabled).map_err(|error| error.to_string())?;
    tray.discord.set_checked(discord_enabled).map_err(|error| error.to_string())?;
    update_pause_menu(&app, &serde_json::json!({ "detectionPaused": paused }));
    Ok(())
}

fn resolve_open_target(detail: &serde_json::Value) -> Option<String> {
    let card = detail.get("card")?;
    match card.get("kind")?.as_str()? {
        "Image" => detail.get("contentPath")?.as_str().map(str::to_owned),
        "Collection" => detail
            .get("members")?
            .as_array()?
            .iter()
            .find_map(|member| {
                member
                    .get("contentPath")
                    .and_then(serde_json::Value::as_str)
                    .or_else(|| member.get("originalUrl").and_then(serde_json::Value::as_str))
                    .filter(|value| !value.is_empty())
                    .map(str::to_owned)
            }),
        _ => card
            .get("originalUrl")?
            .as_str()
            .filter(|value| !value.is_empty())
            .map(str::to_owned),
    }
}

fn copy_detail_to_clipboard(detail: &serde_json::Value) -> Result<(), String> {
    let card = detail
        .get("card")
        .ok_or_else(|| "항목 정보를 읽지 못했습니다.".to_string())?;
    let kind = card
        .get("kind")
        .and_then(serde_json::Value::as_str)
        .ok_or_else(|| "항목 종류를 읽지 못했습니다.".to_string())?;
    if kind == "Collection" {
        let payload = collection_clipboard_payload(detail)?;
        return copy_collection_to_clipboard(&payload);
    }
    let mut clipboard = arboard::Clipboard::new()
        .map_err(|error| format!("클립보드를 열지 못했습니다: {error}"))?;
    if kind == "Image" {
        let path = detail
            .get("contentPath")
            .and_then(serde_json::Value::as_str)
            .ok_or_else(|| "사진 원본을 찾지 못했습니다.".to_string())?;
        return copy_image(&mut clipboard, path);
    }
    let url = card
        .get("originalUrl")
        .and_then(serde_json::Value::as_str)
        .filter(|value| !value.is_empty())
        .ok_or_else(|| "링크 원본을 찾지 못했습니다.".to_string())?;
    clipboard
        .set_text(url)
        .map_err(|error| format!("링크를 복사하지 못했습니다: {error}"))
}

#[derive(Debug, PartialEq)]
struct CollectionClipboardPayload {
    urls: Vec<String>,
    image_paths: Vec<String>,
}

fn collection_clipboard_payload(
    detail: &serde_json::Value,
) -> Result<CollectionClipboardPayload, String> {
    let members = detail
        .get("members")
        .and_then(serde_json::Value::as_array)
        .ok_or_else(|| "묶음 항목을 읽지 못했습니다.".to_string())?;
    let mut urls = Vec::new();
    let mut image_paths = Vec::new();
    for member in members {
        match member.get("kind").and_then(serde_json::Value::as_str) {
            Some("Image") => {
                let Some(path) = member
                    .get("contentPath")
                    .and_then(serde_json::Value::as_str)
                    .filter(|value| !value.is_empty() && std::path::Path::new(value).is_file())
                else {
                    continue;
                };
                if !image_paths
                    .iter()
                    .any(|existing: &String| existing.eq_ignore_ascii_case(path))
                {
                    image_paths.push(path.to_owned());
                }
            }
            Some("Url") => {
                let Some(url) = member
                    .get("originalUrl")
                    .and_then(serde_json::Value::as_str)
                    .filter(|value| !value.trim().is_empty())
                else {
                    continue;
                };
                if !urls.iter().any(|existing| existing == url) {
                    urls.push(url.to_owned());
                }
            }
            _ => {}
        }
    }
    if urls.is_empty() && image_paths.is_empty() {
        return Err("복사할 묶음 원본이 없습니다.".to_string());
    }
    Ok(CollectionClipboardPayload { urls, image_paths })
}

#[cfg(target_os = "windows")]
fn copy_collection_to_clipboard(payload: &CollectionClipboardPayload) -> Result<(), String> {
    use clipboard_win::options::NoClear;

    let mut last_error = None;
    let mut clipboard = None;
    for attempt in 0..5 {
        match clipboard_win::Clipboard::new() {
            Ok(opened) => {
                clipboard = Some(opened);
                break;
            }
            Err(error) => {
                last_error = Some(format!("{error:?}"));
                if attempt < 4 {
                    std::thread::sleep(std::time::Duration::from_millis(35));
                }
            }
        }
    }
    let _clipboard = clipboard.ok_or_else(|| {
        format!(
            "클립보드를 열지 못했습니다: {}",
            last_error.unwrap_or_else(|| "알 수 없는 오류".to_string())
        )
    })?;
    clipboard_win::raw::empty()
        .map_err(|error| format!("클립보드를 비우지 못했습니다: {error:?}"))?;
    if !payload.urls.is_empty() {
        clipboard_win::raw::set_string_with(&payload.urls.join("\r\n"), NoClear)
            .map_err(|error| format!("묶음 링크를 복사하지 못했습니다: {error:?}"))?;
    }
    if !payload.image_paths.is_empty() {
        clipboard_win::raw::set_file_list(&payload.image_paths)
            .map_err(|error| format!("묶음 사진을 복사하지 못했습니다: {error:?}"))?;
    }
    Ok(())
}

#[cfg(not(target_os = "windows"))]
fn copy_collection_to_clipboard(_payload: &CollectionClipboardPayload) -> Result<(), String> {
    Err("묶음 파일 복사는 Windows에서만 지원합니다.".to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn collection_clipboard_payload_keeps_all_existing_images_and_unique_links() {
        let root = std::env::temp_dir().join(format!(
            "sentory-tauri-collection-clipboard-{}",
            std::process::id()
        ));
        std::fs::create_dir_all(&root).expect("임시 폴더를 만들어야 합니다.");
        let image_paths = (0..9)
            .map(|index| {
                let path = root.join(format!("photo-{index}.png"));
                std::fs::write(&path, [index as u8]).expect("임시 사진을 만들어야 합니다.");
                path
            })
            .collect::<Vec<_>>();
        let missing = root.join("missing.png");
        let mut members = image_paths
            .iter()
            .map(|path| serde_json::json!({
                "kind": "Image",
                "contentPath": path.to_string_lossy()
            }))
            .collect::<Vec<_>>();
        members.extend([
            serde_json::json!({
                "kind": "Image",
                "contentPath": image_paths[0].to_string_lossy()
            }),
            serde_json::json!({ "kind": "Url", "originalUrl": "https://example.com" }),
            serde_json::json!({ "kind": "Url", "originalUrl": "https://example.com" }),
            serde_json::json!({ "kind": "Url", "originalUrl": "https://openai.com" }),
            serde_json::json!({ "kind": "Image", "contentPath": missing.to_string_lossy() }),
        ]);
        let detail = serde_json::json!({ "members": members });

        let payload = collection_clipboard_payload(&detail)
            .expect("복사 가능한 묶음 페이로드를 만들어야 합니다.");

        assert_eq!(
            payload.urls,
            ["https://example.com", "https://openai.com"]
        );
        assert_eq!(
            payload.image_paths,
            image_paths
                .iter()
                .map(|path| path.to_string_lossy().into_owned())
                .collect::<Vec<_>>()
        );
        std::fs::remove_dir_all(root).expect("임시 폴더를 정리해야 합니다.");
    }
}

fn resolve_detail_target(
    detail: &serde_json::Value,
    member_position: Option<i32>,
) -> Result<(&str, String), String> {
    if let Some(position) = member_position {
        let member = detail
            .get("members")
            .and_then(serde_json::Value::as_array)
            .and_then(|members| {
                members.iter().find(|member| {
                    member.get("position").and_then(serde_json::Value::as_i64)
                        == Some(i64::from(position))
                })
            })
            .ok_or_else(|| "묶음 항목을 찾지 못했습니다.".to_string())?;
        let kind = member
            .get("kind")
            .and_then(serde_json::Value::as_str)
            .ok_or_else(|| "묶음 항목 종류를 읽지 못했습니다.".to_string())?;
        let target = if kind == "Image" {
            member.get("contentPath")
        } else {
            member.get("originalUrl")
        }
        .and_then(serde_json::Value::as_str)
        .filter(|value| !value.is_empty())
        .ok_or_else(|| "묶음 원본을 찾지 못했습니다.".to_string())?;
        return Ok((kind, target.to_owned()));
    }

    let card = detail
        .get("card")
        .ok_or_else(|| "항목 정보를 읽지 못했습니다.".to_string())?;
    let kind = card
        .get("kind")
        .and_then(serde_json::Value::as_str)
        .ok_or_else(|| "항목 종류를 읽지 못했습니다.".to_string())?;
    let target = if kind == "Image" {
        detail.get("contentPath")
    } else {
        card.get("originalUrl")
    }
    .and_then(serde_json::Value::as_str)
    .filter(|value| !value.is_empty())
    .ok_or_else(|| "원본을 찾지 못했습니다.".to_string())?;
    Ok((kind, target.to_owned()))
}

fn copy_detail_target_to_clipboard(
    detail: &serde_json::Value,
    member_position: Option<i32>,
) -> Result<(), String> {
    let (kind, target) = resolve_detail_target(detail, member_position)?;
    let mut clipboard = arboard::Clipboard::new()
        .map_err(|error| format!("클립보드를 열지 못했습니다: {error}"))?;
    if kind == "Image" {
        copy_image(&mut clipboard, &target)
    } else {
        clipboard
            .set_text(target)
            .map_err(|error| format!("링크를 복사하지 못했습니다: {error}"))
    }
}

fn copy_image(clipboard: &mut arboard::Clipboard, path: &str) -> Result<(), String> {
    let decoded = image::ImageReader::open(path)
        .map_err(|error| format!("사진을 열지 못했습니다: {error}"))?
        .decode()
        .map_err(|error| format!("사진을 읽지 못했습니다: {error}"))?
        .into_rgba8();
    let width = decoded.width() as usize;
    let height = decoded.height() as usize;
    clipboard
        .set_image(arboard::ImageData {
            width,
            height,
            bytes: Cow::Owned(decoded.into_raw()),
        })
        .map_err(|error| format!("사진을 복사하지 못했습니다: {error}"))
}

#[tauri::command]
fn ui_diagnostic(event: String, detail: String) {
    append_diagnostic(
        &event.chars().take(48).collect::<String>(),
        &detail.chars().take(240).collect::<String>(),
    );
}

fn append_diagnostic(event: &str, detail: &str) {
    let Some(path) = diagnostic_path() else {
        return;
    };
    if let Some(parent) = path.parent() {
        let _ = create_dir_all(parent);
    }
    let Ok(mut file) = OpenOptions::new().create(true).append(true).open(path) else {
        return;
    };
    let timestamp = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|duration| duration.as_millis())
        .unwrap_or_default();
    let normalized = detail.replace(['\r', '\n'], " ");
    let _ = writeln!(file, "{timestamp}\t{event}\t{normalized}");
}

fn diagnostic_path() -> Option<PathBuf> {
    std::env::var_os("LOCALAPPDATA").map(|local| {
        PathBuf::from(local)
            .join("Sentory")
            .join("logs")
            .join("tauri-ui.log")
    })
}

fn show_main_window(app: &AppHandle) {
    if let Some(window) = app.get_webview_window("main") {
        let _ = window.unminimize();
        let _ = window.show();
        let _ = window.set_focus();
    }
}

fn update_pause_menu(app: &AppHandle, status: &serde_json::Value) {
    let Some(tray) = app.try_state::<TrayMenuState>() else {
        return;
    };
    let paused = status
        .get("detectionPaused")
        .and_then(serde_json::Value::as_bool)
        .unwrap_or(false);
    let label = if paused {
        tray.resume_label.lock().ok().map(|value| value.clone())
    } else {
        tray.pause_label.lock().ok().map(|value| value.clone())
    };
    if let Some(label) = label {
        let _ = tray.pause.set_text(label);
    }
    let tooltip = if paused {
        tray.paused_tooltip.lock().ok().map(|value| value.clone())
    } else if tray.detection_enabled.load(Ordering::Acquire) {
        tray.detecting_tooltip.lock().ok().map(|value| value.clone())
    } else {
        tray.detection_off_tooltip.lock().ok().map(|value| value.clone())
    };
    if let (Some(icon), Some(tooltip)) = (app.tray_by_id("sentory"), tooltip) {
        let _ = icon.set_tooltip(Some(tooltip));
    }
}

fn create_tray(app: &tauri::App) -> tauri::Result<()> {
    let open = MenuItem::with_id(app, "open", "보관함 열기", true, None::<&str>)?;
    let pause = MenuItem::with_id(app, "pause", "감지 일시정지", true, None::<&str>)?;
    let startup = CheckMenuItem::with_id(
        app,
        "startup",
        "Windows 시작 시 실행",
        true,
        read_startup_enabled().unwrap_or(false),
        None::<&str>,
    )?;
    let discord = CheckMenuItem::with_id(
        app,
        "discord",
        "Discord 자동 연결",
        true,
        true,
        None::<&str>,
    )?;
    let repair = MenuItem::with_id(
        app,
        "repair",
        "Discord 재시작 후 연결",
        true,
        None::<&str>,
    )?;
    let open_data = MenuItem::with_id(
        app,
        "open-data",
        "데이터 폴더 열기",
        true,
        None::<&str>,
    )?;
    let exit = MenuItem::with_id(app, "exit", "Sentory 종료", true, None::<&str>)?;
    let first_separator = PredefinedMenuItem::separator(app)?;
    let second_separator = PredefinedMenuItem::separator(app)?;
    let menu = Menu::with_items(
        app,
        &[
            &open,
            &pause,
            &first_separator,
            &startup,
            &discord,
            &repair,
            &open_data,
            &second_separator,
            &exit,
        ],
    )?;
    app.manage(TrayMenuState {
        open,
        pause,
        startup,
        discord,
        repair,
        open_data,
        exit,
        pause_label: StdMutex::new("감지 일시정지".to_string()),
        resume_label: StdMutex::new("감지 다시 시작".to_string()),
        detecting_tooltip: StdMutex::new("Sentory - 메신저 감지 중".to_string()),
        paused_tooltip: StdMutex::new("Sentory - 감지 일시정지됨".to_string()),
        detection_off_tooltip: StdMutex::new("Sentory - 메신저 감지 꺼짐".to_string()),
        detection_enabled: AtomicBool::new(true),
    });
    TrayIconBuilder::with_id("sentory")
        .menu(&menu)
        .show_menu_on_left_click(false)
        .tooltip("Sentory - 메신저 감지 중")
        .icon(app.default_window_icon().expect("Sentory 아이콘이 필요합니다.").clone())
        .on_menu_event(handle_tray_menu_event)
        .on_tray_icon_event(|tray, event| {
            if matches!(
                event,
                TrayIconEvent::DoubleClick {
                    button: MouseButton::Left,
                    ..
                }
            ) {
                show_main_window(tray.app_handle());
            }
        })
        .build(app)?;
    Ok(())
}

fn handle_tray_menu_event(app: &AppHandle, event: tauri::menu::MenuEvent) {
    match event.id().as_ref() {
        "open" => show_main_window(app),
        "exit" => {
            app.state::<AppLifecycleState>()
                .exiting
                .store(true, Ordering::Release);
            app.exit(0);
        }
        "pause" => {
            let handle = app.clone();
            tauri::async_runtime::spawn(async move {
                let engine = handle.state::<EngineClient>();
                if let Ok(status) = engine
                    .request(&handle, "runtime-pause-toggle", serde_json::Value::Null)
                    .await
                {
                    update_pause_menu(&handle, &status);
                    let _ = handle.emit("runtime-status", status);
                }
            });
        }
        "startup" => {
            let handle = app.clone();
            tauri::async_runtime::spawn(async move {
                let enabled = !read_startup_enabled().unwrap_or(false);
                if write_startup_enabled(enabled).is_ok() {
                    let engine = handle.state::<EngineClient>();
                    if let Ok(settings) = engine
                        .request(
                            &handle,
                            "settings-update",
                            serde_json::json!({ "startWithWindows": enabled }),
                        )
                        .await
                    {
                        if let Some(tray) = handle.try_state::<TrayMenuState>() {
                            let _ = tray.startup.set_checked(enabled);
                        }
                        let _ = handle.emit("settings-changed", settings);
                    }
                }
            });
        }
        "discord" => {
            let handle = app.clone();
            tauri::async_runtime::spawn(async move {
                let engine = handle.state::<EngineClient>();
                if let Ok(settings) = engine
                    .request(&handle, "settings-get", serde_json::Value::Null)
                    .await
                {
                    let enabled = !settings
                        .get("sources")
                        .and_then(|sources| sources.get("Discord"))
                        .and_then(serde_json::Value::as_bool)
                        .unwrap_or(true);
                    if let Ok(updated) = engine
                        .request(
                            &handle,
                            "settings-update",
                            serde_json::json!({ "discordSupportEnabled": enabled }),
                        )
                        .await
                    {
                        if let Some(tray) = handle.try_state::<TrayMenuState>() {
                            let _ = tray.discord.set_checked(enabled);
                        }
                        let _ = handle.emit("settings-changed", updated);
                    }
                }
            });
        }
        "repair" => {
            let handle = app.clone();
            tauri::async_runtime::spawn(async move {
                let engine = handle.state::<EngineClient>();
                if let Ok(status) = engine
                    .request(&handle, "discord-repair", serde_json::Value::Null)
                    .await
                {
                    let _ = handle.emit("runtime-status", status);
                }
            });
        }
        "open-data" => {
            let handle = app.clone();
            tauri::async_runtime::spawn(async move {
                let engine = handle.state::<EngineClient>();
                if let Ok(path) = engine
                    .request(&handle, "data-directory", serde_json::Value::Null)
                    .await
                {
                    if let Some(path) = path.as_str() {
                        let _ = open::that_detached(path);
                    }
                }
            });
        }
        _ => {}
    }
}

#[cfg(windows)]
fn read_startup_enabled() -> Result<bool, String> {
    use winreg::enums::{HKEY_CURRENT_USER, KEY_READ};
    use winreg::RegKey;
    let current_user = RegKey::predef(HKEY_CURRENT_USER);
    let Ok(key) = current_user.open_subkey_with_flags(STARTUP_REGISTRY_PATH, KEY_READ) else {
        return Ok(false);
    };
    Ok(key
        .get_value::<String, _>(STARTUP_VALUE_NAME)
        .is_ok_and(|value| !value.trim().is_empty()))
}

#[cfg(not(windows))]
fn read_startup_enabled() -> Result<bool, String> {
    Ok(false)
}

#[cfg(windows)]
fn write_startup_enabled(enabled: bool) -> Result<(), String> {
    use winreg::enums::HKEY_CURRENT_USER;
    use winreg::RegKey;
    let current_user = RegKey::predef(HKEY_CURRENT_USER);
    let (key, _) = current_user
        .create_subkey(STARTUP_REGISTRY_PATH)
        .map_err(|error| format!("자동 실행 설정을 변경하지 못했습니다: {error}"))?;
    if enabled {
        let executable = std::env::current_exe()
            .map_err(|error| format!("실행 파일 경로를 확인하지 못했습니다: {error}"))?;
        key.set_value(
            STARTUP_VALUE_NAME,
            &format!("\"{}\"", executable.display()),
        )
        .map_err(|error| format!("자동 실행 설정을 변경하지 못했습니다: {error}"))
    } else {
        match key.delete_value(STARTUP_VALUE_NAME) {
            Ok(()) => Ok(()),
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(()),
            Err(error) => Err(format!("자동 실행 설정을 변경하지 못했습니다: {error}")),
        }
    }
}

#[cfg(not(windows))]
fn write_startup_enabled(_enabled: bool) -> Result<(), String> {
    Err("이 운영체제에서는 Windows 자동 실행을 사용할 수 없습니다.".to_string())
}

fn main() {
    let engine = EngineClient::default();
    let setup_engine = engine.clone();
    let shutdown_engine = engine.clone();
    let app = tauri::Builder::default()
        .plugin(tauri_plugin_single_instance::init(|app, _args, _cwd| {
            show_main_window(app);
        }))
        .plugin(tauri_plugin_shell::init())
        .manage(AppLifecycleState::default())
        .manage(engine)
        .setup(move |app| {
            create_tray(app)?;
            let handle = app.handle().clone();
            let client = setup_engine.clone();
            tauri::async_runtime::spawn(async move {
                if let Err(error) = client
                    .request(&handle, "health", serde_json::Value::Null)
                    .await
                {
                    append_diagnostic("engine-prewarm-failed", &error);
                }

                let mut last_revision = client
                    .request(&handle, "gallery-revision", serde_json::Value::Null)
                    .await
                    .ok();
                let mut ticker = interval(Duration::from_secs(1));
                ticker.tick().await;
                loop {
                    ticker.tick().await;
                    match client
                        .request(&handle, "gallery-revision", serde_json::Value::Null)
                        .await
                    {
                        Ok(revision) => {
                            if last_revision.as_ref().is_some_and(|previous| previous != &revision) {
                                let _ = handle.emit("gallery-changed", revision.clone());
                            }
                            last_revision = Some(revision);
                        }
                        Err(error) => append_diagnostic("gallery-monitor-failed", &error),
                    }
                    match client
                        .request(&handle, "runtime-poll", serde_json::Value::Null)
                        .await
                    {
                        Ok(poll) => {
                            if let Some(status) = poll.get("status") {
                                let _ = handle.emit("runtime-status", status.clone());
                            }
                            if let Some(events) = poll.get("events").and_then(serde_json::Value::as_array) {
                                for event in events {
                                    let Some(event_type) = event.get("type").and_then(serde_json::Value::as_str) else {
                                        continue;
                                    };
                                    let payload = event.get("payload").cloned().unwrap_or(serde_json::Value::Null);
                                    let event_name = match event_type {
                                        "captured" => "capture-event",
                                        "runtime-issue" => "runtime-issue",
                                        "settings-changed" => "settings-changed",
                                        _ => "runtime-status",
                                    };
                                    let _ = handle.emit(event_name, payload);
                                }
                            }
                        }
                        Err(error) => append_diagnostic("runtime-poll-failed", &error),
                    }
                }
            });
            Ok(())
        })
        .on_window_event(|window, event| {
            if let WindowEvent::CloseRequested { api, .. } = event {
                let lifecycle = window.app_handle().state::<AppLifecycleState>();
                if !lifecycle.exiting.load(Ordering::Acquire) {
                    api.prevent_close();
                    let _ = window.hide();
                }
            }
        })
        .invoke_handler(tauri::generate_handler![
            gallery_list,
            gallery_page,
            gallery_item,
            gallery_favorite,
            gallery_delete,
            gallery_open,
            gallery_copy,
            gallery_detail_target_open,
            gallery_detail_target_copy,
            settings_get,
            settings_update,
            discord_repair,
            runtime_pause_toggle,
            data_statistics,
            data_cleanup_preview,
            data_cleanup,
            open_data_directory,
            update_check,
            startup_get,
            startup_set,
            tray_configure,
            ui_diagnostic
        ])
        .build(tauri::generate_context!())
        .expect("Sentory Tauri UI를 실행하지 못했습니다.");
    app.run(move |_handle, event| {
        if matches!(event, tauri::RunEvent::Exit) {
            tauri::async_runtime::block_on(shutdown_engine.stop());
        }
    });
}
