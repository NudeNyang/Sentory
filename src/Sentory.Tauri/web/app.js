const CELL_WIDTH = 268;
const CARD_WIDTH = 252;
const CARD_HEIGHT = 320;
const ROW_HEIGHT = 336;
const TOP_PADDING = 20;
const BOTTOM_PADDING = 28;
const MINIMUM_SIDE_PADDING = 14;
const OVERSCAN_ROWS = 3;
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
    select: "선택", selectExit: "선택 종료", settings: "설정", search: "제목, URL, 도메인 검색", switchToDark: "다크 테마로 전환", switchToLight: "밝은 테마로 전환", newest: "최신순",
    oldest: "오래된순", mostCaptured: "많이 저장한 순", mostCopied: "많이 복사한 순",
    recentlyCopied: "최근 복사한 순", name: "이름순", sortLabel: value => `정렬 ${value}`, general: "일반", messenger: "메신저 감지",
    settingsDescription: "Sentory의 실행, 메신저 감지와 보관 데이터를 한곳에서 관리합니다", theme: "화면 테마", themeDescription: "라이트 모드와 다크 모드를 선택합니다",
    language: "Language", languageDescription: "화면에 표시할 언어를 선택합니다", light: "라이트 모드", dark: "다크 모드", system: "시스템 테마",
    auto: "Auto", korean: "한국어", detected: "감지 준비 완료", disabled: "사용 안 함", disabledSource: source => `${source === "KakaoTalk" ? "카카오톡" : source} 감지를 사용하지 않습니다`, detectionPaused: "감지 일시정지됨", connecting: "연결 준비 중",
    recovering: "워커 복구 중", reconnect: "Discord 재연결 필요", repair: "다시 연결", discordDetection: "Discord 감지",
    copy: "복사", copyClipboard: "클립보드에 복사", copied: "복사됨", photoCopied: "사진을 복사했습니다.", urlCopied: "URL을 복사했습니다.", collectionCopied: "묶음 항목을 클립보드에 복사했습니다.", addFavorite: "즐겨찾기에 추가했습니다.",
    removeFavorite: "즐겨찾기에서 제거했습니다.", favoriteAddAction: "즐겨찾기에 추가", favoriteRemoveAction: "즐겨찾기에서 제거", savedOnInput: "입력 시 저장됨", savedOnSend: "전송 시 저장됨",
    copyCount: n => `복사 ${n}회`, selectedCount: n => `${n}개 선택`, visibleSelect: "전체 선택",
    clearSelection: "선택 취소", deleteSelected: "선택 항목 삭제", emptyFiltered: "검색 결과가 없습니다", emptyFilteredDescription: "다른 검색어나 필터로 다시 찾아보세요.", empty: "아직 보관된 항목이 없습니다", emptyDescription: "메신저에 URL이나 사진을 붙여넣어 보세요.",
    items: n => `${n.toLocaleString("ko-KR")}개`, loading: "보관함을 불러오는 중", loadFailed: "보관함을 불러오지 못했습니다",
    close: "알림 닫기", detail: "Sentory 항목 상세", favoriteMarked: "★ 즐겨찾기", captureCount: "저장 횟수", copyCountLabel: "복사 횟수", messageSource: "마지막 출처", savedAt: "마지막 저장", photos: "사진", collectionLinks: "링크", previousPhoto: "이전 사진", nextPhoto: "다음 사진", copyCurrentPhoto: "현재 사진 복사", previousLink: "이전 링크", nextLink: "다음 링크", collectionItems: n => `항목 ${n.toLocaleString("ko-KR")}개`, collectionTitle: (photos, links) => `사진 ${photos.toLocaleString("ko-KR")}개 · 링크 ${links.toLocaleString("ko-KR")}개`,
    times: n => `${n.toLocaleString("ko-KR")}회`, openPhoto: "사진 열기", openLink: "링크 열기", openPreview: "원본 바로 열기", copyPhoto: "사진 복사", copyUrl: "URL 복사", copyCollection: "묶음 복사", delete: "삭제", openOriginal: "원본 열기", openOriginalFolder: "원본 폴더 열기", openOriginalLink: "원본 링크 열기", cancel: "취소", deleteQuestion: n => n === 1 ? "항목을 삭제할까요?" : `선택한 ${n.toLocaleString("ko-KR")}개 항목을 삭제할까요?`,
    deleteWarning: n => n === 1 ? "이 항목을 보관함에서 삭제합니다.\n이 작업은 되돌릴 수 없습니다." : "선택한 항목과 저장된 사진 파일을 보관함에서 삭제합니다.\n이 작업은 되돌릴 수 없습니다.", deleted: n => `${n.toLocaleString("ko-KR")}개 항목을 삭제했습니다.`,
    repairQuestion: "Discord를 다시 연결할까요?", repairWarning: "Discord를 접근성 모드로 다시 시작합니다. 작성 중인 메시지와 진행 중인 통화가 종료될 수 있습니다.", restart: "다시 시작",
    repairing: "워커 복구 중", repaired: "Discord를 연결 복구 모드로 다시 시작했습니다.", settingsFailed: "Sentory를 시작하지 못했습니다",
    discordPhotoSaved: "Discord에서 사진 전송을 확인해 저장했습니다.", discordUrlSaved: "Discord에서 URL 전송을 확인해 저장했습니다.", discordUrlsSaved: n => `Discord에서 URL ${n.toLocaleString("ko-KR")}개 전송을 확인해 저장했습니다.`, discordCollectionSaved: "Discord에서 여러 항목의 전송을 확인해 하나의 묶음으로 저장했습니다.",
    inputPhotoSaved: "사진을 입력 시 저장했습니다.", inputUrlSaved: "URL을 입력 시 저장했습니다.", inputUrlsSaved: n => `URL ${n.toLocaleString("ko-KR")}개를 입력 시 저장했습니다.`, inputCollectionSaved: "여러 항목을 입력 시 하나의 묶음으로 저장했습니다.",
    galleryRefreshing: "보관함을 불러오는 중", enginePreparing: "시작 중...",
    engineRecovering: "워커 복구 중", engineFailed: "Sentory를 시작하지 못했습니다", itemNotFound: "항목을 찾지 못했습니다.",
    windowsStartup: "Windows 시작 시 실행", startupEnabledDescription: "현재 Windows 로그인 시 자동으로 실행됩니다", startupDisabledDescription: "현재 자동 실행을 사용하지 않습니다",
    turnOn: "켜기", turnOff: "끄기", startupEnabled: "Windows 자동 실행을 켰습니다.", startupDisabled: "Windows 자동 실행을 껐습니다.", startupChangeFailed: "자동 실행 설정을 변경하지 못했습니다.",
    dataManagement: "데이터 관리", favoriteCleanupExclusion: "즐겨찾기에 등록된 항목은 자동 정리에서 포함되지 않음", stored: "보관 중", imageStorage: "사진 저장 용량",
    itemsCount: n => `${n.toLocaleString("ko-KR")}개`, kindsCount: (links, photos) => `링크 ${links.toLocaleString("ko-KR")} · 사진 ${photos.toLocaleString("ko-KR")}`,
    favoritesPreserved: n => `즐겨찾기 ${n.toLocaleString("ko-KR")}개 보존 중`, statisticsLoadFailed: "데이터 현황을 불러오지 못했습니다.",
    autoFavorite: "자동 즐겨찾기", autoFavoriteDescription: "같은 링크나 사진을 반복해서 사용하면 즐겨찾기에 추가합니다", autoFavoriteOff: "사용 안 함", autoFavoriteCount: n => `${n}회 반복 사용 후 추가`,
    autoFavoriteDisabled: "자동 즐겨찾기를 사용하지 않습니다.", autoFavoriteSaved: n => `${n}회 반복 사용하면 자동으로 즐겨찾기에 추가합니다.`, autoFavoriteSaveFailed: "자동 즐겨찾기 설정을 저장하지 못했습니다.",
    autoCleanup: "자동 정리", autoCleanupDefault: "기본값 사용 안 함", cleanupOff: "자동 정리 사용 안 함", cleanup7: "7일 기준으로 정리", cleanup30: "30일 기준으로 정리", cleanup90: "90일 기준으로 정리", cleanup180: "180일 기준으로 정리",
    autoCleanupDisabled: "자동 정리를 사용하지 않습니다.", autoCleanupSaved: n => `${n}일 기준 자동 정리를 저장했습니다.`, autoCleanupSaveFailed: "자동 정리 설정을 저장하지 못했습니다.", saveSettings: "설정 저장",
    openDataFolder: "데이터 폴더 열기", openDataFolderFailed: "데이터 폴더를 열지 못했습니다.", deleteNonFavorites: "즐겨찾기 제외 항목 모두 삭제", allNonFavoriteItems: "즐겨찾기가 아닌 모든 항목",
    nothingToCleanup: "정리할 항목이 없습니다.", cleanupHeading: "항목을 정리할까요?", cleanupMessage: (total, links, photos, size) => `즐겨찾기가 아닌 모든 항목 ${total.toLocaleString("ko-KR")}개를 삭제할까요?\n\n링크 ${links.toLocaleString("ko-KR")}개 · 사진 ${photos.toLocaleString("ko-KR")}개 (${size})\n즐겨찾기는 삭제되지 않습니다.`,
    deleteAll: "모두 삭제", cleanupCancelled: "정리를 취소했습니다.", cleanupComplete: n => `${n.toLocaleString("ko-KR")}개 항목을 정리했습니다.`, cleanupPartial: n => `${n.toLocaleString("ko-KR")}개를 정리했지만 일부 사진 파일은 다음 실행 때 다시 정리합니다.`, cleanupFailed: "데이터를 정리하지 못했습니다.", checkingCleanup: "삭제 대상을 확인하고 있습니다...",
    appInfo: "앱 정보", version: value => `버전 ${value}`, developmentVersion: "개발 버전", checkForUpdates: "수동 업데이트 확인", checkForUpdatesDescription: "자동 확인 대기 시간과 관계없이 새 버전을 확인합니다", checkNow: "지금 확인", checkingForUpdates: "업데이트를 확인하고 있습니다.", appIsUpToDate: "현재 최신 버전을 사용하고 있습니다.", updateReady: version => `${version} 업데이트를 설치할 수 있습니다.`, updateCheckFailed: "업데이트를 확인하지 못했습니다. 네트워크 연결을 확인해 주세요.",
    copyrightNotice: "Copyright © 2026 NudeNyang", licenseSummary: "GNU GPL v3에 따라 이용 가능", viewLicense: "라이선스 보기", licenseHeading: "라이선스 및 제3자 고지", licenseDescription: "Sentory의 배포 조건과 포함된 오픈소스 구성 요소", openLibrary: "보관함 열기", pauseDetection: "감지 일시정지", resumeDetection: "감지 다시 시작", discordAutoConnect: "Discord 자동 연결", discordReconnect: "Discord 재시작 후 연결", exitSentory: "Sentory 종료",
    trayDetecting: "Sentory - 메신저 감지 중", trayPaused: "Sentory - 감지 일시정지됨", trayDetectionOff: "Sentory - 메신저 감지 꺼짐", trayStatus: status => `상태: ${status}`, doubleClick: "더블클릭", accessibilityMode: "접근성 모드로 시작",
    discordNotRunning: "Discord 미실행", discordRecoveryIssue: "Discord 연결 복구가 필요합니다. 설정에서 다시 연결해 주세요.", discordRepairFailed: "Discord 연결을 복구하지 못했습니다. Discord를 종료한 뒤 다시 시도해 주세요.", captureIssue: "일부 입력을 처리하지 못했습니다. 감지는 계속됩니다.", favoriteChangeFailed: "즐겨찾기를 변경하지 못했습니다.", copyFailedShort: "복사 실패", copyHistorySaveFailed: "복사했지만 사용 기록을 저장하지 못했습니다.", openOriginalFailed: "원본을 열지 못했습니다.", deleteSelectedFailed: "선택한 항목을 삭제하지 못했습니다.",
    themeApplied: mode => mode === "Dark" ? "다크 모드를 적용했습니다." : mode === "System" ? "시스템 테마 모드를 적용했습니다." : "라이트 모드를 적용했습니다.", themeSaveFailed: "테마 설정을 저장하지 못했습니다.", languageApplied: "언어를 변경했습니다.", languageSaveFailed: "언어 설정을 저장하지 못했습니다.",
    sourceEnabled: source => `${source === "KakaoTalk" ? "카카오톡" : source === "Line" ? "LINE" : source} 감지를 켰습니다.`, sourceDisabled: source => `${source === "KakaoTalk" ? "카카오톡" : source === "Line" ? "LINE" : source} 감지를 껐습니다.`, sourceSettingFailed: source => `${source === "KakaoTalk" ? "카카오톡" : source === "Line" ? "LINE" : source} 감지 설정을 저장하지 못했습니다.`,
  },
  "en-US": {
    tagline: "Moments scattered across your conversations, all in one place", all: "All", link: "Links", photo: "Photos", image: "Photo", typeLink: "Link", collection: "Collection", favorite: "Favorites",
    filter: "Filter", reset: "Reset", source: "Messenger", period: "Period", allPeriod: "All time", today: "Today", last7: "Last 7 days", last30: "Last 30 days",
    select: "Select", selectExit: "Done", settings: "Settings", search: "Search title, URL, or domain", switchToDark: "Switch to dark theme", switchToLight: "Switch to light theme", newest: "Newest",
    oldest: "Oldest", mostCaptured: "Most saved", mostCopied: "Most copied", recentlyCopied: "Recently copied", name: "Name", sortLabel: value => `Sort: ${value}`,
    general: "General", messenger: "Messenger detection", settingsDescription: "Manage Sentory, messenger detection, and saved data in one place",
    theme: "Theme", themeDescription: "Choose light or dark mode", language: "Language", languageDescription: "Choose the display language",
    light: "Light mode", dark: "Dark mode", system: "System theme", auto: "Auto", korean: "한국어", detected: "Ready to detect", disabled: "Off", disabledSource: source => `${source} detection is disabled`,
    connecting: "Preparing connection", recovering: "Recovering worker", reconnect: "Discord reconnect required", repair: "Reconnect", detectionPaused: "Detection paused", discordDetection: "Discord detection",
    copy: "Copy", copyClipboard: "Copy to clipboard", copied: "Copied", photoCopied: "Photo copied.", urlCopied: "URL copied.", collectionCopied: "Collection copied to the clipboard.", addFavorite: "Added to favorites.", removeFavorite: "Removed from favorites.",
    favoriteAddAction: "Add to favorites", favoriteRemoveAction: "Remove from favorites", savedOnInput: "Saved on paste", savedOnSend: "Saved on send", copyCount: n => `Copied ${n} times`, selectedCount: n => `${n} selected`,
    visibleSelect: "Select all", clearSelection: "Clear selection", deleteSelected: "Delete selected", emptyFiltered: "No results found", emptyFilteredDescription: "Try another search term or filter.",
    empty: "Nothing saved yet", emptyDescription: "Paste a URL or photo into a messenger.", items: n => `${n.toLocaleString("en-US")} items`, loading: "Loading library", loadFailed: "Could not load the library",
    close: "Dismiss notification", detail: "Sentory Item Details", favoriteMarked: "★ Favorite", captureCount: "Times saved", copyCountLabel: "Times copied", messageSource: "Latest source", savedAt: "Last saved", photos: "Photos", collectionLinks: "Links", previousPhoto: "Previous photo", nextPhoto: "Next photo", copyCurrentPhoto: "Copy current photo", previousLink: "Previous link", nextLink: "Next link", collectionItems: n => `${n.toLocaleString("en-US")} items`, collectionTitle: (photos, links) => `${photos.toLocaleString("en-US")} photos · ${links.toLocaleString("en-US")} links`, times: n => `${n.toLocaleString("en-US")}`, openPhoto: "Open photo", openLink: "Open link", copyPhoto: "Copy photo", copyUrl: "Copy URL", copyCollection: "Copy collection",
    delete: "Delete", openPreview: "Open original", openOriginal: "Open original", openOriginalFolder: "Open containing folder", openOriginalLink: "Open original link", cancel: "Cancel", deleteQuestion: n => n === 1 ? "Delete this item?" : `Delete ${n} selected items?`,
    deleteWarning: n => n === 1 ? "This item will be removed from the library.\nThis cannot be undone." : "The selected items and saved photo files will be removed from the library.\nThis cannot be undone.", deleted: n => `Deleted ${n} items.`, repairQuestion: "Reconnect Discord?",
    repairWarning: "Discord will restart in accessibility mode. Draft messages and active calls may be ended.", restart: "Restart", repairing: "Recovering worker",
    repaired: "Discord restarted in connection recovery mode.", settingsFailed: "Could not load settings.", galleryRefreshing: "Loading library",
    discordPhotoSaved: "Saved a photo confirmed as sent in Discord.", discordUrlSaved: "Saved a URL confirmed as sent in Discord.", discordUrlsSaved: n => `Saved ${n.toLocaleString("en-US")} URLs confirmed as sent in Discord.`, discordCollectionSaved: "Saved multiple Discord items as one collection.",
    inputPhotoSaved: "Saved the photo when pasted.", inputUrlSaved: "Saved the URL when pasted.", inputUrlsSaved: n => `Saved ${n.toLocaleString("en-US")} URLs when pasted.`, inputCollectionSaved: "Saved multiple pasted items as one collection.",
    enginePreparing: "Starting...", engineRecovering: "Recovering worker",
    engineFailed: "Could not recover the C# engine connection.", itemNotFound: "Item not found.",
    windowsStartup: "Start with Windows", startupEnabledDescription: "Currently starts when you sign in to Windows", startupDisabledDescription: "Automatic startup is currently off",
    turnOn: "Turn on", turnOff: "Turn off", startupEnabled: "Start with Windows is on.", startupDisabled: "Start with Windows is off.", startupChangeFailed: "Could not change the startup setting.",
    dataManagement: "Data management", favoriteCleanupExclusion: "Favorites are excluded from automatic cleanup", stored: "Stored", imageStorage: "Photo storage",
    itemsCount: n => `${n.toLocaleString("en-US")} items`, kindsCount: (links, photos) => `Links ${links.toLocaleString("en-US")} · Photos ${photos.toLocaleString("en-US")}`,
    favoritesPreserved: n => `${n.toLocaleString("en-US")} favorites preserved`, statisticsLoadFailed: "Could not load storage statistics.",
    autoFavorite: "Automatic favorites", autoFavoriteDescription: "Add a link or photo to favorites after repeated use", autoFavoriteOff: "Disabled", autoFavoriteCount: n => `Add after ${n} repeated uses`,
    autoFavoriteDisabled: "Automatic favorites are disabled.", autoFavoriteSaved: n => `Items will be added to favorites after ${n} repeated uses.`, autoFavoriteSaveFailed: "Could not save the automatic favorites setting.",
    autoCleanup: "Automatic cleanup", autoCleanupDefault: "Disabled by default", cleanupOff: "Do not clean automatically", cleanup7: "Delete after 7 days", cleanup30: "Delete after 30 days", cleanup90: "Delete after 90 days", cleanup180: "Delete after 180 days",
    autoCleanupDisabled: "Automatic cleanup is disabled.", autoCleanupSaved: n => `Automatic cleanup after ${n} days was saved.`, autoCleanupSaveFailed: "Could not save automatic cleanup.", saveSettings: "Save",
    openDataFolder: "Open data folder", openDataFolderFailed: "Could not open the data folder.", deleteNonFavorites: "Delete all except favorites", allNonFavoriteItems: "all non-favorite items",
    nothingToCleanup: "There is nothing to clean up.", cleanupHeading: "Clean up items?", cleanupMessage: (total, links, photos, size) => `Delete ${total.toLocaleString("en-US")} all non-favorite items?\n\nLinks ${links.toLocaleString("en-US")} · Photos ${photos.toLocaleString("en-US")} (${size})\nFavorites will not be deleted.`,
    deleteAll: "Delete all", cleanupCancelled: "Cleanup cancelled.", cleanupComplete: n => `Cleaned up ${n.toLocaleString("en-US")} items.`, cleanupPartial: n => `Cleaned up ${n.toLocaleString("en-US")} items; some photo files will be retried next time.`, cleanupFailed: "Could not clean up the data.", checkingCleanup: "Checking items to delete...",
    appInfo: "About", version: value => `Version ${value}`, developmentVersion: "Development build", checkForUpdates: "Manual update check", checkForUpdatesDescription: "Check for a new version without waiting for the automatic interval", checkNow: "Check now", checkingForUpdates: "Checking for updates.", appIsUpToDate: "Sentory is up to date.", updateReady: version => `Update ${version} is ready to install.`, updateCheckFailed: "Could not check for updates. Check your network connection.",
    copyrightNotice: "Copyright © 2026 NudeNyang", licenseSummary: "Licensed under GNU GPL v3", viewLicense: "View license", licenseHeading: "Licenses and third-party notices", licenseDescription: "Distribution terms and included open-source components", openLibrary: "Open library", pauseDetection: "Pause detection", resumeDetection: "Resume detection", discordAutoConnect: "Discord auto-connect", discordReconnect: "Restart and reconnect Discord", exitSentory: "Exit Sentory",
    trayDetecting: "Sentory - Detecting messengers", trayPaused: "Sentory - Detection paused", trayDetectionOff: "Sentory - Detection off", trayStatus: status => `Status: ${status}`, doubleClick: "Double-click", accessibilityMode: "Starts in accessibility mode",
    discordNotRunning: "Discord is not running", discordRecoveryIssue: "Discord needs to be reconnected. Reconnect it in Settings.", discordRepairFailed: "Could not repair the Discord connection. Exit Discord and try again.", captureIssue: "Some input could not be processed. Detection is continuing.", favoriteChangeFailed: "Could not update favorites.", copyFailedShort: "Copy failed", copyHistorySaveFailed: "Copied, but the usage history could not be saved.", openOriginalFailed: "Could not open the original.", deleteSelectedFailed: "Could not delete the selected items.",
    themeApplied: mode => mode === "Dark" ? "Dark mode applied." : mode === "System" ? "System theme mode applied." : "Light mode applied.", themeSaveFailed: "Could not save the theme setting.", languageApplied: "Language changed.", languageSaveFailed: "Could not save the language setting.",
    sourceEnabled: source => `${source} detection is on.`, sourceDisabled: source => `${source} detection is off.`, sourceSettingFailed: source => `Could not save the ${source} detection setting.`,
  },
};
TRANSLATIONS["ja-JP"] = {
  ...TRANSLATIONS["en-US"],
  tagline: "会話に散らばる瞬間を、一か所に", all: "すべて", link: "リンク", photo: "写真", image: "写真", typeLink: "リンク", collection: "まとめ", favorite: "お気に入り",
  filter: "フィルター", reset: "リセット", source: "メッセンジャー", period: "期間", allPeriod: "全期間", today: "今日", last7: "過去7日", last30: "過去30日",
  select: "選択", selectExit: "選択終了", settings: "設定", search: "タイトル、URL、ドメインを検索", switchToDark: "ダークテーマに切り替え", switchToLight: "ライトテーマに切り替え", newest: "新しい順", oldest: "古い順", mostCaptured: "保存回数順", mostCopied: "コピー回数順", recentlyCopied: "最近コピーした順", name: "名前順", sortLabel: value => `並べ替え: ${value}`,
  general: "一般", messenger: "メッセンジャー検出", settingsDescription: "Sentory の動作、メッセンジャー検出、保存データを一か所で管理します",
  language: "Language", languageDescription: "表示する言語を選択します", theme: "画面テーマ", themeDescription: "ライトモードとダークモードを選択します", light: "ライトモード", dark: "ダークモード", system: "システムテーマ", auto: "Auto",
  detected: "検出準備完了", disabled: "使用しない", disabledSource: source => `${source === "KakaoTalk" ? "カカオトーク" : source} 検出を使用していません`, detectionPaused: "検出一時停止中", connecting: "接続準備中", recovering: "ワーカーを復旧中", reconnect: "Discord の再接続が必要", repair: "再接続", discordDetection: "Discord 検出",
  savedOnInput: "入力時に保存", savedOnSend: "送信時に保存", photoCopied: "写真をコピーしました。", urlCopied: "URL をコピーしました。", collectionCopied: "まとめた項目をクリップボードにコピーしました。", addFavorite: "お気に入りに追加しました。", removeFavorite: "お気に入りから削除しました。", favoriteAddAction: "お気に入りに追加", favoriteRemoveAction: "お気に入りから削除",
  selectedCount: n => `${n.toLocaleString("ja-JP")}件選択`, visibleSelect: "すべて選択", clearSelection: "選択を解除", deleteSelected: "選択項目を削除", emptyFiltered: "検索結果がありません", emptyFilteredDescription: "別の検索語やフィルターをお試しください。", empty: "まだ保存された項目はありません", emptyDescription: "メッセンジャーに URL や写真を貼り付けてみてください。", loading: "ライブラリを読み込み中", loadFailed: "ライブラリを読み込めませんでした",
  close: "通知を閉じる", detail: "Sentory 項目の詳細", favoriteMarked: "★ お気に入り", captureCount: "保存回数", copyCountLabel: "コピー回数", messageSource: "最後の送信元", savedAt: "最終保存", photos: "写真", collectionLinks: "リンク", previousPhoto: "前の写真", nextPhoto: "次の写真", copyCurrentPhoto: "現在の写真をコピー", previousLink: "前のリンク", nextLink: "次のリンク", collectionItems: n => `${n.toLocaleString("ja-JP")}件`, collectionTitle: (photos, links) => `写真 ${photos.toLocaleString("ja-JP")}件・リンク ${links.toLocaleString("ja-JP")}件`, times: n => `${n.toLocaleString("ja-JP")}回`, openPhoto: "写真を開く", openLink: "リンクを開く", openPreview: "元をすぐ開く", copyPhoto: "写真をコピー", copyUrl: "URL をコピー", copyCollection: "まとめてコピー", delete: "削除", openOriginal: "元を開く", openOriginalFolder: "元のフォルダーを開く", openOriginalLink: "元のリンクを開く", cancel: "キャンセル",
  deleteQuestion: n => n === 1 ? "この項目を削除しますか？" : `選択した ${n.toLocaleString("ja-JP")}件を削除しますか？`, deleteWarning: n => n === 1 ? "この項目をライブラリから削除します。\nこの操作は元に戻せません。" : "選択した項目と保存された写真ファイルをライブラリから削除します。\nこの操作は元に戻せません。", deleted: n => `${n.toLocaleString("ja-JP")}件を削除しました。`,
  repairQuestion: "Discord を再接続しますか？", repairWarning: "Discord をアクセシビリティモードで再起動します。作成中のメッセージや通話が終了する場合があります。", restart: "再起動", repaired: "Discord を接続復旧モードで再起動しました。",
  discordPhotoSaved: "Discord で写真の送信を確認して保存しました。", discordUrlSaved: "Discord で URL の送信を確認して保存しました。", discordUrlsSaved: n => `Discord で URL ${n.toLocaleString("ja-JP")}件の送信を確認して保存しました。`, discordCollectionSaved: "Discord の複数項目を1つのまとめとして保存しました。",
  inputPhotoSaved: "写真を入力時に保存しました。", inputUrlSaved: "URL を入力時に保存しました。", inputUrlsSaved: n => `URL ${n.toLocaleString("ja-JP")}件を入力時に保存しました。`, inputCollectionSaved: "複数の入力項目を1つのまとめとして保存しました。",
  windowsStartup: "Windows 起動時に実行", startupEnabledDescription: "現在 Windows サインイン時に自動実行されます", startupDisabledDescription: "現在、自動起動は使用していません", turnOn: "オン", turnOff: "オフ", startupEnabled: "Windows 自動起動をオンにしました。", startupDisabled: "Windows 自動起動をオフにしました。", startupChangeFailed: "自動起動設定を変更できませんでした。",
  dataManagement: "データ管理", favoriteCleanupExclusion: "お気に入りは自動整理の対象外です", stored: "保存中", imageStorage: "写真の保存容量", itemsCount: n => `${n.toLocaleString("ja-JP")}件`, kindsCount: (links, photos) => `リンク ${links.toLocaleString("ja-JP")} · 写真 ${photos.toLocaleString("ja-JP")}`, favoritesPreserved: n => `お気に入り ${n.toLocaleString("ja-JP")}件を保持`, statisticsLoadFailed: "データの状況を読み込めませんでした。",
  autoFavorite: "自動お気に入り", autoFavoriteDescription: "同じリンクや写真を繰り返し使用するとお気に入りに追加します", autoFavoriteOff: "使用しない", autoFavoriteCount: n => `${n}回の繰り返し使用後に追加`, autoFavoriteDisabled: "自動お気に入りを使用しません。", autoFavoriteSaved: n => `${n}回繰り返し使用すると自動的にお気に入りへ追加します。`, autoFavoriteSaveFailed: "自動お気に入りの設定を保存できませんでした。",
  autoCleanup: "自動整理", autoCleanupDefault: "初期設定では使用しません", cleanupOff: "自動整理を使用しない", cleanup7: "7日を基準に整理", cleanup30: "30日を基準に整理", cleanup90: "90日を基準に整理", cleanup180: "180日を基準に整理", autoCleanupDisabled: "自動整理を使用しません。", autoCleanupSaved: n => `${n}日基準の自動整理を保存しました。`, autoCleanupSaveFailed: "自動整理設定を保存できませんでした。", saveSettings: "設定を保存",
  openDataFolder: "データフォルダーを開く", openDataFolderFailed: "データフォルダーを開けませんでした。", deleteNonFavorites: "お気に入り以外をすべて削除", allNonFavoriteItems: "お気に入り以外のすべての項目", nothingToCleanup: "整理する項目はありません。", cleanupHeading: "項目を整理しますか？", cleanupMessage: (total, links, photos, size) => `お気に入り以外のすべての項目 ${total.toLocaleString("ja-JP")}件を削除しますか？\n\nリンク ${links.toLocaleString("ja-JP")}件 · 写真 ${photos.toLocaleString("ja-JP")}件 (${size})\nお気に入りは削除されません。`, deleteAll: "すべて削除", cleanupCancelled: "整理をキャンセルしました。", cleanupComplete: n => `${n.toLocaleString("ja-JP")}件を整理しました。`, cleanupPartial: n => `${n.toLocaleString("ja-JP")}件を整理しましたが、一部の写真ファイルは次回再試行します。`, cleanupFailed: "データを整理できませんでした。", checkingCleanup: "削除対象を確認しています...",
  appInfo: "アプリ情報", version: value => `バージョン ${value}`, developmentVersion: "開発版", checkForUpdates: "手動アップデート確認", checkForUpdatesDescription: "自動確認の待機時間に関係なく新しいバージョンを確認します", checkNow: "今すぐ確認", checkingForUpdates: "アップデートを確認しています。", appIsUpToDate: "現在、最新バージョンを使用しています。", updateReady: version => `${version} アップデートをインストールできます。`, updateCheckFailed: "アップデートを確認できませんでした。ネットワーク接続を確認してください。", copyrightNotice: "Copyright © 2026 NudeNyang", licenseSummary: "GNU GPL v3 に基づいて利用できます", viewLicense: "ライセンスを見る", licenseHeading: "ライセンスと第三者表記", licenseDescription: "Sentory の配布条件と同梱オープンソース構成要素", openLibrary: "ライブラリを開く", pauseDetection: "検出を一時停止", resumeDetection: "検出を再開", discordAutoConnect: "Discord 自動接続", discordReconnect: "Discord を再起動して接続", exitSentory: "Sentory を終了",
  trayDetecting: "Sentory - メッセンジャー検出中", trayPaused: "Sentory - 検出一時停止", trayDetectionOff: "Sentory - メッセンジャー検出オフ", trayStatus: status => `状態: ${status}`, doubleClick: "ダブルクリック", accessibilityMode: "アクセシビリティモードで開始", discordNotRunning: "Discord は実行されていません", discordRecoveryIssue: "Discord の接続復旧が必要です。設定から再接続してください。", discordRepairFailed: "Discord 接続を復旧できませんでした。Discord を終了して再試行してください。", captureIssue: "一部の入力を処理できませんでした。検出は継続しています。", favoriteChangeFailed: "お気に入りを変更できませんでした。", copyFailedShort: "コピー失敗", copyHistorySaveFailed: "コピーしましたが、使用履歴を保存できませんでした。", openOriginalFailed: "元のデータを開けませんでした。", deleteSelectedFailed: "選択した項目を削除できませんでした。",
  themeApplied: mode => mode === "Dark" ? "ダークモードを適用しました。" : mode === "System" ? "システムテーマモードを適用しました。" : "ライトモードを適用しました。", themeSaveFailed: "テーマ設定を保存できませんでした。", languageApplied: "言語を変更しました。", languageSaveFailed: "言語設定を保存できませんでした。", sourceEnabled: source => `${source === "KakaoTalk" ? "カカオトーク" : source} 検出をオンにしました。`, sourceDisabled: source => `${source === "KakaoTalk" ? "カカオトーク" : source} 検出をオフにしました。`, sourceSettingFailed: source => `${source === "KakaoTalk" ? "カカオトーク" : source} 検出設定を保存できませんでした。`,
};
TRANSLATIONS["zh-CN"] = {
  ...TRANSLATIONS["en-US"],
  tagline: "将散落在对话中的瞬间汇聚一处", all: "全部", link: "链接", photo: "图片", image: "图片", typeLink: "链接", collection: "组合", favorite: "收藏",
  filter: "筛选", reset: "重置", source: "聊天应用", period: "时间范围", allPeriod: "全部时间", today: "今天", last7: "最近 7 天", last30: "最近 30 天",
  select: "选择", selectExit: "完成选择", settings: "设置", search: "搜索标题、URL 或域名", switchToDark: "切换到深色主题", switchToLight: "切换到浅色主题", newest: "最新优先", oldest: "最早优先", mostCaptured: "保存次数最多", mostCopied: "复制次数最多", recentlyCopied: "最近复制", name: "按名称", sortLabel: value => `排序：${value}`,
  general: "常规", messenger: "聊天应用检测", settingsDescription: "在一个位置管理 Sentory、聊天应用检测和保存的数据",
  language: "Language", languageDescription: "选择界面显示语言", theme: "界面主题", themeDescription: "选择浅色或深色模式", light: "浅色模式", dark: "深色模式", system: "系统主题", auto: "Auto",
  detected: "检测已就绪", disabled: "未使用", disabledSource: source => `${source === "WeChat" ? "微信" : source} 检测已关闭`, detectionPaused: "检测已暂停", connecting: "正在准备连接", recovering: "正在恢复工作进程", reconnect: "需要重新连接 Discord", repair: "重新连接", discordDetection: "Discord 检测",
  savedOnInput: "粘贴时保存", savedOnSend: "发送时保存", photoCopied: "图片已复制。", urlCopied: "URL 已复制。", collectionCopied: "组合项目已复制到剪贴板。", addFavorite: "已添加到收藏。", removeFavorite: "已从收藏中移除。", favoriteAddAction: "添加到收藏", favoriteRemoveAction: "从收藏中移除",
  selectedCount: n => `已选择 ${n.toLocaleString("zh-CN")} 项`, visibleSelect: "全选", clearSelection: "取消选择", deleteSelected: "删除所选项目", emptyFiltered: "没有搜索结果", emptyFilteredDescription: "请尝试其他关键词或筛选条件。", empty: "尚未保存任何项目", emptyDescription: "请在聊天应用中粘贴链接或图片。", loading: "正在加载收藏库", loadFailed: "无法加载收藏库",
  close: "关闭通知", detail: "Sentory 项目详情", favoriteMarked: "★ 已收藏", captureCount: "保存次数", copyCountLabel: "复制次数", messageSource: "最近来源", savedAt: "最后保存", photos: "图片", collectionLinks: "链接", previousPhoto: "上一张图片", nextPhoto: "下一张图片", copyCurrentPhoto: "复制当前图片", previousLink: "上一个链接", nextLink: "下一个链接", collectionItems: n => `${n.toLocaleString("zh-CN")} 项`, collectionTitle: (photos, links) => `${photos.toLocaleString("zh-CN")} 张图片 · ${links.toLocaleString("zh-CN")} 个链接`, times: n => `${n.toLocaleString("zh-CN")} 次`, openPhoto: "打开图片", openLink: "打开链接", openPreview: "直接打开原文件", copyPhoto: "复制图片", copyUrl: "复制 URL", copyCollection: "复制组合", delete: "删除", openOriginal: "打开原文件", openOriginalFolder: "打开原文件所在文件夹", openOriginalLink: "打开原链接", cancel: "取消",
  deleteQuestion: n => n === 1 ? "要删除此项目吗？" : `要删除所选的 ${n.toLocaleString("zh-CN")} 个项目吗？`, deleteWarning: n => n === 1 ? "将从收藏库中删除此项目。\n此操作无法撤销。" : "将从收藏库中删除所选项目及保存的图片文件。\n此操作无法撤销。", deleted: n => `已删除 ${n.toLocaleString("zh-CN")} 个项目。`,
  repairQuestion: "要重新连接 Discord 吗？", repairWarning: "Discord 将以无障碍模式重启。正在编辑的消息和通话可能会结束。", restart: "重新启动", repaired: "Discord 已以连接恢复模式重新启动。",
  discordPhotoSaved: "已保存经确认在 Discord 中发送的图片。", discordUrlSaved: "已保存经确认在 Discord 中发送的 URL。", discordUrlsSaved: n => `已保存 ${n.toLocaleString("zh-CN")} 个经确认在 Discord 中发送的 URL。`, discordCollectionSaved: "已将 Discord 中发送的多个项目保存为一个组合。",
  inputPhotoSaved: "已在粘贴图片时保存。", inputUrlSaved: "已在粘贴 URL 时保存。", inputUrlsSaved: n => `已在粘贴时保存 ${n.toLocaleString("zh-CN")} 个 URL。`, inputCollectionSaved: "已将粘贴的多个项目保存为一个组合。",
  windowsStartup: "Windows 启动时运行", startupEnabledDescription: "当前会在登录 Windows 时自动运行", startupDisabledDescription: "当前未使用自动启动", turnOn: "开启", turnOff: "关闭", startupEnabled: "已开启 Windows 自动启动。", startupDisabled: "已关闭 Windows 自动启动。", startupChangeFailed: "无法更改自动启动设置。",
  dataManagement: "数据管理", favoriteCleanupExclusion: "收藏项目不会被自动清理", stored: "已保存", imageStorage: "图片存储空间", itemsCount: n => `${n.toLocaleString("zh-CN")} 项`, kindsCount: (links, photos) => `链接 ${links.toLocaleString("zh-CN")} · 图片 ${photos.toLocaleString("zh-CN")}`, favoritesPreserved: n => `保留 ${n.toLocaleString("zh-CN")} 个收藏项目`, statisticsLoadFailed: "无法加载数据统计。",
  autoFavorite: "自动收藏", autoFavoriteDescription: "同一链接或图片被重复使用后自动收藏", autoFavoriteOff: "不使用", autoFavoriteCount: n => `重复使用 ${n} 次后收藏`, autoFavoriteDisabled: "已关闭自动收藏。", autoFavoriteSaved: n => `重复使用 ${n} 次后将自动添加到收藏。`, autoFavoriteSaveFailed: "无法保存自动收藏设置。",
  autoCleanup: "自动清理", autoCleanupDefault: "默认关闭", cleanupOff: "不使用自动清理", cleanup7: "清理超过 7 天的项目", cleanup30: "清理超过 30 天的项目", cleanup90: "清理超过 90 天的项目", cleanup180: "清理超过 180 天的项目", autoCleanupDisabled: "自动清理已关闭。", autoCleanupSaved: n => `已保存按 ${n} 天自动清理的设置。`, autoCleanupSaveFailed: "无法保存自动清理设置。", saveSettings: "保存设置",
  openDataFolder: "打开数据文件夹", openDataFolderFailed: "无法打开数据文件夹。", deleteNonFavorites: "删除除收藏外的所有项目", allNonFavoriteItems: "所有非收藏项目", nothingToCleanup: "没有可清理的项目。", cleanupHeading: "要清理项目吗？", cleanupMessage: (total, links, photos, size) => `要删除 ${total.toLocaleString("zh-CN")} 个所有非收藏项目吗？\n\n链接 ${links.toLocaleString("zh-CN")} · 图片 ${photos.toLocaleString("zh-CN")}（${size}）\n收藏项目不会被删除。`, deleteAll: "全部删除", cleanupCancelled: "已取消清理。", cleanupComplete: n => `已清理 ${n.toLocaleString("zh-CN")} 个项目。`, cleanupPartial: n => `已清理 ${n.toLocaleString("zh-CN")} 个项目；部分图片文件将在下次启动时重试。`, cleanupFailed: "无法清理数据。", checkingCleanup: "正在检查要删除的项目…",
  appInfo: "应用信息", version: value => `版本 ${value}`, developmentVersion: "开发版本", checkForUpdates: "手动检查更新", checkForUpdatesDescription: "无需等待自动检查间隔即可检查新版本", checkNow: "立即检查", checkingForUpdates: "正在检查更新。", appIsUpToDate: "当前已是最新版本。", updateReady: version => `可以安装 ${version} 更新。`, updateCheckFailed: "无法检查更新，请检查网络连接。", copyrightNotice: "Copyright © 2026 NudeNyang", licenseSummary: "依据 GNU GPL v3 使用", viewLicense: "查看许可协议", licenseHeading: "许可证与第三方声明", licenseDescription: "Sentory 的分发条款及所含开源组件", openLibrary: "打开收藏库", pauseDetection: "暂停检测", resumeDetection: "恢复检测", discordAutoConnect: "Discord 自动连接", discordReconnect: "重启并重新连接 Discord", exitSentory: "退出 Sentory",
  trayDetecting: "Sentory - 正在检测聊天应用", trayPaused: "Sentory - 检测已暂停", trayDetectionOff: "Sentory - 聊天应用检测已关闭", trayStatus: status => `状态：${status}`, doubleClick: "双击", accessibilityMode: "以辅助功能模式启动", discordNotRunning: "Discord 未运行", discordRecoveryIssue: "需要恢复 Discord 连接。请在设置中重新连接。", discordRepairFailed: "无法恢复 Discord 连接。请退出 Discord 后重试。", captureIssue: "部分输入无法处理，检测仍在继续。", favoriteChangeFailed: "无法更新收藏。", copyFailedShort: "复制失败", copyHistorySaveFailed: "已复制，但无法保存使用记录。", openOriginalFailed: "无法打开原文件。", deleteSelectedFailed: "无法删除所选项目。",
  themeApplied: mode => mode === "Dark" ? "已应用深色模式。" : mode === "System" ? "已应用系统主题模式。" : "已应用浅色模式。", themeSaveFailed: "无法保存主题设置。", languageApplied: "语言已更改。", languageSaveFailed: "无法保存语言设置。", sourceEnabled: source => `已开启 ${source} 检测。`, sourceDisabled: source => `已关闭 ${source} 检测。`, sourceSettingFailed: source => `无法保存 ${source} 检测设置。`,
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
  detailPhotoIndex: 0,
  detailLinkIndex: 0,
  detailArtworkTarget: null,
  suppressCardClick: false,
  toastTimer: 0,
  settingsScrollTimer: 0,
  settings: null,
  runtimeStatus: null,
  startupEnabled: false,
  dataStatistics: null,
  locale: "ko-KR",
  settingsBusy: false,
  contextItemId: null,
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
const cardContextMenu = document.querySelector("#card-context-menu");
const contextFavorite = document.querySelector("#context-favorite");
const contextCopy = document.querySelector("#context-copy");
const contextReveal = document.querySelector("#context-reveal");
const contextDelete = document.querySelector("#context-delete");
const detailLayer = document.querySelector("#detail-layer");
const detailWindowTitle = document.querySelector("#detail-window-title");
const detailClose = document.querySelector("#detail-close");
const detailType = document.querySelector("#detail-type");
const detailFavoriteMark = document.querySelector("#detail-favorite-mark");
const detailTitle = document.querySelector("#detail-title");
const detailArtwork = document.querySelector("#detail-artwork");
const detailDescription = document.querySelector("#detail-description");
const detailPhotoSection = document.querySelector("#detail-photo-section");
const detailPhotoHeading = document.querySelector("#detail-photo-heading");
const detailPhotoName = document.querySelector("#detail-photo-name");
const detailPhotoCopy = document.querySelector("#detail-photo-copy");
const detailPhotoNavigation = document.querySelector("#detail-photo-navigation");
const detailPhotoPrevious = document.querySelector("#detail-photo-previous");
const detailPhotoNext = document.querySelector("#detail-photo-next");
const detailPhotoDots = document.querySelector("#detail-photo-dots");
const detailLinkSection = document.querySelector("#detail-link-section");
const detailLinkHeading = document.querySelector("#detail-link-heading");
const detailLinkValue = document.querySelector("#detail-link-value");
const detailLinkCopy = document.querySelector("#detail-link-copy");
const detailLinkNavigation = document.querySelector("#detail-link-navigation");
const detailLinkPrevious = document.querySelector("#detail-link-previous");
const detailLinkNext = document.querySelector("#detail-link-next");
const detailLinkDots = document.querySelector("#detail-link-dots");
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
const settingsScrollRegion = document.querySelector(".settings-scroll-region");
const settingsScroll = document.querySelector("#settings-scroll");
const settingsScrollThumb = document.querySelector("#settings-scroll-indicator .scroll-indicator-thumb");
const themeSetting = document.querySelector("#setting-theme");
const languageSetting = document.querySelector("#setting-language");
const settingsSources = document.querySelector("#settings-sources");
const startupDescription = document.querySelector("#startup-description");
const startupToggle = document.querySelector("#startup-toggle");
const autoFavoriteSelect = document.querySelector("#auto-favorite-select");
const autoFavoriteSave = document.querySelector("#auto-favorite-save");
const autoCleanupSelect = document.querySelector("#auto-cleanup-select");
const autoCleanupSave = document.querySelector("#auto-cleanup-save");
const openDataFolder = document.querySelector("#open-data-folder");
const deleteNonFavorites = document.querySelector("#delete-non-favorites");
const updateCheck = document.querySelector("#update-check");
const viewLicense = document.querySelector("#view-license");
const licenseLayer = document.querySelector("#license-layer");
const licenseClose = document.querySelector("#license-close");
const licenseHeading = document.querySelector("#license-heading");
const licenseDescription = document.querySelector("#license-description");
const licenseText = document.querySelector("#license-text");
const licenseScrollRegion = document.querySelector(".license-scroll-region");
const licenseScrollThumb = document.querySelector("#license-scroll-indicator .scroll-indicator-thumb");
const storedValue = document.querySelector("#stored-value");
const kindsValue = document.querySelector("#kinds-value");
const imageStorageValue = document.querySelector("#image-storage-value");
const favoritesValue = document.querySelector("#favorites-value");
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
  themeButton.title = t(dark ? "switchToLight" : "switchToDark");
  themeButton.setAttribute("aria-label", themeButton.title);
  themeSetting.value = mode || "Light";
  syncEnhancedSelect(themeSetting);
  void tauriCore().invoke("window_theme_set", { dark }).catch(() => {});
  void configureTray();
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

function refreshLocalizedVisibleCards() {
  const itemsById = new Map(state.items.filter(Boolean).map(item => [item.itemId, item]));
  for (const card of virtualSpace.querySelectorAll(".card[data-item-id]")) {
    const item = itemsById.get(card.dataset.itemId);
    if (!item) continue;
    card.setAttribute("aria-label", `${localizedType(item)}, ${item.title}, ${localizedDate(item.lastCapturedAt)}`);
    const type = card.querySelector(".card-meta strong");
    if (type) {
      for (const node of [...type.childNodes]) {
        if (node.nodeType === Node.TEXT_NODE) node.remove();
      }
      type.append(document.createTextNode(localizedType(item)));
    }
    const date = card.querySelector(".card-meta > span");
    if (date) date.textContent = localizedDate(item.lastCapturedAt);
    const chip = card.querySelector(".status-chip");
    if (chip) chip.textContent = localizedStatus(item);
    const usage = card.querySelector(".copy-usage");
    if (usage) usage.textContent = t("copyCount", item.copyCount);
    const badge = card.querySelector(".collection-badge");
    if (badge) badge.textContent = t("collectionItems", item.memberCount);
    const artwork = card.querySelector(".artwork");
    if (artwork) artwork.title = t("openPreview");
    const favorite = card.querySelector(".favorite");
    if (favorite) {
      favorite.title = item.isFavorite ? t("favoriteRemoveAction") : t("favoriteAddAction");
      favorite.setAttribute("aria-label", favorite.title);
    }
  }
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
  document.querySelector("#select-label").textContent = t(state.selectionMode ? "selectExit" : "select");
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
  document.querySelector("#startup-title").textContent = t("windowsStartup");
  document.querySelector("#data-heading").textContent = t("dataManagement");
  document.querySelector("#favorite-cleanup-note").textContent = t("favoriteCleanupExclusion");
  document.querySelector("#stored-label").textContent = t("stored");
  document.querySelector("#image-storage-label").textContent = t("imageStorage");
  document.querySelector("#auto-favorite-title").textContent = t("autoFavorite");
  document.querySelector("#auto-favorite-description").textContent = t("autoFavoriteDescription");
  document.querySelector("#auto-cleanup-title").textContent = t("autoCleanup");
  document.querySelector("#auto-cleanup-description").textContent = t("autoCleanupDefault");
  document.querySelector("#app-info-heading").textContent = t("appInfo");
  document.querySelector("#version-label").textContent = `${t("version", "2.0.0")} · for Developers`;
  document.querySelector("#update-title").textContent = t("checkForUpdates");
  document.querySelector("#update-description").textContent = t("checkForUpdatesDescription");
  document.querySelector("#copyright-label").textContent = t("copyrightNotice");
  document.querySelector("#license-summary").textContent = t("licenseSummary");
  viewLicense.textContent = t("viewLicense");
  licenseHeading.textContent = t("licenseHeading");
  licenseDescription.textContent = t("licenseDescription");
  autoFavoriteSave.textContent = t("saveSettings");
  autoCleanupSave.textContent = t("saveSettings");
  openDataFolder.textContent = t("openDataFolder");
  deleteNonFavorites.textContent = t("deleteNonFavorites");
  updateCheck.textContent = t("checkNow");
  themeSetting.options[0].textContent = t("light");
  themeSetting.options[1].textContent = t("dark");
  themeSetting.options[2].textContent = t("system");
  languageSetting.options[0].textContent = t("auto");
  languageSetting.options[1].textContent = t("korean");
  renderStartupState();
  renderDataOptions();
  renderDataStatistics();
  const sortKeys = ["newest", "oldest", "mostCaptured", "mostCopied", "recentlyCopied", "name"];
  [...sortMenu.querySelectorAll("button")].forEach((button, index) => { button.textContent = t(sortKeys[index]); });
  updateSortUi();
  selectVisibleButton.textContent = t("visibleSelect");
  clearSelectionButton.textContent = t("clearSelection");
  deleteSelectedButton.textContent = t("deleteSelected");
  detailClose.setAttribute("aria-label", t("close"));
  detailWindowTitle.textContent = t("detail");
  detailFavoriteMark.textContent = t("favoriteMarked");
  detailPhotoHeading.textContent = t("photos");
  detailPhotoCopy.title = t("copyCurrentPhoto");
  detailPhotoCopy.setAttribute("aria-label", t("copyCurrentPhoto"));
  detailPhotoPrevious.title = t("previousPhoto");
  detailPhotoPrevious.setAttribute("aria-label", t("previousPhoto"));
  detailPhotoNext.title = t("nextPhoto");
  detailPhotoNext.setAttribute("aria-label", t("nextPhoto"));
  detailLinkHeading.textContent = t("collectionLinks");
  detailLinkCopy.title = t("copyUrl");
  detailLinkCopy.setAttribute("aria-label", t("copyUrl"));
  detailLinkPrevious.title = t("previousLink");
  detailLinkPrevious.setAttribute("aria-label", t("previousLink"));
  detailLinkNext.title = t("nextLink");
  detailLinkNext.setAttribute("aria-label", t("nextLink"));
  detailCaptureCount.closest("div").querySelector("dt").textContent = t("captureCount");
  detailCopyCount.closest("div").querySelector("dt").textContent = t("copyCountLabel");
  detailSource.closest("div").querySelector("dt").textContent = t("messageSource");
  detailDate.closest("div").querySelector("dt").textContent = t("savedAt");
  detailDelete.textContent = t("delete");
  detailOpen.textContent = t("openOriginal");
  detailCopy.textContent = t("copy");
  confirmCancel.textContent = t("cancel");
  refreshCardContextMenu();
  settingsClose.setAttribute("aria-label", t("close"));
  updateSelectionUi();
  renderSourceSettings();
  applyRuntimeStatus(state.runtimeStatus);
  applyThemeMode(state.settings?.themeMode || "Light");
  refreshLocalizedVisibleCards();
  syncAllEnhancedSelects();
  if (state.detailItem && !detailLayer.hidden) populateDetails(state.detailItem);
  void configureTray();
}

const enhancedSelects = new WeakMap();

function enhanceSelect(select) {
  if (enhancedSelects.has(select)) return;
  const wrapper = document.createElement("div");
  wrapper.className = "wpf-select";
  const trigger = document.createElement("button");
  trigger.className = "wpf-select-trigger";
  trigger.type = "button";
  trigger.setAttribute("aria-haspopup", "listbox");
  trigger.setAttribute("aria-expanded", "false");
  const value = document.createElement("span");
  const chevron = document.createElement("span");
  chevron.className = "fluent";
  chevron.setAttribute("aria-hidden", "true");
  chevron.innerHTML = "&#xE70D;";
  trigger.append(value, chevron);
  const popup = document.createElement("div");
  popup.className = "wpf-select-popup";
  popup.role = "listbox";
  popup.hidden = true;
  const labelledBy = select.getAttribute("aria-labelledby");
  if (labelledBy) trigger.setAttribute("aria-labelledby", labelledBy);
  select.before(wrapper);
  wrapper.append(select, trigger, popup);
  select.classList.add("native-select-control");
  const close = () => {
    wrapper.classList.remove("open");
    popup.hidden = true;
    trigger.setAttribute("aria-expanded", "false");
  };
  const open = () => {
    for (const other of document.querySelectorAll(".wpf-select.open")) {
      if (other !== wrapper) other.querySelector(".wpf-select-trigger")?.click();
    }
    wrapper.classList.add("open");
    popup.hidden = false;
    trigger.setAttribute("aria-expanded", "true");
    popup.querySelector(".selected")?.focus();
  };
  trigger.addEventListener("click", () => popup.hidden ? open() : close());
  trigger.addEventListener("keydown", event => {
    if (["ArrowDown", "ArrowUp", "Enter", " "].includes(event.key)) {
      event.preventDefault();
      open();
    }
  });
  popup.addEventListener("keydown", event => {
    const options = [...popup.querySelectorAll("button")];
    const current = options.indexOf(document.activeElement);
    if (event.key === "Escape") {
      close();
      trigger.focus();
    } else if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      const delta = event.key === "ArrowDown" ? 1 : -1;
      options[(current + delta + options.length) % options.length]?.focus();
    }
  });
  document.addEventListener("pointerdown", event => {
    if (!wrapper.contains(event.target)) close();
  });
  select.addEventListener("change", () => syncEnhancedSelect(select));
  const observer = new MutationObserver(() => syncEnhancedSelect(select));
  observer.observe(select, { childList: true, subtree: true, characterData: true, attributes: true });
  enhancedSelects.set(select, { wrapper, trigger, value, popup, close });
  syncEnhancedSelect(select);
}

