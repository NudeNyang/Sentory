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
const SOURCES = ["Discord", "KakaoTalk", "Slack", "WhatsApp", "Telegram", "Line", "WeChat"];
const SOURCE_PATCH_KEYS = {
  Discord: "discordSupportEnabled",
  KakaoTalk: "kakaoTalkSupportEnabled",
  Slack: "slackSupportEnabled",
  WhatsApp: "whatsAppSupportEnabled",
  Telegram: "telegramSupportEnabled",
  Line: "lineSupportEnabled",
  WeChat: "weChatSupportEnabled",
};
const TRANSLATIONS = {
  "ko-KR": {
    tagline: "이야기 속, 흩어진 순간들을 한 곳에", all: "전체", link: "링크", photo: "사진", image: "사진", typeLink: "링크", collection: "묶음", favorite: "즐겨찾기",
    filter: "필터", reset: "초기화", source: "메신저", period: "기간", allPeriod: "전체 기간", today: "오늘", last7: "최근 7일", last30: "최근 30일",
    select: "선택", settings: "설정", search: "제목, URL, 도메인 검색", newest: "최신순",
    oldest: "오래된순", mostCaptured: "많이 저장한 순", mostCopied: "많이 복사한 순",
    recentlyCopied: "최근 복사한 순", name: "이름순", sortLabel: value => `정렬 ${value}`, general: "일반", messenger: "메신저 감지",
    settingsDescription: "Sentory의 실행, 메신저 감지와 보관 데이터를 한곳에서 관리합니다", theme: "화면 테마", themeDescription: "라이트 모드와 다크 모드를 선택합니다",
    language: "Language", languageDescription: "화면에 표시할 언어를 선택합니다", light: "라이트 모드", dark: "다크 모드", system: "시스템 테마",
    auto: "Auto", korean: "한국어", detected: "감지 준비 완료", disabled: "사용 안 함", disabledSource: source => `${source} 감지를 사용하지 않습니다`, connecting: "연결 준비 중",
    recovering: "워커 복구 중", reconnect: "Discord 재연결 필요", repair: "다시 연결", discordDetection: "Discord 감지",
    copy: "복사", copyClipboard: "클립보드에 복사", copied: "복사됨", photoCopied: "사진을 복사했습니다.", urlCopied: "URL을 복사했습니다.", collectionCopied: "묶음 항목을 클립보드에 복사했습니다.", addFavorite: "즐겨찾기에 추가했습니다.",
    removeFavorite: "즐겨찾기에서 제거했습니다.", savedOnInput: "입력 시 저장됨", savedOnSend: "전송 시 저장됨",
    copyCount: n => `복사 ${n}회`, selectedCount: n => `${n}개 선택`, visibleSelect: "전체 선택",
    clearSelection: "선택 취소", deleteSelected: "선택 항목 삭제", emptyFiltered: "검색 결과가 없습니다", empty: "아직 보관된 항목이 없습니다",
    items: n => `${n.toLocaleString("ko-KR")}개 항목`, engineConnected: "C# 엔진 연결됨", loading: "보관함을 불러오는 중",
    close: "닫기", detail: "Sentory 항목 상세", favoriteMarked: "★ 즐겨찾기", captureCount: "저장 횟수", copyCountLabel: "복사 횟수", messageSource: "마지막 출처", savedAt: "마지막 저장",
    savedState: "저장 상태", delete: "삭제", openOriginal: "원본 열기", cancel: "취소", deleteQuestion: n => n === 1 ? "항목을 삭제할까요?" : `선택한 ${n.toLocaleString("ko-KR")}개 항목을 삭제할까요?`,
    deleteWarning: n => n === 1 ? "이 항목을 보관함에서 삭제합니다.\n이 작업은 되돌릴 수 없습니다." : "선택한 항목과 저장된 사진 파일을 보관함에서 삭제합니다.\n이 작업은 되돌릴 수 없습니다.", deleted: n => `${n.toLocaleString("ko-KR")}개 항목을 삭제했습니다.`,
    repairQuestion: "Discord를 다시 연결할까요?", repairWarning: "Discord를 접근성 모드로 다시 시작합니다. 작성 중인 메시지와 진행 중인 통화가 종료될 수 있습니다.", restart: "다시 시작",
    repairing: "워커 복구 중", repaired: "Discord를 연결 복구 모드로 다시 시작했습니다.", settingsFailed: "설정을 불러오지 못했습니다.",
    discordPhotoSaved: "Discord에서 사진 전송을 확인해 저장했습니다.", discordUrlSaved: "Discord에서 URL 전송을 확인해 저장했습니다.", discordUrlsSaved: n => `Discord에서 URL ${n.toLocaleString("ko-KR")}개 전송을 확인해 저장했습니다.`, discordCollectionSaved: "Discord에서 여러 항목의 전송을 확인해 하나의 묶음으로 저장했습니다.",
    inputPhotoSaved: "사진을 입력 시 저장했습니다.", inputUrlSaved: "URL을 입력 시 저장했습니다.", inputUrlsSaved: n => `URL ${n.toLocaleString("ko-KR")}개를 입력 시 저장했습니다.`, inputCollectionSaved: "여러 항목을 입력 시 하나의 묶음으로 저장했습니다.",
    galleryRefreshing: "보관함을 불러오는 중", newItem: "새 항목을 보관함에 반영했습니다.", enginePreparing: "시작 중...",
    engineRecovering: "워커 복구 중", engineFailed: "Sentory를 시작하지 못했습니다", itemNotFound: "항목을 찾지 못했습니다.",
  },
  "en-US": {
    tagline: "Moments scattered across your conversations, all in one place", all: "All", link: "Links", photo: "Photos", image: "Photo", typeLink: "Link", collection: "Collection", favorite: "Favorites",
    filter: "Filter", reset: "Reset", source: "Messenger", period: "Period", allPeriod: "All time", today: "Today", last7: "Last 7 days", last30: "Last 30 days",
    select: "Select", settings: "Settings", search: "Search title, URL, or domain", newest: "Newest",
    oldest: "Oldest", mostCaptured: "Most saved", mostCopied: "Most copied", recentlyCopied: "Recently copied", name: "Name", sortLabel: value => `Sort: ${value}`,
    general: "General", messenger: "Messenger detection", settingsDescription: "Manage Sentory, messenger detection, and saved data in one place",
    theme: "Theme", themeDescription: "Choose light or dark mode", language: "Language", languageDescription: "Choose the display language",
    light: "Light mode", dark: "Dark mode", system: "System theme", auto: "Auto", korean: "한국어", detected: "Ready to detect", disabled: "Off", disabledSource: source => `${source} detection is disabled`,
    connecting: "Preparing connection", recovering: "Recovering worker", reconnect: "Discord reconnect required", repair: "Reconnect", discordDetection: "Discord detection",
    copy: "Copy", copyClipboard: "Copy to clipboard", copied: "Copied", photoCopied: "Photo copied.", urlCopied: "URL copied.", collectionCopied: "Collection copied to the clipboard.", addFavorite: "Added to favorites.", removeFavorite: "Removed from favorites.",
    savedOnInput: "Saved on input", savedOnSend: "Saved on send", copyCount: n => `Copied ${n} times`, selectedCount: n => `${n} selected`,
    visibleSelect: "Select all", clearSelection: "Clear selection", deleteSelected: "Delete selected", emptyFiltered: "No results found",
    empty: "Nothing saved yet", items: n => `${n.toLocaleString("en-US")} items`, engineConnected: "C# engine connected", loading: "Loading library",
    close: "Close", detail: "Sentory Item Details", favoriteMarked: "★ Favorite", captureCount: "Times saved", copyCountLabel: "Times copied", messageSource: "Latest source", savedAt: "Last saved", savedState: "Save state",
    delete: "Delete", openOriginal: "Open original", cancel: "Cancel", deleteQuestion: n => n === 1 ? "Delete this item?" : `Delete ${n} selected items?`,
    deleteWarning: n => n === 1 ? "This item will be removed from the library.\nThis cannot be undone." : "The selected items and saved photo files will be removed from the library.\nThis cannot be undone.", deleted: n => `Deleted ${n} items.`, repairQuestion: "Reconnect Discord?",
    repairWarning: "Discord will restart in accessibility mode. Draft messages and active calls may be ended.", restart: "Restart", repairing: "Recovering worker",
    repaired: "Discord restarted in connection recovery mode.", settingsFailed: "Could not load settings.", galleryRefreshing: "Loading library",
    discordPhotoSaved: "Saved a photo confirmed as sent in Discord.", discordUrlSaved: "Saved a URL confirmed as sent in Discord.", discordUrlsSaved: n => `Saved ${n.toLocaleString("en-US")} URLs confirmed as sent in Discord.`, discordCollectionSaved: "Saved multiple Discord items as one collection.",
    inputPhotoSaved: "Saved the photo when pasted.", inputUrlSaved: "Saved the URL when pasted.", inputUrlsSaved: n => `Saved ${n.toLocaleString("en-US")} URLs when pasted.`, inputCollectionSaved: "Saved multiple pasted items as one collection.",
    newItem: "A new item was added to the gallery.", enginePreparing: "Preparing the C# engine…", engineRecovering: "Recovering the worker connection…",
    engineFailed: "Could not recover the C# engine connection.", itemNotFound: "Item not found.",
  },
};
TRANSLATIONS["ja-JP"] = {
  ...TRANSLATIONS["en-US"],
  tagline: "会話に散らばる瞬間を、一か所に", all: "すべて", link: "リンク", photo: "写真", image: "写真", typeLink: "リンク", collection: "まとめ", favorite: "お気に入り",
  filter: "フィルター", reset: "リセット", source: "メッセンジャー", period: "期間", allPeriod: "全期間", today: "今日", last7: "過去7日", last30: "過去30日",
  select: "選択", settings: "設定", search: "タイトル、URL、ドメインを検索", newest: "新しい順", oldest: "古い順", mostCaptured: "保存回数順", mostCopied: "コピー回数順", recentlyCopied: "最近コピーした順", name: "名前順", sortLabel: value => `並べ替え: ${value}`,
  general: "一般", messenger: "メッセンジャー検出", settingsDescription: "Sentory の動作、メッセンジャー検出、保存データを一か所で管理します",
  language: "Language", languageDescription: "表示する言語を選択します", theme: "画面テーマ", themeDescription: "ライトモードとダークモードを選択します", light: "ライトモード", dark: "ダークモード", system: "システムテーマ", auto: "Auto",
  detected: "検出準備完了", disabled: "使用しない", disabledSource: source => `${source} 検出を使用していません`, connecting: "接続準備中", recovering: "ワーカーを復旧中", reconnect: "Discord の再接続が必要", repair: "再接続", discordDetection: "Discord 検出",
  savedOnInput: "入力時に保存", savedOnSend: "送信時に保存", photoCopied: "写真をコピーしました。", urlCopied: "URL をコピーしました。", collectionCopied: "まとめた項目をクリップボードにコピーしました。", addFavorite: "お気に入りに追加しました。", removeFavorite: "お気に入りから削除しました。",
  selectedCount: n => `${n.toLocaleString("ja-JP")}件選択`, visibleSelect: "すべて選択", clearSelection: "選択を解除", deleteSelected: "選択項目を削除", emptyFiltered: "検索結果がありません", empty: "まだ保存された項目はありません", loading: "ライブラリを読み込み中",
  detail: "Sentory 項目の詳細", favoriteMarked: "★ お気に入り", captureCount: "保存回数", copyCountLabel: "コピー回数", messageSource: "最後の送信元", savedAt: "最終保存", delete: "削除", openOriginal: "元を開く", cancel: "キャンセル",
  deleteQuestion: n => n === 1 ? "この項目を削除しますか？" : `選択した ${n.toLocaleString("ja-JP")}件を削除しますか？`, deleteWarning: n => n === 1 ? "この項目をライブラリから削除します。\nこの操作は元に戻せません。" : "選択した項目と保存された写真ファイルをライブラリから削除します。\nこの操作は元に戻せません。", deleted: n => `${n.toLocaleString("ja-JP")}件を削除しました。`,
  repairQuestion: "Discord を再接続しますか？", repairWarning: "Discord をアクセシビリティモードで再起動します。作成中のメッセージや通話が終了する場合があります。", restart: "再起動", repaired: "Discord を接続復旧モードで再起動しました。",
  discordPhotoSaved: "Discord で写真の送信を確認して保存しました。", discordUrlSaved: "Discord で URL の送信を確認して保存しました。", discordUrlsSaved: n => `Discord で URL ${n.toLocaleString("ja-JP")}件の送信を確認して保存しました。`, discordCollectionSaved: "Discord の複数項目を1つのまとめとして保存しました。",
  inputPhotoSaved: "写真を入力時に保存しました。", inputUrlSaved: "URL を入力時に保存しました。", inputUrlsSaved: n => `URL ${n.toLocaleString("ja-JP")}件を入力時に保存しました。`, inputCollectionSaved: "複数の入力項目を1つのまとめとして保存しました。",
};
TRANSLATIONS["zh-CN"] = {
  ...TRANSLATIONS["en-US"],
  tagline: "将散落在对话中的瞬间汇聚一处", all: "全部", link: "链接", photo: "图片", image: "图片", typeLink: "链接", collection: "组合", favorite: "收藏",
  filter: "筛选", reset: "重置", source: "聊天应用", period: "时间范围", allPeriod: "全部时间", today: "今天", last7: "最近 7 天", last30: "最近 30 天",
  select: "选择", settings: "设置", search: "搜索标题、URL 或域名", newest: "最新优先", oldest: "最早优先", mostCaptured: "保存次数最多", mostCopied: "复制次数最多", recentlyCopied: "最近复制", name: "按名称", sortLabel: value => `排序：${value}`,
  general: "常规", messenger: "聊天应用检测", settingsDescription: "在一个位置管理 Sentory、聊天应用检测和保存的数据",
  language: "Language", languageDescription: "选择界面显示语言", theme: "界面主题", themeDescription: "选择浅色或深色模式", light: "浅色模式", dark: "深色模式", system: "系统主题", auto: "Auto",
  detected: "检测已就绪", disabled: "未使用", disabledSource: source => `${source} 检测已关闭`, connecting: "正在准备连接", recovering: "正在恢复工作进程", reconnect: "需要重新连接 Discord", repair: "重新连接", discordDetection: "Discord 检测",
  savedOnInput: "粘贴时保存", savedOnSend: "发送时保存", photoCopied: "图片已复制。", urlCopied: "URL 已复制。", collectionCopied: "组合项目已复制到剪贴板。", addFavorite: "已添加到收藏。", removeFavorite: "已从收藏中移除。",
  selectedCount: n => `已选择 ${n.toLocaleString("zh-CN")} 项`, visibleSelect: "全选", clearSelection: "取消选择", deleteSelected: "删除所选项目", emptyFiltered: "没有搜索结果", empty: "尚未保存任何项目", loading: "正在加载收藏库",
  detail: "Sentory 项目详情", favoriteMarked: "★ 已收藏", captureCount: "保存次数", copyCountLabel: "复制次数", messageSource: "最近来源", savedAt: "最后保存", delete: "删除", openOriginal: "打开原文件", cancel: "取消",
  deleteQuestion: n => n === 1 ? "要删除此项目吗？" : `要删除所选的 ${n.toLocaleString("zh-CN")} 个项目吗？`, deleteWarning: n => n === 1 ? "将从收藏库中删除此项目。\n此操作无法撤销。" : "将从收藏库中删除所选项目及保存的图片文件。\n此操作无法撤销。", deleted: n => `已删除 ${n.toLocaleString("zh-CN")} 个项目。`,
  repairQuestion: "要重新连接 Discord 吗？", repairWarning: "Discord 将以无障碍模式重启。正在编辑的消息和通话可能会结束。", restart: "重新启动", repaired: "Discord 已以连接恢复模式重新启动。",
  discordPhotoSaved: "已保存经确认在 Discord 中发送的图片。", discordUrlSaved: "已保存经确认在 Discord 中发送的 URL。", discordUrlsSaved: n => `已保存 ${n.toLocaleString("zh-CN")} 个经确认在 Discord 中发送的 URL。`, discordCollectionSaved: "已将 Discord 中发送的多个项目保存为一个组合。",
  inputPhotoSaved: "已在粘贴图片时保存。", inputUrlSaved: "已在粘贴 URL 时保存。", inputUrlsSaved: n => `已在粘贴时保存 ${n.toLocaleString("zh-CN")} 个 URL。`, inputCollectionSaved: "已将粘贴的多个项目保存为一个组合。",
};

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
  selectionMode: false,
  selectedIds: new Set(),
  selectionDrag: null,
  autoScrollFrame: 0,
  autoScrollPausedUntil: 0,
  detailItem: null,
  suppressCardClick: false,
  toastTimer: 0,
  settings: null,
  runtimeStatus: null,
  locale: "ko-KR",
  settingsBusy: false,
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
const selectModeButton = document.querySelector("#select-mode");
const selectionRectangle = document.querySelector("#selection-rectangle");
const selectionBar = document.querySelector("#selection-bar");
const selectedCount = document.querySelector("#selected-count");
const selectVisibleButton = document.querySelector("#select-visible");
const clearSelectionButton = document.querySelector("#clear-selection");
const deleteSelectedButton = document.querySelector("#delete-selected");
const detailLayer = document.querySelector("#detail-layer");
const detailClose = document.querySelector("#detail-close");
const detailType = document.querySelector("#detail-type");
const detailFavoriteMark = document.querySelector("#detail-favorite-mark");
const detailTitle = document.querySelector("#detail-title");
const detailArtwork = document.querySelector("#detail-artwork");
const detailDescription = document.querySelector("#detail-description");
const detailCaptureCount = document.querySelector("#detail-capture-count");
const detailCopyCount = document.querySelector("#detail-copy-count");
const detailSource = document.querySelector("#detail-source");
const detailDate = document.querySelector("#detail-date");
const detailDelivery = document.querySelector("#detail-delivery");
const detailDelete = document.querySelector("#detail-delete");
const detailOpen = document.querySelector("#detail-open");
const detailCopy = document.querySelector("#detail-copy");
const confirmLayer = document.querySelector("#confirm-layer");
const confirmTitle = document.querySelector("#confirm-title");
const confirmMessage = document.querySelector("#confirm-message");
const confirmCancel = document.querySelector("#confirm-cancel");
const confirmOk = document.querySelector("#confirm-ok");
const toast = document.querySelector("#toast");
const settingsButton = document.querySelector("#settings");
const settingsLayer = document.querySelector("#settings-layer");
const settingsClose = document.querySelector("#settings-close");
const themeSetting = document.querySelector("#setting-theme");
const languageSetting = document.querySelector("#setting-language");
const settingsSources = document.querySelector("#settings-sources");
const detectionStatus = document.querySelector("#detection-status");
const detectionStatusText = document.querySelector("#detection-status-text");
const colorScheme = window.matchMedia("(prefers-color-scheme: dark)");

