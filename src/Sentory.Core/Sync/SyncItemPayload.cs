namespace Sentory.Core.Sync;

public static class SyncItemContentKinds
{
    public const string Url = "url";
    public const string Image = "image";
}

public sealed record SyncUrlContent(
    string OriginalUrl,
    string NormalizedUrl,
    string Domain);

public sealed record SyncImageContent(
    string ContentSha256,
    long ByteSize,
    int PixelWidth,
    int PixelHeight,
    string MimeType,
    string FileExtension,
    string? OriginalFileName);

public sealed class SyncItemPayload
{
    public const int CurrentPayloadVersion = 1;
    public const int MaximumContextHashLength = 512;
    public const int MaximumIdentifierLength = 64;
    public const int MaximumSignalCount = 32;
    public const int MaximumSignalLength = 512;
    public const int MaximumUrlLength = 16 * 1024;
    public const int MaximumDomainLength = 255;
    public const int MaximumFileNameLength = 512;
    public const long MaximumImageBytes = 100L * 1024 * 1024;
    private readonly string[] _confirmationSignals;

    public SyncItemPayload(
        int payloadVersion,
        string contentKind,
        string sourceApp,
        string captureMethod,
        string deliveryStatus,
        string contextHash,
        DateTimeOffset capturedAt,
        IReadOnlyList<string> confirmationSignals,
        SyncUrlContent? url,
        SyncImageContent? image)
    {
        ArgumentNullException.ThrowIfNull(confirmationSignals);
        ValidateVersion(payloadVersion);
        ValidateIdentifier(contentKind, nameof(contentKind));
        ValidateIdentifier(sourceApp, nameof(sourceApp));
        ValidateIdentifier(captureMethod, nameof(captureMethod));
        ValidateIdentifier(deliveryStatus, nameof(deliveryStatus));
        ValidateText(
            contextHash,
            MaximumContextHashLength,
            nameof(contextHash));

        if (confirmationSignals.Count > MaximumSignalCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(confirmationSignals),
                $"확인 신호는 {MaximumSignalCount}개 이하여야 합니다.");
        }

        _confirmationSignals = confirmationSignals
            .Select((signal, index) =>
            {
                ValidateText(
                    signal,
                    MaximumSignalLength,
                    $"{nameof(confirmationSignals)}[{index}]");
                return signal;
            })
            .ToArray();

        switch (contentKind)
        {
            case SyncItemContentKinds.Url:
                if (url is null || image is not null)
                {
                    throw new ArgumentException(
                        "URL 동기화 본문에는 URL 정보만 있어야 합니다.");
                }

                ValidateUrl(url);
                break;
            case SyncItemContentKinds.Image:
                if (image is null || url is not null)
                {
                    throw new ArgumentException(
                        "사진 동기화 본문에는 사진 정보만 있어야 합니다.");
                }

                ValidateImage(image);
                break;
            default:
                throw new NotSupportedException(
                    $"지원하지 않는 동기화 콘텐츠 종류입니다: {contentKind}");
        }

        PayloadVersion = payloadVersion;
        ContentKind = contentKind;
        SourceApp = sourceApp;
        CaptureMethod = captureMethod;
        DeliveryStatus = deliveryStatus;
        ContextHash = contextHash;
        CapturedAt = capturedAt;
        Url = url;
        Image = image;
    }

    public int PayloadVersion { get; }

    public string ContentKind { get; }

    public string SourceApp { get; }

    public string CaptureMethod { get; }

    public string DeliveryStatus { get; }

    public string ContextHash { get; }

    public DateTimeOffset CapturedAt { get; }

    public IReadOnlyList<string> ConfirmationSignals =>
        _confirmationSignals.ToArray();

    public SyncUrlContent? Url { get; }

    public SyncImageContent? Image { get; }

    public static SyncItemPayload CreateUrl(
        SyncUrlContent url,
        SourceApp sourceApp,
        CaptureMethod captureMethod,
        DeliveryStatus deliveryStatus,
        string contextHash,
        DateTimeOffset capturedAt,
        IReadOnlyList<string> confirmationSignals) =>
        new(
            CurrentPayloadVersion,
            SyncItemContentKinds.Url,
            sourceApp.ToString(),
            captureMethod.ToString(),
            deliveryStatus.ToString(),
            contextHash,
            capturedAt,
            confirmationSignals,
            url,
            null);

    public static SyncItemPayload CreateImage(
        SyncImageContent image,
        SourceApp sourceApp,
        CaptureMethod captureMethod,
        DeliveryStatus deliveryStatus,
        string contextHash,
        DateTimeOffset capturedAt,
        IReadOnlyList<string> confirmationSignals) =>
        new(
            CurrentPayloadVersion,
            SyncItemContentKinds.Image,
            sourceApp.ToString(),
            captureMethod.ToString(),
            deliveryStatus.ToString(),
            contextHash,
            capturedAt,
            confirmationSignals,
            null,
            image);

    private static void ValidateVersion(int payloadVersion)
    {
        if (payloadVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(payloadVersion));
        }

        if (payloadVersion > CurrentPayloadVersion)
        {
            throw new NotSupportedException(
                "현재 Sentory보다 새로운 항목 동기화 형식입니다.");
        }
    }

    private static void ValidateUrl(SyncUrlContent url)
    {
        ValidateText(
            url.OriginalUrl,
            MaximumUrlLength,
            nameof(url.OriginalUrl));
        ValidateText(
            url.NormalizedUrl,
            MaximumUrlLength,
            nameof(url.NormalizedUrl));
        ValidateText(
            url.Domain,
            MaximumDomainLength,
            nameof(url.Domain));
    }

    private static void ValidateImage(SyncImageContent image)
    {
        if (!SyncHash.IsSha256(image.ContentSha256))
        {
            throw new ArgumentException(
                "사진 콘텐츠 SHA-256 형식이 올바르지 않습니다.",
                nameof(image.ContentSha256));
        }

        if (image.ByteSize <= 0 ||
            image.ByteSize > MaximumImageBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(image.ByteSize));
        }

        if (image.PixelWidth <= 0 || image.PixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(image),
                "사진 크기는 0보다 커야 합니다.");
        }

        ValidateText(image.MimeType, 128, nameof(image.MimeType));
        ValidateText(
            image.FileExtension,
            16,
            nameof(image.FileExtension));
        if (image.FileExtension[0] != '.' ||
            image.FileExtension.Any(character =>
                character != '.' &&
                !char.IsAsciiLetterOrDigit(character)))
        {
            throw new ArgumentException(
                "사진 파일 확장자 형식이 올바르지 않습니다.",
                nameof(image.FileExtension));
        }

        if (image.OriginalFileName is not null &&
            image.OriginalFileName.Length > MaximumFileNameLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(image.OriginalFileName));
        }
    }

    private static void ValidateIdentifier(
        string value,
        string parameterName)
    {
        ValidateText(value, MaximumIdentifierLength, parameterName);
        if (value.Any(character =>
                !char.IsAsciiLetterOrDigit(character) &&
                character is not '-' and not '_' and not '.'))
        {
            throw new ArgumentException(
                "동기화 식별자 형식이 올바르지 않습니다.",
                parameterName);
        }
    }

    private static void ValidateText(
        string value,
        int maximumLength,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximumLength)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

public static class SyncHash
{
    public static bool IsSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(Uri.IsHexDigit);
}
