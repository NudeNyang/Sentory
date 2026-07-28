using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Sentory.Core;
using Sentory.Infrastructure.Data;
using Sentory.Infrastructure.Ocr;

namespace Sentory.Infrastructure.Tests;

public sealed class OcrEnrichmentServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "Sentory.Ocr.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void TitleGeneratorUsesFirstUsefulLineAndRemovesUnsafeCharacters()
    {
        var title = OcrTitleGenerator.Create(
            ["https://example.com", "  분기별: 계획/2026  ", "세부 내용"]);

        Assert.Equal("분기별 계획2026", title);
    }

    [Fact]
    public void MeaningfulMetadataTitleTakesPriorityOverOcrText()
    {
        var title = OcrTitleGenerator.CreatePreferred(
            "  2026 여름 여행 계획  ",
            ["영수증", "합계 12,000원"]);

        Assert.Equal("2026 여름 여행 계획", title);
    }

    [Fact]
    public void TitleGeneratorJoinsJapaneseQuoteAcrossOcrLines()
    {
        var title = OcrTitleGenerator.Create(
            ["「彼女と", "デ1トなう」", "って呟いて", "もいいよ?"]);

        Assert.Equal("「彼女とデートなう」", title);
    }

    [Theory]
    [InlineData("A20628AC2C2601242CB56FD690A431D2530F3E318613F029684C835E902A5639")]
    [InlineData("550e8400-e29b-41d4-a716-446655440000")]
    [InlineData("image")]
    [InlineData("제목 없음")]
    [InlineData("087")]
    [InlineData("httpssoulettebooth.pm")]
    public void MeaninglessMetadataTitleFallsBackToOcrText(string metadataTitle)
    {
        var title = OcrTitleGenerator.CreatePreferred(
            metadataTitle,
            ["프로젝트 일정"]);

        Assert.Equal("프로젝트 일정", title);
    }

    [Theory]
    [InlineData("VRChat 2025-01-28 23-02-56.776 1080x1920.png", "VRChat 2025-01-28 23-02-56.776 1080x1920")]
    [InlineData("캐릭터 설정화 최종.png", "캐릭터 설정화 최종")]
    public void MeaningfulOriginalFileNameBecomesTitle(
        string fileName,
        string expected)
    {
        Assert.Equal(expected, OcrTitleGenerator.CreateFileNameCandidate(fileName));
    }

    [Theory]
    [InlineData("image_001.png")]
    public void GeneratedFileNameYieldsToMeaningfulOcr(string fileName)
    {
        Assert.Equal(
            "프로젝트 일정",
            OcrTitleGenerator.CreateBestDisplayTitle(
                fileName,
                "프로젝트 일정"));
    }

    [Theory]
    [InlineData("2025-07-14 01.53.43.png", "2025-07-14 01.53.43")]
    [InlineData("2025-02-29 01.53.43.png", null)]
    public void DateOnlyFileNameMustContainAValidCalendarDate(
        string fileName,
        string? expected)
    {
        Assert.Equal(
            expected,
            OcrTitleGenerator.CreateFileNameCandidate(fileName));
    }

    [Theory]
    [InlineData("IMG_20250128_230256.png", "IMG_20250128_230256")]
    [InlineData("Screenshot_2025-01-28-230256.png", "Screenshot_2025-01-28-230256")]
    [InlineData("2025-08-18 03.38.48.png", "2025-08-18 03.38.48")]
    public void ClearDateFileNameTakesPriorityOverRecognizedTitle(
        string fileName,
        string expected)
    {
        Assert.Equal(
            expected,
            OcrTitleGenerator.CreateBestDisplayTitle(
                fileName,
                "{applicationVRCX,version1}"));
    }

    [Fact]
    public void GeneratedFileNameRemainsWhenOcrHasNoUsefulTitle()
    {
        Assert.Equal(
            "IMG_20250128_230256",
            OcrTitleGenerator.CreateBestDisplayTitle(
                "IMG_20250128_230256.png",
                null));
    }

    [Fact]
    public void MeaningfulFileNameBeatsShortOcrTitle()
    {
        Assert.Equal(
            "VRChat 2025-01-28 23-02-56.776 1080x1920",
            OcrTitleGenerator.CreateBestDisplayTitle(
                "VRChat 2025-01-28 23-02-56.776 1080x1920.png",
                "VRChat"));
    }

    [Theory]
    [InlineData("A20628AC2C2601242CB56FD690A431D2530F3E318613F029684C835E902A5639.png")]
    [InlineData("550e8400-e29b-41d4-a716-446655440000.jpg")]
    public void OpaqueFileNameYieldsToOcr(string fileName)
    {
        Assert.Equal(
            "프로젝트 일정",
            OcrTitleGenerator.CreateBestDisplayTitle(
                fileName,
                "프로젝트 일정"));
    }

    [Fact]
    public async Task BackgroundEnrichmentPrefersMetadataTitle()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        var repository = new SqliteCaptureRepository(paths);
        await repository.InitializeAsync();
        byte[] bytes = [7, 14, 21, 28];
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        await repository.UpsertImageAsync(CreateImageRequest(bytes, hash));
        var recognizer = new FakeRecognizer(
            new ImageTextRecognitionResult(
                "작은 OCR 문구",
                ["작은 OCR 문구"],
                "ko-KR",
                "fake-ocr"));
        var service = new OcrEnrichmentService(
            repository,
            recognizer,
            paths,
            metadataTitleReader: new FakeMetadataTitleReader("원본 작품 제목"));

        await service.EnrichBatchAsync(1);
        var item = Assert.Single(await repository.GetRecentAsync(10));

        Assert.Equal("원본 작품 제목", item.OcrDisplayName);
        Assert.Equal("작은 OCR 문구", item.OcrText);
    }

    [Fact]
    public async Task BackgroundEnrichmentStoresOneResultPerImageHash()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        var repository = new SqliteCaptureRepository(paths);
        await repository.InitializeAsync();
        byte[] bytes = [1, 4, 9, 16, 25];
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        await repository.UpsertImageAsync(CreateImageRequest(bytes, hash));
        await repository.UpsertImageAsync(
            CreateImageRequest(bytes, hash) with { EventId = Guid.NewGuid() });
        var recognizer = new FakeRecognizer(
            new ImageTextRecognitionResult(
                "프로젝트 일정\n회의는 오후 3시",
                ["프로젝트 일정", "회의는 오후 3시"],
                "ko-KR",
                "fake-ocr"));
        var service = new OcrEnrichmentService(
            repository,
            recognizer,
            paths);

        var result = await service.EnrichBatchAsync(4);
        var item = Assert.Single(await repository.GetRecentAsync(10));
        var remaining = await repository.GetPendingImageOcrAsync(
            recognizer.EngineName,
            4);

        Assert.Equal(1, result.Attempted);
        Assert.Equal(1, result.Updated);
        Assert.Equal(1, recognizer.CallCount);
        Assert.Equal("프로젝트 일정", item.OcrDisplayName);
        Assert.Equal("프로젝트 일정\n회의는 오후 3시", item.OcrText);
        Assert.Equal(ImageOcrStatus.Completed, item.OcrStatus);
        Assert.Equal("ko-KR", item.OcrLanguage);
        Assert.Empty(remaining);

        Assert.True(await repository.DeleteItemAsync(item.ItemId));
        await using var connection = new SqliteConnection(
            $"Data Source={paths.DatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM image_ocr;";
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task NewEngineReprocessesAnImagePreviouslyHandledByWindowsOcr()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        var repository = new SqliteCaptureRepository(paths);
        await repository.InitializeAsync();
        byte[] bytes = [1, 2, 3, 5, 8];
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        await repository.UpsertImageAsync(CreateImageRequest(bytes, hash));
        await repository.UpsertImageOcrAsync(new ImageOcrUpdate(
            hash,
            "多七卜D",
            "多七卜D",
            ImageOcrStatus.Completed,
            "ko-KR",
            "Windows.Media.Ocr",
            DateTimeOffset.UtcNow));

        var recognizer = new FakeRecognizer(
            new ImageTextRecognitionResult(
                "空に広がる物語",
                ["空に広がる物語"],
                "ja",
                "PaddleOCR.PP-OCRv5.Mobile"));
        var service = new OcrEnrichmentService(repository, recognizer, paths);

        var result = await service.EnrichBatchAsync(1);
        var item = Assert.Single(await repository.GetRecentAsync(10));

        Assert.Equal(1, result.Updated);
        Assert.Equal("空に広がる物語", item.OcrDisplayName);
        Assert.Empty(await repository.GetPendingImageOcrAsync(
            recognizer.EngineName,
            1));
    }

    [Fact]
    public async Task OcrMetadataIsSharedWithCollectionImageMember()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        var repository = new SqliteCaptureRepository(paths);
        await repository.InitializeAsync();
        byte[] bytes = [2, 3, 5, 7, 11];
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        await repository.UpsertCollectionAsync(
            CreateCollectionRequest(bytes, hash));

        await repository.UpsertImageOcrAsync(new ImageOcrUpdate(
            hash,
            "카페 영수증",
            "카페 영수증\n합계 12,000원",
            ImageOcrStatus.Completed,
            "ko-KR",
            "fake-ocr",
            DateTimeOffset.UtcNow));
        var collection = Assert.Single(await repository.GetRecentAsync(10));
        var image = Assert.Single(
            collection.Members!,
            member => member.Kind == ContentKind.Image);

        Assert.Equal("카페 영수증", image.OcrDisplayName);
        Assert.Contains("12,000원", image.OcrText);
        Assert.Equal(ImageOcrStatus.Completed, image.OcrStatus);
    }

    private static ImageCaptureRequest CreateImageRequest(
        byte[] bytes,
        string hash) =>
        new(
            Guid.NewGuid(),
            bytes,
            hash,
            48,
            32,
            "image/png",
            ".png",
            SourceApp.KakaoTalk,
            CaptureMethod.KakaoCtrlVImage,
            DeliveryStatus.NotObserved,
            "ocr-test",
            DateTimeOffset.UtcNow,
            ["test"]);

    private static CollectionCaptureRequest CreateCollectionRequest(
        byte[] imageBytes,
        string hash)
    {
        CollectionMemberCaptureRequest[] members =
        [
            new(
                ContentKind.Url,
                "https://example.com",
                "https://example.com/",
                "example.com",
                ReadOnlyMemory<byte>.Empty,
                null,
                0,
                0,
                null,
                null),
            new(
                ContentKind.Image,
                string.Empty,
                $"sha256:{hash.ToLowerInvariant()}",
                string.Empty,
                imageBytes,
                hash,
                10,
                10,
                "image/png",
                ".png")
        ];
        return new CollectionCaptureRequest(
            Guid.NewGuid(),
            CaptureCollectionIdentity.CreateSignature(members),
            members,
            SourceApp.Discord,
            CaptureMethod.DiscordConfirmedImage,
            DeliveryStatus.Confirmed,
            "ocr-collection-test",
            DateTimeOffset.UtcNow,
            ["test"]);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FakeRecognizer(ImageTextRecognitionResult result)
        : IImageTextRecognizer
    {
        public int CallCount { get; private set; }

        public bool IsAvailable => true;

        public string EngineName => result.EngineName;

        public Task<ImageTextRecognitionResult> RecognizeAsync(
            string imagePath,
            CancellationToken cancellationToken = default)
        {
            Assert.True(File.Exists(imagePath));
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeMetadataTitleReader(string? title)
        : IImageMetadataTitleReader
    {
        public string? ReadTitle(string imagePath) => title;
    }
}