function tauriCore() {
  const core = window.__TAURI__?.core;
  if (!core) throw new Error("Tauri 명령 브리지를 찾지 못했습니다.");
  return core;
}

function resolveLocale(language) {
  if (language && language !== "auto" && TRANSLATIONS[language]) return language;
  const browser = navigator.language || "en-US";
  if (browser.toLowerCase().startsWith("ko")) return "ko-KR";
  if (browser.toLowerCase().startsWith("ja")) return "ja-JP";
  if (browser.toLowerCase().startsWith("zh")) return "zh-CN";
  return "en-US";
}

function t(key, ...args) {
  const value = TRANSLATIONS[state.locale]?.[key] ?? TRANSLATIONS["en-US"][key] ?? key;
  return typeof value === "function" ? value(...args) : value;
}

function applyThemeMode(mode) {
  const dark = mode === "Dark" || (mode === "System" && colorScheme.matches);
  document.documentElement.dataset.theme = dark ? "dark" : "light";
  themeButton.querySelector("span").innerHTML = dark ? "&#xE706;" : "&#xE708;";
  const nextLabel = dark ? t("light") : t("dark");
  themeButton.title = `${nextLabel} ${t("theme")}`;
  themeButton.setAttribute("aria-label", themeButton.title);
  themeSetting.value = mode || "Light";
}

