import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const script = readFileSync(new URL("../web/app.js", import.meta.url), "utf8");
const css = readFileSync(new URL("../web/styles.css", import.meta.url), "utf8");

test("overlay scroll indicators reveal at the same 44 pixel proximity as WPF", () => {
  assert.match(script, /SCROLL_INDICATOR_REVEAL_DISTANCE\s*=\s*44/);
  assert.match(script, /function bindOverlayScrollIndicator/);
  assert.match(script, /distanceX \* distanceX \+ distanceY \* distanceY/);
  assert.match(script, /classList\.toggle\("scroll-near", isNear\)/);
  assert.match(css, /\.scroll-near\s*>\s*\.scroll-indicator[^}]*opacity:\s*1/);
});

test("overlay scroll indicators keep WPF track and hover thumb dimensions", () => {
  assert.match(css, /\.scroll-indicator\s*\{[^}]*width:\s*10px/);
  assert.match(css, /\.scroll-indicator-thumb\s*\{[^}]*width:\s*3px[^}]*opacity:\s*0\.46/);
  assert.match(css, /\.scroll-indicator:hover\s+\.scroll-indicator-thumb\s*\{[^}]*width:\s*6px[^}]*opacity:\s*0\.95/);
  assert.match(script, /setPointerCapture/);
  assert.match(script, /target\.scrollTop\s*=\s*thumbTop/);
});
