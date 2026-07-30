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

test("only detected cloud folders and an arbitrary folder are selectable", () => {
  assert.doesNotMatch(script, /SUPPORTED_CLOUD_PROVIDERS/);
  assert.doesNotMatch(script, /chooseProviderFolder/);
  assert.match(script, /for \(const candidate of state\.syncCandidates\)[\s\S]*?candidate\.folderPath[\s\S]*?candidate\.displayName/);
  assert.match(script, /chooseOtherFolder/);
  assert.match(script, /placeholder\.hidden\s*=\s*true/);
  assert.match(script, /\[\.\.\.select\.options\]\.filter\(option => !option\.hidden\)/);
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
