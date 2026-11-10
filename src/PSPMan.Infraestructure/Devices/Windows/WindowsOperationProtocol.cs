using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace PSPMan.Infraestructure.Devices.Windows;

/// <summary>Bounded single-message framing and strict JSON primitives; no transport or execution.</summary>
internal static class WindowsOperationProtocol
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static byte[] Write(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            write(writer);
            writer.Flush();
        }
        if (buffer.WrittenCount is 0 or > WindowsDeviceAuthorization.MaxPayloadBytes) throw Invalid();
        var frame = new byte[buffer.WrittenCount + 4];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)buffer.WrittenCount);
        buffer.WrittenSpan.CopyTo(frame.AsSpan(4));
        return frame;
    }

    internal static T Read<T>(ReadOnlyMemory<byte> frame, Func<JsonElement, T> read)
    {
        if (frame.Length < 4) throw Invalid();
        var length = BinaryPrimitives.ReadUInt32LittleEndian(frame.Span);
        // Check before allocation or addition: untrusted lengths cannot overflow or allocate arbitrary buffers.
        if (length is 0 or > WindowsDeviceAuthorization.MaxPayloadBytes || frame.Length != (int)length + 4) throw Invalid();
        try
        {
            StrictUtf8.GetCharCount(frame.Span[4..]);
            using var document = JsonDocument.Parse(frame[4..], new JsonDocumentOptions { MaxDepth = 3 });
            return read(document.RootElement);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or ArgumentException or OverflowException)
        {
            // Never reflect attacker-controlled payload text into protocol diagnostics.
            throw Invalid();
        }
    }

    internal static Dictionary<string, JsonElement> Object(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Invalid();
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!names.Contains(property.Name, StringComparer.Ordinal) || !properties.TryAdd(property.Name, property.Value))
                throw Invalid(); // Includes escaped duplicates, unknown names and case variants.
        return properties;
    }

    internal static JsonElement Required(Dictionary<string, JsonElement> values, string name) =>
        values.TryGetValue(name, out var element) ? element : throw Invalid();
    internal static string String(Dictionary<string, JsonElement> values, string name) =>
        Required(values, name).ValueKind == JsonValueKind.String ? values[name].GetString()! : throw Invalid();
    internal static uint UInt(Dictionary<string, JsonElement> values, string name) =>
        Required(values, name).ValueKind == JsonValueKind.Number && values[name].TryGetUInt32(out var value) ? value : throw Invalid();
    internal static uint? OptionalUInt(Dictionary<string, JsonElement> values, string name) => values.ContainsKey(name) ? UInt(values, name) : null;
    internal static int Int(Dictionary<string, JsonElement> values, string name) =>
        Required(values, name).ValueKind == JsonValueKind.Number && values[name].TryGetInt32(out var value) ? value : throw Invalid();
    internal static string? OptionalString(Dictionary<string, JsonElement> values, string name) =>
        values.ContainsKey(name) ? String(values, name) : null;
    internal static Guid Id(Dictionary<string, JsonElement> values, string name) =>
        Guid.TryParseExact(String(values, name), "D", out var id) && id != Guid.Empty ? id : throw Invalid();

    internal static void Text(string? value, int maximum, bool allowEmpty = false)
    {
        if (value is null || value.Length > maximum || (!allowEmpty && value.Length == 0)
            || value.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t'))) throw Invalid();
        try { StrictUtf8.GetByteCount(value); }
        catch (EncoderFallbackException) { throw Invalid(); }
    }

    internal static void Token(string? value)
    {
        Text(value, 64);
        if (!char.IsAsciiLetter(value![0]) || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_')) throw Invalid();
    }

    internal static string Operation(WindowsElevatedOperation operation) => operation switch
    {
        WindowsElevatedOperation.MountVolume => "mountVolume",
        WindowsElevatedOperation.RemoveDevice => "removeDevice",
        WindowsElevatedOperation.EjectOpticalMedia => "ejectOpticalMedia",
        _ => throw Invalid()
    };
    internal static WindowsElevatedOperation Operation(string operation) => operation switch
    {
        "mountVolume" => WindowsElevatedOperation.MountVolume,
        "removeDevice" => WindowsElevatedOperation.RemoveDevice,
        "ejectOpticalMedia" => WindowsElevatedOperation.EjectOpticalMedia,
        _ => throw Invalid()
    };

    internal static InvalidDataException Invalid() => new("Invalid Windows operation protocol message.");
}
