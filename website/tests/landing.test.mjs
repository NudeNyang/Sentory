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
    const localPath = reference.slice(2).split(/[?#]/, 1)[0];
    assert.ok(existsSync(resolve(siteDirectory, localPath)), `${reference} 파일이 필요합니다.`);
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

test("저장된 선택이 없으면 밝은 테마로 시작한다", () => {
  const themeInitializer = html.match(/<script>\s*\(\(\) => \{[\s\S]*?sentory-site-theme[\s\S]*?<\/script>/)?.[0] || "";
  assert.match(themeInitializer, /savedTheme === "dark" \? "dark" : "light"/);
  assert.ok(html.indexOf(themeInitializer) < html.indexOf("styles.css"));
  assert.match(script, /return root\.dataset\.theme \|\| "light"/);
  assert.doesNotMatch(script, /colorScheme\.matches/);
});

test("기능 섹션은 지원 메신저에서 정리하는 항목을 담백하게 설명한다", () => {
  assert.match(html, /<h2 id="features-title">필요한 기록만 모아둡니다\.<\/h2>/);
  assert.match(html, /Discord, Slack 등 지원 메신저에서 전송한 항목만 자동으로 정리합니다\./);
  assert.doesNotMatch(html, /보관함을 만드는 기준부터 다릅니다/);
});

test("지원 메신저 로고는 각 공식 다운로드 페이지로 연결한다", () => {
  const messengerStrip = html.match(/<section class="messenger-strip"[\s\S]*?<\/section>/)?.[0] || "";
  const links = [
    "https://discord.com/download",
    "https://slack.com/downloads/windows",
    "https://www.whatsapp.com/download/",
    "https://telegram.org/desktop/download",
    "https://www.kakaocorp.com/page/service/service/KakaoTalk?lang=ko",
    "https://www.line.me/ko/",
    "https://windows.weixin.qq.com/"
  ];

  assert.equal((messengerStrip.match(/<a\b/g) || []).length, links.length);
  links.forEach((link) => assert.match(messengerStrip, new RegExp(`href="${link.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}"`)));
  assert.equal((messengerStrip.match(/target="_blank"/g) || []).length, links.length);
  assert.equal((messengerStrip.match(/rel="noreferrer"/g) || []).length, links.length);
  assert.equal((messengerStrip.match(/공식 다운로드 페이지 열기 \(새 탭\)/g) || []).length, links.length);
  assert.match(css, /\.messenger-logos a:focus-visible/);
});

test("동기화 기능 문구를 명확히 하고 중복 개인정보 섹션은 제거한다", () => {
  assert.match(html, /클라우드 · NAS를 통해 다른 컴퓨터와 동기화도 가능/);
  assert.doesNotMatch(html, /내가 고른 저장소로 동기화/);
  assert.doesNotMatch(html, /id="privacy"/);
  assert.doesNotMatch(html, /href="#privacy"/);
  assert.doesNotMatch(css, /\.privacy(?:\b|-)/);
  assert.match(html, /<summary>데이터는 어디에 저장되나요\?<\/summary>/);
});

test("스크롤 이벤트 대신 IntersectionObserver를 사용한다", () => {
  assert.match(script, /IntersectionObserver/);
  assert.doesNotMatch(script, /addEventListener\(["']scroll["']/);
});

test("사용 흐름 영상은 문구가 보인 뒤 1초 후 재생한다", () => {
  assert.match(html, /보내고 잊어버려도,<br \/>필요할 때 다시 찾을 수 있게\./);

  const video = html.match(/<video[\s\S]*?<\/video>/)?.[0] || "";
  assert.match(video, /data-delayed-autoplay/);
  assert.match(video, /width="1920"/);
  assert.match(video, /height="1080"/);
  assert.match(video, /poster="\.\/assets\/sentory-demo-poster\.jpg"/);
  assert.match(video, /muted/);
  assert.match(video, /playsinline/);
  assert.doesNotMatch(video, /(?:^|\s)autoplay(?:\s|=|>)/);
  assert.doesNotMatch(video, /(?:^|\s)controls(?:\s|=|>)/);
  assert.match(video, /src="\.\/assets\/sentory-demo\.mp4"/);

  assert.equal(existsSync(resolve(siteDirectory, "assets", "sentory-demo.mp4")), true);
  assert.equal(existsSync(resolve(siteDirectory, "assets", "sentory-demo-poster.jpg")), true);
  assert.match(script, /storyHeading/);
  assert.match(script, /storyDemo\.play\(\)/);
  assert.match(script, /}, 1000\);/);
  assert.match(script, /storyDemo\.addEventListener\("click"/);
  assert.match(css, /\.search-figure\s*\{[\s\S]*?max-width:\s*100%/);
  assert.match(css, /\.search-figure video\s*\{[\s\S]*?max-width:\s*100%/);
  assert.match(css, /object-fit:\s*contain/);
  assert.match(css, /video::\-webkit-media-controls/);
});

test("OCR 기능 카드에 실제 검색과 상세 화면을 함께 표시한다", () => {
  const feature = html.match(/<article class="feature feature-search reveal">[\s\S]*?<\/article>/)?.[0] || "";
  assert.equal((feature.match(/<img\b/g) || []).length, 2);
  assert.match(feature, /assets\/sentory-ocr-search\.png/);
  assert.match(feature, /assets\/sentory-ocr-detail\.png/);
  assert.doesNotMatch(feature, /assets\/sentory-gallery\.jpg/);
  assert.match(css, /\.feature-search-media/);
  assert.match(css, /\.feature-shot-search/);
  assert.match(css, /\.feature-shot-detail/);
  const shotRule = css.match(/\.feature-shot\s*\{[\s\S]*?\}/)?.[0] || "";
  const searchShotRule = css.match(/\.feature-shot-search\s*\{[\s\S]*?\}/)?.[0] || "";
  const detailShotRule = css.match(/\.feature-shot-detail\s*\{[\s\S]*?\}/)?.[0] || "";
  assert.match(shotRule, /border-radius:\s*var\(--radius-control\)/);
  assert.match(searchShotRule, /transform:\s*rotate\(-0\.55deg\)/);
  assert.match(detailShotRule, /transform:\s*rotate\(0\.65deg\)/);
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
