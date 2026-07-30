import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const css = readFileSync(new URL("../web/styles.css", import.meta.url), "utf8");
const app = readFileSync(new URL("../web/app.js", import.meta.url), "utf8");

test("link preview artwork uses the same centered background crop as WPF", () => {
  const rule = css.match(/\.artwork-cover\s*\{([^}]*)\}/);
  assert.ok(rule, "missing link preview background artwork rule");
  for (const declaration of [
    "width: 100%",
    "height: 100%",
    "background-position: 50% 50%",
    "background-repeat: no-repeat",
    "background-size: cover",
  ]) {
    assert.match(rule[1], new RegExp(declaration.replace(/[()%]/g, "\\$&")));
  }
  assert.match(app, /item\.artworkMode === "cover"/);
  assert.match(app, /cover\.style\.backgroundImage = `url\("\$\{artworkUrl\}"\)`/);
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
