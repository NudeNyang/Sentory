(() => {
  "use strict";

  const viewport = document.getElementById("viewport");
  const canvas = document.getElementById("canvas");
  const cellWidth = 268;
  const cellHeight = 336;
  const cardWidth = 252;
  const topPadding = 20;
  const bottomPadding = 28;
  const overscanRows = 2;
  const maximumRange = 120;
  const maximumImageLoads = 4;

  let revision = 0;
  let total = 0;
  let columns = 1;
  let leftPadding = 14;
  let requestedStart = -1;
  let requestedCount = -1;
  let scheduled = false;
  const cards = new Map();
  const imageQueue = [];
  let activeImageLoads = 0;

  const post = (message) => window.chrome.webview.postMessage(message);

  function calculateLayout() {
    const available = Math.max(0, viewport.clientWidth - 28);
    columns = Math.max(1, Math.floor(available / cellWidth));
    leftPadding = Math.max(14, (viewport.clientWidth - columns * cellWidth) / 2);
    const rows = Math.ceil(total / columns);
    canvas.style.height = `${topPadding + rows * cellHeight + bottomPadding}px`;
  }

  function visibleRange() {
    const firstRow = Math.max(0, Math.floor((viewport.scrollTop - topPadding) / cellHeight) - overscanRows);
    const lastRow = Math.max(firstRow, Math.ceil((viewport.scrollTop + viewport.clientHeight - topPadding) / cellHeight) + overscanRows);
    const start = Math.min(total, firstRow * columns);
    const count = Math.min(maximumRange, Math.max(0, Math.min(total, (lastRow + 1) * columns) - start));
    return { start, count };
  }

  function scheduleRange() {
    if (scheduled) return;
    scheduled = true;
    requestAnimationFrame(() => {
      scheduled = false;
      calculateLayout();
      const range = visibleRange();
      pruneCards(range.start, range.count);
      if (range.start === requestedStart && range.count === requestedCount) return;
      requestedStart = range.start;
      requestedCount = range.count;
      post({ type: "requestRange", revision, start: range.start, count: range.count });
    });
  }

  function reset(message) {
    revision = Number(message.revision) || 0;
    total = Math.max(0, Number(message.total) || 0);
    document.documentElement.dataset.theme = message.theme === "dark" ? "dark" : "light";
    requestedStart = -1;
    requestedCount = -1;
    for (const card of cards.values()) removeCard(card);
    cards.clear();
    imageQueue.splice(0).forEach(job => job.controller.abort());
    calculateLayout();
    scheduleRange();
  }

  function setTheme(message) {
    document.documentElement.dataset.theme = message.theme === "dark" ? "dark" : "light";
  }

  function positionCard(element, index) {
    const row = Math.floor(index / columns);
    const column = index % columns;
    element.style.transform = `translate3d(${leftPadding + column * cellWidth + (cellWidth - cardWidth) / 2}px, ${topPadding + row * cellHeight}px, 0)`;
  }

  function textElement(className, value) {
    const element = document.createElement("div");
    element.className = className;
    element.textContent = value || "";
    return element;
  }

  function createCard(item) {
    const card = document.createElement("article");
    card.className = "card";
    card.dataset.index = String(item.index);
    card.setAttribute("aria-label", `${item.typeLabel} ${item.title} ${item.dateLabel}`);

    const artwork = document.createElement("div");
    artwork.className = `artwork ${item.artworkMode || "cover"}`;
    artwork.appendChild(textElement("initial", item.initial));
    if (item.artworkUrl) {
      const image = document.createElement("img");
      image.alt = "";
      image.decoding = "async";
      artwork.appendChild(image);
      card._pendingImageJob = { card, artwork, image, url: item.artworkUrl, controller: new AbortController(), objectUrl: null };
    }
    card.appendChild(artwork);

    const body = document.createElement("div");
    body.className = "body";
    const meta = document.createElement("div");
    meta.className = "meta";
    meta.append(textElement("type", item.typeLabel), textElement("date", item.dateLabel));
    body.append(meta, textElement("title", item.title), textElement("subtitle", item.subtitle));
    card.appendChild(body);

    const footer = document.createElement("div");
    footer.className = "footer";
    footer.appendChild(textElement(item.hasCollectionBadge ? "badge" : "status", item.hasCollectionBadge ? item.collectionBadge : item.statusLabel));
    footer.appendChild(textElement("favorite", item.isFavorite ? "\uE735" : "\uE734"));
    card.appendChild(footer);

    card._imageJob = null;
    return card;
  }

  function applyRange(message) {
    if (Number(message.revision) !== revision || !Array.isArray(message.items)) return;
    for (const item of message.items) {
      const index = Number(item.index);
      if (!Number.isInteger(index) || index < 0 || index >= total) continue;
      const previous = cards.get(index);
      if (previous) removeCard(previous);
      const card = createCard(item);
      positionCard(card, index);
      cards.set(index, card);
      canvas.appendChild(card);
      if (card._pendingImageJob) {
        enqueueImage(card._pendingImageJob);
        card._pendingImageJob = null;
      }
    }
  }

  function pruneCards(start, count) {
    const end = start + count;
    for (const [index, card] of cards) {
      if (index < start || index >= end) {
        removeCard(card);
        cards.delete(index);
      } else {
        positionCard(card, index);
      }
    }
  }

  function removeCard(card) {
    const job = card._imageJob;
    if (job) {
      job.controller.abort();
      if (job.objectUrl) URL.revokeObjectURL(job.objectUrl);
      card._imageJob = null;
    }
    if (card._pendingImageJob) {
      card._pendingImageJob.controller.abort();
      card._pendingImageJob = null;
    }
    card.remove();
  }

  function enqueueImage(job) {
    job.card._imageJob = job;
    imageQueue.push(job);
    drainImages();
  }

  function drainImages() {
    while (activeImageLoads < maximumImageLoads && imageQueue.length > 0) {
      const job = imageQueue.shift();
      if (job.controller.signal.aborted || !job.card.isConnected) continue;
      activeImageLoads += 1;
      fetch(job.url, { signal: job.controller.signal, cache: "no-store" })
        .then(response => {
          if (!response.ok) throw new Error(`image ${response.status}`);
          return response.blob();
        })
        .then(blob => {
          if (job.controller.signal.aborted || !job.card.isConnected) return;
          job.objectUrl = URL.createObjectURL(blob);
          job.image.src = job.objectUrl;
          job.image.onload = () => job.artwork.classList.add("loaded");
        })
        .catch(error => {
          if (error.name !== "AbortError") job.artwork.classList.add("loaded");
        })
        .finally(() => {
          activeImageLoads -= 1;
          drainImages();
        });
    }
  }

  viewport.addEventListener("scroll", scheduleRange, { passive: true });
  new ResizeObserver(() => {
    requestedStart = -1;
    requestedCount = -1;
    scheduleRange();
  }).observe(viewport);

  window.chrome.webview.addEventListener("message", event => {
    const message = event.data || {};
    switch (message.type) {
      case "reset": reset(message); break;
      case "range": applyRange(message); break;
      case "theme": setTheme(message); break;
      case "scrollToTop": viewport.scrollTo({ top: 0, behavior: "auto" }); break;
    }
  });

  post({ type: "ready" });
})();
