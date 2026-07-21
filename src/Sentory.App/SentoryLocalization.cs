using System.Globalization;
using System.Windows;

namespace Sentory.App;

internal static class SentoryLocalization
{
    public const string DefaultLanguage = "ko-KR";

    private static readonly string[] SupportedLanguages =
        [DefaultLanguage, "en-US", "ja-JP", "zh-CN"];

    private static readonly IReadOnlyDictionary<string, LocalizedText> Texts =
        CreateTexts();

    public static string CurrentLanguage { get; private set; } =
        DefaultLanguage;

    public static CultureInfo Culture =>
        CultureInfo.GetCultureInfo(CurrentLanguage);

    public static void Apply(ResourceDictionary resources, string? language)
    {
        SetLanguage(language);
        ApplyCurrent(resources);
    }

    public static void SetLanguage(string? language) =>
        CurrentLanguage = NormalizeLanguage(language);

    public static void ApplyCurrent(ResourceDictionary resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        foreach (var (key, value) in Texts)
        {
            resources[$"Loc.{key}"] = value.Get(CurrentLanguage);
        }
    }

    public static string Text(string key) =>
        Texts.TryGetValue(key, out var value)
            ? value.Get(CurrentLanguage)
            : key;

    public static string Format(string key, params object[] arguments) =>
        string.Format(Culture, Text(key), arguments);

    public static string FormatDate(DateTime value) =>
        CurrentLanguage switch
        {
            "en-US" => value.ToString("MMM d · HH:mm", Culture),
            "ja-JP" => value.ToString("M月d日 · HH:mm", Culture),
            "zh-CN" => value.ToString("M月d日 · HH:mm", Culture),
            _ => value.ToString("M월 d일 · HH:mm", Culture)
        };

    public static string NormalizeLanguage(string? language) =>
        SupportedLanguages.FirstOrDefault(value =>
            string.Equals(value, language, StringComparison.OrdinalIgnoreCase))
        ?? DefaultLanguage;

    public static IReadOnlyList<LanguageOption> GetLanguageOptions() =>
    [
        new("ko-KR", "한국어"),
        new("en-US", "English"),
        new("ja-JP", "日本語"),
        new("zh-CN", "简体中文")
    ];

