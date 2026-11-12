using System.Reflection;
using System.Reflection.Emit;
using PSPMan.Core.Devices;
using PSPMan.Core.Games;
using PSPMan.Desktop.ViewModels;

namespace PSPMan.Desktop.Tests;

public sealed class DeviceBoundaryTests
{
    [Fact]
    public void CoreReferencesNeitherUiPersistenceNorPlatformAssemblies()
    {
        var references = typeof(DeviceSessionService).Assembly.GetReferencedAssemblies();
        Assert.NotEmpty(references);
        Assert.All(references, reference => Assert.True(reference.Name == "System.Runtime"
            || reference.Name!.StartsWith("System.", StringComparison.Ordinal), reference.FullName));
    }

    [Fact]
    public void InfrastructureDependsOnCoreButNotDesktopOrAvalonia()
    {
        var references = typeof(PSPMan.Infraestructure.Devices.DeviceProviderFactory).Assembly.GetReferencedAssemblies();
        Assert.Contains(references, reference => reference.Name == "PSPMan.Core");
        Assert.DoesNotContain(references, reference => reference.Name == "PSPMan.Desktop"
            || reference.Name!.StartsWith("Avalonia", StringComparison.Ordinal));
    }

    [Fact]
    public void DeviceLogicHasNoDirectFilesystemCatalogProcessOrReflectionAccess()
    {
        var types = typeof(DeviceSessionService).Assembly.GetTypes().Where(type => type.Namespace == "PSPMan.Core.Devices")
            .Concat(typeof(DevicesViewModel).Assembly.GetTypes().Where(type =>
                type == typeof(DevicesViewModel) || type.FullName!.StartsWith(typeof(DevicesViewModel).FullName + "+", StringComparison.Ordinal)
                || type == typeof(DeviceItemViewModel) || type == typeof(DeviceVolumeViewModel)
                || type.FullName!.StartsWith(typeof(DeviceItemViewModel).FullName + "+", StringComparison.Ordinal)
                || type.FullName!.StartsWith(typeof(DeviceVolumeViewModel).FullName + "+", StringComparison.Ordinal)));
        var methods = types.SelectMany(type => type.GetMethods(Flags).Cast<MethodBase>().Concat(type.GetConstructors(Flags)))
            .Concat(typeof(MainViewModel).GetMethods(Flags).Where(method => method.Name is "AttachDevices" or "UpdateSelectedGameDevices")).ToArray();
        Assert.NotEmpty(methods);
        foreach (var method in methods)
        {
            Assert.False((method.Attributes & MethodAttributes.PinvokeImpl) != 0, method.ToString());
            foreach (var member in References(method)) Assert.False(Forbidden(member), $"{method.DeclaringType}.{method.Name} -> {member}");
        }
    }

    [Fact]
    public void BoundaryInspectorDetectsForbiddenCallsWithoutExecutingThem()
    {
        var method = typeof(DeviceBoundaryTests).GetMethod(nameof(ForbiddenProbe), Flags)!;
        Assert.Contains(References(method), member => member.DeclaringType == typeof(File) && Forbidden(member));
    }

    // Never executed: a positive control for the compiled-IL guard, not a game-file probe.
    private static bool ForbiddenProbe() => File.Exists("/not-a-real-game.iso");
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    private static bool Forbidden(MemberInfo member)
    {
        var type = member as Type ?? member.DeclaringType;
        return type == typeof(File) || type == typeof(Directory) || type == typeof(FileInfo) || type == typeof(DirectoryInfo)
            || type == typeof(FileStream) || type == typeof(System.Diagnostics.Process)
            || type == typeof(StreamReader) || type == typeof(StreamWriter) || type == typeof(FileSystemWatcher)
            || type == typeof(DriveInfo) || type == typeof(RandomAccess)
            || type?.Namespace?.StartsWith("System.IO.Compression", StringComparison.Ordinal) == true
            || type == typeof(IGameRecordRepository) || type == typeof(GameCatalogService)
            || type?.Namespace?.StartsWith("System.Reflection", StringComparison.Ordinal) == true;
    }

    // Read member operands, including generated async methods; do not search raw bytes for opcodes.
    private static IEnumerable<MemberInfo> References(MethodBase method)
    {
        var bytes = method.GetMethodBody()?.GetILAsByteArray();
        if (bytes is null) yield break;
        var codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(code => unchecked((ushort)code.Value));
        for (var offset = 0; offset < bytes.Length;)
        {
            ushort value = bytes[offset++];
            if (value == 0xfe) value = (ushort)(0xfe00 | bytes[offset++]);
            var code = codes[value];
            if (code.OperandType is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok)
                yield return method.Module.ResolveMember(BitConverter.ToInt32(bytes, offset),
                    method.DeclaringType?.GetGenericArguments(), method.IsGenericMethod ? method.GetGenericArguments() : null)!;
            offset += code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, offset),
                _ => 4
            };
        }
    }
}
