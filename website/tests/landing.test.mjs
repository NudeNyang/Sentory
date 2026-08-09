import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const testDirectory = dirname(fileURLToPath(import.meta.url));
const siteDirectory = resolve(testDirectory, "..");
const html = readFileSync(resolve(siteDirectory, "index.html"), "utf8");
const css = readFileSync(resolve(siteDirectory, "styles.css"), "utf8");
const script = readFileSync(resolve(siteDirectory, "script.js"), "utf8");
const deployWorkflow = readFileSync(
  resolve(siteDirectory, "..", ".github", "workflows", "deploy-pages.yml"),
  "utf8"
);

test("SEO 메타데이터와 소프트웨어 구조화 데이터를 제공한다", () => {
  assert.match(html, /<title>Sentory - 메신저에서 보낸 링크와 사진을 모아주는 Windows 앱<\/title>/);
  assert.match(html, /<link rel="canonical" href="https:\/\/nudenyang\.github\.io\/Sentory\/" \/>/);
  assert.match(html, /"@type": "SoftwareApplication"/);
  assert.match(html, /<meta property="og:image"/);
  assert.match(html, /assets\/og-image\.png/);
  assert.equal(existsSync(resolve(siteDirectory, "assets", "og-image.png")), true);
  assert.ok(existsSync(resolve(siteDirectory, "robots.txt")));
  assert.ok(existsSync(resolve(siteDirectory, "sitemap.xml")));
});

test("문서에는 하나의 H1과 주요 랜드마크가 있다", () => {
  assert.equal((html.match(/<h1(?:\s|>)/g) || []).length, 1);
  assert.match(html, /<header class="site-header">/);
  assert.match(html, /<main id="main-content">/);
  assert.match(html, /<footer class="site-footer">/);
  assert.match(html, /class="skip-link"/);
});

test("모든 이미지가 대체 텍스트를 가진다", () => {
  const imageTags = html.match(/<img\b[^>]*>/g) || [];
  assert.ok(imageTags.length >= 10);
  imageTags.forEach((tag) => assert.match(tag, /\balt="[^"]*"/));
});

test("참조하는 로컬 자산이 모두 존재한다", () => {
  const references = [...html.matchAll(/(?:src|href)="(\.\/(?:assets\/|styles\.css|script\.js|manifest\.webmanifest)[^"]*)"/g)];
  assert.ok(references.length > 0);
  references.forEach(([, reference]) => {
    assert.ok(existsSync(resolve(siteDirectory, reference.slice(2))), `${reference} 파일이 필요합니다.`);
  });
});

test("금지된 대시와 빈 링크를 사용하지 않는다", () => {
  assert.doesNotMatch(html, /[—–]/);
  assert.doesNotMatch(html, /href="#"/);
});

test("다크 모드, 모션 축소와 모바일 레이아웃을 지원한다", () => {
  assert.match(css, /@media \(prefers-color-scheme: dark\)/);
  assert.match(css, /@media \(prefers-reduced-motion: reduce\)/);
  assert.match(css, /@media \(max-width: 767px\)/);
  assert.doesNotMatch(css, /height:\s*100vh/);
});

test("스크롤 이벤트 대신 IntersectionObserver를 사용한다", () => {
  assert.match(script, /IntersectionObserver/);
  assert.doesNotMatch(script, /addEventListener\(["']scroll["']/);
});

test("수동 GitHub Pages 배포 전에 랜딩페이지 테스트를 실행한다", () => {
  assert.match(deployWorkflow, /workflow_dispatch:/);
  assert.match(deployWorkflow, /working-directory: website\s+run: npm test/);
  assert.match(deployWorkflow, /actions\/upload-pages-artifact@v4/);
  assert.match(deployWorkflow, /actions\/deploy-pages@v4/);
});

test("다운로드 CTA는 직접 파일 대신 최신 GitHub 릴리즈로 안내한다", () => {
  assert.doesNotMatch(html, /releases\/latest\/download/);
  assert.ok((html.match(/https:\/\/github\.com\/NudeNyang\/Sentory\/releases\/latest/g) || []).length >= 5);
  assert.match(html, />GitHub에서 다운로드<\/a>/);
  assert.match(html, />GitHub에서 Star<\/a>/);
});

test("히어로는 중복 없이 하나의 다운로드 CTA만 제공한다", () => {
  const heroActions = html.match(/<div class="hero-actions"[\s\S]*?<\/div>/)?.[0] || "";
  assert.equal((heroActions.match(/<a\b/g) || []).length, 1);
  assert.match(heroActions, />GitHub에서 다운로드<\/a>/);
  assert.doesNotMatch(heroActions, />GitHub<\/a>/);
});
