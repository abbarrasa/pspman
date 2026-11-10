using PSPMan.Core.Devices;

namespace PSPMan.Core.Tests;

public sealed class PspUsbRecognitionTests
{
    [Theory]
    [InlineData(0x01c8, "PSP Type A", "T01-hwdata-054c:01c8")]
    [InlineData(0x02d2, "PSP Slim candidate", "T01-hwdata-054c:02d2")]
    public void RecognizesDocumentedCandidatesOnlyWithAccessibleMassStorage(
        int productId, string presentation, string evidenceReference)
    {
        var descriptor = Descriptor(0x054c, productId, storage: true,
            interfaces: [new UsbInterfaceDescriptor(0x08, 0x06, 0x50)]);

        var result = PspUsbRecognition.Recognize(descriptor);

        Assert.Equal(PspUsbRecognitionStatus.Recognized, result.Status);
        Assert.Equal(presentation, result.Presentation);
        Assert.Equal(evidenceReference, result.EvidenceReference);
    }

    [Fact]
    public void DoesNotRecognizeTheDevelopmentProtocolAsPspStorage()
    {
        var result = PspUsbRecognition.Recognize(Descriptor(0x054c, 0x01c9, storage: true,
            interfaces: [new UsbInterfaceDescriptor(0xFF, 0xFF, 0xFF)]));

        Assert.Equal(PspUsbRecognitionStatus.Rejected, result.Status);
        Assert.Equal("DevelopmentProtocol", result.ReasonCode);
        Assert.Null(result.Presentation);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ReportsCandidateEvidenceAsInconclusiveWithoutStorageProof(bool storage, bool massStorageInterface)
    {
        var interfaces = massStorageInterface
            ? new[] { new UsbInterfaceDescriptor(0x08, 0x06, 0x50) }
            : new[] { new UsbInterfaceDescriptor(0x03, 0x01, 0x01) };

        var result = PspUsbRecognition.Recognize(Descriptor(0x054c, 0x01c8, storage, interfaces));

        Assert.Equal(PspUsbRecognitionStatus.Inconclusive, result.Status);
        Assert.Null(result.Presentation);
        Assert.NotNull(result.ReasonCode);
    }

    [Fact]
    public void DoesNotUseSonyLabelsOrPspDirectoriesAsRecognitionEvidence()
    {
        var descriptor = Descriptor(0x1234, 0x5678, storage: true,
            interfaces: [new UsbInterfaceDescriptor(0x08, 0x06, 0x50)]) with
        {
            ProductLabel = "Sony PSP",
            VolumeLabel = "PSP",
            DirectoryHint = "PSP/GAME"
        };

        var result = PspUsbRecognition.Recognize(descriptor);

        Assert.Equal(PspUsbRecognitionStatus.Inconclusive, result.Status);
        Assert.Null(result.Presentation);
    }

    [Fact]
    public void DoesNotTreatAnUnrelatedSonyStorageDeviceAsPsp()
    {
        var descriptor = Descriptor(0x054c, 0x0abc, storage: true,
            interfaces: [new UsbInterfaceDescriptor(0x08, 0x06, 0x50)]);

        var result = PspUsbRecognition.Recognize(descriptor);

        Assert.NotEqual(PspUsbRecognitionStatus.Recognized, result.Status);
        Assert.Null(result.Presentation);
    }

    private static PspUsbDescriptor Descriptor(ushort vendorId, int productId, bool storage,
        IReadOnlyList<UsbInterfaceDescriptor> interfaces) => new(
        vendorId, (ushort)productId, storage, interfaces);
}