    private static IReadOnlyDictionary<string, LocalizedText> CreateTexts()
    {
        var entries = new LocalizedText[]
        {
            L("SettingsTitle", "Sentory 설정", "Sentory Settings", "Sentory 設定", "Sentory 设置"),
            L("SettingsHeading", "설정", "Settings", "設定", "设置"),
            L("SettingsDescription", "Sentory의 실행, 메신저 감지와 보관 데이터를 한곳에서 관리합니다", "Manage Sentory, messenger detection, and saved data in one place", "Sentory の動作、メッセンジャー検出、保存データを一か所で管理します", "在一个位置管理 Sentory、聊天应用检测和保存的数据"),
            L("General", "일반", "General", "一般", "常规"),
            L("ScreenTheme", "화면 테마", "Theme", "画面テーマ", "界面主题"),
            L("ScreenThemeDescription", "라이트 모드와 다크 모드를 선택합니다", "Choose light or dark mode", "ライトモードとダークモードを選択します", "选择浅色或深色模式"),
            L("Language", "Language", "Language", "Language", "Language"),
            L("LanguageDescription", "화면에 표시할 언어를 선택합니다", "Choose the display language", "表示する言語を選択します", "选择界面显示语言"),
            L("LightMode", "라이트 모드", "Light mode", "ライトモード", "浅色模式"),
            L("DarkMode", "다크 모드", "Dark mode", "ダークモード", "深色模式"),
            L("SystemTheme", "시스템 테마", "System theme", "システムテーマ", "系统主题"),
            L("WindowsStartup", "Windows 시작 시 실행", "Start with Windows", "Windows 起動時に実行", "Windows 启动时运行"),
            L("MessengerDetection", "메신저 감지", "Messenger detection", "メッセンジャー検出", "聊天应用检测"),
            L("Reconnect", "다시 연결", "Reconnect", "再接続", "重新连接"),
            L("KakaoDescription", "채팅 입력창의 사진과 붙여넣은 링크를 감지합니다", "Detects photos and pasted links in chat inputs", "チャット入力欄の写真と貼り付けたリンクを検出します", "检测聊天输入框中的图片和粘贴链接"),
            L("KakaoDropHeading", "카카오톡에 사진 놓기", "Drop photos into KakaoTalk", "カカオトークに写真をドロップ", "将图片拖放到 KakaoTalk"),
            L("KakaoDropDescription", "붙여넣고 Sentory에 함께 저장합니다", "Pastes them and saves them to Sentory", "貼り付けて Sentory にも保存します", "粘贴图片并同时保存到 Sentory"),
            L("DetectionReady", "감지 준비 완료", "Ready to detect", "検出準備完了", "检测已就绪"),
            L("DataManagement", "데이터 관리", "Data management", "データ管理", "数据管理"),
            L("FavoriteCleanupExclusion", "즐겨찾기에 등록된 항목은 자동 정리에서 포함되지 않음", "Favorites are excluded from automatic cleanup", "お気に入りは自動整理の対象外です", "收藏项目不会被自动清理"),
            L("Stored", "보관 중", "Stored", "保存中", "已保存"),
            L("ImageStorage", "사진 저장 용량", "Photo storage", "写真の保存容量", "图片存储空间"),
            L("AutoCleanup", "자동 정리", "Automatic cleanup", "自動整理", "自动清理"),
            L("AutoCleanupDefault", "기본값 사용 안 함", "Disabled by default", "初期設定では使用しません", "默认关闭"),
            L("CleanupOff", "자동 정리 사용 안 함", "Do not clean automatically", "自動整理を使用しない", "不使用自动清理"),
            L("Cleanup7", "7일 기준으로 정리", "Delete after 7 days", "7日を基準に整理", "清理超过 7 天的项目"),
            L("Cleanup30", "30일 기준으로 정리", "Delete after 30 days", "30日を基準に整理", "清理超过 30 天的项目"),
            L("Cleanup90", "90일 기준으로 정리", "Delete after 90 days", "90日を基準に整理", "清理超过 90 天的项目"),
            L("Cleanup180", "180일 기준으로 정리", "Delete after 180 days", "180日を基準に整理", "清理超过 180 天的项目"),
            L("SaveSettings", "설정 저장", "Save", "設定を保存", "保存设置"),
            L("OpenDataFolder", "데이터 폴더 열기", "Open data folder", "データフォルダーを開く", "打开数据文件夹"),
            L("DeleteNonFavorites", "즐겨찾기 제외 항목 모두 삭제", "Delete all except favorites", "お気に入り以外をすべて削除", "删除除收藏外的所有项目"),
            L("AppInfo", "앱 정보", "About", "アプリ情報", "应用信息"),
            L("VersionFormat", "버전 {0}", "Version {0}", "バージョン {0}", "版本 {0}"),
            L("UpdateAvailableHeading", "Sentory 업데이트가 있습니다", "Sentory update available", "Sentory のアップデートがあります", "Sentory 有可用更新"),
            L("UpdateAvailableMessage", "새 버전 {0}을 설치할 수 있습니다.", "Version {0} is ready to install.", "新しいバージョン {0} をインストールできます。", "可以安装新版本 {0}。"),
            L("InstallUpdate", "업데이트 설치", "Install update", "アップデートをインストール", "安装更新"),
            L("InstallUpdateVersionFormat", "{0} 업데이트 설치", "Install update {0}", "{0} にアップデート", "安装 {0} 更新"),
            L("UpdateFailedHeading", "업데이트하지 못했습니다", "Update failed", "アップデートできませんでした", "更新失败"),
            L("UpdateFailedMessage", "다운로드나 설치를 완료하지 못했습니다. 다음 실행 때 다시 확인합니다.", "The download or installation could not be completed. Sentory will check again next time.", "ダウンロードまたはインストールを完了できませんでした。次回の起動時にもう一度確認します。", "无法完成下载或安装。Sentory 将在下次启动时再次检查。"),
            L("Author", "저작자 © 2026 NudeNyang GitHub", "Created by © 2026 NudeNyang GitHub", "作者 © 2026 NudeNyang GitHub", "作者 © 2026 NudeNyang GitHub"),
            L("CopyrightNotice", "Copyright © 2026 NudeNyang", "Copyright © 2026 NudeNyang", "Copyright © 2026 NudeNyang", "Copyright © 2026 NudeNyang"),
            L("LicenseSummary", "GNU GPL v3에 따라 이용 가능", "Licensed under GNU GPL v3", "GNU GPL v3 に基づいて利用できます", "依据 GNU GPL v3 使用"),
            L("ViewLicense", "라이선스 보기", "View license", "ライセンスを見る", "查看许可协议"),
            L("LicenseTitle", "Sentory 라이선스", "Sentory License", "Sentory ライセンス", "Sentory 许可协议"),
            L("LicenseHeading", "GNU GPL v3", "GNU GPL v3", "GNU GPL v3", "GNU GPL v3"),
            L("LicenseDescription", "Sentory의 이용·수정·배포 조건", "Terms for using, modifying, and distributing Sentory", "Sentory の利用・改変・配布条件", "Sentory 的使用、修改与分发条款"),
            L("Tagline", "붙여넣기한 사진과 링크를 한 곳에서", "Pasted photos and links, all in one place", "貼り付けた写真とリンクを一か所に", "将粘贴的图片和链接集中一处"),
            L("DiscordDetection", "Discord 감지", "Discord detection", "Discord 検出", "Discord 检测"),
            L("Search", "보관함 검색", "Search library", "ライブラリを検索", "搜索收藏库"),
            L("SearchPlaceholder", "제목, URL, 도메인 검색", "Search title, URL, or domain", "タイトル、URL、ドメインを検索", "搜索标题、URL 或域名"),
            L("SwitchToDark", "다크 테마로 전환", "Switch to dark theme", "ダークテーマに切り替え", "切换到深色主题"),
            L("SwitchToLight", "밝은 테마로 전환", "Switch to light theme", "ライトテーマに切り替え", "切换到浅色主题"),
            L("All", "전체", "All", "すべて", "全部"),
            L("Links", "링크", "Links", "リンク", "链接"),
            L("Photos", "사진", "Photos", "写真", "图片"),
            L("Favorites", "즐겨찾기", "Favorites", "お気に入り", "收藏"),
            L("Filter", "필터", "Filter", "フィルター", "筛选"),
            L("Settings", "설정", "Settings", "設定", "设置"),
            L("Select", "선택", "Select", "選択", "选择"),
            L("SelectExit", "선택 종료", "Done", "選択終了", "完成选择"),
            L("Reset", "초기화", "Reset", "リセット", "重置"),
            L("Messenger", "메신저", "Messenger", "メッセンジャー", "聊天应用"),
            L("Period", "기간", "Period", "期間", "时间范围"),
            L("AllPeriod", "전체 기간", "All time", "全期間", "全部时间"),
            L("Today", "오늘", "Today", "今日", "今天"),
            L("Last7Days", "최근 7일", "Last 7 days", "過去7日", "最近 7 天"),
            L("Last30Days", "최근 30일", "Last 30 days", "過去30日", "最近 30 天"),
            L("SortNewest", "최신순", "Newest", "新しい順", "最新优先"),
            L("SortOldest", "오래된순", "Oldest", "古い順", "最早优先"),
            L("SortMostCaptured", "많이 저장한 순", "Most saved", "保存回数順", "保存次数最多"),
            L("SortMostCopied", "많이 복사한 순", "Most copied", "コピー回数順", "复制次数最多"),
            L("SortRecentlyCopied", "최근 복사한 순", "Recently copied", "最近コピーした順", "最近复制"),
            L("SortName", "이름순", "Name", "名前順", "按名称"),
            L("SortLabelFormat", "정렬 {0}", "Sort: {0}", "並べ替え: {0}", "排序：{0}"),
            L("DiscordPrepareHeading", "Discord 재시작이 필요합니다", "Discord needs to restart", "Discord の再起動が必要です", "需要重启 Discord"),
            L("DiscordPrepareDescription", "전송 감지를 다시 사용하려면 Discord를 접근성 모드로 재시작해야 합니다.", "Restart Discord in accessibility mode to resume sent-item detection.", "送信検出を再開するには、Discord をアクセシビリティモードで再起動する必要があります。", "要恢复发送检测，需要以无障碍模式重启 Discord。"),
            L("Later", "나중에", "Later", "後で", "稍后"),
            L("ApplyNow", "지금 적용", "Apply now", "今すぐ適用", "立即应用"),
            L("Copy", "복사", "Copy", "コピー", "复制"),
            L("Copied", "복사됨", "Copied", "コピー済み", "已复制"),
            L("CopyFailedShort", "복사 실패", "Copy failed", "コピー失敗", "复制失败"),
            L("OpenOriginal", "원본 열기", "Open original", "元を開く", "打开原文件"),
            L("Delete", "삭제", "Delete", "削除", "删除"),
            L("CopyToClipboard", "클립보드에 복사", "Copy to clipboard", "クリップボードにコピー", "复制到剪贴板"),
            L("OpenPreview", "원본 바로 열기", "Open original", "元をすぐ開く", "直接打开原文件"),
            L("SelectedCountFormat", "{0:N0}개 선택", "{0:N0} selected", "{0:N0}件選択", "已选择 {0:N0} 项"),
            L("SelectVisible", "전체 선택", "Select all", "すべて選択", "全选"),
            L("ClearSelection", "선택 취소", "Clear selection", "選択を解除", "取消选择"),
            L("DeleteSelected", "선택 항목 삭제", "Delete selected", "選択項目を削除", "删除所选项目"),
            L("LoadingLibrary", "보관함을 불러오는 중", "Loading library", "ライブラリを読み込み中", "正在加载收藏库"),
            L("NoItems", "아직 보관된 항목이 없습니다", "Nothing saved yet", "まだ保存された項目はありません", "尚未保存任何项目"),
            L("NoItemsDescription", "메신저에 URL이나 사진을 붙여넣어 보세요.", "Paste a URL or photo into a messenger.", "メッセンジャーに URL や写真を貼り付けてみてください。", "请在聊天应用中粘贴链接或图片。"),
            L("NoSearchResults", "검색 결과가 없습니다", "No results found", "検索結果がありません", "没有搜索结果"),
            L("NoSearchResultsDescription", "다른 검색어나 필터로 다시 찾아보세요.", "Try another search term or filter.", "別の検索語やフィルターをお試しください。", "请尝试其他关键词或筛选条件。"),
            L("LoadFailed", "보관함을 불러오지 못했습니다", "Could not load the library", "ライブラリを読み込めませんでした", "无法加载收藏库"),
            L("Retry", "다시 시도", "Try again", "再試行", "重试"),
            L("CloseNotification", "알림 닫기", "Dismiss notification", "通知を閉じる", "关闭通知"),
            L("DetailTitle", "Sentory 항목 상세", "Sentory Item Details", "Sentory 項目の詳細", "Sentory 项目详情"),
            L("FavoriteMarked", "★ 즐겨찾기", "★ Favorite", "★ お気に入り", "★ 已收藏"),
            L("CaptureCount", "저장 횟수", "Times saved", "保存回数", "保存次数"),
            L("CopyCount", "복사 횟수", "Times copied", "コピー回数", "复制次数"),
            L("LastSource", "마지막 출처", "Latest source", "最後の送信元", "最近来源"),
            L("LastSaved", "마지막 저장", "Last saved", "最終保存", "最后保存"),
            L("OpenPhoto", "사진 열기", "Open photo", "写真を開く", "打开图片"),
            L("OpenLink", "링크 열기", "Open link", "リンクを開く", "打开链接"),
            L("CopyPhoto", "사진 복사", "Copy photo", "写真をコピー", "复制图片"),
            L("CopyUrl", "URL 복사", "Copy URL", "URL をコピー", "复制 URL"),
            L("CopyCollection", "묶음 복사", "Copy collection", "まとめてコピー", "复制组合"),
            L("CollectionLinks", "링크", "Links", "リンク", "链接"),
            L("CurrentPhoto", "현재 사진", "Current photo", "現在の写真", "当前图片"),
            L("PreviousPhoto", "이전 사진", "Previous photo", "前の写真", "上一张图片"),
            L("NextPhoto", "다음 사진", "Next photo", "次の写真", "下一张图片"),
            L("CopyCurrentPhoto", "현재 사진 복사", "Copy current photo", "現在の写真をコピー", "复制当前图片"),
            L("PreviousLink", "이전 링크", "Previous link", "前のリンク", "上一个链接"),
            L("NextLink", "다음 링크", "Next link", "次のリンク", "下一个链接"),
            L("PhotoPositionFormat", "{0} / {1}", "{0} / {1}", "{0} / {1}", "{0} / {1}"),
            L("StoredPhoto", "저장된 사진", "Saved photo", "保存された写真", "已保存图片"),
            L("MissingPhotoPath", "사진 파일 경로를 찾지 못했습니다.", "Photo file path was not found.", "写真ファイルのパスが見つかりません。", "找不到图片文件路径。"),
            L("TimesFormat", "{0:N0}회", "{0:N0}", "{0:N0}回", "{0:N0} 次"),
            L("OpenLibrary", "보관함 열기", "Open library", "ライブラリを開く", "打开收藏库"),
            L("QuickSettingsTitle", "Sentory 빠른 설정", "Sentory Quick Settings", "Sentory クイック設定", "Sentory 快速设置"),
            L("DoubleClick", "더블클릭", "Double-click", "ダブルクリック", "双击"),
            L("PauseDetection", "감지 일시정지", "Pause detection", "検出を一時停止", "暂停检测"),
            L("ResumeDetection", "감지 다시 시작", "Resume detection", "検出を再開", "恢复检测"),
            L("DiscordAutoConnect", "Discord 자동 연결", "Discord auto-connect", "Discord 自動接続", "Discord 自动连接"),
            L("AccessibilityMode", "접근성 모드로 시작", "Start in accessibility mode", "アクセシビリティモードで開始", "以无障碍模式启动"),
            L("DiscordReconnect", "Discord 재시작 후 연결", "Restart and reconnect Discord", "Discord を再起動して接続", "重启并重新连接 Discord"),
            L("ExitSentory", "Sentory 종료", "Exit Sentory", "Sentory を終了", "退出 Sentory"),
            L("Cancel", "취소", "Cancel", "キャンセル", "取消"),
            L("Confirm", "확인", "OK", "確認", "确定"),
            L("StateReady", "감지 준비 완료", "Ready", "検出準備完了", "检测已就绪"),
            L("StateReconnect", "Discord 재연결 필요", "Discord reconnect required", "Discord の再接続が必要", "需要重新连接 Discord"),
            L("StateRecovering", "워커 복구 중", "Recovering worker", "ワーカーを復旧中", "正在恢复工作进程"),
            L("StateConnecting", "연결 준비 중", "Preparing connection", "接続準備中", "正在准备连接"),
            L("KakaoTalk", "카카오톡", "KakaoTalk", "カカオトーク", "KakaoTalk"),
            L("Image", "사진", "Photo", "写真", "图片"),
            L("Link", "링크", "Link", "リンク", "链接"),
            L("Collection", "묶음", "Collection", "まとめ", "组合"),
            L("CollectionTitleFormat", "사진 {0}개 · 링크 {1}개", "{0} photos · {1} links", "写真 {0}件・リンク {1}件", "{0} 张图片 · {1} 个链接"),
            L("CollectionItemsFormat", "항목 {0}개", "{0} items", "{0}件", "{0} 项"),
            L("ClipboardImage", "클립보드 이미지", "Clipboard image", "クリップボード画像", "剪贴板图片"),
            L("SavedLink", "저장된 링크", "Saved link", "保存されたリンク", "已保存链接"),
            L("PngImage", "PNG 이미지", "PNG image", "PNG 画像", "PNG 图片"),
            L("ImageFormatFormat", "{0} 이미지", "{0} image", "{0} 画像", "{0} 图片"),
            L("SavedOnInput", "입력 시 저장됨", "Saved on paste", "入力時に保存", "粘贴时保存"),
            L("DiscordSent", "전송 시 저장됨", "Saved on send", "送信時に保存", "发送时保存"),
            L("SentConfirmed", "전송 확인됨", "Send confirmed", "送信確認済み", "已确认发送"),
            L("FavoriteAdd", "즐겨찾기에 추가", "Add to favorites", "お気に入りに追加", "添加到收藏"),
            L("FavoriteRemove", "즐겨찾기에서 제거", "Remove from favorites", "お気に入りから削除", "从收藏中移除"),
            L("SelectItem", "항목 선택", "Select item", "項目を選択", "选择项目"),
            L("DeselectItem", "선택 해제", "Deselect item", "選択を解除", "取消选择"),
            L("CopyUsageFormat", "복사 {0:N0}회", "Copied {0:N0} times", "コピー {0:N0}回", "已复制 {0:N0} 次"),
            L("ItemAutomationFormat", "{0}, {1}, {2}, {3:N0}회 저장, {4:N0}회 복사", "{0}, {1}, {2}, saved {3:N0} times, copied {4:N0} times", "{0}、{1}、{2}、{3:N0}回保存、{4:N0}回コピー", "{0}，{1}，{2}，保存 {3:N0} 次，复制 {4:N0} 次"),
            L("ItemsCountFormat", "{0:N0}개", "{0:N0} items", "{0:N0}件", "{0:N0} 项"),
            L("KindsCountFormat", "링크 {0:N0} · 사진 {1:N0}", "Links {0:N0} · Photos {1:N0}", "リンク {0:N0} · 写真 {1:N0}", "链接 {0:N0} · 图片 {1:N0}"),
            L("FavoritesPreservedFormat", "즐겨찾기 {0:N0}개 보존 중", "{0:N0} favorites preserved", "お気に入り {0:N0}件を保持", "保留 {0:N0} 个收藏项目"),
            L("StatisticsLoadFailed", "데이터 현황을 불러오지 못했습니다.", "Could not load storage statistics.", "データの状況を読み込めませんでした。", "无法加载数据统计。"),
            L("DarkModeApplied", "다크 모드를 적용했습니다.", "Dark mode applied.", "ダークモードを適用しました。", "已应用深色模式。"),
            L("LightModeApplied", "라이트 모드를 적용했습니다.", "Light mode applied.", "ライトモードを適用しました。", "已应用浅色模式。"),
            L("ThemeSaveFailed", "테마 설정을 저장하지 못했습니다.", "Could not save the theme setting.", "テーマ設定を保存できませんでした。", "无法保存主题设置。"),
            L("LanguageApplied", "언어를 변경했습니다.", "Language changed.", "言語を変更しました。", "语言已更改。"),
            L("LanguageSaveFailed", "언어 설정을 저장하지 못했습니다.", "Could not save the language setting.", "言語設定を保存できませんでした。", "无法保存语言设置。"),
            L("StartupEnabled", "Windows 자동 실행을 켰습니다.", "Start with Windows is on.", "Windows 自動起動をオンにしました。", "已开启 Windows 自动启动。"),
            L("StartupDisabled", "Windows 자동 실행을 껐습니다.", "Start with Windows is off.", "Windows 自動起動をオフにしました。", "已关闭 Windows 自动启动。"),
            L("StartupChangeFailed", "자동 실행 설정을 변경하지 못했습니다.", "Could not change the startup setting.", "自動起動設定を変更できませんでした。", "无法更改自动启动设置。"),
            L("DiscordDetectionEnabled", "Discord 감지를 켰습니다.", "Discord detection is on.", "Discord 検出をオンにしました。", "已开启 Discord 检测。"),
            L("DiscordDetectionDisabled", "Discord 감지를 껐습니다.", "Discord detection is off.", "Discord 検出をオフにしました。", "已关闭 Discord 检测。"),
            L("DiscordSettingFailed", "Discord 감지 설정을 저장하지 못했습니다.", "Could not save the Discord detection setting.", "Discord 検出設定を保存できませんでした。", "无法保存 Discord 检测设置。"),
            L("KakaoDetectionEnabled", "카카오톡 감지를 켰습니다.", "KakaoTalk detection is on.", "カカオトーク検出をオンにしました。", "已开启 KakaoTalk 检测。"),
            L("KakaoDetectionDisabled", "카카오톡 감지를 껐습니다.", "KakaoTalk detection is off.", "カカオトーク検出をオフにしました。", "已关闭 KakaoTalk 检测。"),
            L("KakaoSettingFailed", "카카오톡 감지 설정을 저장하지 못했습니다.", "Could not save the KakaoTalk detection setting.", "カカオトーク検出設定を保存できませんでした。", "无法保存 KakaoTalk 检测设置。"),
            L("OpenDataFolderFailed", "데이터 폴더를 열지 못했습니다.", "Could not open the data folder.", "データフォルダーを開けませんでした。", "无法打开数据文件夹。"),
            L("OpenGitHubFailed", "GitHub 페이지를 열지 못했습니다.", "Could not open the GitHub page.", "GitHub ページを開けませんでした。", "无法打开 GitHub 页面。"),
            L("AllNonFavoriteItems", "즐겨찾기가 아닌 모든 항목", "all non-favorite items", "お気に入り以外のすべての項目", "所有非收藏项目"),
            L("AutoCleanupDisabled", "자동 정리를 사용하지 않습니다.", "Automatic cleanup is disabled.", "自動整理を使用しません。", "自动清理已关闭。"),
            L("AutoCleanupSavedFormat", "{0}일 기준 자동 정리를 저장했습니다.", "Automatic cleanup after {0} days was saved.", "{0}日基準の自動整理を保存しました。", "已保存按 {0} 天自动清理的设置。"),
            L("AutoCleanupSaveFailed", "자동 정리 설정을 저장하지 못했습니다.", "Could not save automatic cleanup.", "自動整理設定を保存できませんでした。", "无法保存自动清理设置。"),
            L("NothingToCleanup", "정리할 항목이 없습니다.", "There is nothing to clean up.", "整理する項目はありません。", "没有可清理的项目。"),
            L("CleanupConfirmMessage", "{0} {1:N0}개를 삭제할까요?\n\n링크 {2:N0}개 · 사진 {3:N0}개 ({4})\n즐겨찾기는 삭제되지 않습니다.", "Delete {1:N0} {0}?\n\nLinks {2:N0} · Photos {3:N0} ({4})\nFavorites will not be deleted.", "{0} {1:N0}件を削除しますか？\n\nリンク {2:N0}件 · 写真 {3:N0}件 ({4})\nお気に入りは削除されません。", "要删除 {1:N0} 个{0}吗？\n\n链接 {2:N0} · 图片 {3:N0}（{4}）\n收藏项目不会被删除。"),
            L("CleanupConfirmHeading", "항목을 정리할까요?", "Clean up items?", "項目を整理しますか？", "要清理项目吗？"),
            L("DeleteAll", "모두 삭제", "Delete all", "すべて削除", "全部删除"),
            L("CleanupCancelled", "정리를 취소했습니다.", "Cleanup cancelled.", "整理をキャンセルしました。", "已取消清理。"),
            L("CleanupCompleteFormat", "{0:N0}개 항목을 정리했습니다.", "Cleaned up {0:N0} items.", "{0:N0}件を整理しました。", "已清理 {0:N0} 个项目。"),
            L("CleanupPartialFormat", "{0:N0}개를 정리했지만 일부 사진 파일은 다음 실행 때 다시 정리합니다.", "Cleaned up {0:N0} items; some photo files will be retried next time.", "{0:N0}件を整理しましたが、一部の写真ファイルは次回再試行します。", "已清理 {0:N0} 个项目；部分图片文件将在下次启动时重试。"),
            L("CleanupFailed", "데이터를 정리하지 못했습니다.", "Could not clean up the data.", "データを整理できませんでした。", "无法清理数据。"),
            L("CheckingCleanup", "삭제 대상을 확인하고 있습니다...", "Checking items to delete...", "削除対象を確認しています...", "正在检查要删除的项目…"),
            L("StartupCurrentlyEnabled", "현재 Windows 로그인 시 자동으로 실행됩니다", "Currently starts when you sign in to Windows", "現在 Windows サインイン時に自動実行されます", "当前会在登录 Windows 时自动运行"),
            L("StartupCurrentlyDisabled", "현재 자동 실행을 사용하지 않습니다", "Automatic startup is currently off", "現在、自動起動は使用していません", "当前未使用自动启动"),
            L("TurnOff", "끄기", "Turn off", "オフ", "关闭"),
            L("TurnOn", "켜기", "Turn on", "オン", "开启"),
            L("StartupStatusFailed", "자동 실행 상태를 확인하지 못했습니다", "Could not check startup status", "自動起動の状態を確認できませんでした", "无法检查自动启动状态"),
            L("InUse", "사용 중", "On", "使用中", "使用中"),
            L("NotInUse", "사용 안 함", "Off", "使用しない", "未使用"),
            L("DiscordNotInUse", "Discord 감지를 사용하지 않습니다", "Discord detection is disabled", "Discord 検出を使用していません", "Discord 检测已关闭"),
            L("DiscordDetectionOn", "Discord 감지 켜짐", "Discord detection is on", "Discord 検出はオンです", "Discord 检测已开启"),
            L("KakaoNotInUse", "카카오톡 감지를 사용하지 않습니다", "KakaoTalk detection is disabled", "カカオトーク検出を使用していません", "KakaoTalk 检测已关闭"),
            L("DevelopmentVersion", "개발 버전", "Development build", "開発版", "开发版本"),
            L("Starting", "시작 중...", "Starting...", "起動中...", "正在启动…"),
            L("AlreadyRunningHeading", "Sentory가 이미 실행 중입니다", "Sentory is already running", "Sentory はすでに実行中です", "Sentory 已在运行"),
            L("AlreadyRunningMessage", "작업 표시줄 알림 영역의 Sentory 아이콘을 확인해 주세요.", "Check the Sentory icon in the notification area.", "通知領域の Sentory アイコンを確認してください。", "请查看通知区域中的 Sentory 图标。"),
            L("StorageRepairIssue", "일부 사진 파일을 확인하지 못했습니다. 설정에서 데이터 폴더를 확인해 주세요.", "Some photo files could not be verified. Check the data folder in Settings.", "一部の写真ファイルを確認できませんでした。設定でデータフォルダーを確認してください。", "无法验证部分图片文件。请在设置中检查数据文件夹。"),
            L("StorageCheckTitle", "Sentory 데이터 확인", "Sentory data check", "Sentory データ確認", "Sentory 数据检查"),
            L("StorageCheckMessage", "일부 사진 파일을 확인하지 못했습니다. 데이터 폴더를 확인해 주세요.", "Some photo files could not be verified. Check the data folder.", "一部の写真ファイルを確認できませんでした。データフォルダーを確認してください。", "无法验证部分图片文件。请检查数据文件夹。"),
            L("StartupFailedHeading", "Sentory를 시작하지 못했습니다", "Could not start Sentory", "Sentory を起動できませんでした", "无法启动 Sentory"),
            L("StatusPaused", "상태: 감지가 일시정지되었습니다.", "Status: Detection is paused.", "状態: 検出を一時停止しています。", "状态：检测已暂停。"),
            L("StatusDetecting", "상태: Discord와 카카오톡을 감지하고 있습니다.", "Status: Detecting Discord and KakaoTalk.", "状態: Discord とカカオトークを検出しています。", "状态：正在检测 Discord 和 KakaoTalk。"),
            L("StatusDetectingDiscord", "상태: Discord를 감지하고 있습니다.", "Status: Detecting Discord.", "状態: Discord を検出しています。", "状态：正在检测 Discord。"),
            L("StatusDetectingKakao", "상태: 카카오톡을 감지하고 있습니다.", "Status: Detecting KakaoTalk.", "状態: カカオトークを検出しています。", "状态：正在检测 KakaoTalk。"),
            L("StatusDetectionDisabled", "상태: 메신저 감지를 사용하지 않습니다.", "Status: Messenger detection is off.", "状態: メッセンジャー検出はオフです。", "状态：聊天应用检测已关闭。"),
            L("TrayPaused", "Sentory - 감지 일시정지됨", "Sentory - Detection paused", "Sentory - 検出一時停止", "Sentory - 检测已暂停"),
            L("TrayDetecting", "Sentory - 메신저 감지 중", "Sentory - Detecting messengers", "Sentory - メッセンジャー検出中", "Sentory - 正在检测聊天应用"),
            L("TrayDetectionDisabled", "Sentory - 메신저 감지 꺼짐", "Sentory - Detection off", "Sentory - メッセンジャー検出オフ", "Sentory - 聊天应用检测已关闭"),
            L("DiscordPhotoSaved", "Discord에서 사진 전송을 확인해 저장했습니다.", "Saved a photo confirmed as sent in Discord.", "Discord で写真の送信を確認して保存しました。", "已保存经确认在 Discord 中发送的图片。"),
            L("DiscordUrlSaved", "Discord에서 URL 전송을 확인해 저장했습니다.", "Saved a URL confirmed as sent in Discord.", "Discord で URL の送信を確認して保存しました。", "已保存经确认在 Discord 中发送的 URL。"),
            L("DiscordUrlsSavedFormat", "Discord에서 URL {0:N0}개 전송을 확인해 저장했습니다.", "Saved {0:N0} URLs confirmed as sent in Discord.", "Discord で URL {0:N0}件の送信を確認して保存しました。", "已保存 {0:N0} 个经确认在 Discord 中发送的 URL。"),
            L("DiscordCollectionSaved", "Discord에서 여러 항목의 전송을 확인해 하나의 묶음으로 저장했습니다.", "Saved multiple Discord items as one collection.", "Discord の複数項目を1つのまとめとして保存しました。", "已将 Discord 中发送的多个项目保存为一个组合。"),
            L("InputPhotoSaved", "사진을 입력 시 저장했습니다.", "Saved the photo when pasted.", "写真を入力時に保存しました。", "已在粘贴图片时保存。"),
            L("InputUrlSaved", "URL을 입력 시 저장했습니다.", "Saved the URL when pasted.", "URL を入力時に保存しました。", "已在粘贴 URL 时保存。"),
            L("InputUrlsSavedFormat", "URL {0:N0}개를 입력 시 저장했습니다.", "Saved {0:N0} URLs when pasted.", "URL {0:N0}件を入力時に保存しました。", "已在粘贴时保存 {0:N0} 个 URL。"),
            L("InputCollectionSaved", "여러 항목을 입력 시 하나의 묶음으로 저장했습니다.", "Saved multiple pasted items as one collection.", "複数の入力項目を1つのまとめとして保存しました。", "已将粘贴的多个项目保存为一个组合。"),
            L("DiscordRecoveryIssue", "Discord 연결 복구가 필요합니다. 설정에서 다시 연결해 주세요.", "Discord needs to be reconnected. Reconnect it in Settings.", "Discord の接続復旧が必要です。設定から再接続してください。", "需要恢复 Discord 连接。请在设置中重新连接。"),
            L("CaptureIssue", "일부 입력을 처리하지 못했습니다. 감지는 계속됩니다.", "Some input could not be processed. Detection is continuing.", "一部の入力を処理できませんでした。検出は継続しています。", "部分输入无法处理，检测仍在继续。"),
            L("StatusDiscordRecovery", "상태: Discord 연결 복구가 필요합니다.", "Status: Discord reconnect required.", "状態: Discord の再接続が必要です。", "状态：需要重新连接 Discord。"),
            L("StatusCaptureIssue", "상태: 일부 입력 처리에 실패했지만 감지 중입니다.", "Status: Detection continues after an input error.", "状態: 一部の入力処理に失敗しましたが、検出中です。", "状态：部分输入处理失败，检测仍在继续。"),
            L("TrayDiscordRecovery", "Sentory - Discord 연결 복구 필요", "Sentory - Discord reconnect required", "Sentory - Discord 再接続が必要", "Sentory - 需要重新连接 Discord"),
            L("ApplyDiscordRecovery", "Sentory 보관함에서 Discord 연결을 적용해 주세요.", "Reconnect Discord from the Sentory library.", "Sentory ライブラリから Discord を再接続してください。", "请在 Sentory 收藏库中重新连接 Discord。"),
            L("StatusFormat", "상태: {0}", "Status: {0}", "状態: {0}", "状态：{0}"),
            L("ReconnectConfirmHeading", "Discord를 다시 연결할까요?", "Reconnect Discord?", "Discord を再接続しますか？", "要重新连接 Discord 吗？"),
            L("ReconnectConfirmMessage", "Discord를 접근성 모드로 다시 시작합니다. 작성 중인 메시지와 진행 중인 통화가 종료될 수 있습니다.", "Discord will restart in accessibility mode. Draft messages and active calls may be ended.", "Discord をアクセシビリティモードで再起動します。作成中のメッセージや通話が終了する場合があります。", "Discord 将以无障碍模式重启。正在编辑的消息和通话可能会结束。"),
            L("Restart", "다시 시작", "Restart", "再起動", "重新启动"),
            L("DiscordRestarted", "Discord를 연결 복구 모드로 다시 시작했습니다.", "Discord restarted in connection recovery mode.", "Discord を接続復旧モードで再起動しました。", "Discord 已以连接恢复模式重新启动。"),
            L("DiscordRepairFailed", "Discord 연결을 복구하지 못했습니다. Discord를 종료한 뒤 다시 시도해 주세요.", "Could not repair the Discord connection. Exit Discord and try again.", "Discord 接続を復旧できませんでした。Discord を終了して再試行してください。", "无法恢复 Discord 连接。请退出 Discord 后重试。"),
            L("AutoCleanupTitle", "Sentory 자동 정리", "Sentory automatic cleanup", "Sentory 自動整理", "Sentory 自动清理"),
            L("AutoCleanupCompletedFormat", "즐겨찾기를 제외한 {0:N0}개 항목을 정리했습니다.", "Cleaned up {0:N0} non-favorite items.", "お気に入り以外の {0:N0}件を整理しました。", "已清理 {0:N0} 个非收藏项目。"),
            L("AutoCleanupFailedNextTime", "자동 정리를 완료하지 못했습니다. 다음 실행 때 다시 시도합니다.", "Automatic cleanup could not finish and will retry next time.", "自動整理を完了できませんでした。次回起動時に再試行します。", "自动清理未能完成，将在下次启动时重试。"),
            L("NoRecentIssue", "최근 문제 없음", "No recent issues", "最近の問題なし", "最近没有问题"),
            L("RecentIssueFormat", "최근 문제 {0}", "Recent issue: {0}", "最近の問題: {0}", "最近的问题：{0}"),
            L("SelectedFavoritesWarningFormat", "\n\n즐겨찾기 {0:N0}개도 선택되어 함께 삭제됩니다.", "\n\n{0:N0} selected favorites will also be deleted.", "\n\n選択したお気に入り {0:N0}件も削除されます。", "\n\n所选项目中有 {0:N0} 个收藏项目，也将被删除。"),
            L("DeleteSelectedHeadingFormat", "선택한 {0:N0}개 항목을 삭제할까요?", "Delete {0:N0} selected items?", "選択した {0:N0}件を削除しますか？", "要删除所选的 {0:N0} 个项目吗？"),
            L("DeleteSelectedMessage", "선택한 항목과 저장된 사진 파일을 보관함에서 삭제합니다.", "The selected items and saved photo files will be removed from the library.", "選択した項目と保存された写真ファイルをライブラリから削除します。", "将从收藏库中删除所选项目及保存的图片文件。"),
            L("CannotUndoLine", "\n이 작업은 되돌릴 수 없습니다.", "\nThis cannot be undone.", "\nこの操作は元に戻せません。", "\n此操作无法撤销。"),
            L("DeletedItemsFormat", "{0:N0}개 항목을 삭제했습니다.", "Deleted {0:N0} items.", "{0:N0}件を削除しました。", "已删除 {0:N0} 个项目。"),
            L("DeletedItemsMissingFormat", "{0:N0}개를 삭제했고 {1:N0}개는 이미 없었습니다.", "Deleted {0:N0} items; {1:N0} were already missing.", "{0:N0}件を削除し、{1:N0}件はすでにありませんでした。", "已删除 {0:N0} 个项目，另有 {1:N0} 个已不存在。"),
            L("DeleteSelectedFailed", "선택한 항목을 삭제하지 못했습니다.", "Could not delete the selected items.", "選択した項目を削除できませんでした。", "无法删除所选项目。"),
            L("SortSaveFailed", "정렬 설정을 저장하지 못했습니다.", "Could not save the sort setting.", "並べ替え設定を保存できませんでした。", "无法保存排序设置。"),
            L("FilterSaveFailed", "필터 설정을 저장하지 못했습니다.", "Could not save the filter setting.", "フィルター設定を保存できませんでした。", "无法保存筛选设置。"),
            L("ItemNotFound", "항목을 찾지 못했습니다.", "Item not found.", "項目が見つかりません。", "找不到项目。"),
            L("FavoriteAdded", "즐겨찾기에 추가했습니다.", "Added to favorites.", "お気に入りに追加しました。", "已添加到收藏。"),
            L("FavoriteRemoved", "즐겨찾기에서 제거했습니다.", "Removed from favorites.", "お気に入りから削除しました。", "已从收藏中移除。"),
            L("FavoriteChangeFailed", "즐겨찾기를 변경하지 못했습니다.", "Could not update favorites.", "お気に入りを変更できませんでした。", "无法更新收藏。"),
            L("PhotoFileNotFound", "사진 파일을 찾지 못했습니다.", "Photo file not found.", "写真ファイルが見つかりません。", "找不到图片文件。"),
            L("PhotoCopied", "사진을 복사했습니다.", "Photo copied.", "写真をコピーしました。", "图片已复制。"),
            L("UrlCopied", "URL을 복사했습니다.", "URL copied.", "URL をコピーしました。", "URL 已复制。"),
            L("CollectionCopied", "묶음 항목을 클립보드에 복사했습니다.", "Collection copied to the clipboard.", "まとめた項目をクリップボードにコピーしました。", "组合项目已复制到剪贴板。"),
            L("ClipboardBusy", "클립보드가 사용 중입니다. 다시 눌러 주세요.", "The clipboard is busy. Try again.", "クリップボードが使用中です。もう一度お試しください。", "剪贴板正忙，请重试。"),
            L("CopyHistorySaveFailed", "복사했지만 사용 기록을 저장하지 못했습니다.", "Copied, but the usage history could not be saved.", "コピーしましたが、使用履歴を保存できませんでした。", "已复制，但无法保存使用记录。"),
            L("OriginalNotFound", "원본을 찾지 못했습니다.", "Original not found.", "元のデータが見つかりません。", "找不到原文件。"),
            L("OpenOriginalFailed", "원본을 열지 못했습니다.", "Could not open the original.", "元のデータを開けませんでした。", "无法打开原文件。"),
            L("FavoriteDeleteWarning", "\n\n이 항목은 즐겨찾기에 등록되어 있습니다.", "\n\nThis item is a favorite.", "\n\nこの項目はお気に入りに登録されています。", "\n\n此项目已收藏。"),
            L("DeleteItemHeading", "항목을 삭제할까요?", "Delete this item?", "この項目を削除しますか？", "要删除此项目吗？"),
            L("DeleteItemMessage", "이 항목을 보관함에서 삭제합니다.", "This item will be removed from the library.", "この項目をライブラリから削除します。", "将从收藏库中删除此项目。"),
            L("Deleted", "삭제했습니다.", "Deleted.", "削除しました。", "已删除。"),
            L("DeleteFileFailed", "파일을 삭제하지 못했습니다.", "Could not delete the file.", "ファイルを削除できませんでした。", "无法删除文件。"),
            L("AllMessengers", "전체 메신저", "All messengers", "すべてのメッセンジャー", "所有聊天应用"),
            L("FilterActiveFormat", "필터 {0}개 적용됨", "{0} filters active", "フィルター {0}件適用中", "已应用 {0} 个筛选条件")
        };

        return entries.ToDictionary(entry => entry.Key, StringComparer.Ordinal);
    }

    private static LocalizedText L(
        string key,
        string korean,
        string english,
        string japanese,
        string chinese) =>
        new(key, korean, english, japanese, chinese);

    internal sealed record LanguageOption(string Code, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record LocalizedText(
        string Key,
        string Korean,
        string English,
        string Japanese,
        string Chinese)
    {
        public string Get(string language) =>
            language switch
            {
                "en-US" => English,
                "ja-JP" => Japanese,
                "zh-CN" => Chinese,
                _ => Korean
            };
    }
}
