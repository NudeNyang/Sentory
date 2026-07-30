import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const css = readFileSync(new URL("../web/styles.css", import.meta.url), "utf8");

test("link preview artwork fills the thumbnail and keeps the image center in view", () => {
  const rule = css.match(/\.artwork img\s*\{([^}]*)\}/);
  assert.ok(rule, "missing link preview artwork rule");
  for (const declaration of [
    "width: 100%",
    "height: 100%",
    "object-fit: cover",
    "object-position: center",
  ]) {
    assert.match(rule[1], new RegExp(declaration.replace(/[()%]/g, "\\$&")));
  }

});

test("photo card artwork preserves the full image aspect ratio", () => {
  const rule = css.match(/\.artwork img\.contain\s*\{([^}]*)\}/);
  assert.ok(rule, "missing contained photo artwork rule");
  for (const declaration of [
    "width: auto",
    "height: auto",
    "max-width: 214px",
    "max-height: 144px",
    "object-fit: contain",
  ]) {
    assert.match(rule[1], new RegExp(declaration.replace(/[()%]/g, "\\$&")));
  }
});

test("detail artwork continues to show the full image", () => {
  assert.match(css, /\.detail-artwork \.detail-main-artwork\s*\{[^}]*object-fit:\s*contain/);
});
