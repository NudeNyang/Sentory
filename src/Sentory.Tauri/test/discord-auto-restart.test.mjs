import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const script = readFileSync(new URL("../web/app.js", import.meta.url), "utf8");
const rust = readFileSync(
  new URL("../src-tauri/src/main.rs", import.meta.url),
  "utf8",
);

test("automatic Discord restart uses only the dedicated accessibility event", () => {
  assert.match(
    script,
    /listen\("discord-auto-restart-required"[\s\S]*?scheduleDiscordAutomaticRestart/,
  );
  assert.match(
    script,
    /listen\("runtime-status", event => applyRuntimeStatus\(event\.payload\)\);/,
  );
  assert.match(
    rust,
    /"discord-auto-restart-required"\s*=>\s*"discord-auto-restart-required"/,
  );
});

test("a disconnected paste or drop offers a visible manual restart", () => {
  assert.match(
    script,
    /requiresDiscordRestart[\s\S]{0,160}showDiscordUnavailablePrompt/,
  );
  assert.match(
    script,
    /showDiscordUnavailablePrompt[\s\S]*?main_window_show[\s\S]*?discordSendWarningTitle/,
  );
});