function syncEnhancedSelect(select) {
  const enhanced = enhancedSelects.get(select);
  if (!enhanced) return;
  const { value, popup, close } = enhanced;
  value.textContent = select.selectedOptions[0]?.textContent || "";
  const buttons = [...select.options].map(option => {
    const button = document.createElement("button");
    button.className = `wpf-select-option${option.selected ? " selected" : ""}`;
    button.type = "button";
    button.role = "option";
    button.setAttribute("aria-selected", String(option.selected));
    button.textContent = option.textContent;
    button.addEventListener("click", () => {
      select.value = option.value;
      close();
      select.dispatchEvent(new Event("change", { bubbles: true }));
      enhanced.trigger.focus();
    });
    return button;
  });
  popup.replaceChildren(...buttons);
}

function syncAllEnhancedSelects() {
  [themeSetting, languageSetting, autoFavoriteSelect, autoCleanupSelect].forEach(syncEnhancedSelect);
}

function applySettings(settings) {
  state.settings = settings;
  languageSetting.value = settings.language || "auto";
  applyLocalizedUi(settings.language || "auto");
  applyThemeMode(settings.themeMode || "Light");
  autoFavoriteSelect.value = settings.autoFavoriteEnabled
    ? String(settings.autoFavoriteCopyThreshold)
    : "0";
  autoCleanupSelect.value = String(settings.autoCleanupDays || 0);
  syncAllEnhancedSelects();
  void configureTray();
}

