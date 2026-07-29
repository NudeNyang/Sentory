const CELL_WIDTH = 268;
const CARD_WIDTH = 252;
const CARD_HEIGHT = 320;
const ROW_HEIGHT = 336;
const TOP_PADDING = 20;
const BOTTOM_PADDING = 28;
const MINIMUM_SIDE_PADDING = 14;
const OVERSCAN_ROWS = 2;
const PAGE_SIZE = 80;
const SEARCH_DELAY_MS = 110;
const SOURCES = ["Discord", "KakaoTalk", "Slack", "Telegram", "Line", "WeChat", "WhatsApp"];

const state = {
  items: new Array(PAGE_SIZE),
  total: PAGE_SIZE,
  kind: "all",
  sources: new Set(),
  dateRange: "All",
  query: "",
  sort: "Newest",
  columns: 1,
  gridLeft: MINIMUM_SIDE_PADDING,
  renderedRange: "",
  renderQueued: false,
  renderRevision: 0,
  generation: 0,
  pendingPages: new Map(),
  hasLoaded: false,
  imageDiagnosticCount: 0,
  scrollTimer: 0,
  searchTimer: 0,
};

const scroller = document.querySelector("#scroller");
const galleryRegion = document.querySelector(".gallery-region");
const virtualSpace = document.querySelector("#virtual-space");
const status = document.querySelector("#status");
const search = document.querySelector("#search");
const filterButton = document.querySelector("#filter");
const filterCount = document.querySelector("#filter-count");
const filterMenu = document.querySelector("#filter-menu");
const filterReset = document.querySelector("#filter-reset");
const sourceOptions = document.querySelector("#source-options");
const dateOptions = document.querySelector("#date-options");
const sortButton = document.querySelector("#sort");
const sortLabel = document.querySelector("#sort-label");
const sortMenu = document.querySelector("#sort-menu");
const refreshButton = document.querySelector("#refresh");
const themeButton = document.querySelector("#theme");
const scrollThumb = document.querySelector(".scroll-indicator-thumb");

function tauriCore() {
  const core = window.__TAURI__?.core;
  if (!core) throw new Error("Tauri 명령 브리지를 찾지 못했습니다.");
  return core;
}

function buildRequest(offset) {
  return {
    offset,
    limit: PAGE_SIZE,
    kind: state.kind === "Image" || state.kind === "Url" ? state.kind : null,
    searchText: state.query,
    dateRange: state.dateRange,
    sortMode: state.sort,
    favoritesOnly: state.kind === "favorite",
    sourceApps: [...state.sources],
  };
}

function resetGallery({ announce = false } = {}) {
  state.generation += 1;
  state.pendingPages.clear();
  state.items = new Array(state.hasLoaded ? Math.max(1, state.total) : PAGE_SIZE);
  state.total = state.items.length;
  state.renderRevision += 1;
  state.renderedRange = "";
  scroller.scrollTop = 0;
  measureGrid();
  renderVisibleCards();
  if (announce) setStatus("보관함을 갱신하는 중…");
  void loadPage(0, state.generation, true);
}

async function loadPage(offset, generation, isInitial = false) {
  const pageOffset = Math.max(0, Math.floor(offset / PAGE_SIZE) * PAGE_SIZE);
  if (state.pendingPages.has(pageOffset)) return state.pendingPages.get(pageOffset);
  const task = (async () => {
    try {
      const snapshot = await tauriCore().invoke("gallery_page", {
        request: buildRequest(pageOffset),
      });
      if (generation !== state.generation) return;
      if (snapshot.protocolVersion !== 2 || !Array.isArray(snapshot.items)) {
        throw new Error("지원하지 않는 C# 엔진 응답입니다.");
      }

      if (isInitial) {
        state.total = snapshot.total;
        state.items = new Array(snapshot.total);
        state.hasLoaded = true;
        document.title = `Sentory · ${snapshot.total.toLocaleString()}개`;
      }
      for (let index = 0; index < snapshot.items.length; index += 1) {
        const target = pageOffset + index;
        if (target < state.items.length) state.items[target] = snapshot.items[index];
      }
      state.renderRevision += 1;
      state.renderedRange = "";
      measureGrid();
      renderVisibleCards();

      if (isInitial) {
        setStatus(`${snapshot.total.toLocaleString()}개 항목 · C# 엔진 연결됨`);
        window.setTimeout(() => status.classList.add("hidden"), 1000);
      }
      void tauriCore().invoke("ui_diagnostic", {
        event: "gallery-page-loaded",
        detail: `offset=${pageOffset} count=${snapshot.items.length} total=${snapshot.total}`,
      });
    } catch (error) {
      if (generation !== state.generation) return;
      if (isInitial) {
        state.total = 0;
        state.items = [];
        state.hasLoaded = true;
        state.renderRevision += 1;
        document.title = "Sentory · 연결 오류";
        measureGrid();
        renderVisibleCards();
      }
      const message = error instanceof Error ? error.message : String(error);
      setStatus(message, true);
      void tauriCore().invoke("ui_diagnostic", { event: "gallery-page-failed", detail: message });
    } finally {
      if (generation === state.generation) state.pendingPages.delete(pageOffset);
      refreshButton.disabled = false;
    }
  })();
  state.pendingPages.set(pageOffset, task);
  return task;
}

