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
  assert.doesNotMatch(html, /id="sync-folder-pick"/);
  assert.doesNotMatch(script, /syncFolderPick/);
  assert.doesNotMatch(html, /id="sync-folder-path"|class="sync-current-value"/);
  assert.doesNotMatch(script, /syncFolderPath/);
  assert.match(script, /pendingSyncFolderPath:\s*null/);
  assert.match(script, /currentPath\s*&&[\s\S]*?current\.textContent\s*=\s*currentPath[\s\S]*?syncFolderCandidate\.value\s*=\s*currentPath/);
});

test("the arbitrary folder picker opens without probing the current cloud path", () => {
  const picker = script.match(/async function chooseSyncFolder\(\) \{[\s\S]*?\n\}/)?.[0] || "";
  assert.match(picker, /open\(\{\s*directory:\s*true,\s*multiple:\s*false,?\s*\}\)/);
  assert.doesNotMatch(picker, /defaultPath/);
});

test("sync configuration and power use one stateful action", () => {
  assert.match(html, /<div class="sync-actions">\s*<button id="sync-action"[^>]*>연결 및 동기화<\/button>\s*<\/div>/);
  assert.doesNotMatch(html, /id="sync-save"|id="sync-toggle"/);
  assert.doesNotMatch(script, /syncSave|syncToggle/);
  assert.match(script, /function hasPendingSyncConfiguration\(\)/);
  assert.match(script, /function renderSyncAction\(\)/);
  assert.match(script, /"changeSync"/);
  assert.match(script, /"turnSyncOn"/);
  assert.match(script, /"turnSyncOff"/);
  assert.match(script, /syncAction\.addEventListener\("click",\s*\(\) => \{ void handleSyncAction\(\); \}\)/);
});

test("Tauri bridges sync configuration to the shared engine", () => {
  assert.match(rust, /async fn sync_configure_folder/);
  assert.match(rust, /"sync-configure-folder"/);
  assert.match(rust, /async fn sync_configure_webdav/);
  assert.match(rust, /"sync-configure-webdav"/);
  assert.match(rust, /async fn sync_toggle/);
});