function renderStartupState() {
  startupDescription.textContent = t(state.startupEnabled
    ? "startupEnabledDescription"
    : "startupDisabledDescription");
  startupToggle.textContent = t(state.startupEnabled ? "turnOff" : "turnOn");
}

function renderDataOptions() {
  const favoriteValue = autoFavoriteSelect.value || (state.settings?.autoFavoriteEnabled
    ? String(state.settings.autoFavoriteCopyThreshold)
    : "0");
  autoFavoriteSelect.replaceChildren();
  for (const value of [0, 2, 3, 4, 5]) {
    const option = document.createElement("option");
    option.value = String(value);
    option.textContent = value === 0 ? t("autoFavoriteOff") : t("autoFavoriteCount", value);
    autoFavoriteSelect.append(option);
  }
  autoFavoriteSelect.value = favoriteValue;

  const cleanupValue = autoCleanupSelect.value || String(state.settings?.autoCleanupDays || 0);
  autoCleanupSelect.replaceChildren();
  for (const value of [0, 7, 30, 90, 180]) {
    const option = document.createElement("option");
    option.value = String(value);
    option.textContent = t(value === 0 ? "cleanupOff" : `cleanup${value}`);
    autoCleanupSelect.append(option);
  }
  autoCleanupSelect.value = cleanupValue;
}

