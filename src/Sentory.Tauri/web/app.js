const CELL_WIDTH = 268;
const CARD_WIDTH = 252;
const CARD_HEIGHT = 320;
const ROW_HEIGHT = 336;
const TOP_PADDING = 20;
const BOTTOM_PADDING = 28;
const MINIMUM_SIDE_PADDING = 14;
const OVERSCAN_ROWS = 2;

const state = {
  allItems: [],
  visibleItems: [],
  kind: "all",
  sources: new Set(),
  dateRange: "All",
  query: "",
  sort: "Newest",
  columns: 1,
  gridLeft: MINIMUM_SIDE_PADDING,
  renderedRange: "",
  renderQueued: false,
  imageDiagnosticCount: 0,
  scrollTimer: 0,
};

const scroller = document.querySelector("#scroller");
const galleryRegion = document.querySelector(".gallery-region");
const virtualSpace = document.querySelector("#virtual-space");
const status = document.querySelector("#status");
const search = document.querySelector("#search");
const filterButton = document.querySelector("#filter");
const filterCount = document.querySelector("#filter-count");
const sortButton = document.querySelector("#sort");
const sortLabel = document.querySelector("#sort-label");
const refreshButton = document.querySelector("#refresh");
const themeButton = document.querySelector("#theme");
const scrollThumb = document.querySelector(".scroll-indicator-thumb");

function tauriCore() {
  const core = window.__TAURI__?.core;
  if (!core) throw new Error("Tauri 명령 브리지를 찾지 못했습니다.");
  return core;
}

async function loadGallery() {
  setStatus("C# 엔진에서 보관함을 읽는 중…");
  refreshButton.disabled = true;
  try {
    const snapshot = await tauriCore().invoke("gallery_list", { limit: 500 });
    if (snapshot.protocolVersion !== 1 || !Array.isArray(snapshot.items)) {
      throw new Error("지원하지 않는 C# 엔진 응답입니다.");
    }
    state.allItems = snapshot.items.map(item => ({
      ...item,
      searchText: `${item.title} ${item.subtitle} ${item.domain}`.toLocaleLowerCase(),
    }));
    document.title = `Sentory · ${snapshot.total.toLocaleString()}개`;
    void tauriCore().invoke("ui_diagnostic", {
      event: "gallery-loaded",
      detail: `total=${snapshot.total} artwork=${snapshot.items.filter(item => item.artworkPath).length}`,
    });
    applyFilters(true);
    setStatus(`${snapshot.total.toLocaleString()}개 항목 · C# 엔진 연결됨`);
    window.setTimeout(() => status.classList.add("hidden"), 1400);
  } catch (error) {
    document.title = "Sentory · 연결 오류";
    void tauriCore().invoke("ui_diagnostic", {
      event: "gallery-load-failed",
      detail: error instanceof Error ? error.message : String(error),
    });
    setStatus(error instanceof Error ? error.message : String(error), true);
    state.allItems = [];
    applyFilters(true);
  } finally {
    refreshButton.disabled = false;
  }
}

function applyFilters(resetScroll) {
  const query = state.query.trim().toLocaleLowerCase();
  const cutoff = dateCutoff(state.dateRange);
  state.visibleItems = state.allItems
    .filter(item => {
      if (state.kind === "favorite" && !item.isFavorite) return false;
      if (state.kind !== "all" && state.kind !== "favorite" && item.kind !== state.kind) return false;
      if (state.sources.size > 0 && !state.sources.has(item.sourceApp)) return false;
      if (cutoff && new Date(item.lastCapturedAt) < cutoff) return false;
      return !query || item.searchText.includes(query);
    })
    .sort(compareItems);
  if (resetScroll) scroller.scrollTop = 0;
  state.renderedRange = "";
  measureGrid();
  renderVisibleCards();
  updateFilterCount();
}

function compareItems(left, right) {
  switch (state.sort) {
    case "Oldest":
      return left.lastCapturedAt.localeCompare(right.lastCapturedAt);
    case "MostCopied":
      return right.copyCount - left.copyCount || right.lastCapturedAt.localeCompare(left.lastCapturedAt);
    case "Name":
      return left.title.localeCompare(right.title, undefined, { sensitivity: "base" });
    default:
      return right.lastCapturedAt.localeCompare(left.lastCapturedAt);
  }
}

function dateCutoff(range) {
  const now = new Date();
  if (range === "Today") return new Date(now.getFullYear(), now.getMonth(), now.getDate());
  if (range === "Last7Days") return new Date(now.getTime() - 7 * 86400000);
  if (range === "Last30Days") return new Date(now.getTime() - 30 * 86400000);
  return null;
}

