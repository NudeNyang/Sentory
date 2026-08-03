import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const script = readFileSync(new URL("../web/app.js", import.meta.url), "utf8");
const rustMain = readFileSync(new URL("../src-tauri/src/main.rs", import.meta.url), "utf8");

function functionBody(name, nextName) {
  const start = script.indexOf(`function ${name}`);
  const end = script.indexOf(`function ${nextName}`, start + 1);
  assert.ok(start >= 0 && end > start, `${name} function was not found`);
  return script.slice(start, end);
}

test("copy and favorite metadata keep existing card DOM instances", () => {
  const update = functionBody("updateLoadedItem", "refreshVisibleItemMetadata");
  assert.match(update, /Object\.assign\(current, updated\)/);
  assert.match(update, /refreshVisibleItemMetadata\(current\)/);
  assert.doesNotMatch(update, /renderRevision|renderVisibleCards|resetGallery/);
});

test("the changed card and open detail are patched in place", () => {
  const refresh = functionBody("refreshVisibleItemMetadata", "openItem");
  assert.match(refresh, /\.copy-usage/);
  assert.match(refresh, /\.favorite/);
  assert.match(refresh, /detailUsageCount\.textContent/);
  assert.match(refresh, /detailUsageBreakdown\.textContent/);
  assert.match(refresh, /detailFavoriteMark\.hidden/);
});

test("copy buttons go directly from copy to the success check", () => {
  const itemCopy = functionBody("copyItem", "toggleFavorite");
  const detailCopy = functionBody("copyDetailTarget", "deleteItems");
  for (const copyFlow of [itemCopy, detailCopy]) {
    assert.match(copyFlow, /\.disabled = true/);
    assert.match(copyFlow, /innerHTML = "&#xE73E;"/);
    assert.doesNotMatch(copyFlow, /E895/);
  }
});

test("detail target copies record usage and update the open item", () => {
  const detailCopy = functionBody("copyDetailTarget", "deleteItems");
  assert.match(detailCopy, /const result = await tauriCore\(\)\.invoke\("gallery_detail_target_copy"/);
  assert.match(detailCopy, /updateLoadedItem/);
  assert.match(detailCopy, /copyCount: result\.copyCount/);

  const commandStart = rustMain.indexOf("async fn gallery_detail_target_copy");
  const commandEnd = rustMain.indexOf("async fn settings_get", commandStart);
  assert.ok(commandStart >= 0 && commandEnd > commandStart);
  const command = rustMain.slice(commandStart, commandEnd);
  assert.match(command, /"gallery-copy-record"/);
});