function ensurePagesForRange(start, end) {
  if (!state.hasLoaded) return;
  const firstPage = Math.floor(start / PAGE_SIZE) * PAGE_SIZE;
  const lastPage = Math.floor(Math.max(start, end - 1) / PAGE_SIZE) * PAGE_SIZE;
  for (let offset = firstPage; offset <= lastPage; offset += PAGE_SIZE) {
    const pageEnd = Math.min(state.items.length, offset + PAGE_SIZE);
    let missing = false;
    for (let index = offset; index < pageEnd; index += 1) {
      if (state.items[index] === undefined) {
        missing = true;
        break;
      }
    }
    if (missing) void loadPage(offset, state.generation);
  }
}

function measureGrid() {
  const available = Math.max(CELL_WIDTH, scroller.clientWidth - MINIMUM_SIDE_PADDING * 2);
  state.columns = Math.max(1, Math.floor(available / CELL_WIDTH));
  state.gridLeft = Math.max(MINIMUM_SIDE_PADDING, (scroller.clientWidth - state.columns * CELL_WIDTH) / 2);
  const rows = Math.ceil(state.total / state.columns);
  virtualSpace.style.height = `${Math.max(scroller.clientHeight, TOP_PADDING + rows * ROW_HEIGHT + BOTTOM_PADDING)}px`;
  updateScrollIndicator();
}

function renderVisibleCards() {
  state.renderQueued = false;
  if (state.total === 0) {
    virtualSpace.replaceChildren(createEmptyState());
    state.renderedRange = "empty";
    return;
  }

  const firstRow = Math.max(0, Math.floor((scroller.scrollTop - TOP_PADDING) / ROW_HEIGHT) - OVERSCAN_ROWS);
  const visibleRows = Math.ceil(scroller.clientHeight / ROW_HEIGHT) + OVERSCAN_ROWS * 2;
  const totalRows = Math.ceil(state.total / state.columns);
  const lastRow = Math.min(totalRows, firstRow + visibleRows);
  const rangeKey = `${firstRow}:${lastRow}:${state.columns}:${state.total}:${state.renderRevision}`;
  if (rangeKey === state.renderedRange) return;
  state.renderedRange = rangeKey;

  const fragment = document.createDocumentFragment();
  const start = firstRow * state.columns;
  const end = Math.min(state.total, lastRow * state.columns);
  ensurePagesForRange(start, end);
  for (let index = start; index < end; index += 1) {
    fragment.append(state.items[index]
      ? createCard(state.items[index], index)
      : createSkeletonCard(index));
  }
  virtualSpace.replaceChildren(fragment);
}

function positionCard(card, index) {
  const row = Math.floor(index / state.columns);
  const column = index % state.columns;
  card.style.left = `${state.gridLeft + column * CELL_WIDTH + (CELL_WIDTH - CARD_WIDTH) / 2}px`;
  card.style.top = `${TOP_PADDING + row * ROW_HEIGHT + (ROW_HEIGHT - CARD_HEIGHT) / 2}px`;
}

function createSkeletonCard(index) {
  const card = document.createElement("article");
  card.className = "card skeleton";
  card.setAttribute("aria-hidden", "true");
  positionCard(card, index);
  const artwork = document.createElement("div");
  artwork.className = "skeleton-artwork";
  const short = document.createElement("div");
  short.className = "skeleton-line short";
  const medium = document.createElement("div");
  medium.className = "skeleton-line medium";
  card.append(artwork, short, medium);
  return card;
}

