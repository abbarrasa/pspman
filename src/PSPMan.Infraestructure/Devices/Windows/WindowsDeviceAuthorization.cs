using System.Text.Json;

namespace PSPMan.Infraestructure.Devices.Windows;

public enum WindowsElevatedOperation { MountVolume = 1, RemoveDevice = 2, EjectOpticalMedia = 3 }

/// <summary>Selection observations only. RequestId correlates messages; it never authenticates a peer.</summary>
public sealed record WindowsOperationRequest(int Version, Guid RequestId, WindowsElevatedOperation Operation,
    string DeviceInstanceId, WindowsStorageNumber? StorageNumber = null, string? VolumeGuidPath = null,
    uint? FileSystemSerialNumber = null);

/// <summary>A correlated host report, never proof of current mounted/removed state.</summary>
public sealed record WindowsOperationReply(int Version, Guid RequestId, WindowsElevatedOperation Operation,
    bool Completed, WindowsDeviceRemovalFailure? Failure = null);

/// <summary>Version-one fixed-operation codec only; IPC, peer authentication and UAC are separate work.</summary>
public sealed partial class WindowsDeviceAuthorization
{
    public const int ProtocolVersion = 1;
    public const int MaxPayloadBytes = 16 * 1024;

    public static byte[] EncodeRequest(WindowsOperationRequest request)
    {
        ValidateRequest(request);
        return WindowsOperationProtocol.Write(writer =>
        {
            Envelope(writer, request.Version, request.RequestId, request.Operation);
            writer.WriteString("deviceInstanceId", request.DeviceInstanceId);
            if (request.StorageNumber is { } number)
            {
                writer.WriteNumber("deviceType", number.DeviceType);
                writer.WriteNumber("deviceNumber", number.DeviceNumber);
            }
            if (request.VolumeGuidPath is not null) writer.WriteString("volumeGuidPath", request.VolumeGuidPath);
            if (request.FileSystemSerialNumber is { } serial) writer.WriteNumber("fileSystemSerialNumber", serial);
            writer.WriteEndObject();
        });
    }

    public static WindowsOperationRequest DecodeRequest(ReadOnlyMemory<byte> frame) => WindowsOperationProtocol.Read(frame, root =>
    {
        var values = WindowsOperationProtocol.Object(root, "version", "requestId", "operation", "deviceInstanceId",
            "deviceType", "deviceNumber", "volumeGuidPath", "fileSystemSerialNumber");
        var type = WindowsOperationProtocol.OptionalUInt(values, "deviceType");
        var number = WindowsOperationProtocol.OptionalUInt(values, "deviceNumber");
        if (type.HasValue != number.HasValue) throw WindowsOperationProtocol.Invalid();
        var request = new WindowsOperationRequest(WindowsOperationProtocol.Int(values, "version"),
            WindowsOperationProtocol.Id(values, "requestId"),
            WindowsOperationProtocol.Operation(WindowsOperationProtocol.String(values, "operation")),
            WindowsOperationProtocol.String(values, "deviceInstanceId"),
            type is { } kind ? new(kind, number!.Value) : null,
            WindowsOperationProtocol.OptionalString(values, "volumeGuidPath"),
            WindowsOperationProtocol.OptionalUInt(values, "fileSystemSerialNumber"));
        ValidateRequest(request);
        return request;
    });

    public static byte[] EncodeReply(WindowsOperationReply reply)
    {
        ValidateReply(reply);
        return WindowsOperationProtocol.Write(writer =>
        {
            Envelope(writer, reply.Version, reply.RequestId, reply.Operation);
            writer.WriteBoolean("completed", reply.Completed);
            if (reply.Failure is { } failure)
            {
                writer.WriteStartObject("failure");
                writer.WriteString("code", failure.Error.Code);
                writer.WriteString("operation", failure.Error.Operation);
                if (failure.Error.NativeCode is { } native) writer.WriteNumber("nativeCode", native);
                if (failure.Error.Message is not null) writer.WriteString("message", failure.Error.Message);
                if (failure.ConfigurationStatus is { } status) writer.WriteNumber("configurationStatus", status);
                if (failure.VetoType is { } veto) writer.WriteNumber("vetoType", veto);
                if (failure.VetoName is not null) writer.WriteString("vetoName", failure.VetoName);
                if (failure.VolumeGuidPath is not null) writer.WriteString("volumeGuidPath", failure.VolumeGuidPath);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        });
    }

    public static WindowsOperationReply DecodeReply(ReadOnlyMemory<byte> frame, WindowsOperationRequest expectedRequest)
    {
        ValidateRequest(expectedRequest);
        return WindowsOperationProtocol.Read(frame, root =>
        {
            var values = WindowsOperationProtocol.Object(root, "version", "requestId", "operation", "completed", "failure");
            var completed = WindowsOperationProtocol.Required(values, "completed").ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw WindowsOperationProtocol.Invalid()
            };
            WindowsDeviceRemovalFailure? failure = null;
            if (values.TryGetValue("failure", out var failureElement))
            {
                var fields = WindowsOperationProtocol.Object(failureElement, "code", "operation", "nativeCode", "message",
                    "configurationStatus", "vetoType", "vetoName", "volumeGuidPath");
                var error = new WindowsDeviceError(WindowsOperationProtocol.String(fields, "code"),
                    WindowsOperationProtocol.String(fields, "operation"),
                    fields.ContainsKey("nativeCode") ? WindowsOperationProtocol.Int(fields, "nativeCode") : null,
                    WindowsOperationProtocol.OptionalString(fields, "message"));
                failure = new(error)
                {
                    ConfigurationStatus = WindowsOperationProtocol.OptionalUInt(fields, "configurationStatus"),
                    VetoType = WindowsOperationProtocol.OptionalUInt(fields, "vetoType"),
                    VetoName = WindowsOperationProtocol.OptionalString(fields, "vetoName"),
                    VolumeGuidPath = WindowsOperationProtocol.OptionalString(fields, "volumeGuidPath")
                };
            }
            var reply = new WindowsOperationReply(WindowsOperationProtocol.Int(values, "version"),
                WindowsOperationProtocol.Id(values, "requestId"),
                WindowsOperationProtocol.Operation(WindowsOperationProtocol.String(values, "operation")), completed, failure);
            ValidateReply(reply);
            if (reply.RequestId != expectedRequest.RequestId || reply.Operation != expectedRequest.Operation)
                throw WindowsOperationProtocol.Invalid();
            return reply;
        });
    }

