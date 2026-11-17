using PSPMan.Infraestructure.Transfers;
using PSPMan.Core.Transfers;
using System.Runtime.InteropServices;

namespace PSPMan.Infraestructure.Tests;

/// <summary>Portable policy tests; these are not native Windows execution evidence.</summary>
public sealed class WindowsCopyPathRulesTests
{
    [Fact]
    public void NativeLayoutsMatchPointerWidthAndFixedWindowsStructures()
    {
        Assert.Equal(IntPtr.Size == 8 ? 16 : 8, Marshal.SizeOf<WindowsCopyNative.UnicodeString>());
        Assert.Equal(IntPtr.Size == 8 ? 48 : 24, Marshal.SizeOf<WindowsCopyNative.ObjectAttributes>());
        Assert.Equal(IntPtr.Size * 2, Marshal.SizeOf<WindowsCopyNative.IoStatusBlock>());
        Assert.Equal(52, Marshal.SizeOf<WindowsCopyNative.FileInformation>());
        Assert.Equal(32, Marshal.SizeOf<WindowsCopyNative.FullSizeInformation>());
    }

    [Fact]
    public void NativeOpenPlansCannotBlindlyOverwriteOrFollowAReparsePoint()
    {
        foreach (var mode in Enum.GetValues<WindowsCopyOpenMode>())
        {
            var plan = WindowsCopyNative.Parameters(mode);
            Assert.Equal(0x200000u, plan.Options & 0x200000u);
            Assert.Equal(0x20u, plan.Options & 0x20u);
            Assert.Equal(mode is WindowsCopyOpenMode.CreateFile or WindowsCopyOpenMode.CreateDirectory ? 2u : 1u, plan.Disposition);
            if (mode is WindowsCopyOpenMode.Write or WindowsCopyOpenMode.CreateFile or WindowsCopyOpenMode.Directory)
                Assert.Equal(1u, plan.Share); // neither write sharing nor delete/rename sharing
        }
    }

    [Fact]
    public void NativeMissingAndStructuralStatusesRemainDistinct()
    {
        Assert.True(WindowsCopyNative.IsMissing(WindowsCopyNative.NameMissing));
        Assert.False(WindowsCopyNative.IsMissing(WindowsCopyNative.Collision));
        Assert.Equal(GameCopyErrorCode.FileDirectoryConflict, WindowsCopyNative.ClassifiedError(unchecked((int)0xc0000103)));
        Assert.Equal(GameCopyErrorCode.InvalidName, WindowsCopyNative.ClassifiedError(unchecked((int)0xc0000106)));
        Assert.Null(WindowsCopyNative.ClassifiedError(unchecked((int)0xc0000022))); // permission failure, not a name conflict
    }
    [Fact]
    public void CanonicalVolumeNamesAndMountReparseTargetsCannotRedirectToAJunctionOrAnotherVolume()
    {
        var guid = Guid.Parse("8dbe7095-2d18-42d6-8817-d87cbf62a831");
        Assert.True(WindowsCopyPathRules.TryVolumeGuidRoot(@"\\?\Volume{" + guid + @"}\", out var actual));
        Assert.Equal(guid, actual);
        Assert.False(WindowsCopyPathRules.TryVolumeGuidRoot(@"\\server\share\", out _));
        Assert.False(WindowsCopyPathRules.TryVolumeGuidRoot(@"\\?\Volume{" + guid + @"}\subdir", out _));
        var target = System.Text.Encoding.Unicode.GetBytes(@"\??\Volume{" + guid + @"}\");
        var data = new byte[16 + target.Length + 2];
        BitConverter.GetBytes(0xa0000003u).CopyTo(data, 0);
        BitConverter.GetBytes(checked((ushort)(data.Length - 8))).CopyTo(data, 4);
        BitConverter.GetBytes(checked((ushort)target.Length)).CopyTo(data, 10);
        target.CopyTo(data, 16);
        Assert.True(WindowsCopyPathRules.IsSelectedVolumeMount(data, guid));
        Assert.False(WindowsCopyPathRules.IsSelectedVolumeMount(data, Guid.NewGuid()));
        Assert.False(WindowsCopyPathRules.IsSelectedVolumeMount(data.AsSpan(0, 15), guid));
        var junctionTarget = System.Text.Encoding.Unicode.GetBytes(@"\??\C:\unrelated");
        var junction = new byte[16 + junctionTarget.Length + 2];
        BitConverter.GetBytes(0xa0000003u).CopyTo(junction, 0);
        BitConverter.GetBytes(checked((ushort)(junction.Length - 8))).CopyTo(junction, 4);
        BitConverter.GetBytes(checked((ushort)junctionTarget.Length)).CopyTo(junction, 10);
        junctionTarget.CopyTo(junction, 16);
        Assert.False(WindowsCopyPathRules.IsSelectedVolumeMount(junction, guid));
        BitConverter.GetBytes(ushort.MaxValue).CopyTo(data, 8);
        Assert.False(WindowsCopyPathRules.IsSelectedVolumeMount(data, guid));
    }
    [Theory]
    [InlineData("CON")]
    [InlineData("con.iso")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("PRN.txt")]
    [InlineData("COM1.iso")]
    [InlineData("LPT9")]
    [InlineData("COM¹")]
    [InlineData("LPT²")]
    [InlineData("game.")]
    [InlineData("game ")]
    [InlineData("game:stream")]
    [InlineData("game?.iso")]
    [InlineData("bad\0name")]
    public void IncompatibleWindowsNamesAreRejected(string name) => Assert.False(WindowsCopyPathRules.IsCompatibleName(name));

    [Fact]
    public void InvalidUtf16CannotBeReinterpretedAsAReplacementFilename() =>
        Assert.False(WindowsCopyPathRules.IsCompatibleName("bad\ud800name"));

    [Theory]
    [InlineData("game.iso")]
    [InlineData("COM10.iso")]
    [InlineData("CONSOLE.iso")]
    [InlineData("日本語🎮.iso")]
    public void OrdinaryUnicodeAndNonReservedNamesArePreserved(string name) => Assert.True(WindowsCopyPathRules.IsCompatibleName(name));

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("/absolute")]
    [InlineData("C:relative")]
    [InlineData("\\server")]
    [InlineData("dir/file")]
    public void PathsCannotBeSmuggledIntoRelativeLeafOpens(string name) => Assert.False(WindowsCopyPathRules.IsSafe([name]));

    [Fact]
    public void EmptyDirectoryEntryAndParentNativeCaseFlagsAreExplicit()
    {
        Assert.True(WindowsCopyPathRules.IsSafe([], allowEmpty: true));
        Assert.False(WindowsCopyPathRules.IsSafe([]));
        Assert.Equal(0x40u, WindowsCopyPathRules.ObjectAttributesForCaseFlags(0));
        Assert.Equal(0u, WindowsCopyPathRules.ObjectAttributesForCaseFlags(1));
    }

    [Fact]
    public void NativeSpaceConversionIsCheckedAndRejectsUnknownGeometry()
    {
        Assert.Equal(4096, WindowsCopyPathRules.AvailableBytes(2, 4, 512));
        Assert.Equal(0, WindowsCopyPathRules.AvailableBytes(0, 4, 512));
        Assert.Null(WindowsCopyPathRules.AvailableBytes(-1, 4, 512));
        Assert.Null(WindowsCopyPathRules.AvailableBytes(1, 0, 512));
        Assert.Null(WindowsCopyPathRules.AvailableBytes(long.MaxValue, uint.MaxValue, uint.MaxValue));
    }
}
