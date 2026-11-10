namespace PSPMan.Core.Devices;

public enum PspUsbRecognitionStatus
{
    Recognized,
    Rejected,
    Inconclusive
}

/// <summary>USB interface information supplied by a platform adapter.</summary>
public sealed record UsbInterfaceDescriptor(byte ClassCode, byte SubclassCode, byte ProtocolCode);

/// <summary>
/// Platform-neutral USB evidence. Storage association is supplied by the adapter and is not
/// inferred from labels, directories or the USB identifiers themselves.
/// </summary>
public sealed record PspUsbDescriptor(
    ushort VendorId,
    ushort ProductId,
    bool HasAccessibleStorage,
    IReadOnlyList<UsbInterfaceDescriptor> Interfaces,
    string? ProductLabel = null,
    string? VolumeLabel = null,
    string? DirectoryHint = null);

public sealed record PspUsbRecognitionResult(
    PspUsbRecognitionStatus Status,
    string? Presentation = null,
    string? EvidenceReference = null,
    string? ReasonCode = null);

/// <summary>
/// Applies only provenance-backed USB candidate rules. It does not assign device or volume
/// identity; those are separate observations owned by the platform provider.
/// </summary>
public static class PspUsbRecognition
{
    private const ushort SonyVendorId = 0x054c;
    private const ushort DevelopmentProtocolProductId = 0x01c9;

    public static PspUsbRecognitionResult Recognize(PspUsbDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(descriptor.Interfaces);

        if (descriptor.VendorId == SonyVendorId
            && descriptor.ProductId == DevelopmentProtocolProductId)
        {
            return new(PspUsbRecognitionStatus.Rejected, ReasonCode: "DevelopmentProtocol");
        }

        var candidate = (descriptor.VendorId, descriptor.ProductId) switch
        {
            (SonyVendorId, 0x01c8) => ("PSP Type A", "T01-hwdata-054c:01c8"),
            (SonyVendorId, 0x02d2) => ("PSP Slim candidate", "T01-hwdata-054c:02d2"),
            _ => ((string Presentation, string EvidenceReference)?)null
        };
        if (candidate is null)
            return new(PspUsbRecognitionStatus.Inconclusive, ReasonCode: "UnknownUsbIdentity");
        if (!descriptor.HasAccessibleStorage)
            return new(PspUsbRecognitionStatus.Inconclusive, ReasonCode: "StorageAssociationMissing");
        if (!descriptor.Interfaces.Any(IsMassStorage))
            return new(PspUsbRecognitionStatus.Inconclusive, ReasonCode: "MassStorageInterfaceMissing");

        return new(PspUsbRecognitionStatus.Recognized, candidate.Value.Presentation,
            candidate.Value.EvidenceReference);
    }

    private static bool IsMassStorage(UsbInterfaceDescriptor descriptor) =>
        descriptor.ClassCode == 0x08 && descriptor.SubclassCode == 0x06 && descriptor.ProtocolCode == 0x50;
}
