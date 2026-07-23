using System.Windows;

namespace Sentory.App.Tests;

public sealed class SentoryLocalizationTests
{
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