    internal static void ValidateRequest(WindowsOperationRequest request)
    {
        if (request is null) throw WindowsOperationProtocol.Invalid();
        ValidateEnvelope(request.Version, request.RequestId, request.Operation);
        WindowsOperationProtocol.Text(request.DeviceInstanceId, 199);
        var parts = request.DeviceInstanceId.Split('\\');
        if (parts.Length != 3 || parts.Any(part => part.Length == 0 || part is "." or "..")
            || parts[0].Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_')
            || request.DeviceInstanceId.Any(character => character <= 0x20 || character >= 0x7F || character is ',' or '/' or ':'))
            throw WindowsOperationProtocol.Invalid();
        if (request.StorageNumber is { DeviceType: not (2 or 7) }) throw WindowsOperationProtocol.Invalid();
        switch (request.Operation)
        {
            case WindowsElevatedOperation.MountVolume:
                if (request.StorageNumber is null || request.VolumeGuidPath is null
                    || !WindowsDeviceApi.IsVolumeGuidPath(request.VolumeGuidPath) || request.FileSystemSerialNumber is null)
                    throw WindowsOperationProtocol.Invalid();
                break;
            case WindowsElevatedOperation.RemoveDevice:
                if (request.StorageNumber is not { DeviceType: 7 }) throw WindowsOperationProtocol.Invalid();
                goto case WindowsElevatedOperation.EjectOpticalMedia;
            case WindowsElevatedOperation.EjectOpticalMedia:
                if (request.VolumeGuidPath is not null || request.FileSystemSerialNumber is not null
                    || (request.Operation == WindowsElevatedOperation.EjectOpticalMedia && request.StorageNumber is { DeviceType: not 2 }))
                    throw WindowsOperationProtocol.Invalid();
                break;
        }
    }

    private static void ValidateReply(WindowsOperationReply reply)
    {
        if (reply is null) throw WindowsOperationProtocol.Invalid();
        ValidateEnvelope(reply.Version, reply.RequestId, reply.Operation);
        if (reply.Completed != (reply.Failure is null)) throw WindowsOperationProtocol.Invalid();
        if (reply.Failure is not { } failure) return;
        if (failure.Error is null) throw WindowsOperationProtocol.Invalid();
        WindowsOperationProtocol.Token(failure.Error.Code);
        WindowsOperationProtocol.Token(failure.Error.Operation);
        if (failure.Error.Message is not null) WindowsOperationProtocol.Text(failure.Error.Message, 2048, allowEmpty: true);
        if (failure.VetoName is not null) WindowsOperationProtocol.Text(failure.VetoName, 260, allowEmpty: true);
        if (failure.VolumeGuidPath is not null && !WindowsDeviceApi.IsVolumeGuidPath(failure.VolumeGuidPath)) throw WindowsOperationProtocol.Invalid();
        if ((failure.ConfigurationStatus is { } status && (failure.Error.NativeCode is not { } native || unchecked((uint)native) != status))
            || (failure.VetoType is not null && failure.ConfigurationStatus is null)
            || (failure.VetoName is not null && failure.VetoType is null)) throw WindowsOperationProtocol.Invalid();
    }

    private static void ValidateEnvelope(int version, Guid id, WindowsElevatedOperation operation)
    {
        if (version != ProtocolVersion || id == Guid.Empty) throw WindowsOperationProtocol.Invalid();
        WindowsOperationProtocol.Operation(operation);
    }

    private static void Envelope(Utf8JsonWriter writer, int version, Guid id, WindowsElevatedOperation operation)
    {
        writer.WriteStartObject();
        writer.WriteNumber("version", version);
        writer.WriteString("requestId", id.ToString("D"));
        writer.WriteString("operation", WindowsOperationProtocol.Operation(operation));
    }
}
