import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const script = readFileSync(new URL("../web/app.js", import.meta.url), "utf8");

test("each card row is centered using its actual item count", () => {
  assert.match(script, /function cardLeft\(index\)/);
  assert.match(script, /const itemsInRow = Math\.min\(state\.columns, state\.total - row \* state\.columns\);/);
  assert.match(script, /const rowLeft = Math\.max\(MINIMUM_SIDE_PADDING, \(scroller\.clientWidth - itemsInRow \* CELL_WIDTH\) \/ 2\);/);
  assert.match(script, /card\.style\.left = `\$\{cardLeft\(index\)\}px`/);
});

test("drag selection uses the same centered card coordinates", () => {
  assert.match(script, /const itemLeft = cardLeft\(index\);/);
});
