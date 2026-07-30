import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const script = readFileSync(new URL("../web/app.js", import.meta.url), "utf8");

test("Discord notices are reserved for accessibility reconnects", () => {
  assert.match(
    script,
    /function shouldShowDiscordReconnectNotice\(\)[\s\S]*?discordState === "ReconnectRequired"/,
  );
  assert.match(
    script,
    /showDiscordStatus: shouldShowDiscordReconnectNotice\(\)/,
  );
  assert.match(
    script,
    /headerStatusVisible = shouldShowDiscordReconnectNotice\(\)/,
  );
});