function localizedType(item) {
  const kind = item.kind === "Image" ? t("image") : item.kind === "Collection" ? t("collection") : t("typeLink");
  return `${kind} · ${sourceLabel(item.sourceApp)}`;
}

function localizedDate(value) {
  const date = new Date(value);
  if (Number.isNaN(date.valueOf())) return "";
  return new Intl.DateTimeFormat(state.locale, {
    month: "long", day: "numeric", hour: "2-digit", minute: "2-digit", hour12: false,
  }).format(date).replace(",", " ·");
}

function localizedStatus(item) {
  return item.deliveryStatus === "NotObserved" ? t("savedOnInput") : t("savedOnSend");
}

function localizedCaptureMessage(payload) {
  const count = Math.max(1, Number(payload?.count || 1));
  const isDiscordConfirmed = payload?.sourceApp === "Discord" && payload?.deliveryStatus === "Confirmed";
  if (payload?.kind === "Collection") return t(isDiscordConfirmed ? "discordCollectionSaved" : "inputCollectionSaved");
  if (payload?.kind === "Image") return t(isDiscordConfirmed ? "discordPhotoSaved" : "inputPhotoSaved");
  if (count === 1) return t(isDiscordConfirmed ? "discordUrlSaved" : "inputUrlSaved");
  return t(isDiscordConfirmed ? "discordUrlsSaved" : "inputUrlsSaved", count);
}

