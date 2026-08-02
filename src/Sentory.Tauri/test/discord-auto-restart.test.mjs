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
  assert.match(
    script,
    /Discord에 필요한 접근성 실행 옵션이 없습니다\.[\\n]+작성 중인 메시지가 취소되거나 통화가 종료될 수 있습니다\./,
  );
  assert.doesNotMatch(script, /메시지와 통화를 보호하려면/);
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
  assert.doesNotMatch(script, /DISCORD_AUTO_RESTART_CONSENT_KEY/);
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
  assert.match(
    script,
    /작성 중인 메시지가 취소되거나 통화가 종료될 수 있습니다\./,
  );
  assert.doesNotMatch(
    script,
    /작성 중인 메시지와 진행 중인 통화가 종료될 수 있습니다\./,
  );
});

test("Discord consent persists only after an enabled setting is saved", () => {
  const consentFunction = script.match(
    /async function ensureDiscordAutoRestartConsent\([\s\S]*?\n}\r?\n\r?\nasync function scheduleDiscordAutomaticRestart/,
  )?.[0] ?? "";

  assert.doesNotMatch(consentFunction, /settings_update/);
  assert.match(
    script,
    /function syncDiscordAutoRestartConsentWithSettings\(\)[\s\S]*?needsMessengerSetup\(state\.settings\)[\s\S]*?clearDiscordAutoRestartConsent\(\)[\s\S]*?settings\.discordAutoRestartConsentGranted/,
  );
  assert.match(
    script,
    /async function persistSettings\(patch\)[\s\S]*?applySettings\(settings\);[\s\S]{0,120}syncDiscordAutoRestartConsentWithSettings\(\)/,
  );
  assert.match(
    script,
    /async function persistLatestSourceSetting\(source\)[\s\S]*?applySettings\(settings\);[\s\S]{0,160}syncDiscordAutoRestartConsentWithSettings\(\)/,
  );
});

test("automatic Discord restart is gated by the same consent", () => {
  assert.match(
    script,
    /async function scheduleDiscordAutomaticRestart\(payload\)[\s\S]*?ensureDiscordAutoRestartConsent\(\)/,
  );
});

test("a persisted Discord consent is loaded before pending restarts", () => {
  assert.match(
    script,
    /async function loadSettings\(\)[\s\S]*?applySettings[\s\S]{0,160}syncDiscordAutoRestartConsentWithSettings\(\)[\s\S]{0,220}scheduleDiscordAutomaticRestart/,
  );
  assert.match(
    script,
    /function syncDiscordAutoRestartConsentWithSettings\(\)[\s\S]*?settings\.discordAutoRestartConsentGranted/,
  );
});
