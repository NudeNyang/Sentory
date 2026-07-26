using System.Windows;

namespace Sentory.App.Tests;

public sealed class SentoryLocalizationTests
{
    [Fact]
    public void AutomaticFavoriteUsesConciseRepeatedUsageDescription()
    {
        SentoryLocalization.Apply(new ResourceDictionary(), "ko-KR");

        Assert.Equal(
            "같은 링크나 사진을 반복해서 사용하면 즐겨찾기에 추가합니다",
            SentoryLocalization.Text("AutoFavoriteDescription"));
        Assert.Equal(
            "3회 반복 사용 후 추가",
            SentoryLocalization.Format(
                "AutoFavoriteCopyCountFormat",
                3));
    }

    [Fact]
    public void EmptyLibraryDescriptionUsesMessengerPasteInstruction()
    {
        SentoryLocalization.Apply(new ResourceDictionary(), "ko-KR");

        Assert.Equal(
            "메신저에 URL이나 사진을 붙여넣어 보세요.",
            SentoryLocalization.Text("NoItemsDescription"));
    }

    [Fact]
    public void SystemThemeAppliedMessageNamesTheSelectedMode()
    {
        SentoryLocalization.Apply(new ResourceDictionary(), "ko-KR");

        Assert.Equal(
            "시스템 테마 모드를 적용했습니다.",
            SentoryLocalization.Text("SystemThemeApplied"));
    }

    [Fact]
    public void ManualUpdateCheckExplainsImmediateCheckAndResult()
    {
        SentoryLocalization.Apply(new ResourceDictionary(), "ko-KR");

        Assert.Equal("지금 확인", SentoryLocalization.Text("CheckNow"));
        Assert.Contains(
            "자동 확인 대기 시간과 관계없이",
            SentoryLocalization.Text("CheckForUpdatesDescription"));
        Assert.Equal(
            "현재 최신 버전을 사용하고 있습니다.",
            SentoryLocalization.Text("AppIsUpToDate"));
    }

    [Theory]
    [InlineData("ko-KR", "감지 일시정지됨")]
    [InlineData("en-US", "Detection paused")]
    [InlineData("ja-JP", "検出一時停止中")]
    [InlineData("zh-CN", "检测已暂停")]
    public void DetectionPausedStatusIsLocalized(
        string language,
        string expected)
    {
        SentoryLocalization.Apply(new ResourceDictionary(), language);

        Assert.Equal(expected, SentoryLocalization.Text("DetectionPaused"));
    }

    [Theory]
    [InlineData("ko-KR", "컴퓨터 · 모바일 간 동기화", "클라우드 공유", "동기화 중")]
    [InlineData("en-US", "Computer and mobile sync", "Cloud sharing", "Syncing")]
    [InlineData("ja-JP", "パソコン・モバイル間同期", "クラウド共有", "同期中")]
    [InlineData("zh-CN", "电脑与手机同步", "云端共享", "正在同步")]
    public void ComputerSyncSettingsAreLocalized(
        string language,
        string expectedHeading,
        string expectedCloudHeading,
        string expectedState)
    {
        SentoryLocalization.Apply(new ResourceDictionary(), language);

        Assert.Equal(
            expectedHeading,
            SentoryLocalization.Text("ComputerSync"));
        Assert.Equal(
            expectedCloudHeading,
            SentoryLocalization.Text("ComputerSyncHeading"));
        Assert.Equal(
            expectedState,
            SentoryLocalization.Text("SyncStateSyncing"));
        Assert.NotEqual(
            "ChooseSyncFolder",
            SentoryLocalization.Text("ChooseSyncFolder"));
        Assert.NotEqual(
            "SyncStateMigrating",
            SentoryLocalization.Text("SyncStateMigrating"));
    }

    [Fact]
    public void ComputerSyncExplainsPlaintextCloudAccess()
    {
        SentoryLocalization.Apply(new ResourceDictionary(), "ko-KR");

        var description = SentoryLocalization.Text(
            "ComputerSyncDescription");
        Assert.Contains("일반 파일", description);
        Assert.Contains("접근할 수 있는 사람", description);
        Assert.Equal(
            "동기화 시작",
            SentoryLocalization.Text("StartSync"));
        Assert.Equal(
            "폴더 선택",
            SentoryLocalization.Text("SelectSyncFolder"));
        Assert.Contains(
            "자동으로 만듭니다",
            SentoryLocalization.Format(
                "SyncAutomaticLocationFormat",
                "Google Drive"));
    }

    [Theory]
    [InlineData("ko-KR", "지금 재시작")]
    [InlineData("en-US", "Restart now")]
    [InlineData("ja-JP", "今すぐ再起動")]
    [InlineData("zh-CN", "立即重新启动")]
    public void AutomaticDiscordRestartPromptIsLocalized(
        string language,
        string expectedButton)
    {
        SentoryLocalization.Apply(new ResourceDictionary(), language);

        Assert.Equal(
            expectedButton,
            SentoryLocalization.Text("RestartNow"));
        Assert.Contains(
            "15",
            SentoryLocalization.Format(
                "AutomaticReconnectMessageFormat",
                15));
    }

    [Fact]
    public void LicenseWindowDescribesBundledThirdPartyNotices()
    {
        SentoryLocalization.Apply(new ResourceDictionary(), "ko-KR");

        Assert.Equal(
            "라이선스 및 제3자 고지",
            SentoryLocalization.Text("LicenseHeading"));
        Assert.Contains(
            "오픈소스 구성 요소",
            SentoryLocalization.Text("LicenseDescription"));

        var resourceNames = typeof(LicenseWindow).Assembly
            .GetManifestResourceNames();
        Assert.Contains("Sentory.LICENSE.txt", resourceNames);
        Assert.Contains("Sentory.THIRD-PARTY-NOTICES.txt", resourceNames);
        Assert.Contains("Sentory.MODEL-PROVENANCE.md", resourceNames);
    }
}
