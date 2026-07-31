import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const settingsSource = readFileSync(
  new URL("../../Sentory.Infrastructure/Data/SentorySettingsStore.cs", import.meta.url),
  "utf8",
);
const markup = readFileSync(new URL("../web/index.html", import.meta.url), "utf8");
const script = readFileSync(new URL("../web/app.js", import.meta.url), "utf8");

test("a fresh install starts with every messenger disabled", () => {
  for (const property of [
    "DiscordSupportEnabled",
    "KakaoTalkSupportEnabled",
    "SlackSupportEnabled",
    "WhatsAppSupportEnabled",
    "TelegramSupportEnabled",
    "LineSupportEnabled",
    "WeChatSupportEnabled",
  ]) {
    assert.match(settingsSource, new RegExp(`public bool ${property} \\{ get; set; \\}(?! = true)`));
  }
});

test("a fresh install opens messenger selection before the library", () => {
  assert.match(markup, /id="messenger-setup-layer"/);
  assert.match(script, /needsMessengerSetup\(state\.settings\)/);
  assert.match(script, /messengerSetupLayer\.hidden = false/);
});
