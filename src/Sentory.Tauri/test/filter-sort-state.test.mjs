import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const script = readFileSync(new URL("../web/app.js", import.meta.url), "utf8");

test("selecting the active period does not reload the gallery", () => {
  assert.match(
    script,
    /dateOptions\.addEventListener[\s\S]*?if \(state\.dateRange === button\.dataset\.date\) return;[\s\S]*?state\.dateRange = button\.dataset\.date;[\s\S]*?resetGallery\(\);/,
  );
});

test("selecting the active sort closes the menu without reloading", () => {
  assert.match(
    script,
    /sortMenu\.addEventListener[\s\S]*?if \(state\.sort === button\.dataset\.sort\)[\s\S]*?return;[\s\S]*?state\.sort = button\.dataset\.sort;[\s\S]*?resetGallery\(\);/,
  );
});

test("the default newest sort is highlighted during startup", () => {
  assert.match(script, /function updateSortUi\(\)[\s\S]*?classList\.toggle\("selected", state\.sort === button\.dataset\.sort\)/);
  const startup = script.slice(script.lastIndexOf("updateFilterUi();"));
  assert.match(startup, /updateFilterUi\(\);\s*updateSortUi\(\);\s*updateSelectionUi\(\);/);
});
