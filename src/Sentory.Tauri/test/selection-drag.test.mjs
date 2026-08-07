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

test("selection drag does not select card text", () => {
  const beginDrag = script.slice(
    script.indexOf("function beginSelectionDrag"),
    script.indexOf("function moveSelectionDrag"),
  );
  const moveDrag = script.slice(
    script.indexOf("function moveSelectionDrag"),
    script.indexOf("function endSelectionDrag"),
  );
  const endDrag = script.slice(
    script.indexOf("function endSelectionDrag"),
    script.indexOf("function updateSelectionDrag"),
  );

  assert.match(beginDrag, /event\.preventDefault\(\)/);
  assert.match(beginDrag, /document\.body\.classList\.add\("selection-dragging"\)/);
  assert.match(moveDrag, /document\.getSelection\(\)\?\.removeAllRanges\(\)/);
  assert.match(
    endDrag,
    /window\.setTimeout\(\(\)\s*=>\s*\{[\s\S]*document\.getSelection\(\)\?\.removeAllRanges\(\)[\s\S]*document\.body\.classList\.remove\("selection-dragging"\)[\s\S]*\},\s*0\)/,
  );
  assert.match(
    script,
    /document\.addEventListener\("selectstart",\s*event\s*=>\s*\{[\s\S]*?classList\.contains\("selection-dragging"\)[\s\S]*?event\.preventDefault\(\)/,
  );
  assert.match(
    css,
    /body\.selection-dragging\s*\{[^}]*-webkit-user-select:\s*none[^}]*user-select:\s*none/s,
  );
});

test("selection drag release does not click the card under the pointer", () => {
  const createCard = script.slice(
    script.indexOf("function createCard"),
    script.indexOf("function patchCard"),
  );
  const endDrag = script.slice(
    script.indexOf("function endSelectionDrag"),
    script.indexOf("function updateSelectionDrag"),
  );

  assert.match(createCard, /if \(state\.suppressCardClick\) return/);
  assert.doesNotMatch(endDrag, /suppressCardClick\s*=\s*false/);
  assert.match(
    script,
    /document\.addEventListener\("pointerdown",\s*\(\)\s*=>\s*\{[\s\S]*?!state\.selectionDrag[\s\S]*?state\.suppressCardClick\s*=\s*false[\s\S]*?\},\s*true\)/,
  );
});

test("dragging selectable card text does not open the card", () => {
  const createCard = script.slice(
    script.indexOf("function createCard"),
    script.indexOf("function patchCard"),
  );

  assert.match(createCard, /card\.addEventListener\("pointerdown",\s*beginCardTextDrag\)/);
  assert.match(
    script,
    /function beginCardTextDrag\(event\)[\s\S]*?event\.target\.closest\("button, \.artwork"\)[\s\S]*?state\.cardTextDrag\s*=\s*\{/,
  );
  assert.match(
    script,
    /function moveCardTextDrag\(event\)[\s\S]*?Math\.abs\([\s\S]*?<\s*4[\s\S]*?state\.suppressCardClick\s*=\s*true/,
  );
  assert.match(script, /document\.addEventListener\("pointermove",\s*moveCardTextDrag\)/);
  assert.match(script, /document\.addEventListener\("pointerup",\s*endCardTextDrag\)/);
  assert.match(script, /document\.addEventListener\("pointercancel",\s*endCardTextDrag\)/);
});