function formatBytes(bytes) {
  const units = ["B", "KB", "MB", "GB"];
  let value = Math.max(0, Number(bytes || 0));
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }
  return `${value.toLocaleString(state.locale, {
    minimumFractionDigits: unit === 0 ? 0 : 1,
    maximumFractionDigits: unit === 0 ? 0 : 1,
  })} ${units[unit]}`;
}

function renderDataStatistics() {
  const statistics = state.dataStatistics;
  if (!statistics) {
    storedValue.textContent = "-";
    kindsValue.textContent = "";
    imageStorageValue.textContent = "-";
    favoritesValue.textContent = "";
    return;
  }
  storedValue.textContent = t("itemsCount", statistics.totalItems);
  kindsValue.textContent = t("kindsCount", statistics.urlItems, statistics.imageItems);
  imageStorageValue.textContent = formatBytes(statistics.imageBytes);
  favoritesValue.textContent = t("favoritesPreserved", statistics.favoriteItems);
}

async function loadStartupState() {
  try {
    state.startupEnabled = await tauriCore().invoke("startup_get");
    renderStartupState();
    void configureTray();
  } catch {
    startupDescription.textContent = t("startupChangeFailed");
  }
}

async function loadDataStatistics() {
  try {
    state.dataStatistics = await tauriCore().invoke("data_statistics");
    renderDataStatistics();
  } catch {
    showToast(t("statisticsLoadFailed"));
  }
}

