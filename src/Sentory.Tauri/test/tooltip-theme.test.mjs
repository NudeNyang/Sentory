import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const html = readFileSync(new URL("../web/index.html", import.meta.url), "utf8");
const css = readFileSync(new URL("../web/styles.css", import.meta.url), "utf8");
const script = readFileSync(new URL("../web/app.js", import.meta.url), "utf8");

test("theme toggle keeps its accessible label without showing a tooltip", () => {
  const themeButton = html.match(/<button id="theme"[^>]*>/)?.[0] ?? "";

  assert.ok(themeButton);
  assert.doesNotMatch(themeButton, /\stitle=/);
  assert.doesNotMatch(script, /themeButton\.title\s*=/);
  assert.match(script, /themeButton\.setAttribute\("aria-label",\s*themeLabel\)/);
});

test("interactive hints use the borderless app tooltip instead of native title popups", () => {
  assert.match(html, /id="app-tooltip"[^>]*role="tooltip"[^>]*hidden/);
  assert.match(css, /\.app-tooltip\s*\{[^}]*border:\s*0\b/s);
  assert.match(script, /function setTooltip\(/);
  assert.match(script, /setTooltip\(artwork,\s*t\("openPreview"\)\)/);
});

test("native title bar theme is not repainted when the resolved theme did not change", () => {
  assert.match(script, /windowThemeDark:\s*null/);
  assert.match(script, /function syncWindowTheme\(dark\)/);
  assert.match(script, /if \(state\.windowThemeDark === dark\) return;/);
  assert.match(script, /invoke\("window_theme_set",\s*\{ dark \}\)/);
});
