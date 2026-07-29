use std::borrow::Cow;
use std::fs::{create_dir_all, OpenOptions};
use std::io::Write;
use std::path::PathBuf;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::Arc;
use std::time::{SystemTime, UNIX_EPOCH};
use tauri::async_runtime::Receiver;
use tauri::{AppHandle, Emitter, State};
use tauri_plugin_shell::process::{CommandChild, CommandEvent};
use tauri_plugin_shell::ShellExt;
use tokio::sync::Mutex;
use tokio::time::{interval, Duration};

const MAXIMUM_GALLERY_ITEMS: u16 = 2_000;

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
    let mut clipboard = arboard::Clipboard::new()
        .map_err(|error| format!("클립보드를 열지 못했습니다: {error}"))?;
    if kind == "Image" {
        let path = detail
            .get("contentPath")
            .and_then(serde_json::Value::as_str)
            .ok_or_else(|| "사진 원본을 찾지 못했습니다.".to_string())?;
        return copy_image(&mut clipboard, path);
    }
    if kind == "Collection" {
        let members = detail
            .get("members")
            .and_then(serde_json::Value::as_array)
            .ok_or_else(|| "모음 항목을 읽지 못했습니다.".to_string())?;
        if let Some(path) = members.iter().find_map(|member| {
            member
                .get("contentPath")
                .and_then(serde_json::Value::as_str)
        }) {
            return copy_image(&mut clipboard, path);
        }
        let links = members
            .iter()
            .filter_map(|member| member.get("originalUrl").and_then(serde_json::Value::as_str))
            .filter(|value| !value.is_empty())
            .collect::<Vec<_>>()
            .join("\r\n");
        if links.is_empty() {
            return Err("복사할 모음 원본이 없습니다.".to_string());
        }
        return clipboard
            .set_text(links)
            .map_err(|error| format!("링크를 복사하지 못했습니다: {error}"));
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

fn main() {
    let engine = EngineClient::default();
    let setup_engine = engine.clone();
    let shutdown_engine = engine.clone();
    let app = tauri::Builder::default()
        .plugin(tauri_plugin_shell::init())
        .manage(engine)
        .setup(move |app| {
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
        .invoke_handler(tauri::generate_handler![
            gallery_list,
            gallery_page,
            gallery_item,
            gallery_favorite,
            gallery_delete,
            gallery_open,
            gallery_copy,
            settings_get,
            settings_update,
            discord_repair,
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
