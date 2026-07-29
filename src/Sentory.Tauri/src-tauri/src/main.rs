use std::fs::{create_dir_all, OpenOptions};
use std::io::Write;
use std::path::PathBuf;
use std::time::{SystemTime, UNIX_EPOCH};
use tauri::AppHandle;
use tauri_plugin_shell::ShellExt;

const MAXIMUM_GALLERY_ITEMS: u16 = 2_000;

#[tauri::command]
async fn gallery_list(
    app: AppHandle,
    limit: u16,
) -> Result<serde_json::Value, String> {
    let safe_limit = limit.clamp(1, MAXIMUM_GALLERY_ITEMS);
    append_diagnostic("gallery-request", &format!("limit={safe_limit}"));
    let sidecar = app
        .shell()
        .sidecar("sentory-engine")
        .map_err(|error| format!("C# 엔진 실행 파일을 찾지 못했습니다: {error}"))?;
    let output = sidecar
        .args(["gallery-list", &safe_limit.to_string()])
        .output()
        .await
        .map_err(|error| format!("C# 엔진을 실행하지 못했습니다: {error}"))?;

    if !output.status.success() {
        let error = String::from_utf8_lossy(&output.stderr).trim().to_owned();
        append_diagnostic(
            "gallery-sidecar-failed",
            &format!("status={:?} detail={}", output.status.code(), error),
        );
        return Err(if error.is_empty() {
            format!("C# 엔진이 종료 코드 {:?}로 끝났습니다.", output.status.code())
        } else {
            error
        });
    }

    let result = serde_json::from_slice::<serde_json::Value>(&output.stdout)
        .map_err(|error| format!("C# 엔진 응답을 읽지 못했습니다: {error}"));
    match &result {
        Ok(value) => append_diagnostic(
            "gallery-response",
            &format!(
                "bytes={} total={}",
                output.stdout.len(),
                value["total"].as_u64().unwrap_or_default()
            ),
        ),
        Err(error) => append_diagnostic("gallery-parse-failed", error),
    }
    result
}

#[tauri::command]
async fn gallery_page(
    app: AppHandle,
    request: serde_json::Value,
) -> Result<serde_json::Value, String> {
    let request_json = serde_json::to_string(&request)
        .map_err(|error| format!("갤러리 요청을 만들지 못했습니다: {error}"))?;
    append_diagnostic("gallery-page-request", &request_json);
    run_sidecar_json(&app, &["gallery-page", &request_json]).await
}

async fn run_sidecar_json(
    app: &AppHandle,
    args: &[&str],
) -> Result<serde_json::Value, String> {
    let sidecar = app
        .shell()
        .sidecar("sentory-engine")
        .map_err(|error| format!("C# 엔진 실행 파일을 찾지 못했습니다: {error}"))?;
    let output = sidecar
        .args(args)
        .output()
        .await
        .map_err(|error| format!("C# 엔진을 실행하지 못했습니다: {error}"))?;
    if !output.status.success() {
        let error = String::from_utf8_lossy(&output.stderr).trim().to_owned();
        append_diagnostic(
            "gallery-sidecar-failed",
            &format!("status={:?} detail={}", output.status.code(), error),
        );
        return Err(if error.is_empty() {
            format!("C# 엔진이 종료 코드 {:?}로 끝났습니다.", output.status.code())
        } else {
            error
        });
    }
    serde_json::from_slice::<serde_json::Value>(&output.stdout)
        .map_err(|error| format!("C# 엔진 응답을 읽지 못했습니다: {error}"))
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
    tauri::Builder::default()
        .plugin(tauri_plugin_shell::init())
        .invoke_handler(tauri::generate_handler![
            gallery_list,
            gallery_page,
            ui_diagnostic
        ])
        .run(tauri::generate_context!())
        .expect("Sentory Tauri UI를 실행하지 못했습니다.");
}
