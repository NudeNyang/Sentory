using System.Text;
using Sentory.Core.Sync;

namespace Sentory.Core.Tests;

public sealed class SyncOperationTests
{
    [Fact]
    public void SerializerRoundTripsSupportedOperation()
    {
        var operation = SyncOperation.Create(
            SyncDeviceIdentity.Create(),
            7,
            Guid.NewGuid(),
            SyncOperationKind.Upsert,
            DateTimeOffset.Parse("2026-07-25T10:20:30+09:00"),
            Encoding.UTF8.GetBytes("""{"kind":"url"}"""),
            Guid.NewGuid());

        var serialized = SyncOperationSerializer.Serialize(operation);
        var restored = SyncOperationSerializer.Deserialize(serialized);

        Assert.Equal(operation.FormatVersion, restored.FormatVersion);
        Assert.Equal(operation.EncryptionMode, restored.EncryptionMode);
        Assert.Equal(operation.OperationId, restored.OperationId);
        Assert.Equal(operation.DeviceId, restored.DeviceId);
        Assert.Equal(operation.Sequence, restored.Sequence);
        Assert.Equal(operation.ItemId, restored.ItemId);
        Assert.Equal(operation.Kind, restored.Kind);
        Assert.Equal(operation.OccurredAt, restored.OccurredAt);
        Assert.Equal(operation.PayloadSha256, restored.PayloadSha256);
        Assert.Equal(operation.Payload, restored.Payload);
        Assert.Contains(
            @"""kind"":""upsert""",
            Encoding.UTF8.GetString(serialized));
    }

    [Fact]
    public void DeserializeRejectsModifiedPayload()
    {
        var operation = SyncOperation.Create(
            SyncDeviceIdentity.Create(),
            1,
            Guid.NewGuid(),
            SyncOperationKind.Upsert,
            DateTimeOffset.UtcNow,
            [1, 2, 3]);
        var serialized = Encoding.UTF8.GetString(
            SyncOperationSerializer.Serialize(operation));
        var modified = serialized.Replace(
            Convert.ToBase64String([1, 2, 3]),
            Convert.ToBase64String([1, 2, 4]),
            StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() =>
            SyncOperationSerializer.Deserialize(
                Encoding.UTF8.GetBytes(modified)));
    }

    [Fact]
    public void DeserializeRejectsNewerFormat()
    {
        var operation = new SyncOperation(
            SyncOperation.CurrentFormatVersion + 1,
            SyncOperation.NoEncryption,
            Guid.NewGuid(),
            SyncDeviceIdentity.Create(),
            1,
            Guid.NewGuid(),
            SyncOperationKind.Upsert,
            DateTimeOffset.UtcNow,
            Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData([]))
                .ToLowerInvariant(),
            []);

        Assert.Throws<NotSupportedException>(() =>
            SyncOperationSerializer.Serialize(operation));
    }

    [Fact]
    public void MaximumPayloadRoundTripsAfterBase64Expansion()
    {
        var payload = new byte[SyncOperation.MaximumPayloadBytes];
        Random.Shared.NextBytes(payload);
        var operation = SyncOperation.Create(
            SyncDeviceIdentity.Create(),
            1,
            Guid.NewGuid(),
            SyncOperationKind.Upsert,
            DateTimeOffset.UtcNow,
            payload);

        var restored = SyncOperationSerializer.Deserialize(
            SyncOperationSerializer.Serialize(operation));

        Assert.Equal(payload, restored.Payload);
    }

    [Fact]
    public void PayloadReturnedToCallerCannotModifyOperation()
    {
        var operation = SyncOperation.Create(
            SyncDeviceIdentity.Create(),
            1,
            Guid.NewGuid(),
            SyncOperationKind.Upsert,
            DateTimeOffset.UtcNow,
            [1, 2, 3]);
        var exposed = operation.Payload;

        exposed[0] = 9;

        Assert.Equal(new byte[] { 1, 2, 3 }, operation.Payload);
        Assert.True(operation.HasValidPayloadHash());
    }

    [Fact]
    public void OperationObjectKeyRoundTripsIdentity()
    {
        var operation = SyncOperation.Create(
            SyncDeviceIdentity.Create(),
            42,
            Guid.NewGuid(),
            SyncOperationKind.Delete,
            DateTimeOffset.UtcNow,
            []);

        var key = SyncOperationObjectKey.Create(operation);

        Assert.True(SyncOperationObjectKey.TryParse(
            key,
            out var deviceId,
            out var sequence,
            out var operationId));
        Assert.Equal(operation.DeviceId, deviceId);
        Assert.Equal(operation.Sequence, sequence);
        Assert.Equal(operation.OperationId, operationId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("devices/not-a-device/operations/0001-x.json")]
    [InlineData("devices/0123456789abcdef0123456789abcdef/wrong/operation.json")]
    [InlineData("devices/0123456789abcdef0123456789abcdef/operations/00000000000000000000-00000000000000000000000000000000.json")]
    public void OperationObjectKeyRejectsInvalidPath(string key)
    {
        Assert.False(SyncOperationObjectKey.TryParse(
            key,
            out _,
            out _,
            out _));
    }
}
