import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const html = readFileSync(new URL("../web/index.html", import.meta.url), "utf8");
const css = readFileSync(new URL("../web/styles.css", import.meta.url), "utf8");
const script = readFileSync(new URL("../web/app.js", import.meta.url), "utf8");
const tauriConfig = readFileSync(new URL("../src-tauri/tauri.conf.json", import.meta.url), "utf8");

test("app info typography keeps the WPF semantic roles", () => {
  for (const className of [
    "app-product-name",
    "app-version",
    "app-author-link",
    "app-github-link",
    "app-update-title",
    "app-update-description",
    "app-copyright",
    "app-license-summary",
  ]) {
    assert.match(html, new RegExp(`class=["'][^"']*${className}`));
  }
});

test("app info typography matches the WPF font sizes and weights", () => {
  const expectedRules = [
    ["#app-info-heading", "font-size: 12px"],
    [".app-product-name", "font-size: 14px", "font-weight: 600"],
    [".app-version", "font-size: 11px"],
    [".app-links", "font-size: 10px"],
    [".app-author-link", "font-weight: 600"],
    [".app-update-title", "font-size: 11px", "font-weight: 600"],
    [".app-update-description", "font-size: 10.5px"],
    [".app-copyright", "font-size: 10.5px", "font-weight: 400"],
    [".app-license-summary", "font-size: 11px"],
  ];

  for (const [selector, ...declarations] of expectedRules) {
    const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    const match = css.match(new RegExp(`${escaped}\\s*\\{([^}]*)\\}`));
    assert.ok(match, `missing CSS rule for ${selector}`);
    for (const declaration of declarations) {
      assert.match(match[1], new RegExp(declaration.replace(".", "\\.")));
    }
  }
});

test("app info actions use the common settings button size", () => {
  const commonRule = css.match(/\.settings-action\s*\{([^}]*)\}/);
  assert.ok(commonRule, "missing common settings action rule");
  for (const declaration of [
    "min-height: 34px",
    "padding: 0 12px",
    "border-radius: 8px",
    "font-size: 11px",
  ]) {
    assert.match(commonRule[1], new RegExp(declaration));
  }
  assert.doesNotMatch(css, /\.app-info-card\s+\.settings-action\s*\{/);
});

test("app info profile links never add an underline on hover", () => {
  assert.doesNotMatch(
    css,
    /\.app-info-card\s+\.app-links\s+a:hover\s*\{[^}]*text-decoration:\s*underline/,
  );
  assert.match(
    css,
    /\.app-info-card\s+\.app-links\s+a(?:,\s*\.app-info-card\s+\.app-links\s+a:hover)?\s*\{[^}]*text-decoration:\s*none/,
  );
});

test("the public 2.0.2 identity has no preview or developer marker", () => {
  assert.match(html, /id="version-label"[^>]*>버전 2\.0\.2<\/small>/);
  assert.match(script, /t\("version", "2\.0\.2"\)/);
  assert.doesNotMatch(`${html}\n${script}\n${tauriConfig}`, /for Developers|Tauri Preview|com\.sentory\.preview/);
  assert.match(tauriConfig, /"productName": "Sentory"/);
  assert.match(tauriConfig, /"identifier": "com\.nudenyang\.sentory"/);
});

test("manual update checks are exposed only on the GitHub channel", () => {
  assert.match(html, /id="update-setting-row"[^>]*hidden/);
  assert.match(script, /invoke\("distribution_channel"\)/);
  assert.match(script, /updateSettingRow\.hidden = channel !== "github"/);
  assert.match(
    script,
    /invoke\("open_external_url", \{ url: result\.releasePage \}\)/,
  );
});
