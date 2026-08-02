import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

import {
  localizedCardSubtitle,
  localizedCardTitle,
  localizedMemberTitle,
} from "../web/gallery-localization.js";

const script = readFileSync(new URL("../web/app.js", import.meta.url), "utf8");
const translations = {
  clipboardImage: "Clipboard image",
  savedLink: "Saved link",
  imageFormat: format => `${format} image`,
  collectionTitle: (photos, links) => `${photos} photos · ${links} links`,
  collectionItems: count => `${count} items`,
  image: "Photo",
};
const translate = (key, ...args) => {
  const value = translations[key];
  return typeof value === "function" ? value(...args) : value;
};

function translationBlock(locale, nextLocale) {
  const start = script.indexOf(`TRANSLATIONS["${locale}"] = {`);
  const end = nextLocale
    ? script.indexOf(`TRANSLATIONS["${nextLocale}"] = {`, start)
    : script.indexOf("const state =", start);

  assert.notEqual(start, -1, `${locale} translation block is missing`);
  assert.notEqual(end, -1, `${locale} translation block end is missing`);
  return script.slice(start, end);
}

test("generated image labels follow the selected language", () => {
  const item = {
    kind: "Image",
    title: "클립보드 이미지",
    subtitle: "PNG 이미지",
    generatedTitleKind: "ClipboardImage",
    generatedSubtitleKind: "ImageFormat",
    imageFormat: "PNG",
  };

  assert.equal(localizedCardTitle(item, translate), "Clipboard image");
  assert.equal(localizedCardSubtitle(item, translate), "PNG image");
});

test("real OCR and file metadata text is never translated as a generated label", () => {
  const item = {
    kind: "Image",
    title: "클립보드 이미지",
    subtitle: "원본 작품 설명",
    generatedTitleKind: null,
    generatedSubtitleKind: null,
  };

  assert.equal(localizedCardTitle(item, translate), "클립보드 이미지");
  assert.equal(localizedCardSubtitle(item, translate), "원본 작품 설명");
});

test("generated collection and detail member labels follow the selected language", () => {
  const collection = {
    kind: "Collection",
    title: "사진 2개 · 링크 1개",
    subtitle: "항목 3개",
    generatedTitleKind: "Collection",
    generatedSubtitleKind: "CollectionCount",
    imageCount: 2,
    urlCount: 1,
    memberCount: 3,
  };
  const member = {
    kind: "Image",
    title: "이미지",
    generatedTitleKind: "Image",
  };

  assert.equal(localizedCardTitle(collection, translate), "2 photos · 1 links");
  assert.equal(localizedCardSubtitle(collection, translate), "3 items");
  assert.equal(localizedMemberTitle(member, translate), "Photo");
});

test("card and detail rendering use localized generated metadata", () => {
  assert.match(script, /localizedCardTitle\(item, t\)/);
  assert.match(script, /localizedCardSubtitle\(item, t\)/);
  assert.match(script, /localizedMemberTitle\(member, t\)/);
  assert.match(script, /refreshLocalizedVisibleCards[\s\S]*?localizedCardTitle/);
});

test("Japanese and Chinese card copy counts do not fall back to English", () => {
  const japanese = translationBlock("ja-JP", "zh-CN");
  const chinese = translationBlock("zh-CN");

  assert.match(japanese, /copyCount:\s*n\s*=>\s*`\$\{n\.toLocaleString\("ja-JP"\)\}回コピー`/);
  assert.match(chinese, /copyCount:\s*n\s*=>\s*`已复制 \$\{n\.toLocaleString\("zh-CN"\)\} 次`/);
});
