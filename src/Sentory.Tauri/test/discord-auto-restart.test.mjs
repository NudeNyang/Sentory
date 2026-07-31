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

test("enabling Discord requires one-time automatic restart consent", () => {
  assert.match(
    script,
    /const DISCORD_AUTO_RESTART_CONSENT_KEY = "sentory\.discord-auto-restart-consent\.v1";/,
  );
  assert.match(
    script,
    /async function ensureDiscordAutoRestartConsent\([\s\S]*?discordAutoRestartConsentTitle[\s\S]*?discordAutoRestartConsentAction/,
  );
  assert.match(
    script,
    /source === "Discord" && enabled[\s\S]{0,180}ensureDiscordAutoRestartConsent/,
  );
  assert.match(
    script,
    /if \(!confirmed\) \{[\s\S]{0,120}input\.checked = false;[\s\S]{0,120}return;/,
  );
});

test("automatic Discord restart is gated by the same consent", () => {
  assert.match(
    script,
    /async function scheduleDiscordAutomaticRestart\(payload\)[\s\S]*?ensureDiscordAutoRestartConsent\(\)/,
  );
});

test("an existing enabled Discord setting is grandfathered before pending restarts", () => {
  assert.match(
    script,
    /async function loadSettings\(\)[\s\S]*?applySettings[\s\S]{0,160}adoptDiscordAutoRestartConsentForExistingSetting\(\)[\s\S]{0,220}scheduleDiscordAutomaticRestart/,
  );
  assert.match(
    script,
    /function adoptDiscordAutoRestartConsentForExistingSetting\(\)[\s\S]*?settings\?\.sources\?\.Discord[\s\S]*?saveDiscordAutoRestartConsent\(\)/,
  );
});
