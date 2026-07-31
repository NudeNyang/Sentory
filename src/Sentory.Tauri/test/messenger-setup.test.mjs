import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import {
  MESSENGER_SOURCES,
  createMessengerSourcePatch,
  hasEnabledMessengerSource,
  needsMessengerSetup,
} from "../web/messenger-setup.js";

const markup = readFileSync(
  new URL("../web/index.html", import.meta.url),
  "utf8",
);
const script = readFileSync(
  new URL("../web/app.js", import.meta.url),
  "utf8",
);

test("fresh settings require setup and start with every source disabled", () => {
  const settings = {
    messengerDetectionSetupCompleted: false,
    sources: Object.fromEntries(MESSENGER_SOURCES.map(source => [source, false])),
  };

  assert.equal(needsMessengerSetup(settings), true);
  assert.equal(hasEnabledMessengerSource(settings), false);
});

test("setup patch persists every source and completion as one update", () => {
  assert.deepEqual(
    createMessengerSourcePatch(new Set(["Discord", "Line"])),
    {
      discordSupportEnabled: true,
      kakaoTalkSupportEnabled: false,
      slackSupportEnabled: false,
      whatsAppSupportEnabled: false,
      telegramSupportEnabled: false,
      lineSupportEnabled: true,
      weChatSupportEnabled: false,
      messengerDetectionSetupCompleted: true,
    },
  );
});

test("onboarding and the disabled detection notice are wired into the UI", () => {
  assert.match(markup, /id="messenger-setup-layer"/);
  assert.match(markup, /id="detection-off-banner"/);
  assert.match(script, /createMessengerSourcePatch\(state\.messengerSetupSources\)/);
  assert.match(script, /needsMessengerSetup\(state\.settings\)/);
});

test("setup copy keeps only the later settings guidance", () => {
  assert.match(markup, /나중에 설정에서 변경할 수 있습니다\./);
  assert.doesNotMatch(markup, /사용하는 메신저만 켜면 됩니다/);
  assert.doesNotMatch(script, /이 컴퓨터에서 감지할 수 있습니다/);
  assert.doesNotMatch(script, /messengerAvailable/);
});

test("messenger toggles render immediately and save through the async latest-value queue", () => {
  assert.match(
    script,
    /state\.pendingSourceSettings\.set\(source, enabled\);[\s\S]*renderSourceSettings\(\);[\s\S]*scheduleSourceSettingSave\(source\);/,
  );
  assert.match(
    script,
    /while \(state\.pendingSourceSettings\.has\(source\)\)/,
  );
  assert.match(
    script,
    /input\.addEventListener\("change", \(\) => \{[\s\S]*updateSourceSetting\(source, enabled\);/,
  );
});
