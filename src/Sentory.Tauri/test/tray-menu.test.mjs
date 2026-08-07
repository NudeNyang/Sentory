import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const html = readFileSync(new URL("../web/tray.html", import.meta.url), "utf8");
const css = readFileSync(new URL("../web/tray.css", import.meta.url), "utf8");
const script = readFileSync(new URL("../web/tray.js", import.meta.url), "utf8");
const rust = readFileSync(new URL("../src-tauri/src/main.rs", import.meta.url), "utf8");
const capability = readFileSync(new URL("../src-tauri/capabilities/default.json", import.meta.url), "utf8");

test("custom tray surface keeps the WPF action order", () => {
  const ids = [
    "tray-open",
    "tray-pause",
    "tray-startup",
    "tray-discord",
    "tray-repair",
    "tray-open-data",
    "tray-exit",
  ];
  let previous = -1;
  for (const id of ids) {
    const position = html.indexOf(`id="${id}"`);
    assert.ok(position > previous, `${id} must follow the WPF order`);
    previous = position;
  }
  assert.match(html, /id="tray-status"/);
  assert.match(html, /id="tray-double-click"/);
  assert.match(html, /id="tray-accessibility"/);
});

test("custom tray surface matches the compact WPF dimensions", () => {
  assert.match(rust, /const TRAY_MENU_BASE_HEIGHT:\s*f64\s*=\s*362\.0/);
  assert.match(
    css,
    /\.tray-card\s*\{[^}]*margin:\s*10px[^}]*padding:\s*10px[^}]*border:\s*1px solid color-mix\(in srgb, var\(--accent\) 82%, var\(--text\)\)[^}]*border-radius:\s*15px/s,
  );
  assert.match(css, /\.tray-action\s*\{[^}]*height:\s*42px[^}]*border-radius:\s*9px[^}]*font-size:\s*12px/s);
  assert.match(css, /\.tray-title\s*\{[^}]*font-size:\s*16px[^}]*font-weight:\s*600/s);
});

test("right click opens a custom Tauri window and actions stay connected", () => {
  assert.match(rust, /WebviewWindowBuilder::new[\s\S]*"tray-menu"[\s\S]*tray\.html/);
  assert.match(rust, /TrayIconEvent::Click[\s\S]*MouseButton::Right/);
  assert.doesNotMatch(rust, /\.menu\(&menu\)/);
  assert.match(script, /invoke\("tray_action",\s*\{\s*action\s*\}\)/);
  assert.match(script, /listen\("tray-state"/);
  assert.deepEqual(JSON.parse(capability).windows, ["main", "tray-menu"]);
});

test("left click restores the main Sentory window", () => {
  assert.match(
    rust,
    /TrayIconEvent::Click\s*\{[\s\S]*button:\s*MouseButton::Left[\s\S]*button_state:\s*MouseButtonState::Up[\s\S]*show_main_window\(tray\.app_handle\(\)\)/,
  );
});

test("tray surface does not depend on transparent WebView composition", () => {
  assert.match(
    rust,
    /WebviewWindowBuilder::new[\s\S]*"tray-menu"[\s\S]*\.transparent\(false\)[\s\S]*\.background_color\(Color\(/,
  );
  assert.doesNotMatch(rust, /"tray-menu"[\s\S]*?\.transparent\(true\)/);
  assert.match(rust, /\.background_color\(Color\(247, 243, 236, 255\)\)/);
  assert.match(rust, /apply_tray_window_style\(&window\)/);
  assert.match(css, /html,\s*body\s*\{[^}]*background:\s*var\(--surface\)/s);
});

test("opaque tray window is clipped to the original rounded card bounds", () => {
  assert.match(rust, /const TRAY_MENU_INSET:\s*f64\s*=\s*10\.0/);
  assert.match(rust, /const TRAY_MENU_RADIUS:\s*f64\s*=\s*15\.0/);
  assert.match(rust, /CreateRoundRectRgn[\s\S]*SetWindowRgn/);
  assert.match(
    rust,
    /\(size\.width as f64 - inset\)\.round\(\) as i32 \+ 1[\s\S]*\(size\.height as f64 - inset\)\.round\(\) as i32 \+ 1/,
  );
  const showTray = rust.slice(
    rust.indexOf("fn show_tray_menu"),
    rust.indexOf("fn hide_tray_menu"),
  );
  assert.match(showTray, /apply_tray_window_style\(&window\)/);
});

test("only navigation and exit actions close the tray menu", () => {
  assert.match(
    rust,
    /fn tray_action_closes_menu\(action:\s*&str\)[\s\S]*matches!\(action,\s*"open"\s*\|\s*"open-data"\s*\|\s*"exit"\)/,
  );
  const trayAction = rust.slice(
    rust.indexOf("async fn tray_action"),
    rust.indexOf("async fn read_startup_enabled"),
  );
  assert.match(
    trayAction,
    /if tray_action_closes_menu\(&action\)\s*\{\s*hide_tray_menu\(&app\);\s*\}/,
  );
});