function measureGrid() {
  const available = Math.max(CELL_WIDTH, scroller.clientWidth - MINIMUM_SIDE_PADDING * 2);
  state.columns = Math.max(1, Math.floor(available / CELL_WIDTH));
  state.gridLeft = Math.max(MINIMUM_SIDE_PADDING, (scroller.clientWidth - state.columns * CELL_WIDTH) / 2);
  const rows = Math.ceil(state.visibleItems.length / state.columns);
  virtualSpace.style.height = `${Math.max(scroller.clientHeight, TOP_PADDING + rows * ROW_HEIGHT + BOTTOM_PADDING)}px`;
  updateScrollIndicator();
}

function renderVisibleCards() {
  state.renderQueued = false;
  if (state.visibleItems.length === 0) {
    virtualSpace.replaceChildren(createEmptyState());
    state.renderedRange = "empty";
    return;
  }

  const firstRow = Math.max(0, Math.floor((scroller.scrollTop - TOP_PADDING) / ROW_HEIGHT) - OVERSCAN_ROWS);
  const visibleRows = Math.ceil(scroller.clientHeight / ROW_HEIGHT) + OVERSCAN_ROWS * 2;
  const totalRows = Math.ceil(state.visibleItems.length / state.columns);
  const lastRow = Math.min(totalRows, firstRow + visibleRows);
  const rangeKey = `${firstRow}:${lastRow}:${state.columns}:${state.visibleItems.length}`;
  if (rangeKey === state.renderedRange) return;
  state.renderedRange = rangeKey;

  const fragment = document.createDocumentFragment();
  const start = firstRow * state.columns;
  const end = Math.min(state.visibleItems.length, lastRow * state.columns);
  for (let index = start; index < end; index += 1) {
    fragment.append(createCard(state.visibleItems[index], index));
  }
  virtualSpace.replaceChildren(fragment);
}

function createCard(item, index) {
  const row = Math.floor(index / state.columns);
  const column = index % state.columns;
  const card = document.createElement("article");
  card.className = "card";
  card.dataset.itemId = item.itemId;
  card.style.left = `${state.gridLeft + column * CELL_WIDTH + (CELL_WIDTH - CARD_WIDTH) / 2}px`;
  card.style.top = `${TOP_PADDING + row * ROW_HEIGHT + (ROW_HEIGHT - CARD_HEIGHT) / 2}px`;
  card.setAttribute("aria-label", `${item.typeLabel}, ${item.title}, ${item.dateLabel}`);

  const artwork = document.createElement("div");
  artwork.className = "artwork";
  if (item.artworkPath) {
    const image = document.createElement("img");
    image.alt = "";
    image.className = item.artworkMode;
    image.loading = "lazy";
    image.decoding = "async";
    image.addEventListener("load", () => {
      image.classList.add("loaded");
      reportImageDiagnostic("image-loaded", item);
    }, { once: true });
    image.addEventListener("error", () => reportImageDiagnostic("image-failed", item), { once: true });
    image.src = tauriCore().convertFileSrc(item.artworkPath);
    artwork.append(image);
  } else {
    artwork.append(createUrlFallback(item));
  }

  const copyButton = document.createElement("button");
  copyButton.className = "copy-button fluent";
  copyButton.type = "button";
  copyButton.title = "클립보드에 복사";
  copyButton.setAttribute("aria-label", "복사");
  copyButton.innerHTML = "&#xE8C8;";
  copyButton.addEventListener("click", event => event.stopPropagation());

  const body = document.createElement("div");
  body.className = "card-body";
  const meta = document.createElement("div");
  meta.className = "card-meta";
  const type = document.createElement("strong");
  type.textContent = item.typeLabel;
  const date = document.createElement("span");
  date.textContent = item.dateLabel;
  meta.append(type, date);

  const title = document.createElement("h2");
  title.textContent = item.title;
  title.title = item.title;
  const subtitle = document.createElement("p");
  subtitle.className = "subtitle";
  subtitle.textContent = item.subtitle;

  const footer = document.createElement("div");
  footer.className = "card-footer";
  const chip = document.createElement("span");
  chip.className = "status-chip";
  chip.textContent = item.statusLabel;
  footer.append(chip);
  if (item.copyCount > 0) {
    const usage = document.createElement("span");
    usage.className = "copy-usage";
    usage.textContent = `복사 ${item.copyCount}회`;
    footer.append(usage);
  }
  const favorite = document.createElement("button");
  favorite.className = `favorite${item.isFavorite ? " active" : ""}`;
  favorite.type = "button";
  favorite.innerHTML = item.isFavorite ? "&#xE735;" : "&#xE734;";
  favorite.title = item.isFavorite ? "즐겨찾기에서 제거" : "즐겨찾기에 추가";
  favorite.setAttribute("aria-label", favorite.title);
  favorite.addEventListener("click", event => event.stopPropagation());
  footer.append(favorite);

  body.append(meta, title, subtitle, footer);
  card.append(artwork, copyButton, body);
  return card;
}

