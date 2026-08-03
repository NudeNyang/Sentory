import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const styles = readFileSync(new URL("../web/styles.css", import.meta.url), "utf8");
const script = readFileSync(new URL("../web/app.js", import.meta.url), "utf8");

test("each supported language uses one native UI family for mixed text", () => {
  assert.match(
    styles,
    /:root:lang\(ko\)[^{]*\{[^}]*"Segoe UI Variable Text",\s*"Segoe UI",\s*"Malgun Gothic"/s,
  );
  assert.match(styles, /:root:lang\(en\)[^{]*\{[^}]*"Segoe UI Variable Text"/s);
  assert.match(styles, /:root:lang\(ja\)[^{]*\{[^}]*"Yu Gothic UI"/s);
  assert.match(styles, /:root:lang\(zh-CN\)[^{]*\{[^}]*"Microsoft YaHei UI"/s);
  assert.match(styles, /font-family:\s*var\(--ui-font-family\)/);
  assert.match(styles, /font-synthesis:\s*none/);
});

test("changing the language updates the root lang before rendering labels", () => {
  assert.match(
    script,
    /state\.locale = resolveLocale\(language\);\s*document\.documentElement\.lang = state\.locale;/,
  );
});

test("the Sentory brand title keeps the original Korean UI stack in every language", () => {
  assert.match(
    styles,
    /\.identity h1\s*\{[^}]*font-family:\s*"Segoe UI Variable Text",\s*"Segoe UI",\s*"Malgun Gothic",\s*sans-serif;/s,
  );
});

test("Chinese uses a lighter shared emphasis weight without changing the brand", () => {
  assert.match(styles, /--ui-semibold-weight:\s*600/);
  assert.match(
    styles,
    /:root:lang\(zh-CN\)[^{]*\{[^}]*--ui-semibold-weight:\s*500/s,
  );
  assert.ok(
    (styles.match(/font-weight:\s*var\(--ui-semibold-weight\)/g) ?? []).length >= 30,
  );
  assert.match(
    styles,
    /\.identity h1\s*\{[^}]*font-weight:\s*600/s,
  );
});
