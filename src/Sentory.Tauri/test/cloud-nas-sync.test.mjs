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

test("all supported cloud providers and an arbitrary folder remain selectable", () => {
  assert.match(script, /SUPPORTED_CLOUD_PROVIDERS\s*=\s*\[[\s\S]*?onedrive[\s\S]*?google-drive[\s\S]*?dropbox[\s\S]*?mega[\s\S]*?\]/);
  assert.match(script, /chooseProviderFolder/);
  assert.match(script, /chooseOtherFolder/);
  assert.match(script, /value\.startsWith\("pick:"\)[\s\S]*?chooseSyncFolder/);
  assert.match(script, /syncFolderPick\.textContent\s*=\s*t\(folderPath\s*\?\s*"changeFolder"\s*:\s*"chooseFolder"\)/);
  assert.match(html, /id="sync-folder-pick"[^>]*class="settings-action sync-folder-action"/);
});

test("Tauri bridges sync configuration to the shared engine", () => {
  assert.match(rust, /async fn sync_configure_folder/);
  assert.match(rust, /"sync-configure-folder"/);
  assert.match(rust, /async fn sync_configure_webdav/);
  assert.match(rust, /"sync-configure-webdav"/);
  assert.match(rust, /async fn sync_toggle/);
});
