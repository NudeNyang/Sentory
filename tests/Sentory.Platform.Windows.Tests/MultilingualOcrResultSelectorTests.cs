using Sentory.Platform.Windows.Ocr;

namespace Sentory.Platform.Windows.Tests;

public sealed class MultilingualOcrResultSelectorTests
{
    [Fact]
    public void PrefersKoreanModelWhenItFindsHangul()
    {
        var selected = MultilingualOcrResultSelector.Select(
        [
            new OcrRecognitionCandidate(
                "프로젝트 일정",
                ["프로젝트 일정"],
                "ko",
                0.82),
            new OcrRecognitionCandidate(
                "至呈弔甫 筐酊",
                ["至呈弔甫 筐酊"],
                "cjk",
                0.87)
        ]);

        Assert.Equal("프로젝트 일정", selected.Text);
        Assert.Equal("ko", selected.Language);
    }

    [Fact]
    public void PrefersCjkModelWhenItFindsJapaneseKana()
    {
        var selected = MultilingualOcrResultSelector.Select(
        [
            new OcrRecognitionCandidate(
                "空己広何乃物語",
                ["空己広何乃物語"],
                "ko",
                0.90),
            new OcrRecognitionCandidate(
                "空に広がる物語",
                ["空に広がる物語"],
                "cjk",
                0.79)
        ]);

        Assert.Equal("空に広がる物語", selected.Text);
        Assert.Equal("cjk", selected.Language);
    }

    [Fact]
    public void RejectsLowConfidenceGarbageInsteadOfCreatingATitle()
    {
        var selected = MultilingualOcrResultSelector.Select(
        [
            new OcrRecognitionCandidate("多七卜D", ["多七卜D"], "cjk", 0.31),
            new OcrRecognitionCandidate("叫刃卜口", ["叫刃卜口"], "ko", 0.28)
        ]);

        Assert.Empty(selected.Text);
        Assert.Empty(selected.Lines);
    }

    [Fact]
    public void UsesConfidenceForPlainEnglishSharedByBothModels()
    {
        var selected = MultilingualOcrResultSelector.Select(
        [
            new OcrRecognitionCandidate(
                "JAPANESE SCHOOL CLASSROOM",
                ["JAPANESE SCHOOL CLASSROOM"],
                "ko",
                0.88),
            new OcrRecognitionCandidate(
                "JAPANESE SCHOOL CLASSRO0M",
                ["JAPANESE SCHOOL CLASSRO0M"],
                "cjk",
                0.72)
        ]);

        Assert.Equal("JAPANESE SCHOOL CLASSROOM", selected.Text);
        Assert.Equal("ko", selected.Language);
    }

    [Fact]
    public void RejectsAnIsolatedHighConfidenceCharacter()
    {
        var selected = MultilingualOcrResultSelector.Select(
        [
            new OcrRecognitionCandidate("낍", ["낍"], "ko", 0.84),
            new OcrRecognitionCandidate("评", ["评"], "cjk", 0.81)
        ]);

        Assert.Empty(selected.Text);
    }
}
