import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const css = readFileSync(new URL("../web/styles.css", import.meta.url), "utf8");

test("contained card artwork preserves the full image aspect ratio", () => {
  const rule = css.match(/\.artwork img\.contain\s*\{([^}]*)\}/);
  assert.ok(rule, "missing contained artwork rule");
  for (const declaration of [
    "width: auto",
    "height: auto",
    "max-width: 214px",
    "max-height: 144px",
  ]) {
    assert.match(rule[1], new RegExp(declaration.replace(/[()%]/g, "\\$&")));
  }
});