async function configureTray() {
  if (!state.settings) return;
  try {
    const detectionEnabled = Object.values(state.settings.sources || {}).some(Boolean);
    const paused = Boolean(state.runtimeStatus?.detectionPaused);
    const overallStatus = paused ? t("detectionPaused") : detectionEnabled ? t("detected") : t("disabled");
    const discordRuntime = sourceRuntimeLabel("Discord");
    const showDiscordRepair = Boolean(
      state.settings.sources?.Discord
      && state.runtimeStatus?.discordState === "ReconnectRequired",
    );
    await tauriCore().invoke("tray_configure", {
      statusLabel: t("trayStatus", overallStatus),
      openLabel: t("openLibrary"),
      doubleClickLabel: t("doubleClick"),
      pauseLabel: t("pauseDetection"),
      resumeLabel: t("resumeDetection"),
      startupLabel: t("windowsStartup"),
      discordLabel: t("discordAutoConnect"),
      discordDetectionLabel: t("discordDetection"),
      accessibilityLabel: t("accessibilityMode"),
      discordStatusLabel: discordRuntime.text,
      repairLabel: t("discordReconnect"),
      openDataLabel: t("openDataFolder"),
      exitLabel: t("exitSentory"),
      detectingTooltip: t("trayDetecting"),
      pausedTooltip: t("trayPaused"),
      detectionOffTooltip: t("trayDetectionOff"),
      paused,
      detectionEnabled,
      startupEnabled: state.startupEnabled,
      discordEnabled: Boolean(state.settings.sources?.Discord),
      showDiscordStatus: shouldShowDiscordReconnectNotice(),
      showDiscordRepair,
      dark: document.documentElement.dataset.theme === "dark",
    });
  } catch {
    // The gallery remains usable if Windows has not created the tray yet.
  }
}

