import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const project = readFileSync(
  new URL("../../Sentory.Engine.Bridge/Sentory.Engine.Bridge.csproj", import.meta.url),
  "utf8",
);
const rustMain = readFileSync(new URL("../src-tauri/src/main.rs", import.meta.url), "utf8");

test("the release Tauri host uses the Windows GUI subsystem", () => {
  assert.match(
    rustMain,
    /^#!\[cfg_attr\(not\(debug_assertions\), windows_subsystem = "windows"\)\]/,
  );
});

test("the long-running engine sidecar uses the Windows GUI subsystem", () => {
  assert.match(project, /<OutputType>WinExe<\/OutputType>/);
  assert.doesNotMatch(project, /<OutputType>Exe<\/OutputType>/);
});
