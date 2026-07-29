const CARD_HEIGHT = 370;
const ROW_GAP = 18;
const COLUMN_GAP = 18;
const HORIZONTAL_PADDING = 28;
const MINIMUM_CARD_WIDTH = 250;
const OVERSCAN_ROWS = 2;

const state = {
  allItems: [],
  visibleItems: [],
  kind: "all",
  source: "all",
  query: "",
  newestFirst: true,
  columns: 1,
  cardWidth: MINIMUM_CARD_WIDTH,
  renderedRange: "",
  renderQueued: false,
  imageDiagnosticCount: 0,
};

const scroller = document.querySelector("#scroller");
const virtualSpace = document.querySelector("#virtual-space");
const status = document.querySelector("#status");
const search = document.querySelector("#search");
const sourceFilter = document.querySelector("#source-filter");
const sortButton = document.querySelector("#sort");
const refreshButton = document.querySelector("#refresh");

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
    rebuildSourceFilter();
    applyFilters(true);
    setStatus(`${snapshot.total.toLocaleString()}개 항목 · C# 엔진 연결됨`, false);
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

function rebuildSourceFilter() {
  const previous = state.source;
  const sources = [...new Set(state.allItems.map(item => item.sourceApp))]
    .sort((left, right) => left.localeCompare(right));
  sourceFilter.replaceChildren(new Option("전체", "all"));
  for (const source of sources) sourceFilter.add(new Option(sourceLabel(source), source));
  state.source = sources.includes(previous) ? previous : "all";
  sourceFilter.value = state.source;
}

function applyFilters(resetScroll) {
  const query = state.query.trim().toLocaleLowerCase();
  state.visibleItems = state.allItems
    .filter(item => {
      if (state.kind === "favorite" && !item.isFavorite) return false;
      if (state.kind !== "all" && state.kind !== "favorite" && item.kind !== state.kind) return false;
      if (state.source !== "all" && item.sourceApp !== state.source) return false;
      return !query || item.searchText.includes(query);
    })
    .sort((left, right) => {
      const order = left.lastCapturedAt.localeCompare(right.lastCapturedAt);
      return state.newestFirst ? -order : order;
    });
  if (resetScroll) scroller.scrollTop = 0;
  state.renderedRange = "";
  measureGrid();
  renderVisibleCards();
}

function measureGrid() {
  const available = Math.max(1, scroller.clientWidth - HORIZONTAL_PADDING * 2);
  state.columns = Math.max(
    1,
    Math.floor((available + COLUMN_GAP) / (MINIMUM_CARD_WIDTH + COLUMN_GAP)),
  );
  state.cardWidth = (available - COLUMN_GAP * (state.columns - 1)) / state.columns;
  const rows = Math.ceil(state.visibleItems.length / state.columns);
  virtualSpace.style.height = `${Math.max(scroller.clientHeight, HORIZONTAL_PADDING * 2 + rows * (CARD_HEIGHT + ROW_GAP) - ROW_GAP)}px`;
}

function renderVisibleCards() {
  state.renderQueued = false;
  if (state.visibleItems.length === 0) {
    virtualSpace.replaceChildren(createEmptyState());
    state.renderedRange = "empty";
    return;
  }

  const rowHeight = CARD_HEIGHT + ROW_GAP;
  const firstRow = Math.max(0, Math.floor(scroller.scrollTop / rowHeight) - OVERSCAN_ROWS);
  const visibleRows = Math.ceil(scroller.clientHeight / rowHeight) + OVERSCAN_ROWS * 2;
  const lastRow = Math.min(
    Math.ceil(state.visibleItems.length / state.columns),
    firstRow + visibleRows,
  );
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
  card.style.width = `${state.cardWidth}px`;
  card.style.transform = `translate(${HORIZONTAL_PADDING + column * (state.cardWidth + COLUMN_GAP)}px, ${HORIZONTAL_PADDING + row * (CARD_HEIGHT + ROW_GAP)}px)`;

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
  }

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
  const favorite = document.createElement("span");
  favorite.className = "favorite";
  favorite.textContent = item.isFavorite ? "★" : "☆";
  favorite.setAttribute("aria-label", item.isFavorite ? "즐겨찾기" : "즐겨찾기 아님");
  footer.append(chip, favorite);

  card.append(artwork, meta, title, subtitle, footer);
  return card;
}

function createEmptyState() {
  const empty = document.createElement("p");
  empty.className = "empty";
  empty.textContent = state.allItems.length === 0
    ? "아직 표시할 항목이 없어."
    : "조건에 맞는 항목이 없어.";
  return empty;
}

function requestRender() {
  if (state.renderQueued) return;
  state.renderQueued = true;
  window.requestAnimationFrame(renderVisibleCards);
}

function reportImageDiagnostic(event, item) {
  if (state.imageDiagnosticCount >= 8) return;
  state.imageDiagnosticCount += 1;
  void tauriCore().invoke("ui_diagnostic", {
    event,
    detail: `item=${item.itemId} mode=${item.artworkMode}`,
  });
}

function setStatus(message, isError = false) {
  status.textContent = message;
  status.classList.toggle("error", isError);
  status.classList.remove("hidden");
}

function sourceLabel(source) {
  return source === "KakaoTalk" ? "카카오톡" : source === "Line" ? "LINE" : source;
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

sourceFilter.addEventListener("change", () => {
  state.source = sourceFilter.value;
  applyFilters(true);
});

sortButton.addEventListener("click", () => {
  state.newestFirst = !state.newestFirst;
  sortButton.textContent = state.newestFirst ? "정렬 최신순" : "정렬 오래된순";
  applyFilters(true);
});

refreshButton.addEventListener("click", loadGallery);
scroller.addEventListener("scroll", requestRender, { passive: true });
new ResizeObserver(() => {
  state.renderedRange = "";
  measureGrid();
  requestRender();
}).observe(scroller);

loadGallery();