function applyLocalizedUi(language) {
  state.locale = resolveLocale(language);
  document.documentElement.lang = state.locale;
  document.querySelector("#tagline").textContent = t("tagline");
  const tabs = [...document.querySelectorAll(".tab")];
  tabs[0].textContent = t("all");
  tabs[1].textContent = t("link");
  tabs[2].textContent = t("photo");
  tabs[3].lastElementChild.textContent = t("favorite");
  document.querySelector("#filter-label").textContent = t("filter");
  document.querySelector("#select-label").textContent = t("select");
  document.querySelector("#settings-label").textContent = t("settings");
  document.querySelector("#detection-label").textContent = t("discordDetection");
  search.placeholder = t("search");
  search.setAttribute("aria-label", t("search"));
  document.querySelector(".popup-heading strong").textContent = t("filter");
  filterReset.textContent = t("reset");
  const filterHeadings = document.querySelectorAll(".filter-columns h2");
  filterHeadings[0].textContent = t("source");
  filterHeadings[1].textContent = t("period");
  const dateKeys = ["allPeriod", "today", "last7", "last30"];
  [...dateOptions.querySelectorAll("button")].forEach((button, index) => { button.textContent = t(dateKeys[index]); });
  document.querySelector("#settings-title").textContent = t("settings");
  document.querySelector("#settings-description").textContent = t("settingsDescription");
  document.querySelector("#general-heading").textContent = t("general");
  document.querySelector("#messenger-heading").textContent = t("messenger");
  document.querySelector("#theme-setting-title").textContent = t("theme");
  document.querySelector("#theme-setting-description").textContent = t("themeDescription");
  document.querySelector("#language-setting-title").textContent = t("language");
  document.querySelector("#language-setting-description").textContent = t("languageDescription");
  themeSetting.options[0].textContent = t("light");
  themeSetting.options[1].textContent = t("dark");
  themeSetting.options[2].textContent = t("system");
  languageSetting.options[0].textContent = t("auto");
  languageSetting.options[1].textContent = t("korean");
  const sortKeys = ["newest", "oldest", "mostCaptured", "mostCopied", "recentlyCopied", "name"];
  [...sortMenu.querySelectorAll("button")].forEach((button, index) => { button.textContent = t(sortKeys[index]); });
  const selectedSort = sortMenu.querySelector(`[data-sort="${state.sort}"]`);
  sortLabel.textContent = t("sortLabel", selectedSort?.textContent || t("newest"));
  selectVisibleButton.textContent = t("visibleSelect");
  clearSelectionButton.textContent = t("clearSelection");
  deleteSelectedButton.textContent = t("deleteSelected");
  detailClose.setAttribute("aria-label", t("close"));
  detailFavoriteMark.textContent = t("favoriteMarked");
  detailCaptureCount.closest("div").querySelector("dt").textContent = t("captureCount");
  detailCopyCount.closest("div").querySelector("dt").textContent = t("copyCountLabel");
  detailSource.closest("div").querySelector("dt").textContent = t("messageSource");
  detailDate.closest("div").querySelector("dt").textContent = t("savedAt");
  detailDelivery.closest("div").querySelector("dt").textContent = t("savedState");
  detailDelete.textContent = t("delete");
  detailOpen.textContent = t("openOriginal");
  detailCopy.textContent = t("copy");
  confirmCancel.textContent = t("cancel");
  settingsClose.setAttribute("aria-label", t("close"));
  updateSelectionUi();
  renderSourceSettings();
  applyRuntimeStatus(state.runtimeStatus);
  applyThemeMode(state.settings?.themeMode || "Light");
  state.renderRevision += 1;
  state.renderedRange = "";
  renderVisibleCards();
  if (state.detailItem && !detailLayer.hidden) populateDetails(state.detailItem);
}