function createUrlFallback(item) {
  const fallback = document.createElement("div");
  fallback.className = "url-fallback";
  const initial = document.createElement("span");
  initial.className = "url-initial";
  initial.textContent = (item.domain || item.title || "L").trim().charAt(0).toLocaleUpperCase();
  const domain = document.createElement("span");
  domain.className = "url-domain";
  domain.textContent = item.domain;
  fallback.append(initial, domain);
  return fallback;
}

function createEmptyState() {
  const empty = document.createElement("p");
  empty.className = "empty";
  empty.textContent = state.allItems.length === 0 ? "아직 표시할 항목이 없어." : "조건에 맞는 항목이 없어.";
  return empty;
}

function requestRender() {
  if (state.renderQueued) return;
  state.renderQueued = true;
  window.requestAnimationFrame(renderVisibleCards);
}

function updateScrollIndicator() {
  const trackHeight = scroller.clientHeight;
  const scrollHeight = scroller.scrollHeight;
  const maxScroll = Math.max(0, scrollHeight - trackHeight);
  if (maxScroll <= 0) {
    scrollThumb.style.height = "0";
    return;
  }
  const thumbHeight = Math.max(32, trackHeight * trackHeight / scrollHeight);
  const top = (scroller.scrollTop / maxScroll) * (trackHeight - thumbHeight);
  scrollThumb.style.height = `${thumbHeight}px`;
  scrollThumb.style.transform = `translateY(${top}px)`;
}

function reportImageDiagnostic(event, item) {
  if (state.imageDiagnosticCount >= 8) return;
  state.imageDiagnosticCount += 1;
  void tauriCore().invoke("ui_diagnostic", { event, detail: `item=${item.itemId} mode=${item.artworkMode}` });
}

function setStatus(message, isError = false) {
  status.textContent = message;
  status.classList.toggle("error", isError);
  status.classList.remove("hidden");
}

function updateFilterCount() {
  const count = state.sources.size + (state.dateRange === "All" ? 0 : 1);
  filterCount.hidden = count === 0;
  filterCount.textContent = String(count);
}

for (const tab of document.querySelectorAll(".tab")) {
  tab.addEventListener("click", () => {
    document.querySelector(".tab.active")?.classList.remove("active");
    tab.classList.add("active");
    state.kind = tab.dataset.kind;
    applyFilters(true);
  });
}

search.addEventListener("input", () => {
  state.query = search.value;
  applyFilters(true);
});

sortButton.addEventListener("click", () => {
  state.sort = state.sort === "Newest" ? "Oldest" : "Newest";
  sortLabel.textContent = state.sort === "Newest" ? "정렬 최신순" : "정렬 오래된순";
  applyFilters(true);
});

filterButton.addEventListener("click", () => {
  setStatus("필터 메뉴는 SQL 페이지 조회 단계에서 연결할 예정이야.");
  window.setTimeout(() => status.classList.add("hidden"), 1400);
});

themeButton.addEventListener("click", () => {
  const dark = document.documentElement.dataset.theme !== "dark";
  document.documentElement.dataset.theme = dark ? "dark" : "light";
  themeButton.querySelector("span").innerHTML = dark ? "&#xE706;" : "&#xE708;";
  themeButton.title = dark ? "라이트 테마로 전환" : "다크 테마로 전환";
  themeButton.setAttribute("aria-label", themeButton.title);
});

refreshButton.addEventListener("click", loadGallery);
scroller.addEventListener("scroll", () => {
  requestRender();
  updateScrollIndicator();
  galleryRegion.classList.add("scrolling");
  window.clearTimeout(state.scrollTimer);
  state.scrollTimer = window.setTimeout(() => galleryRegion.classList.remove("scrolling"), 550);
}, { passive: true });

new ResizeObserver(() => {
  state.renderedRange = "";
  measureGrid();
  requestRender();
}).observe(scroller);

loadGallery();
