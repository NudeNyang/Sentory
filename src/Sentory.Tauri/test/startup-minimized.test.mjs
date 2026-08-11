import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const config = readFileSync(new URL("../src-tauri/tauri.conf.json", import.meta.url), "utf8");
const rust = readFileSync(new URL("../src-tauri/src/main.rs", import.meta.url), "utf8");

test("the main window is created without an initial flash or focus", () => {
  assert.match(config, /"visible": false/);
  assert.match(config, /"focus": false/);
});

test("startup shows the main window minimized without activating it", () => {
  assert.match(
    rust,
    /fn show_main_window_minimized[\s\S]*?SW_SHOWMINNOACTIVE[\s\S]*?ShowWindow\(hwnd\.0 as \*mut c_void, SW_SHOWMINNOACTIVE\)/,
  );
  assert.match(
    rust,
    /if let Some\(window\) = app\.get_webview_window\("main"\)[\s\S]*?apply_window_title_bar\(&window, false\)[\s\S]*?show_main_window_minimized\(&window\)\?/,
  );
});

test("a second launch and tray actions still restore the main window", () => {
  assert.match(
    rust,
    /fn show_main_window\(app: &AppHandle\)[\s\S]*?window\.unminimize\(\)[\s\S]*?window\.show\(\)[\s\S]*?window\.set_focus\(\)/,
  );
  assert.match(
    rust,
    /tauri_plugin_single_instance::init\([\s\S]*?show_main_window\(app\)/,
  );
  assert.match(rust, /"open" => show_main_window\(&app\)/);
});

test("Windows login startup repairs an outdated registration before the next restart", () => {
  assert.match(rust, /const WINDOWS_STARTUP_ARGUMENT: &str = "--windows-startup"/);
  assert.match(
    rust,
    /fn startup_command[\s\S]*?WINDOWS_STARTUP_ARGUMENT/,
  );
  assert.match(
    rust,
    /synchronize_registry_startup_preference[\s\S]*?"startup-preference-get"[\s\S]*?set_registry_startup_enabled\(enabled, false\)/,
  );
  assert.match(
    rust,
    /STARTUP_APPROVED_REGISTRY_PATH[\s\S]*?startup_approval_allows/,
  );
  assert.match(
    rust,
    /repair_registry_startup_before_tauri\(\);[\s\S]*?wait_for_windows_shell\(&arguments\);[\s\S]*?tauri::Builder::default\(\)/,
  );
});

test("Windows login waits briefly for Explorer before creating the tray", () => {
  assert.match(
    rust,
    /fn wait_for_windows_shell[\s\S]*?Shell_TrayWnd[\s\S]*?Duration::from_millis\(500\)/,
  );
  assert.match(
    rust,
    /wait_for_windows_shell\(&arguments\);[\s\S]*?tauri::Builder::default\(\)/,
  );
});