function applySettings(settings) {
  state.settings = settings;
  languageSetting.value = settings.language || "auto";
  applyLocalizedUi(settings.language || "auto");
  applyThemeMode(settings.themeMode || "Light");
}

async function loadSettings() {
  try {
    applySettings(await tauriCore().invoke("settings_get"));
  } catch (error) {
    showToast(`${t("settingsFailed")} ${error instanceof Error ? error.message : String(error)}`);
    applyLocalizedUi("auto");
  }
}

async function persistSettings(patch) {
  try {
    const settings = await tauriCore().invoke("settings_update", { patch });
    applySettings(settings);
    return settings;
  } catch (error) {
    showToast(error instanceof Error ? error.message : String(error));
    await loadSettings();
    return null;
  }
}

function sourceRuntimeLabel(source) {
  if (!state.settings?.sources?.[source]) return { text: t("disabledSource", sourceLabel(source)), tone: "" };
  if (source !== "Discord") return { text: t("detected"), tone: "ready" };
  const runtime = state.runtimeStatus;
  if (!runtime?.discordRunning) return { text: t("disabled"), tone: "" };
  const key = runtime.discordState === "Ready" ? "detected"
    : runtime.discordState === "Recovering" ? "recovering"
      : runtime.discordState === "ReconnectRequired" ? "reconnect" : "connecting";
  return { text: t(key), tone: key === "detected" ? "ready" : key === "reconnect" ? "issue" : "" };
}

function renderSourceSettings() {
  if (!state.settings) return;
  const fragment = document.createDocumentFragment();
  for (const source of SOURCES) {
    const row = document.createElement("div");
    row.className = "setting-row source-setting";
    const label = document.createElement("span");
    const title = document.createElement("strong");
    title.textContent = sourceLabel(source);
    label.append(title);
    const control = document.createElement("div");
    control.className = "source-control";
    const runtime = sourceRuntimeLabel(source);
    const statusLabel = document.createElement("span");
    statusLabel.className = `source-status ${runtime.tone}`.trim();
    statusLabel.textContent = runtime.text;
    control.append(statusLabel);
    if (source === "Discord" && state.runtimeStatus?.discordState === "ReconnectRequired" && state.settings.sources.Discord) {
      const repair = document.createElement("button");
      repair.className = "repair-button";
      repair.type = "button";
      repair.textContent = t("repair");
      repair.addEventListener("click", () => void repairDiscord());
      control.append(repair);
    }
    const switchLabel = document.createElement("label");
    switchLabel.className = "switch";
    const input = document.createElement("input");
    input.type = "checkbox";
    input.checked = Boolean(state.settings.sources[source]);
    input.setAttribute("aria-label", sourceLabel(source));
    const track = document.createElement("span");
    track.className = "switch-track";
    input.addEventListener("change", () => {
      state.settings.sources[source] = input.checked;
      renderSourceSettings();
      void persistSettings({ [SOURCE_PATCH_KEYS[source]]: input.checked });
    });
    switchLabel.append(input, track);
    control.append(switchLabel);
    row.append(label, control);
    fragment.append(row);
  }
  settingsSources.replaceChildren(fragment);
}

function applyRuntimeStatus(runtime) {
  if (runtime) state.runtimeStatus = runtime;
  const enabled = Boolean(state.settings?.sources?.Discord);
  const running = Boolean(state.runtimeStatus?.discordRunning);
  detectionStatus.hidden = !enabled || !running;
  if (enabled && running) {
    const label = sourceRuntimeLabel("Discord");
    detectionStatusText.textContent = label.text;
    detectionStatus.classList.toggle("issue", label.tone === "issue");
    detectionStatus.classList.toggle("ready", label.tone === "ready");
  }
  renderSourceSettings();
}

