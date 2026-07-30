import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const html = readFileSync(new URL("../web/index.html", import.meta.url), "utf8");
const script = readFileSync(new URL("../web/app.js", import.meta.url), "utf8");
const rust = readFileSync(new URL("../src-tauri/src/main.rs", import.meta.url), "utf8");

test("settings expose cloud folder and NAS WebDAV sharing", () => {
  assert.match(html, /id="sync-heading"/);
  assert.match(html, /id="sync-mode"/);
  assert.match(html, /id="sync-webdav-endpoint"/);
  assert.match(html, /id="sync-webdav-password"[^>]*type="password"/);
  assert.match(script, /cloudNasSharing/);
  assert.match(script, /sync_configure_folder/);
  assert.match(script, /sync_configure_webdav/);
  assert.match(script, /sync_toggle/);
});

test("Tauri bridges sync configuration to the shared engine", () => {
  assert.match(rust, /async fn sync_configure_folder/);
  assert.match(rust, /"sync-configure-folder"/);
  assert.match(rust, /async fn sync_configure_webdav/);
  assert.match(rust, /"sync-configure-webdav"/);
  assert.match(rust, /async fn sync_toggle/);
});
