import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const script = readFileSync(new URL("../web/app.js", import.meta.url), "utf8");

test("gallery events are connected before the first page is loaded", () => {
  const startup = script.slice(script.lastIndexOf("updateFilterUi();"));
  const connect = startup.indexOf("await connectEngineEvents();");
  const firstLoad = startup.indexOf("resetGallery();");

  assert.ok(connect >= 0, "startup must await the gallery event listener");
  assert.ok(firstLoad > connect, "the first page must load after the listener is ready");
});