async function repairDiscord() {
  const confirmed = await askConfirmation(t("repairQuestion"), t("repairWarning"), { okText: t("restart"), danger: false });
  if (!confirmed) return;
  detectionStatus.hidden = false;
  detectionStatusText.textContent = t("recovering");
  showToast(t("repairing"));
  try {
    applyRuntimeStatus(await tauriCore().invoke("discord_repair"));
    showToast(t("repaired"));
  } catch (error) {
    showToast(error instanceof Error ? error.message : String(error));
  }
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

function resetGallery({ announce = false, preserveScroll = false } = {}) {
  const previousScrollTop = preserveScroll ? scroller.scrollTop : 0;
  state.generation += 1;
  state.pendingPages.clear();
  state.items = new Array(state.hasLoaded ? Math.max(1, state.total) : PAGE_SIZE);
  state.total = state.items.length;
  state.renderRevision += 1;
  state.renderedRange = "";
  scroller.scrollTop = previousScrollTop;
  measureGrid();
  renderVisibleCards();
  if (announce) setStatus(t("galleryRefreshing"));
  void loadPage(0, state.generation, true);
}

async function connectEngineEvents() {
  const listen = window.__TAURI__?.event?.listen;
  if (!listen) return;
  await listen("gallery-changed", () => {
    const preserveScroll = scroller.scrollTop > ROW_HEIGHT;
    resetGallery({ preserveScroll });
    if (preserveScroll) showToast(t("newItem"));
  });
  await listen("engine-status", event => {
    const engineState = event.payload?.state;
    if (engineState === "connecting") {
      setStatus(t("enginePreparing"));
    } else if (engineState === "recovering") {
      setStatus(t("engineRecovering"));
    } else if (engineState === "ready" &&
               (status.textContent.includes("C# 엔진") ||
                status.textContent.includes("워커 연결"))) {
      status.classList.add("hidden");
    } else if (engineState === "error") {
      setStatus(event.payload?.message || t("engineFailed"), true);
    }
  });
  await listen("runtime-status", event => applyRuntimeStatus(event.payload));
  await listen("capture-event", event => {
    showToast(localizedCaptureMessage(event.payload));
  });
  await listen("runtime-issue", event => {
    if (event.payload?.message) showToast(event.payload.message);
  });
  await listen("settings-changed", event => {
    if (event.payload) applySettings(event.payload);
  });
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
        document.title = `Sentory · ${t("items", snapshot.total)}`;
      }
      for (let index = 0; index < snapshot.items.length; index += 1) {
        const target = pageOffset + index;
        if (target < state.items.length) state.items[target] = snapshot.items[index];
      }
      evictDistantPages();
      state.renderRevision += 1;
      state.renderedRange = "";
      measureGrid();
      renderVisibleCards();

      if (isInitial) {
        setStatus(`${t("items", snapshot.total)} · ${t("engineConnected")}`);
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

function evictDistantPages() {
  if (state.total <= PAGE_SIZE * 5) return;
  const centerIndex = Math.floor((scroller.scrollTop / ROW_HEIGHT) * state.columns);
  const centerPage = Math.floor(centerIndex / PAGE_SIZE) * PAGE_SIZE;
  const minimum = Math.max(0, centerPage - PAGE_SIZE * 2);
  const maximum = Math.min(state.total, centerPage + PAGE_SIZE * 3);
  for (let index = 0; index < state.items.length; index += 1) {
    if (index < minimum || index >= maximum) state.items[index] = undefined;
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
  card.className = `card${state.selectedIds.has(item.itemId) ? " selected" : ""}`;
  card.dataset.itemId = item.itemId;
  positionCard(card, index);
  card.setAttribute("aria-label", `${localizedType(item)}, ${item.title}, ${localizedDate(item.lastCapturedAt)}`);
  card.addEventListener("click", () => {
    if (state.suppressCardClick) return;
    if (state.selectionMode) toggleSelection(item.itemId);
    else void showDetails(item.itemId);
  });

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
  artwork.addEventListener("click", event => {
    event.stopPropagation();
    if (state.selectionMode) toggleSelection(item.itemId);
    else void openItem(item.itemId);
  });

  const copyButton = document.createElement("button");
  copyButton.className = "copy-button fluent";
  copyButton.type = "button";
  copyButton.title = t("copyClipboard");
  copyButton.setAttribute("aria-label", t("copy"));
  copyButton.innerHTML = "&#xE8C8;";
  copyButton.addEventListener("click", event => {
    event.stopPropagation();
    void copyItem(item.itemId, copyButton);
  });

  const selectionToggle = document.createElement("button");
  selectionToggle.className = "selection-toggle";
  selectionToggle.type = "button";
  selectionToggle.innerHTML = state.selectedIds.has(item.itemId) ? "&#xE73E;" : "";
  selectionToggle.title = state.selectedIds.has(item.itemId) ? t("clearSelection") : t("select");
  selectionToggle.addEventListener("click", event => {
    event.stopPropagation();
    toggleSelection(item.itemId);
  });

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
  type.append(document.createTextNode(localizedType(item)));
  const date = document.createElement("span");
  date.textContent = localizedDate(item.lastCapturedAt);
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
  chip.textContent = localizedStatus(item);
  footer.append(chip);
  if (item.copyCount > 0) {
    const usage = document.createElement("span");
    usage.className = "copy-usage";
    usage.textContent = t("copyCount", item.copyCount);
    footer.append(usage);
  }
  const favorite = document.createElement("button");
  favorite.className = `favorite${item.isFavorite ? " active" : ""}`;
  favorite.type = "button";
  favorite.innerHTML = item.isFavorite ? "&#xE735;" : "&#xE734;";
  favorite.title = item.isFavorite ? t("removeFavorite") : t("addFavorite");
  favorite.setAttribute("aria-label", favorite.title);
  favorite.addEventListener("click", event => {
    event.stopPropagation();
    void toggleFavorite(item, favorite);
  });
  footer.append(favorite);

  body.append(meta, title, subtitle, footer);
  card.append(artwork, copyButton, selectionToggle, body);
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
    ? t("emptyFiltered")
    : t("empty");
  return empty;
}

async function showDetails(itemId) {
  detailLayer.hidden = false;
  detailTitle.textContent = t("loading");
  detailType.textContent = "";
  detailDescription.textContent = "";
  detailArtwork.replaceChildren();
  try {
    const detail = await tauriCore().invoke("gallery_item", { itemId });
    if (!detail) throw new Error(t("itemNotFound"));
    state.detailItem = detail;
    populateDetails(detail);
  } catch (error) {
    detailLayer.hidden = true;
    showToast(error instanceof Error ? error.message : String(error));
  }
}

function populateDetails(detail) {
  const card = detail.card;
  detailType.textContent = localizedType(card);
  detailFavoriteMark.hidden = !card.isFavorite;
  detailTitle.textContent = card.title;
  detailDescription.textContent = card.subtitle;
  detailCaptureCount.textContent = state.locale === "ko-KR" ? `${card.captureCount}회` : String(card.captureCount);
  detailCopyCount.textContent = state.locale === "ko-KR" ? `${card.copyCount}회` : String(card.copyCount);
  detailSource.textContent = sourceLabel(card.sourceApp);
  detailDate.textContent = new Intl.DateTimeFormat(state.locale, { dateStyle: "long", timeStyle: "short" }).format(new Date(card.lastCapturedAt));
  detailDelivery.textContent = localizedStatus(card);
  detailArtwork.replaceChildren(createDetailArtwork(detail));
}

function createDetailArtwork(detail) {
  const card = detail.card;
  const source = card.kind === "Image" ? detail.contentPath : card.artworkPath;
  if (source) {
    const image = document.createElement("img");
    image.alt = "";
    image.src = tauriCore().convertFileSrc(source);
    return image;
  }
  return createUrlFallback(card);
}

async function copyItem(itemId, button = null) {
  const previous = button?.innerHTML;
  if (button) {
    button.disabled = true;
    button.innerHTML = "&#xE895;";
  }
  try {
    const result = await tauriCore().invoke("gallery_copy", { itemId });
    if (!result.success) throw new Error("복사 기록을 저장하지 못했습니다.");
    updateLoadedItem(itemId, item => ({
      ...item,
      copyCount: result.copyCount ?? item.copyCount + 1,
      isFavorite: result.isFavorite ?? item.isFavorite,
    }));
    if (state.detailItem?.card.itemId === itemId) {
      state.detailItem.card.copyCount = result.copyCount ?? state.detailItem.card.copyCount + 1;
      state.detailItem.card.isFavorite = result.isFavorite ?? state.detailItem.card.isFavorite;
      detailCopyCount.textContent = `${state.detailItem.card.copyCount}회`;
      detailFavoriteMark.hidden = !state.detailItem.card.isFavorite;
    }
    const loaded = state.items.find(item => item?.itemId === itemId) ?? state.detailItem?.card;
    showToast(loaded?.kind === "Image" ? t("photoCopied") : loaded?.kind === "Collection" ? t("collectionCopied") : t("urlCopied"));
    if (button) button.innerHTML = "&#xE73E;";
  } catch (error) {
    showToast(error instanceof Error ? error.message : String(error));
    if (button) button.innerHTML = "&#xE783;";
  } finally {
    window.setTimeout(() => {
      if (button?.isConnected) {
        button.disabled = false;
        button.innerHTML = previous;
      }
    }, 850);
  }
}

async function toggleFavorite(item, button) {
  const next = !item.isFavorite;
  updateLoadedItem(item.itemId, current => ({ ...current, isFavorite: next }));
  try {
    const result = await tauriCore().invoke("gallery_favorite", {
      itemId: item.itemId,
      isFavorite: next,
    });
    if (!result.success) throw new Error("항목을 찾지 못했습니다.");
    showToast(next ? t("addFavorite") : t("removeFavorite"));
  } catch (error) {
    updateLoadedItem(item.itemId, current => ({ ...current, isFavorite: !next }));
    showToast(error instanceof Error ? error.message : String(error));
  }
}

function updateLoadedItem(itemId, transform) {
  const index = state.items.findIndex(item => item?.itemId === itemId);
  if (index >= 0) state.items[index] = transform(state.items[index]);
  state.renderRevision += 1;
  state.renderedRange = "";
  renderVisibleCards();
}

async function openItem(itemId) {
  try {
    await tauriCore().invoke("gallery_open", { itemId });
  } catch (error) {
    showToast(error instanceof Error ? error.message : String(error));
  }
}

async function deleteItems(itemIds) {
  if (itemIds.length === 0) return false;
  const confirmed = await askConfirmation(
    t("deleteQuestion", itemIds.length),
    t("deleteWarning", itemIds.length),
    { okText: t("delete"), danger: true });
  if (!confirmed) return false;
  try {
    const result = await tauriCore().invoke("gallery_delete", { itemIds });
    if (!result.success && result.missing === 0) throw new Error("항목을 삭제하지 못했습니다.");
    showToast(t("deleted", result.changed));
    state.selectedIds.clear();
    if (!detailLayer.hidden) detailLayer.hidden = true;
    resetGallery();
    updateSelectionUi();
    return true;
  } catch (error) {
    showToast(error instanceof Error ? error.message : String(error));
    return false;
  }
}

function askConfirmation(title, message, { okText = t("delete"), danger = true } = {}) {
  confirmTitle.textContent = title;
  confirmMessage.textContent = message;
  confirmOk.textContent = okText;
  confirmOk.classList.toggle("danger-action", danger);
  confirmOk.classList.toggle("primary-action", !danger);
  confirmLayer.hidden = false;
  return new Promise(resolve => {
    const finish = value => {
      confirmLayer.hidden = true;
      confirmCancel.removeEventListener("click", cancel);
      confirmOk.removeEventListener("click", accept);
      resolve(value);
    };
    const cancel = () => finish(false);
    const accept = () => finish(true);
    confirmCancel.addEventListener("click", cancel);
    confirmOk.addEventListener("click", accept);
    confirmCancel.focus();
  });
}

function showToast(message) {
  window.clearTimeout(state.toastTimer);
  toast.textContent = message;
  toast.hidden = false;
  state.toastTimer = window.setTimeout(() => { toast.hidden = true; }, 1800);
}

function setSelectionMode(enabled) {
  state.selectionMode = enabled;
  document.body.classList.toggle("selection-mode", enabled);
  selectModeButton.classList.toggle("selected", enabled);
  if (!enabled) state.selectedIds.clear();
  updateSelectionUi();
  state.renderRevision += 1;
  state.renderedRange = "";
  renderVisibleCards();
}

function toggleSelection(itemId) {
  if (state.selectedIds.has(itemId)) state.selectedIds.delete(itemId);
  else state.selectedIds.add(itemId);
  updateSelectionUi();
  state.renderRevision += 1;
  state.renderedRange = "";
  renderVisibleCards();
}

function updateSelectionUi() {
  selectionBar.hidden = !state.selectionMode;
  selectedCount.textContent = t("selectedCount", state.selectedIds.size);
  deleteSelectedButton.disabled = state.selectedIds.size === 0;
}

function sourceLabel(source) {
  return source === "KakaoTalk" ? "카카오톡" : source === "Line" ? "LINE" : source;
}

function beginSelectionDrag(event) {
  if (!state.selectionMode || event.button !== 0 || event.target.closest("button")) return;
  const bounds = galleryRegion.getBoundingClientRect();
  const x = event.clientX - bounds.left;
  const y = event.clientY - bounds.top;
  state.selectionDrag = {
    pointerId: event.pointerId,
    startX: x,
    startY: y,
    currentX: x,
    currentY: y,
    startContentX: x,
    startContentY: scroller.scrollTop + y,
    baseIds: event.ctrlKey ? new Set(state.selectedIds) : new Set(),
    active: false,
  };
}

function moveSelectionDrag(event) {
  const drag = state.selectionDrag;
  if (!drag || drag.pointerId !== event.pointerId) return;
  const bounds = galleryRegion.getBoundingClientRect();
  drag.currentX = Math.max(0, Math.min(bounds.width, event.clientX - bounds.left));
  drag.currentY = Math.max(0, Math.min(bounds.height, event.clientY - bounds.top));
  if (!drag.active) {
    if (Math.abs(drag.currentX - drag.startX) < 4 && Math.abs(drag.currentY - drag.startY) < 4) return;
    drag.active = true;
    state.suppressCardClick = true;
    galleryRegion.setPointerCapture(event.pointerId);
    selectionRectangle.hidden = false;
    state.autoScrollFrame = requestAnimationFrame(autoScrollSelection);
  }
  updateSelectionDrag();
}

function endSelectionDrag(event) {
  const drag = state.selectionDrag;
  if (!drag || drag.pointerId !== event.pointerId) return;
  if (drag.active) {
    updateSelectionDrag();
    selectionRectangle.hidden = true;
    cancelAnimationFrame(state.autoScrollFrame);
    if (galleryRegion.hasPointerCapture(event.pointerId)) galleryRegion.releasePointerCapture(event.pointerId);
    window.setTimeout(() => { state.suppressCardClick = false; }, 0);
  }
  state.selectionDrag = null;
}

function updateSelectionDrag() {
  const drag = state.selectionDrag;
  if (!drag?.active) return;
  const left = Math.min(drag.startX, drag.currentX);
  const top = Math.min(drag.startY, drag.currentY);
  const width = Math.abs(drag.currentX - drag.startX);
  const height = Math.abs(drag.currentY - drag.startY);
  Object.assign(selectionRectangle.style, {
    left: `${left}px`,
    top: `${top}px`,
    width: `${width}px`,
    height: `${height}px`,
  });

  const currentContentY = scroller.scrollTop + drag.currentY;
  const selection = {
    left: Math.min(drag.startContentX, drag.currentX),
    right: Math.max(drag.startContentX, drag.currentX),
    top: Math.min(drag.startContentY, currentContentY),
    bottom: Math.max(drag.startContentY, currentContentY),
  };
  const next = new Set(drag.baseIds);
  for (let index = 0; index < state.items.length; index += 1) {
    const item = state.items[index];
    if (!item) continue;
    const row = Math.floor(index / state.columns);
    const column = index % state.columns;
    const itemLeft = state.gridLeft + column * CELL_WIDTH + (CELL_WIDTH - CARD_WIDTH) / 2;
    const itemTop = TOP_PADDING + row * ROW_HEIGHT + (ROW_HEIGHT - CARD_HEIGHT) / 2;
    if (itemLeft < selection.right && itemLeft + CARD_WIDTH > selection.left &&
        itemTop < selection.bottom && itemTop + CARD_HEIGHT > selection.top) {
      next.add(item.itemId);
    }
  }
  state.selectedIds = next;
  updateSelectionUi();
  state.renderRevision += 1;
  state.renderedRange = "";
  requestRender();
}

function autoScrollSelection() {
  const drag = state.selectionDrag;
  if (!drag?.active) return;
  if (performance.now() >= state.autoScrollPausedUntil) {
    const edge = Math.min(72, galleryRegion.clientHeight / 3);
    let delta = 0;
    if (drag.currentY < edge && scroller.scrollTop > 0) {
      const proximity = (edge - drag.currentY) / edge;
      delta = -(2 + 6 * proximity * proximity);
    } else if (drag.currentY > galleryRegion.clientHeight - edge &&
               scroller.scrollTop < scroller.scrollHeight - scroller.clientHeight) {
      const proximity = (drag.currentY - (galleryRegion.clientHeight - edge)) / edge;
      delta = 2 + 6 * proximity * proximity;
    }
    if (delta !== 0) {
      scroller.scrollTop += delta;
      updateSelectionDrag();
    }
  }
  state.autoScrollFrame = requestAnimationFrame(autoScrollSelection);
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
  sortLabel.textContent = t("sortLabel", button.textContent);
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

selectModeButton.addEventListener("click", () => setSelectionMode(!state.selectionMode));
selectVisibleButton.addEventListener("click", () => {
  const firstRow = Math.max(0, Math.floor(scroller.scrollTop / ROW_HEIGHT));
  const lastRow = Math.min(
    Math.ceil(state.total / state.columns),
    Math.ceil((scroller.scrollTop + scroller.clientHeight) / ROW_HEIGHT));
  for (let index = firstRow * state.columns;
       index < Math.min(state.total, lastRow * state.columns);
       index += 1) {
    if (state.items[index]) state.selectedIds.add(state.items[index].itemId);
  }
  updateSelectionUi();
  state.renderRevision += 1;
  state.renderedRange = "";
  renderVisibleCards();
});
clearSelectionButton.addEventListener("click", () => {
  state.selectedIds.clear();
  updateSelectionUi();
  state.renderRevision += 1;
  state.renderedRange = "";
  renderVisibleCards();
});
deleteSelectedButton.addEventListener("click", () => void deleteItems([...state.selectedIds]));

galleryRegion.addEventListener("pointerdown", beginSelectionDrag);
galleryRegion.addEventListener("pointermove", moveSelectionDrag);
galleryRegion.addEventListener("pointerup", endSelectionDrag);
galleryRegion.addEventListener("pointercancel", endSelectionDrag);
galleryRegion.addEventListener("wheel", () => {
  if (state.selectionDrag?.active) state.autoScrollPausedUntil = performance.now() + 180;
}, { passive: true });

detailClose.addEventListener("click", () => { detailLayer.hidden = true; state.detailItem = null; });
detailLayer.addEventListener("pointerdown", event => {
  if (event.target === detailLayer) {
    detailLayer.hidden = true;
    state.detailItem = null;
  }
});
detailCopy.addEventListener("click", () => {
  if (state.detailItem) void copyItem(state.detailItem.card.itemId, detailCopy);
});
detailOpen.addEventListener("click", () => {
  if (state.detailItem) void openItem(state.detailItem.card.itemId);
});
detailArtwork.addEventListener("click", () => {
  if (state.detailItem) void openItem(state.detailItem.card.itemId);
});
detailDelete.addEventListener("click", () => {
  if (state.detailItem) void deleteItems([state.detailItem.card.itemId]);
});

document.addEventListener("keydown", event => {
  if (event.key !== "Escape") return;
  if (!confirmLayer.hidden) confirmCancel.click();
  else if (!settingsLayer.hidden) settingsClose.click();
  else if (!detailLayer.hidden) detailClose.click();
  else if (state.selectionMode) setSelectionMode(false);
});

themeButton.addEventListener("click", () => {
  const next = document.documentElement.dataset.theme === "dark" ? "Light" : "Dark";
  if (state.settings) state.settings.themeMode = next;
  applyThemeMode(next);
  void persistSettings({ themeMode: next });
});

settingsButton.addEventListener("click", () => {
  settingsLayer.hidden = false;
  renderSourceSettings();
  themeSetting.focus();
});
settingsClose.addEventListener("click", () => { settingsLayer.hidden = true; });
settingsLayer.addEventListener("pointerdown", event => {
  if (event.target === settingsLayer) settingsLayer.hidden = true;
});
themeSetting.addEventListener("change", () => {
  const mode = themeSetting.value;
  if (state.settings) state.settings.themeMode = mode;
  applyThemeMode(mode);
  void persistSettings({ themeMode: mode });
});
languageSetting.addEventListener("change", () => {
  const language = languageSetting.value;
  if (state.settings) state.settings.language = language;
  applyLocalizedUi(language);
  void persistSettings({ language });
});
colorScheme.addEventListener("change", () => {
  if (state.settings?.themeMode === "System") applyThemeMode("System");
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

updateFilterUi();
updateSelectionUi();
void connectEngineEvents();
void loadSettings();
resetGallery();
