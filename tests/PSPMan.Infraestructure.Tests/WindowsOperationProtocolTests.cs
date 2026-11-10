using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using PSPMan.Infraestructure.Devices.Windows;

namespace PSPMan.Infraestructure.Tests;

public sealed class WindowsOperationProtocolTests
{
    private const string DeviceId = @"USBSTOR\DISK&VEN_TEST\SERIAL";
    private const string VolumeGuid = @"\\?\Volume{00000000-0000-0000-0000-000000000001}\";
    private static readonly Guid RequestId = new("10000000-0000-0000-0000-000000000001");
    private static WindowsOperationRequest Mount => new(1, RequestId, WindowsElevatedOperation.MountVolume,
        DeviceId, new(7, 1), VolumeGuid, 42);

    [Theory]
    [InlineData(WindowsElevatedOperation.MountVolume)]
    [InlineData(WindowsElevatedOperation.RemoveDevice)]
    [InlineData(WindowsElevatedOperation.EjectOpticalMedia)]
    public void FixedRequestsRoundTripWithoutArbitraryInterfaceOrRootPaths(WindowsElevatedOperation operation)
    {
        var request = operation switch
        {
            WindowsElevatedOperation.MountVolume => Mount,
            WindowsElevatedOperation.RemoveDevice => Mount with { Operation = operation, VolumeGuidPath = null, FileSystemSerialNumber = null },
            _ => Mount with { Operation = operation, StorageNumber = null, VolumeGuidPath = null, FileSystemSerialNumber = null }
        };
        var bytes = WindowsDeviceAuthorization.EncodeRequest(request);
        Assert.Equal(bytes.Length - 4, (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes));
        Assert.Equal(request, WindowsDeviceAuthorization.DecodeRequest(bytes));
        var json = Encoding.UTF8.GetString(bytes.AsSpan(4));
        Assert.DoesNotContain("interfacePath", json);
        Assert.DoesNotContain("root", json);
    }

    [Theory]
    [InlineData("operation")]
    [InlineData("version")]
    [InlineData("requestId")]
    [InlineData("deviceInstanceId")]
    [InlineData("deviceType")]
    [InlineData("deviceNumber")]
    [InlineData("volumeGuidPath")]
    [InlineData("fileSystemSerialNumber")]
    public void MissingRequiredFieldsFailClosed(string field)
    {
        var data = ValidWireRequest();
        data.Remove(field);
        Assert.Throws<InvalidDataException>(() => WindowsDeviceAuthorization.DecodeRequest(Packet(JsonSerializer.Serialize(data))));
    }

    [Theory]
    [InlineData("operation", "format")]
    [InlineData("operation", "MountVolume")]
    [InlineData("deviceInstanceId", "cmd.exe /c echo unsafe")]
    [InlineData("deviceInstanceId", "C:\\folder\\catalog.db")]
    [InlineData("deviceInstanceId", "ROOT\\..\\catalog.db")]
    [InlineData("volumeGuidPath", "C:\\")]
    [InlineData("volumeGuidPath", "\\\\server\\share\\")]
    public void RejectsUnknownOperationsCommandsAndArbitraryPaths(string field, string value)
    {
        var data = ValidWireRequest();
        data[field] = value;
        Assert.Throws<InvalidDataException>(() => WindowsDeviceAuthorization.DecodeRequest(Packet(JsonSerializer.Serialize(data))));
    }

