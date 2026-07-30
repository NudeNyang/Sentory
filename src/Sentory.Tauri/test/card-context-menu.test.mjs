import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const html = readFileSync(new URL("../web/index.html", import.meta.url), "utf8");
const css = readFileSync(new URL("../web/styles.css", import.meta.url), "utf8");
const script = readFileSync(new URL("../web/app.js", import.meta.url), "utf8");

test("card context menu exposes the WPF actions in order", () => {
  const favorite = html.indexOf('id="context-favorite"');
  const copy = html.indexOf('id="context-copy"');
  const reveal = html.indexOf('id="context-reveal"');
  const remove = html.indexOf('id="context-delete"');

  assert.ok(favorite >= 0);
  assert.ok(favorite < copy && copy < reveal && reveal < remove);
  assert.match(html, /원본 폴더 열기/);
});

test("card context menu matches the compact WPF surface", () => {
  assert.match(css, /\.card-context-menu\s*\{[^}]*padding:\s*6px[^}]*border-radius:\s*10px[^}]*font-size:\s*12px/s);
  assert.match(css, /\.card-context-menu\s+button\s*\{[^}]*height:\s*34px[^}]*padding:\s*0 11px[^}]*border-radius:\s*7px/s);
});

test("right click actions use the existing mutations and Explorer reveal command", () => {
  assert.match(script, /addEventListener\("contextmenu"/);
  assert.match(script, /invoke\("gallery_reveal"/);
  assert.match(script, /contextFavorite\.addEventListener[\s\S]*toggleFavorite/);
  assert.match(script, /contextCopy\.addEventListener[\s\S]*copyItem/);
  assert.match(script, /contextDelete\.addEventListener[\s\S]*deleteItems/);
});
