using System.Reflection;
using PSPMan.Infraestructure.Transfers;

namespace PSPMan.Infraestructure.Tests;

/// <summary>Portable architecture checks, never evidence of native Windows execution.</summary>
public sealed class CopyResourceAdapterTests
{
    [Fact]
    public void Both_confined_destinations_implement_the_internal_resource_protocol()
    {
        var protocol = typeof(CopyDestinationResources).Assembly.GetType("PSPMan.Infraestructure.Transfers.ICopyResourceDestination");
        Assert.NotNull(protocol);
        Assert.True(protocol.IsAssignableFrom(typeof(LinuxCopyDestination)));
        Assert.True(protocol.IsAssignableFrom(typeof(WindowsCopyDestination)));
        Assert.False(protocol.IsPublic);
    }

    [Fact]
    public void Shared_owner_has_no_direct_Linux_destination_or_target_fields()
    {
        var fields = typeof(CopyDestinationResources).GetNestedTypes(BindingFlags.NonPublic)
            .SelectMany(t => t.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance));
        Assert.DoesNotContain(fields, f => f.FieldType == typeof(LinuxCopyDestination)
            || f.FieldType.FullName == "PSPMan.Infraestructure.Transfers.LinuxCopyDestination+Target");
    }

    [Fact]
    public void Native_write_plans_allow_file_flush_but_preserve_writer_and_replacement_exclusion()
    {
        foreach (var mode in new[] { WindowsCopyOpenMode.Write, WindowsCopyOpenMode.CreateFile })
        {
            var parameters = WindowsCopyNative.Parameters(mode);
            Assert.Equal(0x40000000u, parameters.Access & 0x40000000u); // GENERIC_WRITE for FlushFileBuffers.
            Assert.Equal(1u, parameters.Share);
        }
        Assert.Equal(7u, WindowsCopyNative.Parameters(WindowsCopyOpenMode.Read).Share);
        Assert.Equal(7u, WindowsCopyNative.Parameters(WindowsCopyOpenMode.Metadata).Share);
    }
}