    [Theory]
    [InlineData("command")]
    [InlineData("arguments")]
    [InlineData("root")]
    [InlineData("interfacePath")]
    [InlineData("isRemovable")]
    [InlineData("force")]
    [InlineData("$type")]
    public void UnknownFieldsCannotExtendTheOperation(string field)
    {
        var data = ValidWireRequest();
        data[field] = "untrusted";
        Assert.Throws<InvalidDataException>(() => WindowsDeviceAuthorization.DecodeRequest(Packet(JsonSerializer.Serialize(data))));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{} {}")]
    [InlineData("{\"version\":1,}")]
    [InlineData("/*comment*/{}")]
    [InlineData("{\"version\":1,\"version\":1}")]
    [InlineData("{\"version\":1,\"versi\\u006fn\":1}")]
    public void MalformedAndAmbiguousJsonIsRejected(string json) =>
        Assert.Throws<InvalidDataException>(() => WindowsDeviceAuthorization.DecodeRequest(Packet(json)));

    [Theory]
    [InlineData("version", -1)]
    [InlineData("version", 2)]
    [InlineData("operation", 1)]
    [InlineData("deviceNumber", -1)]
    [InlineData("deviceType", 9)]
    public void NumericTypesAndVersionAreStrict(string field, int value)
    {
        var data = ValidWireRequest();
        data[field] = value;
        Assert.Throws<InvalidDataException>(() => WindowsDeviceAuthorization.DecodeRequest(Packet(JsonSerializer.Serialize(data))));
    }

    [Fact]
    public void NonMountRequestsCannotCarryVolumeOrFormatFields()
    {
        var data = ValidWireRequest();
        data["operation"] = "removeDevice";
        Assert.Throws<InvalidDataException>(() => WindowsDeviceAuthorization.DecodeRequest(Packet(JsonSerializer.Serialize(data))));
    }

    [Fact]
    public void FramingRejectsOversizeTruncationTrailingFramesInvalidUtf8AndEmptyMessages()
    {
        var valid = WindowsDeviceAuthorization.EncodeRequest(Mount);
        Assert.Throws<InvalidDataException>(() => WindowsDeviceAuthorization.DecodeRequest(valid[..^1]));
        Assert.Throws<InvalidDataException>(() => WindowsDeviceAuthorization.DecodeRequest(valid.Concat(valid).ToArray()));
        Assert.Throws<InvalidDataException>(() => WindowsDeviceAuthorization.DecodeRequest(new byte[4]));
        Assert.Throws<InvalidDataException>(() => WindowsDeviceAuthorization.DecodeRequest(new byte[] { 255, 255, 255, 255 }));
        Assert.Throws<InvalidDataException>(() => WindowsDeviceAuthorization.DecodeRequest(Packet(new byte[] { 255 })));
        var json = JsonSerializer.Serialize(ValidWireRequest());
        var atLimit = Encoding.UTF8.GetBytes(json.PadRight(WindowsDeviceAuthorization.MaxPayloadBytes));
        Assert.Equal(Mount, WindowsDeviceAuthorization.DecodeRequest(Packet(atLimit)));
        Assert.Throws<InvalidDataException>(() => WindowsDeviceAuthorization.DecodeRequest(Packet(atLimit.Append((byte)' ').ToArray())));
    }

    [Fact]
    public void EncodersValidateConstructedRecordsAndTextLimitsToo()
    {
        Assert.Throws<InvalidDataException>(() => WindowsDeviceAuthorization.EncodeRequest(Mount with { Operation = (WindowsElevatedOperation)99 }));
        Assert.Throws<InvalidDataException>(() => WindowsDeviceAuthorization.EncodeRequest(Mount with { RequestId = Guid.Empty }));
        Assert.Throws<InvalidDataException>(() => WindowsDeviceAuthorization.EncodeRequest(Mount with { DeviceInstanceId = "USB\\DISK\\" + new string('A', 200) }));
        Assert.Throws<InvalidDataException>(() => WindowsDeviceAuthorization.EncodeRequest(Mount with { DeviceInstanceId = DeviceId + '\0' }));
    }

    [Fact]
    public void RepliesRetainNativeVetoDetailsButAreOnlyCorrelatedReports()
    {
        var failure = new WindowsDeviceRemovalFailure(new("RemovalVetoed", "CM_Request_Device_Eject", 0x17, "Native message"))
        { ConfigurationStatus = 0x17, VetoType = 5, VetoName = "busy-device", VolumeGuidPath = VolumeGuid };
        var reply = new WindowsOperationReply(1, RequestId, Mount.Operation, false, failure);
        var frame = WindowsDeviceAuthorization.EncodeReply(reply);
        Assert.Equal(reply, WindowsDeviceAuthorization.DecodeReply(frame, Mount));
        Assert.Throws<InvalidDataException>(() => WindowsDeviceAuthorization.DecodeReply(frame, Mount with { RequestId = Guid.NewGuid() }));
        Assert.Throws<InvalidDataException>(() => WindowsDeviceAuthorization.DecodeReply(frame,
            Mount with { Operation = WindowsElevatedOperation.RemoveDevice, VolumeGuidPath = null, FileSystemSerialNumber = null }));
        var success = new WindowsOperationReply(1, RequestId, Mount.Operation, true);
        Assert.Equal(success, WindowsDeviceAuthorization.DecodeReply(WindowsDeviceAuthorization.EncodeReply(success), Mount));
    }

    [Fact]
    public void ReplyShapesLimitsAndNativeStatusNamespacesAreValidated()
    {
        var failure = new WindowsDeviceRemovalFailure(new("NativeFailure", "Remove", 5, new string('x', 2049)));
        Assert.Throws<InvalidDataException>(() => WindowsDeviceAuthorization.EncodeReply(new(1, RequestId, Mount.Operation, false, failure)));
        Assert.Throws<InvalidDataException>(() => WindowsDeviceAuthorization.EncodeReply(new(1, RequestId, Mount.Operation, true, failure)));
        Assert.Throws<InvalidDataException>(() => WindowsDeviceAuthorization.EncodeReply(new(1, RequestId, Mount.Operation, false)));
        failure = new(new("ConfigurationManagerFailure", "Remove", 5)) { ConfigurationStatus = 0x33 };
        Assert.Throws<InvalidDataException>(() => WindowsDeviceAuthorization.EncodeReply(new(1, RequestId, Mount.Operation, false, failure)));
        failure = new(new("NativeFailure", "Remove", 5, "\ud800"));
        Assert.Throws<InvalidDataException>(() => WindowsDeviceAuthorization.EncodeReply(new(1, RequestId, Mount.Operation, false, failure)));
    }

    [Fact]
    public void LongNativeDiagnosticsStillFitBoundedReplyWithoutTruncation()
    {
        var failure = new WindowsDeviceRemovalFailure(new(new string('C', 64), new string('O', 64), 0x17, new string('é', 2048)))
        { ConfigurationStatus = 0x17, VetoType = 99, VetoName = new string('é', 260), VolumeGuidPath = VolumeGuid };
        var reply = new WindowsOperationReply(1, RequestId, Mount.Operation, false, failure);
        var bytes = WindowsDeviceAuthorization.EncodeReply(reply);
        Assert.InRange(bytes.Length, 5, WindowsDeviceAuthorization.MaxPayloadBytes + 4);
        Assert.Equal(reply, WindowsDeviceAuthorization.DecodeReply(bytes, Mount));
    }

    [Fact]
    public void HostResolvesNativePathsOnlyFromTrustedInventoryWithoutExecutingAnything()
    {
        var validation = WindowsDeviceOperationHost.ValidateTarget(Mount, Snapshot());
        Assert.True(validation.IsValid);
        Assert.Equal("trusted-interface", validation.MountTarget!.InterfacePath);
        Assert.Null(validation.RemovalTarget);
        Assert.Null(validation.OpticalTarget);
        var remove = Mount with { Operation = WindowsElevatedOperation.RemoveDevice, VolumeGuidPath = null, FileSystemSerialNumber = null };
        validation = WindowsDeviceOperationHost.ValidateTarget(remove, Snapshot());
        Assert.True(validation.IsValid);
        Assert.Equal("trusted-interface", validation.RemovalTarget!.InterfacePath);
    }

    [Theory]
    [InlineData("internal")]
    [InlineData("ambiguous")]
    [InlineData("missing")]
    [InlineData("unavailable")]
    [InlineData("reformatted")]
    [InlineData("inventory-error")]
    public void HostRejectsInternalIneligibleMissingAndReplacedTargets(string scenario)
    {
        var snapshot = Snapshot();
        var device = snapshot.Devices[0];
        snapshot = scenario switch
        {
            "internal" => snapshot with { Devices = [device with { Bus = WindowsStorageBus.Sata, IsRemovable = false }] },
            "ambiguous" => snapshot with { Devices = [device, device with { InstanceId = @"USBSTOR\OTHER\SERIAL" }] },
            "missing" => new([], [], []),
            "unavailable" => snapshot with { Volumes = [snapshot.Volumes[0] with { State = WindowsVolumeState.Unavailable }] },
            "reformatted" => snapshot with { Volumes = [snapshot.Volumes[0] with { FileSystemSerialNumber = 43 }] },
            _ => snapshot with { Errors = [new("NativeFailure", "Enumerate")] }
        };
        var validation = WindowsDeviceOperationHost.ValidateTarget(Mount, snapshot);
        Assert.False(validation.IsValid);
        Assert.NotNull(validation.Error);
        Assert.Null(validation.MountTarget);
    }

    [Fact]
    public void HostSelectsOpticalBranchWithoutInventingAnEmptyReadersStorageNumber()
    {
        var snapshot = Snapshot();
        snapshot = snapshot with { Devices = [snapshot.Devices[0] with { IsOptical = true, StorageNumber = null }], Volumes = [] };
        var optical = Mount with { Operation = WindowsElevatedOperation.EjectOpticalMedia, StorageNumber = null,
            VolumeGuidPath = null, FileSystemSerialNumber = null };
        var validation = WindowsDeviceOperationHost.ValidateTarget(optical, snapshot);
        Assert.True(validation.IsValid);
        Assert.Null(validation.OpticalTarget!.StorageNumber);
        Assert.Null(validation.RemovalTarget);
        Assert.False(WindowsDeviceOperationHost.ValidateTarget(optical, Snapshot()).IsValid);
        Assert.False(WindowsDeviceOperationHost.ValidateTarget(Mount with { Operation = (WindowsElevatedOperation)99 }, Snapshot()).IsValid);
    }

    private static Dictionary<string, object?> ValidWireRequest() => new()
    {
        ["version"] = 1, ["requestId"] = RequestId.ToString("D"), ["operation"] = "mountVolume",
        ["deviceInstanceId"] = DeviceId, ["deviceType"] = 7, ["deviceNumber"] = 1,
        ["volumeGuidPath"] = VolumeGuid, ["fileSystemSerialNumber"] = 42
    };

    private static byte[] Packet(string json) => Packet(Encoding.UTF8.GetBytes(json));
    private static byte[] Packet(byte[] payload)
    {
        var result = new byte[payload.Length + 4];
        BinaryPrimitives.WriteUInt32LittleEndian(result, (uint)payload.Length);
        payload.CopyTo(result, 4);
        return result;
    }

    private static WindowsStorageSnapshot Snapshot() => new(
        [new(DeviceId, null, "trusted-interface", "Reader", new(7, 1), WindowsStorageBus.Usb, false, true, true)],
        [new("volume", new(7, 1)) { VolumeGuidPath = VolumeGuid, FileSystemSerialNumber = 42, State = WindowsVolumeState.Available }], []);
}
