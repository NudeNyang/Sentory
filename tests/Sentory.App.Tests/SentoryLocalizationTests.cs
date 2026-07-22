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
}
