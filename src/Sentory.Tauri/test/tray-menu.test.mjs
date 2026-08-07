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
  assert.match(
    css,
    /\.tray-card\s*\{[^}]*height:\s*100%[^}]*margin:\s*0[^}]*padding:\s*20px[^}]*border:\s*1px solid color-mix\(in srgb, var\(--accent\) 82%, var\(--text\)\)[^}]*border-radius:\s*8px/s,
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
