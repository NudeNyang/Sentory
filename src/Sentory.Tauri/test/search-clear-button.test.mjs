import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const css = readFileSync(new URL("../web/styles.css", import.meta.url), "utf8");

test("the search clear button uses a quiet neutral gray", () => {
  const rule = css.match(
    /\.search-box input::\-webkit-search-cancel-button\s*\{([^}]*)\}/,
  );
  assert.ok(rule, "missing custom search clear button rule");
  assert.match(rule[1], /-webkit-appearance:\s*none/);
  assert.match(rule[1], /color:\s*var\(--soft\)/);
  assert.doesNotMatch(rule[1], /accent|#[0-9a-f]{3,8}/i);
});
