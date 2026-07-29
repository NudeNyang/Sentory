import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const css = readFileSync(new URL("../web/styles.css", import.meta.url), "utf8");
const script = readFileSync(new URL("../web/app.js", import.meta.url), "utf8");

test("selection rectangle uses a slightly stronger translucent fill", () => {
  assert.match(css, /\.selection-rectangle\s*\{[^}]*background:\s*color-mix\(in srgb, var\(--accent\) 10%, transparent\)/s);
});

test("scrolling while dragging immediately recalculates selected cards", () => {
  assert.match(
    script,
    /scroller\.addEventListener\("scroll",\s*\(\)\s*=>\s*\{[\s\S]*?state\.selectionDrag\?\.active[\s\S]*?updateSelectionDrag\(\)/,
  );
});