async function loadSettings() {
  try {
    applySettings(await tauriCore().invoke("settings_get"));
  } catch (error) {
    showToast(t("settingsFailed"));
    applyLocalizedUi("auto");
  }
}

async function persistSettings(patch) {
  try {
    const settings = await tauriCore().invoke("settings_update", { patch });
    applySettings(settings);
    return settings;
  } catch {
    await loadSettings();
    return null;
  }
}

function sourceRuntimeLabel(source) {
  if (!state.settings?.sources?.[source]) return { text: t("disabledSource", sourceLabel(source)), tone: "" };
  if (state.runtimeStatus?.detectionPaused) return { text: t("detectionPaused"), tone: "" };
  if (source !== "Discord") return { text: t("detected"), tone: "ready" };
  const runtime = state.runtimeStatus;
  if (!runtime?.discordRunning) return { text: t("discordNotRunning"), tone: "" };
  const key = runtime.discordState === "Ready" ? "detected"
    : runtime.discordState === "Recovering" ? "recovering"
      : runtime.discordState === "ReconnectRequired" ? "reconnect" : "connecting";
  return { text: t(key), tone: key === "detected" ? "ready" : key === "reconnect" ? "issue" : "" };
}

function shouldShowDiscordReconnectNotice() {
  return Boolean(
    state.settings?.sources?.Discord
    && state.runtimeStatus?.discordRunning
    && state.runtimeStatus?.discordState === "ReconnectRequired",
  );
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
    label.append(statusLabel);
    if (source === "Discord" && state.runtimeStatus?.discordState === "ReconnectRequired" && state.settings.sources.Discord) {
      const repair = document.createElement("button");
      repair.className = "repair-button";
      repair.type = "button";
      repair.textContent = t("discordReconnect");
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
    input.addEventListener("change", async () => {
      state.settings.sources[source] = input.checked;
      renderSourceSettings();
      const enabled = input.checked;
      const settings = await persistSettings({ [SOURCE_PATCH_KEYS[source]]: enabled });
      const displaySource = sourceLabel(source);
      showToast(settings ? t(enabled ? "sourceEnabled" : "sourceDisabled", displaySource) : t("sourceSettingFailed", displaySource));
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
  let headerStatusVisible = false;
  if (state.settings?.sources?.Discord && state.runtimeStatus?.discordRunning) {
    const label = sourceRuntimeLabel("Discord");
    headerStatusVisible = shouldShowDiscordReconnectNotice();
    detectionStatusText.textContent = label.text;
    detectionStatus.classList.toggle("issue", label.tone === "issue");
    detectionStatus.classList.toggle("ready", label.tone === "ready");
  }
  detectionStatus.hidden = !headerStatusVisible;
  renderSourceSettings();
  void configureTray();
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
  } catch {
    showToast(t("discordRepairFailed"));
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
  state.renderRevision += 1;
  scroller.scrollTop = previousScrollTop;
  if (!state.hasLoaded) {
    state.items = new Array(PAGE_SIZE);
    state.total = PAGE_SIZE;
    state.renderedRange = "";
    measureGrid();
    renderVisibleCards();
  }
  if (announce) setStatus(t("galleryRefreshing"));
  void loadPage(0, state.generation, true);
}

async function connectEngineEvents() {
  const listen = window.__TAURI__?.event?.listen;
  if (!listen) return;
  await listen("gallery-changed", () => {
    const preserveScroll = scroller.scrollTop > ROW_HEIGHT;
    resetGallery({ preserveScroll });
  });
  await listen("engine-status", event => {
    const engineState = event.payload?.state;
    if (engineState === "connecting") {
      setStatus(t("enginePreparing"));
    } else if (engineState === "recovering") {
      setStatus(t("engineRecovering"));
    } else if (engineState === "ready") {
      status.classList.add("hidden");
    } else if (engineState === "error") {
      setStatus(t("engineFailed"), true);
    }
  });
  await listen("runtime-status", event => applyRuntimeStatus(event.payload));
  await listen("capture-event", event => {
    showToast(localizedCaptureMessage(event.payload));
  });
  await listen("runtime-issue", event => {
    if (event.payload?.message) {
      showToast(event.payload.message.includes("Discord") ? t("discordRecoveryIssue") : t("captureIssue"));
    }
  });
  await listen("automatic-cleanup", event => {
    const deleted = Number(event.payload?.deleted || 0);
    if (deleted > 0) {
      showToast(t(event.payload?.fileDeleteFailures > 0 ? "cleanupPartial" : "cleanupComplete", deleted));
      void loadDataStatistics();
      resetGallery({ preserveScroll: true });
    }
  });
  await listen("settings-changed", event => {
    if (event.payload) {
      applySettings(event.payload);
      void loadStartupState();
    }
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
        throw new Error(t("loadFailed"));
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
      state.renderedRange = "";
      measureGrid();
      renderVisibleCards();

      if (isInitial) {
        setStatus(t("items", snapshot.total));
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
        document.title = "Sentory";
        measureGrid();
        renderVisibleCards();
      }
      const detail = error instanceof Error ? error.message : String(error);
      setStatus(t("loadFailed"), true);
      void tauriCore().invoke("ui_diagnostic", { event: "gallery-page-failed", detail });
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

  const start = firstRow * state.columns;
  const end = Math.min(state.total, lastRow * state.columns);
  ensurePagesForRange(start, end);
  const existing = new Map(
    [...virtualSpace.querySelectorAll(".card[data-virtual-index]")]
      .map(card => [Number(card.dataset.virtualIndex), card]));
  const cards = [];
  for (let index = start; index < end; index += 1) {
    const item = state.items[index];
    const reusable = existing.get(index);
    if (canReuseVirtualCard(reusable, item)) {
      positionCard(reusable, index);
      cards.push(reusable);
    } else {
      cards.push(item ? createCard(item, index) : createSkeletonCard(index));
    }
  }
  virtualSpace.replaceChildren(...cards);
}

function canReuseVirtualCard(card, item) {
  if (!card || Number(card.dataset.renderRevision) !== state.renderRevision) return false;
  return item
    ? card.dataset.itemId === item.itemId && !card.classList.contains("skeleton")
    : card.classList.contains("skeleton");
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
  card.dataset.virtualIndex = String(index);
  card.dataset.renderRevision = String(state.renderRevision);
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

function contextMenuItem() {
  if (!state.contextItemId) return null;
  const loaded = state.items.find(item => item?.itemId === state.contextItemId);
  if (loaded) return loaded;
  return state.detailItem?.card?.itemId === state.contextItemId ? state.detailItem.card : null;
}

function refreshCardContextMenu() {
  const item = contextMenuItem();
  if (!item) {
    if (!cardContextMenu.hidden) closeCardContextMenu();
    return;
  }
  contextFavorite.textContent = t(item.isFavorite ? "favoriteRemoveAction" : "favoriteAddAction");
  contextCopy.textContent = t("copy");
  contextReveal.textContent = t(item.kind === "Url" ? "openOriginalLink" : "openOriginalFolder");
  contextDelete.textContent = t("delete");
}

function openCardContextMenu(event, item) {
  event.preventDefault();
  event.stopPropagation();
  if (state.selectionMode) {
    closeCardContextMenu();
    return;
  }
  state.contextItemId = item.itemId;
  refreshCardContextMenu();
  cardContextMenu.hidden = false;
  cardContextMenu.style.visibility = "hidden";
  cardContextMenu.style.left = "0px";
  cardContextMenu.style.top = "0px";
  const bounds = cardContextMenu.getBoundingClientRect();
  const margin = 8;
  const left = Math.min(event.clientX, window.innerWidth - bounds.width - margin);
  const top = Math.min(event.clientY, window.innerHeight - bounds.height - margin);
  cardContextMenu.style.left = `${Math.max(margin, left)}px`;
  cardContextMenu.style.top = `${Math.max(margin, top)}px`;
  cardContextMenu.style.visibility = "visible";
  contextFavorite.focus({ preventScroll: true });
}

function closeCardContextMenu() {
  cardContextMenu.hidden = true;
  cardContextMenu.style.removeProperty("visibility");
  state.contextItemId = null;
}

function createCard(item, index) {
  const card = document.createElement("article");
  card.className = `card${state.selectedIds.has(item.itemId) ? " selected" : ""}`;
  card.dataset.itemId = item.itemId;
  card.dataset.virtualIndex = String(index);
  card.dataset.renderRevision = String(state.renderRevision);
  positionCard(card, index);
  card.setAttribute("aria-label", `${localizedType(item)}, ${item.title}, ${localizedDate(item.lastCapturedAt)}`);
  card.addEventListener("click", () => {
    if (state.suppressCardClick) return;
    if (state.selectionMode) toggleSelection(item.itemId);
    else void showDetails(item.itemId);
  });
  card.addEventListener("contextmenu", event => openCardContextMenu(event, item));

  const artwork = document.createElement("div");
  artwork.className = "artwork";
  artwork.title = t("openPreview");
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
  if (item.kind === "Collection" && item.memberCount > 0) {
    const badge = document.createElement("span");
    badge.className = "collection-badge";
    badge.textContent = t("collectionItems", item.memberCount);
    artwork.append(badge);
  }

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
  favorite.title = item.isFavorite ? t("favoriteRemoveAction") : t("favoriteAddAction");
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
  const filtered = state.query || state.kind !== "all" || state.sources.size > 0 || state.dateRange !== "All";
  const empty = document.createElement("div");
  empty.className = "empty";
  const heading = document.createElement("strong");
  heading.textContent = t(filtered ? "emptyFiltered" : "empty");
  const description = document.createElement("span");
  description.textContent = t(filtered ? "emptyFilteredDescription" : "emptyDescription");
  empty.append(heading, description);
  return empty;
}

async function showDetails(itemId) {
  detailLayer.hidden = false;
  detailTitle.textContent = t("loading");
  detailType.textContent = "";
  detailDescription.textContent = "";
  detailPhotoSection.hidden = true;
  detailLinkSection.hidden = true;
  state.detailPhotoIndex = 0;
  state.detailLinkIndex = 0;
  state.detailArtworkTarget = null;
  detailArtwork.replaceChildren();
  try {
    const detail = await tauriCore().invoke("gallery_item", { itemId });
    if (!detail) throw new Error(t("itemNotFound"));
    state.detailItem = detail;
    populateDetails(detail);
  } catch {
    detailLayer.hidden = true;
    showToast(t("itemNotFound"));
  }
}

function populateDetails(detail) {
  const card = detail.card;
  const photos = getDetailPhotos(detail);
  const links = getDetailLinks(detail);
  detailType.textContent = localizedType(card);
  detailFavoriteMark.hidden = !card.isFavorite;
  detailTitle.textContent = card.kind === "Collection"
    ? t("collectionTitle", photos.length, links.length)
    : card.title;
  detailDescription.textContent = card.kind === "Collection"
    ? t("collectionItems", (detail.members ?? []).length)
    : card.subtitle;
  detailCaptureCount.textContent = t("times", card.captureCount);
  detailCopyCount.textContent = t("times", card.copyCount);
  detailSource.textContent = sourceLabel(card.sourceApp);
  detailDate.textContent = new Intl.DateTimeFormat(state.locale, { dateStyle: "long", timeStyle: "short" }).format(new Date(card.lastCapturedAt));
  detailDelivery.textContent = localizedStatus(card);
  detailOpen.textContent = t(card.kind === "Image" ? "openPhoto" : "openLink");
  detailCopy.textContent = t(card.kind === "Image" ? "copyPhoto" : card.kind === "Collection" ? "copyCollection" : "copyUrl");
  detailPhotoSection.hidden = photos.length === 0;
  detailPhotoNavigation.hidden = photos.length <= 1;
  detailLinkSection.hidden = links.length === 0;
  detailLinkNavigation.hidden = links.length <= 1;
  if (links.length > 0) {
    state.detailLinkIndex = Math.min(state.detailLinkIndex, links.length - 1);
    updateDetailLinkRow(links);
  }
  if (photos.length > 0) {
    state.detailPhotoIndex = Math.min(state.detailPhotoIndex, photos.length - 1);
    showDetailPhoto(state.detailPhotoIndex, false);
  } else if (links.length > 0) {
    showDetailLink(state.detailLinkIndex);
  }
  else detailArtwork.replaceChildren(createUrlFallback(card));
}

function getDetailPhotos(detail = state.detailItem) {
  if (!detail) return [];
  if (detail.card.kind === "Image") {
    return detail.contentPath
      ? [{ position: null, title: detail.card.title, contentPath: detail.contentPath }]
      : [];
  }
  return (detail.members ?? [])
    .filter(member => member.kind === "Image" && member.contentPath)
    .map(member => ({
      position: member.position,
      title: member.title,
      contentPath: member.contentPath,
    }));
}

function getDetailLinks(detail = state.detailItem) {
  if (!detail) return [];
  if (detail.card.kind === "Url") {
    return detail.card.originalUrl
      ? [{
          position: null,
          title: detail.card.originalUrl,
          originalUrl: detail.card.originalUrl,
          domain: detail.card.domain,
          artworkPath: detail.card.artworkPath,
        }]
      : [];
  }
  return (detail.members ?? [])
    .filter(member => member.kind === "Url" && member.originalUrl)
    .map(member => ({
      position: member.position,
      title: member.title,
      originalUrl: member.originalUrl,
      domain: member.domain,
      artworkPath: null,
    }));
}

function showDetailPhoto(requestedIndex, animate = true) {
  const photos = getDetailPhotos();
  if (photos.length === 0) return;
  state.detailPhotoIndex = (requestedIndex % photos.length + photos.length) % photos.length;
  const photo = photos[state.detailPhotoIndex];
  const nodes = [];
  if (photos.length > 2) nodes.push(createDetailImage(
    photos[(state.detailPhotoIndex + 2) % photos.length].contentPath,
    "detail-stack-back"));
  if (photos.length > 1) nodes.push(createDetailImage(
    photos[(state.detailPhotoIndex + 1) % photos.length].contentPath,
    "detail-stack-back detail-stack-back-one"));
  const main = createDetailImage(photo.contentPath, "detail-main-artwork");
  nodes.push(main);
  detailArtwork.replaceChildren(...nodes);
  if (animate) main.animate([{ opacity: 0.3 }, { opacity: 1 }], { duration: 170, easing: "ease-out" });
  detailArtwork.title = t("openPhoto");
  detailPhotoName.textContent = photo.title;
  detailPhotoName.title = photo.title;
  state.detailArtworkTarget = { kind: "Image", memberPosition: photo.position };
  updateDetailPageDots(detailPhotoDots, photos.length, state.detailPhotoIndex);
}

function showDetailLink(requestedIndex) {
  const links = getDetailLinks();
  if (links.length === 0) return;
  state.detailLinkIndex = (requestedIndex % links.length + links.length) % links.length;
  const link = links[state.detailLinkIndex];
  updateDetailLinkRow(links);
  if (link.artworkPath) {
    detailArtwork.replaceChildren(createDetailImage(link.artworkPath, "detail-main-artwork"));
  } else {
    detailArtwork.replaceChildren(createUrlFallback({
      title: link.title,
      domain: link.domain,
    }));
  }
  detailArtwork.title = t("openLink");
  state.detailArtworkTarget = { kind: "Url", memberPosition: link.position };
}

function updateDetailLinkRow(links = getDetailLinks()) {
  if (links.length === 0) return;
  const link = links[state.detailLinkIndex];
  detailLinkValue.textContent = link.originalUrl;
  detailLinkValue.title = link.originalUrl;
  updateDetailPageDots(detailLinkDots, links.length, state.detailLinkIndex);
}

function createDetailImage(path, className) {
  const image = document.createElement("img");
  image.alt = "";
  image.className = className;
  image.decoding = "async";
  image.src = tauriCore().convertFileSrc(path);
  return image;
}

function updateDetailPageDots(container, count, selectedIndex) {
  const dots = [];
  for (let index = 0; index < count; index += 1) {
    const dot = document.createElement("i");
    dot.classList.toggle("active", index === selectedIndex);
    dots.push(dot);
  }
  container.replaceChildren(...dots);
}

async function copyItem(itemId, button = null) {
  const previous = button?.innerHTML;
  if (button) {
    button.disabled = true;
    button.innerHTML = "&#xE895;";
  }
  try {
    const result = await tauriCore().invoke("gallery_copy", { itemId });
    if (!result.success) throw new Error(t("copyHistorySaveFailed"));
    updateLoadedItem(itemId, item => ({
      ...item,
      copyCount: result.copyCount ?? item.copyCount + 1,
      isFavorite: result.isFavorite ?? item.isFavorite,
      lastCopiedAt: new Date().toISOString(),
    }));
    const loaded = state.items.find(item => item?.itemId === itemId) ?? state.detailItem?.card;
    showToast(loaded?.kind === "Image" ? t("photoCopied") : loaded?.kind === "Collection" ? t("collectionCopied") : t("urlCopied"));
    if (button) button.innerHTML = "&#xE73E;";
  } catch {
    showToast(t("copyFailedShort"));
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
    if (!result.success) throw new Error(t("itemNotFound"));
    showToast(next ? t("addFavorite") : t("removeFavorite"));
  } catch (error) {
    updateLoadedItem(item.itemId, current => ({ ...current, isFavorite: !next }));
    showToast(t("favoriteChangeFailed"));
  }
}

function updateLoadedItem(itemId, transform) {
  const index = state.items.findIndex(item => item?.itemId === itemId);
  let current = index >= 0 ? state.items[index] : null;
  if (current) {
    const updated = transform(current);
    Object.assign(current, updated);
  } else if (state.detailItem?.card.itemId === itemId) {
    current = state.detailItem.card;
    const updated = transform(current);
    Object.assign(current, updated);
  }
  if (!current) return null;
  if (state.detailItem?.card.itemId === itemId &&
      state.detailItem.card !== current) {
    Object.assign(state.detailItem.card, current);
  }
  refreshVisibleItemMetadata(current);
  return current;
}

function refreshVisibleItemMetadata(item) {
  for (const card of virtualSpace.querySelectorAll(".card[data-item-id]")) {
    if (card.dataset.itemId !== item.itemId) continue;
    const footer = card.querySelector(".card-footer");
    const favorite = card.querySelector(".favorite");
    let usage = card.querySelector(".copy-usage");
    if (item.copyCount > 0) {
      if (!usage && footer) {
        usage = document.createElement("span");
        usage.className = "copy-usage";
        footer.insertBefore(usage, favorite);
      }
      if (usage) usage.textContent = t("copyCount", item.copyCount);
    } else {
      usage?.remove();
    }
    if (favorite) {
      favorite.classList.toggle("active", item.isFavorite);
      favorite.innerHTML = item.isFavorite ? "&#xE735;" : "&#xE734;";
      favorite.title = item.isFavorite
        ? t("favoriteRemoveAction")
        : t("favoriteAddAction");
      favorite.setAttribute("aria-label", favorite.title);
    }
  }
  if (state.detailItem?.card.itemId === item.itemId) {
    detailCopyCount.textContent = t("times", item.copyCount);
    detailFavoriteMark.hidden = !item.isFavorite;
  }
  if (state.contextItemId === item.itemId) refreshCardContextMenu();
}

async function openItem(itemId) {
  try {
    await tauriCore().invoke("gallery_open", { itemId });
  } catch (error) {
    showToast(t("openOriginalFailed"));
  }
}

async function revealItem(itemId) {
  try {
    await tauriCore().invoke("gallery_reveal", { itemId });
  } catch {
    showToast(t("openOriginalFailed"));
  }
}

async function openDetailArtwork() {
  if (!state.detailItem || !state.detailArtworkTarget) return;
  try {
    if (state.detailArtworkTarget.memberPosition === null) {
      await tauriCore().invoke("gallery_open", {
        itemId: state.detailItem.card.itemId,
      });
    } else {
      await tauriCore().invoke("gallery_detail_target_open", {
        itemId: state.detailItem.card.itemId,
        memberPosition: state.detailArtworkTarget.memberPosition,
      });
    }
  } catch {
    showToast(t("openOriginalFailed"));
  }
}

async function copyCurrentDetailPhoto() {
  if (!state.detailItem) return;
  const photo = getDetailPhotos()[state.detailPhotoIndex];
  if (!photo) return;
  if (state.detailItem.card.kind === "Image" && photo.position === null) {
    await copyItem(state.detailItem.card.itemId, detailPhotoCopy);
    return;
  }
  await copyDetailTarget(photo.position, detailPhotoCopy, "photoCopied");
}

async function copyCurrentDetailLink() {
  const link = getDetailLinks()[state.detailLinkIndex];
  if (!link) return;
  await copyDetailTarget(link.position, detailLinkCopy, "urlCopied");
}

async function copyDetailTarget(memberPosition, button, successKey) {
  if (!state.detailItem) return;
  const previous = button.innerHTML;
  button.disabled = true;
  button.innerHTML = "&#xE895;";
  try {
    await tauriCore().invoke("gallery_detail_target_copy", {
      itemId: state.detailItem.card.itemId,
      memberPosition,
    });
    button.innerHTML = "&#xE73E;";
    showToast(t(successKey));
  } catch {
    button.innerHTML = "&#xE783;";
    showToast(t("copyFailedShort"));
  } finally {
    window.setTimeout(() => {
      if (!button.isConnected) return;
      button.disabled = false;
      button.innerHTML = previous;
    }, 850);
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
    if (!result.success && result.missing === 0) throw new Error(t("deleteSelectedFailed"));
    showToast(t("deleted", result.changed));
    state.selectedIds.clear();
    if (!detailLayer.hidden) detailLayer.hidden = true;
    resetGallery();
    updateSelectionUi();
    return true;
  } catch (error) {
    showToast(t("deleteSelectedFailed"));
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
  closeCardContextMenu();
  state.selectionMode = enabled;
  document.body.classList.toggle("selection-mode", enabled);
  selectModeButton.classList.toggle("selected", enabled);
  document.querySelector("#select-label").textContent = t(enabled ? "selectExit" : "select");
  if (!enabled) state.selectedIds.clear();
  updateSelectionUi();
  updateVisibleSelectionVisuals();
}

function toggleSelection(itemId) {
  if (state.selectedIds.has(itemId)) state.selectedIds.delete(itemId);
  else state.selectedIds.add(itemId);
  updateSelectionUi();
  updateVisibleSelectionVisuals();
}

function updateSelectionUi() {
  selectionBar.hidden = !state.selectionMode;
  selectedCount.textContent = t("selectedCount", state.selectedIds.size);
  deleteSelectedButton.disabled = state.selectedIds.size === 0;
}

function updateVisibleSelectionVisuals() {
  for (const card of virtualSpace.querySelectorAll(".card[data-item-id]")) {
    const selected = state.selectedIds.has(card.dataset.itemId);
    card.classList.toggle("selected", selected);
    const toggle = card.querySelector(".selection-toggle");
    if (!toggle) continue;
    toggle.innerHTML = selected ? "&#xE73E;" : "";
    toggle.title = selected ? t("clearSelection") : t("select");
  }
}

function sourceLabel(source) {
  return source === "Line" ? "LINE" : source;
}

function beginSelectionDrag(event) {
  if (event.button !== 0 || event.target.closest("button, .card")) return;
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
    startedInSelectionMode: state.selectionMode,
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
    if (!state.selectionMode) setSelectionMode(true);
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
  } else if (drag.startedInSelectionMode) {
    setSelectionMode(false);
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
  updateVisibleSelectionVisuals();
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

function updateScrollIndicatorFor(target, thumb) {
  const trackHeight = target.clientHeight;
  const scrollHeight = target.scrollHeight;
  const maxScroll = Math.max(0, scrollHeight - trackHeight);
  if (maxScroll <= 0) {
    thumb.style.height = "0";
    return;
  }
  const thumbHeight = Math.max(32, trackHeight * trackHeight / scrollHeight);
  const top = (target.scrollTop / maxScroll) * (trackHeight - thumbHeight);
  thumb.style.height = `${thumbHeight}px`;
  thumb.style.transform = `translateY(${top}px)`;
}

function updateScrollIndicator() {
  updateScrollIndicatorFor(scroller, scrollThumb);
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

function updateSortUi() {
  for (const button of sortMenu.querySelectorAll("button")) {
    button.classList.toggle("selected", state.sort === button.dataset.sort);
  }
  const selected = sortMenu.querySelector(`[data-sort="${state.sort}"]`);
  sortLabel.textContent = t("sortLabel", selected?.textContent || t("newest"));
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
  button.textContent = sourceLabel(source);
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
  if (state.sources.size === 0 && state.dateRange === "All") return;
  state.sources.clear();
  state.dateRange = "All";
  updateFilterUi();
  resetGallery();
});

dateOptions.addEventListener("click", event => {
  const button = event.target.closest("button[data-date]");
  if (!button) return;
  if (state.dateRange === button.dataset.date) return;
  state.dateRange = button.dataset.date;
  updateFilterUi();
  resetGallery();
});

sortMenu.addEventListener("click", event => {
  const button = event.target.closest("button[data-sort]");
  if (!button) return;
  if (state.sort === button.dataset.sort) {
    sortMenu.hidden = true;
    sortButton.setAttribute("aria-expanded", "false");
    return;
  }
  state.sort = button.dataset.sort;
  updateSortUi();
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
selectVisibleButton.addEventListener("click", async () => {
  selectVisibleButton.disabled = true;
  try {
    const selected = new Set();
    let offset = 0;
    let total = state.total;
    do {
      const page = await tauriCore().invoke("gallery_page", { request: buildRequest(offset) });
      total = page.total;
      for (const item of page.items) selected.add(item.itemId);
      offset += page.items.length;
      if (page.items.length === 0) break;
    } while (offset < total);
    state.selectedIds = selected;
    updateSelectionUi();
    updateVisibleSelectionVisuals();
  } catch {
    showToast(t("loadFailed"));
  } finally {
    selectVisibleButton.disabled = false;
  }
});
clearSelectionButton.addEventListener("click", () => {
  state.selectedIds.clear();
  updateSelectionUi();
  updateVisibleSelectionVisuals();
});
deleteSelectedButton.addEventListener("click", () => void deleteItems([...state.selectedIds]));

contextFavorite.addEventListener("click", () => {
  const item = contextMenuItem();
  closeCardContextMenu();
  if (item) void toggleFavorite(item, null);
});
contextCopy.addEventListener("click", () => {
  const item = contextMenuItem();
  closeCardContextMenu();
  if (item) void copyItem(item.itemId);
});
contextReveal.addEventListener("click", () => {
  const item = contextMenuItem();
  closeCardContextMenu();
  if (item) void revealItem(item.itemId);
});
contextDelete.addEventListener("click", () => {
  const item = contextMenuItem();
  closeCardContextMenu();
  if (item) void deleteItems([item.itemId]);
});
cardContextMenu.addEventListener("keydown", event => {
  const actions = [...cardContextMenu.querySelectorAll("button")];
  const current = actions.indexOf(document.activeElement);
  let next = current;
  if (event.key === "ArrowDown") next = (current + 1) % actions.length;
  else if (event.key === "ArrowUp") next = (current - 1 + actions.length) % actions.length;
  else if (event.key === "Home") next = 0;
  else if (event.key === "End") next = actions.length - 1;
  else return;
  event.preventDefault();
  actions[next].focus();
});
document.addEventListener("pointerdown", event => {
  if (!cardContextMenu.hidden && !cardContextMenu.contains(event.target)) closeCardContextMenu();
}, true);
scroller.addEventListener("scroll", closeCardContextMenu, { passive: true });
window.addEventListener("resize", closeCardContextMenu);
window.addEventListener("blur", closeCardContextMenu);

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
  void openDetailArtwork();
});
detailPhotoCopy.addEventListener("click", event => {
  event.stopPropagation();
  void copyCurrentDetailPhoto();
});
detailPhotoPrevious.addEventListener("click", () => showDetailPhoto(state.detailPhotoIndex - 1));
detailPhotoNext.addEventListener("click", () => showDetailPhoto(state.detailPhotoIndex + 1));
detailLinkCopy.addEventListener("click", event => {
  event.stopPropagation();
  void copyCurrentDetailLink();
});
detailLinkPrevious.addEventListener("click", () => showDetailLink(state.detailLinkIndex - 1));
detailLinkNext.addEventListener("click", () => showDetailLink(state.detailLinkIndex + 1));
detailDelete.addEventListener("click", () => {
  if (state.detailItem) void deleteItems([state.detailItem.card.itemId]);
});

document.addEventListener("keydown", event => {
  if (event.key !== "Escape") return;
  if (!cardContextMenu.hidden) closeCardContextMenu();
  else if (!confirmLayer.hidden) confirmCancel.click();
  else if (!licenseLayer.hidden) licenseClose.click();
  else if (!settingsLayer.hidden) settingsClose.click();
  else if (!detailLayer.hidden) detailClose.click();
  else if (state.selectionMode) setSelectionMode(false);
});

themeButton.addEventListener("click", async () => {
  const next = document.documentElement.dataset.theme === "dark" ? "Light" : "Dark";
  if (state.settings) state.settings.themeMode = next;
  applyThemeMode(next);
  const settings = await persistSettings({ themeMode: next });
  showToast(settings ? t("themeApplied", next) : t("themeSaveFailed"));
});

settingsButton.addEventListener("click", () => {
  settingsLayer.hidden = false;
  renderSourceSettings();
  void loadStartupState();
  void loadDataStatistics();
  updateScrollIndicatorFor(settingsScroll, settingsScrollThumb);
  enhancedSelects.get(themeSetting)?.trigger.focus();
});
settingsClose.addEventListener("click", () => { settingsLayer.hidden = true; });
settingsLayer.addEventListener("pointerdown", event => {
  if (event.target === settingsLayer) settingsLayer.hidden = true;
});
viewLicense.addEventListener("click", async () => {
  viewLicense.disabled = true;
  try {
    if (!licenseText.textContent) {
      licenseText.textContent = await tauriCore().invoke("license_text");
    }
    licenseLayer.hidden = false;
    licenseText.scrollTop = 0;
    updateScrollIndicatorFor(licenseText, licenseScrollThumb);
    licenseClose.focus();
  } finally {
    viewLicense.disabled = false;
  }
});
licenseClose.addEventListener("click", () => { licenseLayer.hidden = true; });
licenseLayer.addEventListener("pointerdown", event => {
  if (event.target === licenseLayer) licenseLayer.hidden = true;
});
themeSetting.addEventListener("change", async () => {
  const mode = themeSetting.value;
  if (state.settings) state.settings.themeMode = mode;
  applyThemeMode(mode);
  const settings = await persistSettings({ themeMode: mode });
  showToast(settings ? t("themeApplied", mode) : t("themeSaveFailed"));
});
languageSetting.addEventListener("change", async () => {
  const language = languageSetting.value;
  if (state.settings) state.settings.language = language;
  applyLocalizedUi(language);
  const settings = await persistSettings({ language });
  showToast(settings ? t("languageApplied") : t("languageSaveFailed"));
});
startupToggle.addEventListener("click", async () => {
  startupToggle.disabled = true;
  const next = !state.startupEnabled;
  try {
    const settings = await tauriCore().invoke("startup_set", { enabled: next });
    state.startupEnabled = next;
    applySettings(settings);
    showToast(t(next ? "startupEnabled" : "startupDisabled"));
  } catch {
    showToast(t("startupChangeFailed"));
  } finally {
    startupToggle.disabled = false;
    renderStartupState();
  }
});
autoFavoriteSave.addEventListener("click", async () => {
  const threshold = Number(autoFavoriteSelect.value);
  autoFavoriteSave.disabled = true;
  const settings = await persistSettings({
    autoFavoriteEnabled: threshold > 0,
    autoFavoriteCopyThreshold: threshold > 0 ? threshold : (state.settings?.autoFavoriteCopyThreshold || 3),
  });
  if (settings) showToast(t(threshold > 0 ? "autoFavoriteSaved" : "autoFavoriteDisabled", threshold));
  else showToast(t("autoFavoriteSaveFailed"));
  autoFavoriteSave.disabled = false;
});
autoCleanupSave.addEventListener("click", async () => {
  const days = Number(autoCleanupSelect.value);
  autoCleanupSave.disabled = true;
  const settings = await persistSettings({ autoCleanupDays: days });
  if (settings) showToast(t(days > 0 ? "autoCleanupSaved" : "autoCleanupDisabled", days));
  else showToast(t("autoCleanupSaveFailed"));
  autoCleanupSave.disabled = false;
});
openDataFolder.addEventListener("click", async () => {
  try {
    await tauriCore().invoke("open_data_directory");
  } catch {
    showToast(t("openDataFolderFailed"));
  }
});
deleteNonFavorites.addEventListener("click", async () => {
  deleteNonFavorites.disabled = true;
  showToast(t("checkingCleanup"));
  try {
    const preview = await tauriCore().invoke("data_cleanup_preview");
    if (!preview.totalItems) {
      showToast(t("nothingToCleanup"));
      return;
    }
    const confirmed = await askConfirmation(
      t("cleanupHeading"),
      t("cleanupMessage", preview.totalItems, preview.urlItems, preview.imageItems, formatBytes(preview.imageBytes)),
      { okText: t("deleteAll"), danger: true });
    if (!confirmed) {
      showToast(t("cleanupCancelled"));
      return;
    }
    const result = await tauriCore().invoke("data_cleanup");
    showToast(t(result.fileDeleteFailures === 0 ? "cleanupComplete" : "cleanupPartial", result.deleted.totalItems));
    await loadDataStatistics();
    resetGallery();
  } catch {
    showToast(t("cleanupFailed"));
  } finally {
    deleteNonFavorites.disabled = false;
  }
});
updateCheck.addEventListener("click", async () => {
  updateCheck.disabled = true;
  showToast(t("checkingForUpdates"));
  try {
    const result = await tauriCore().invoke("update_check");
    showToast(result.updateAvailable ? t("updateReady", result.version) : t("appIsUpToDate"));
    if (result.updateAvailable && result.releasePage) {
      await tauriCore().invoke("ui_diagnostic", { event: "update-available", detail: result.releasePage });
    }
  } catch {
    showToast(t("updateCheckFailed"));
  } finally {
    updateCheck.disabled = false;
  }
});
colorScheme.addEventListener("change", () => {
  if (state.settings?.themeMode === "System") applyThemeMode("System");
});

for (const link of document.querySelectorAll("[data-external-url]")) {
  link.addEventListener("click", event => {
    event.preventDefault();
    void tauriCore().invoke("open_external_url", { url: link.href }).catch(() => {
      showToast(t("openOriginalFailed"));
    });
  });
}

refreshButton.addEventListener("click", () => resetGallery({ announce: true }));
scroller.addEventListener("scroll", () => {
  if (state.selectionDrag?.active) updateSelectionDrag();
  requestRender();
  updateScrollIndicator();
  galleryRegion.classList.add("scrolling");
  window.clearTimeout(state.scrollTimer);
  state.scrollTimer = window.setTimeout(() => galleryRegion.classList.remove("scrolling"), 550);
}, { passive: true });

settingsScroll.addEventListener("scroll", () => {
  updateScrollIndicatorFor(settingsScroll, settingsScrollThumb);
  settingsScrollRegion.classList.add("scrolling");
  window.clearTimeout(state.settingsScrollTimer);
  state.settingsScrollTimer = window.setTimeout(
    () => settingsScrollRegion.classList.remove("scrolling"),
    550);
}, { passive: true });

licenseText.addEventListener("scroll", () => {
  updateScrollIndicatorFor(licenseText, licenseScrollThumb);
  licenseScrollRegion.classList.add("scrolling");
  window.clearTimeout(state.licenseScrollTimer);
  state.licenseScrollTimer = window.setTimeout(
    () => licenseScrollRegion.classList.remove("scrolling"),
    550);
}, { passive: true });

new ResizeObserver(() => {
  state.renderedRange = "";
  measureGrid();
  requestRender();
}).observe(scroller);

new ResizeObserver(() => {
  updateScrollIndicatorFor(settingsScroll, settingsScrollThumb);
}).observe(settingsScroll);

new ResizeObserver(() => {
  updateScrollIndicatorFor(licenseText, licenseScrollThumb);
}).observe(licenseText);

[themeSetting, languageSetting, autoFavoriteSelect, autoCleanupSelect].forEach(enhanceSelect);

updateFilterUi();
updateSortUi();
updateSelectionUi();
void (async () => {
  try {
    await connectEngineEvents();
  } finally {
    resetGallery();
  }
})();
void (async () => {
  await loadSettings();
  await loadStartupState();
})();
