using Sentory.Core;

namespace Sentory.Core.Tests;

public sealed class CaptureCollectionIdentityTests
{
    [Fact]
    public void SignatureIgnoresOrderAndDuplicateMembers()
    {
        var link = new CollectionMemberCaptureRequest(
            ContentKind.Url,
            "https://example.com",
            "https://example.com/",
            "example.com",
            ReadOnlyMemory<byte>.Empty,
            null,
            0,
            0,
            null,
            null);
        var image = new CollectionMemberCaptureRequest(
            ContentKind.Image,
            string.Empty,
            "sha256:abc",
            string.Empty,
            new byte[] { 1 },
            "abc",
            1,
            1,
            "image/png",
            ".png");

        Assert.Equal(
            CaptureCollectionIdentity.CreateSignature([link, image]),
            CaptureCollectionIdentity.CreateSignature([image, link, link]));
    }
}
