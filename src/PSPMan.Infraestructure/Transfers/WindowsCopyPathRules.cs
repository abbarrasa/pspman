using System.Collections.Immutable;
using System.Text;
using System.Buffers.Binary;

namespace PSPMan.Infraestructure.Transfers;

internal static class WindowsCopyPathRules
{
    internal static bool IsSafe(ImmutableArray<string> segments, bool allowEmpty = false)
    {
        if (segments.IsDefaultOrEmpty) return allowEmpty;
        if (segments[0] is { Length: >= 2 } first && char.IsAsciiLetter(first[0]) && first[1] == ':') return false;
        return segments.All(segment => !string.IsNullOrEmpty(segment) && segment is not "." and not ".."
            && segment.IndexOfAny(['/', '\\']) < 0);
    }
    internal static bool IsCompatibleName(string segment)
    {
        if (string.IsNullOrEmpty(segment) || segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ')
            || segment.Any(ch => char.IsControl(ch) || "<>:\"/\\|?*".Contains(ch))) return false;
        try { _ = new UTF8Encoding(false, true).GetByteCount(segment); }
        catch (EncoderFallbackException) { return false; }
        var stem = segment.Split('.', 2)[0].TrimEnd(' ').ToUpperInvariant();
        return stem is not ("CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$")
            && !(stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
                && stem[3] is >= '1' and <= '9' or '¹' or '²' or '³');
    }
    internal static uint ObjectAttributesForCaseFlags(uint flags) => (flags & 1) != 0 ? 0u : 0x40u;
    internal static long? AvailableBytes(long units, uint sectors, uint bytes)
    {
        if (units < 0 || sectors == 0 || bytes == 0) return null;
        try { return checked(checked(units * sectors) * bytes); }
        catch (OverflowException) { return null; }
    }
    internal static bool TryVolumeGuidRoot(string value, out Guid guid)
    {
        guid = default;
        return value.Length == 49 && value.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase)
            && value.EndsWith("}\\", StringComparison.Ordinal) && Guid.TryParse(value.AsSpan(11, 36), out guid);
    }
    internal static bool IsSelectedVolumeMount(ReadOnlySpan<byte> data, Guid expected)
    {
        if (data.Length < 16 || BinaryPrimitives.ReadUInt32LittleEndian(data) != 0xa0000003) return false;
        int length = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        int offset = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
        int count = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]);
        if (length < 8 || length + 8 > data.Length || offset + count > length - 8 || (offset & 1) != 0 || (count & 1) != 0) return false;
        string target;
        try { target = new UnicodeEncoding(false, false, true).GetString(data.Slice(16 + offset, count)); }
        catch (DecoderFallbackException) { return false; }
        return target.StartsWith(@"\??\Volume{", StringComparison.OrdinalIgnoreCase)
            && TryVolumeGuidRoot(@"\\?\" + target[4..], out var actual) && actual == expected;
    }
}
