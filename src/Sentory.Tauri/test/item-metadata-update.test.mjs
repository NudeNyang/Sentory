import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const script = readFileSync(new URL("../web/app.js", import.meta.url), "utf8");

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
  assert.match(refresh, /detailCopyCount\.textContent/);
  assert.match(refresh, /detailFavoriteMark\.hidden/);
});
