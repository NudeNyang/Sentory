import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const html = readFileSync(new URL("../web/index.html", import.meta.url), "utf8");
const css = readFileSync(new URL("../web/styles.css", import.meta.url), "utf8");

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
    [".app-info-card .settings-action", "height: 38px", "font-size: 12px"],
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