function createCard(item, index) {
  const card = document.createElement("article");
  card.className = "card";
  card.dataset.itemId = item.itemId;
  positionCard(card, index);
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
  if (item.siteIconPath) {
    const icon = document.createElement("img");
    icon.className = "card-site-icon";
    icon.alt = "";
    icon.src = tauriCore().convertFileSrc(item.siteIconPath);
    type.append(icon);
  }
  type.append(document.createTextNode(item.typeLabel));
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
  empty.textContent = state.query || state.kind !== "all" || state.sources.size > 0 || state.dateRange !== "All"
    ? "조건에 맞는 항목이 없어."
    : "아직 표시할 항목이 없어.";
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

function updateFilterUi() {
  const count = state.sources.size + (state.dateRange === "All" ? 0 : 1);
  filterCount.hidden = count === 0;
  filterCount.textContent = String(count);
  for (const button of sourceOptions.querySelectorAll("button")) {
    button.classList.toggle("selected", state.sources.has(button.dataset.source));
  }
  for (const button of dateOptions.querySelectorAll("button")) {
    button.classList.toggle("selected", state.dateRange === button.dataset.date);
  }
}

function togglePopup(target, trigger) {
  const willOpen = target.hidden;
  filterMenu.hidden = true;
  sortMenu.hidden = true;
  filterButton.setAttribute("aria-expanded", "false");
  sortButton.setAttribute("aria-expanded", "false");
  if (willOpen) {
    target.hidden = false;
    trigger.setAttribute("aria-expanded", "true");
  }
}

for (const source of SOURCES) {
  const button = document.createElement("button");
  button.type = "button";
  button.dataset.source = source;
  button.textContent = source === "KakaoTalk" ? "카카오톡" : source === "Line" ? "LINE" : source;
  button.addEventListener("click", () => {
    if (state.sources.has(source)) state.sources.delete(source);
    else state.sources.add(source);
    updateFilterUi();
    resetGallery();
  });
  sourceOptions.append(button);
}

for (const tab of document.querySelectorAll(".tab")) {
  tab.addEventListener("click", () => {
    document.querySelector(".tab.active")?.classList.remove("active");
    tab.classList.add("active");
    state.kind = tab.dataset.kind;
    resetGallery();
  });
}

search.addEventListener("input", () => {
  window.clearTimeout(state.searchTimer);
  state.searchTimer = window.setTimeout(() => {
    state.query = search.value;
    resetGallery();
  }, SEARCH_DELAY_MS);
});

filterButton.addEventListener("click", () => togglePopup(filterMenu, filterButton));
sortButton.addEventListener("click", () => togglePopup(sortMenu, sortButton));

filterReset.addEventListener("click", () => {
  state.sources.clear();
  state.dateRange = "All";
  updateFilterUi();
  resetGallery();
});

dateOptions.addEventListener("click", event => {
  const button = event.target.closest("button[data-date]");
  if (!button) return;
  state.dateRange = button.dataset.date;
  updateFilterUi();
  resetGallery();
});

sortMenu.addEventListener("click", event => {
  const button = event.target.closest("button[data-sort]");
  if (!button) return;
  state.sort = button.dataset.sort;
  sortLabel.textContent = button.textContent;
  for (const option of sortMenu.querySelectorAll("button")) option.classList.toggle("selected", option === button);
  sortMenu.hidden = true;
  sortButton.setAttribute("aria-expanded", "false");
  resetGallery();
});

document.addEventListener("pointerdown", event => {
  if (!filterMenu.hidden && !filterMenu.contains(event.target) && !filterButton.contains(event.target)) {
    filterMenu.hidden = true;
    filterButton.setAttribute("aria-expanded", "false");
  }
  if (!sortMenu.hidden && !sortMenu.contains(event.target) && !sortButton.contains(event.target)) {
    sortMenu.hidden = true;
    sortButton.setAttribute("aria-expanded", "false");
  }
});

themeButton.addEventListener("click", () => {
  const dark = document.documentElement.dataset.theme !== "dark";
  document.documentElement.dataset.theme = dark ? "dark" : "light";
  themeButton.querySelector("span").innerHTML = dark ? "&#xE706;" : "&#xE708;";
  themeButton.title = dark ? "라이트 테마로 전환" : "다크 테마로 전환";
  themeButton.setAttribute("aria-label", themeButton.title);
  localStorage.setItem("sentory-theme", dark ? "dark" : "light");
});

refreshButton.addEventListener("click", () => resetGallery({ announce: true }));
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

const savedTheme = localStorage.getItem("sentory-theme");
if (savedTheme === "dark") themeButton.click();
updateFilterUi();
resetGallery();
