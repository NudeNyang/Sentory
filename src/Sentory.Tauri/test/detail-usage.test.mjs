import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const html = readFileSync(new URL("../web/index.html", import.meta.url), "utf8");
const script = readFileSync(new URL("../web/app.js", import.meta.url), "utf8");

test("detail statistics keep the existing tile and show combined usage", () => {
  assert.match(html, /<dt>재사용 횟수<\/dt>/);
  assert.match(html, /id="detail-usage-count"/);
  assert.match(html, /id="detail-usage-breakdown"/);
  assert.match(script, /usageCountLabel: "재사용 횟수"/);
  assert.match(script, /usageCountLabel: "Times reused"/);
  assert.match(script, /usageCountLabel: "再利用回数"/);
  assert.match(script, /usageCountLabel: "重复使用次数"/);
  assert.match(script, /외부 재사용/);
  assert.match(script, /Reused externally/);
  assert.match(script, /外部で再利用/);
  assert.match(script, /在外部重复使用/);
  assert.match(script, /card\.copyCount \+ card\.externalReuseCount/);
  assert.match(
    script,
    /t\("usageBreakdown", card\.copyCount, card\.externalReuseCount\)/,
  );
});

test("automatic favorite settings explanation stays unchanged", () => {
  assert.match(
    script,
    /autoFavoriteDescription: "같은 링크나 사진을 반복해서 사용하면 즐겨찾기에 추가합니다"/,
  );
});
