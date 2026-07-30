import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const css = readFileSync(new URL("../web/styles.css", import.meta.url), "utf8");

test("card artwork fills the thumbnail and keeps the image center in view", () => {
  const rule = css.match(/\.artwork img\s*\{([^}]*)\}/);
  assert.ok(rule, "missing card artwork rule");
  for (const declaration of [
    "width: 100%",
    "height: 100%",
    "object-fit: cover",
    "object-position: center",
  ]) {
    assert.match(rule[1], new RegExp(declaration.replace(/[()%]/g, "\\$&")));
  }

  assert.doesNotMatch(css, /\.artwork img\.contain\s*\{[^}]*object-fit:\s*contain/);
});

test("detail artwork continues to show the full image", () => {
  assert.match(css, /\.detail-artwork \.detail-main-artwork\s*\{[^}]*object-fit:\s*contain/);
});
