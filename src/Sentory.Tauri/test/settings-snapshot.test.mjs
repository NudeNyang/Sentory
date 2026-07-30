import assert from "node:assert/strict";
import test from "node:test";
import { mergeSettingsSnapshot } from "../web/settings-snapshot.js";

test("delayed settings snapshots cannot roll back the current theme", () => {
  const current = { themeMode: "Dark", language: "auto" };
  const delayed = { themeMode: "Light", language: "ko-KR" };

  assert.deepEqual(mergeSettingsSnapshot(current, delayed), {
    themeMode: "Dark",
    language: "ko-KR",
  });
});

test("an authoritative settings load may replace the current theme", () => {
  const current = { themeMode: "Dark", language: "auto" };
  const stored = { themeMode: "Light", language: "ko-KR" };

  assert.equal(
    mergeSettingsSnapshot(current, stored, { replaceTheme: true }).themeMode,
    "Light",
  );
});
