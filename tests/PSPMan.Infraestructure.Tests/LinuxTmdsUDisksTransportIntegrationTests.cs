using System.Collections.Concurrent;
using System.Diagnostics;
using Tmds.DBus.Protocol;
using PSPMan.Infraestructure.Devices.Linux;

namespace PSPMan.Infraestructure.Tests;

public sealed class LinuxTmdsUDisksTransportIntegrationTests
{
    private const string DrivePath = "/org/freedesktop/UDisks2/drives/test";
    private const string VolumePath = "/org/freedesktop/UDisks2/block_devices/test1";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [LinuxFact]
    public async Task ExercisesProductionTransportAgainstAnIsolatedFakeUDisksService()
    {
        await using var bus = await PrivateBus.StartAsync();
        await using var service = await FakeUDisksService.StartAsync(bus.Address);
        await using var transport = await TmdsUDisksTransport.ConnectAsync(bus.Address);
        await using var client = new UDisksClient(transport);

        UDisksObjectManagerSnapshot inventory;
        try
        {
            inventory = await client.GetManagedObjectsAsync().AsTask().WaitAsync(Timeout);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"The fake service failed while client reported {exception.GetType().Name}; " +
                $"service={service.ConnectionException?.Message ?? "none"}; busExited={bus.HasExited}; " +
                $"methods={string.Join(",", service.Methods)}.",
                service.ConnectionException ?? exception);
        }
        var mountPoint = await client.MountAsync(
            VolumePath).AsTask().WaitAsync(Timeout);
        await client.UnmountAsync(VolumePath).AsTask().WaitAsync(Timeout);
        await client.EjectAsync(DrivePath).AsTask().WaitAsync(Timeout);
        var error = await Assert.ThrowsAsync<UDisksClientException>(() =>
            client.PowerOffAsync(DrivePath).AsTask().WaitAsync(Timeout));

        Assert.Equal(2, inventory.Objects.Length);
        var volume = inventory.Objects.Single(item => item.ObjectPath == VolumePath);
        Assert.Equal("/dev/fake1", volume.Interface("org.freedesktop.UDisks2.Block")
            .Property("Device").AsBytes());
        Assert.Equal(DrivePath, volume.Interface("org.freedesktop.UDisks2.Block")
            .Property("Drive").AsObjectPath());
        Assert.Equal("/run/media/fake", Assert.Single(volume.Interface("org.freedesktop.UDisks2.Filesystem")
            .Property("MountPoints").AsStringArray()));
        Assert.True(inventory.Objects.Single(item => item.ObjectPath == DrivePath)
            .Interface("org.freedesktop.UDisks2.Drive").Property("CanPowerOff").AsBoolean());
        Assert.Equal("optical_dvd", Assert.Single(inventory.Objects.Single(item => item.ObjectPath == DrivePath)
            .Interface("org.freedesktop.UDisks2.Drive").Property("MediaCompatibility").AsStringArray()));
        Assert.Equal("/run/media/fake", mountPoint);
        Assert.Equal("org.freedesktop.UDisks2.Error.Busy", error.Code);
        Assert.Equal("The fake device is busy.", error.Message);
        Assert.Equal(
            ["GetManagedObjects", "Mount", "Unmount", "Eject", "PowerOff"],
            service.Methods);
    }

    [LinuxFact]
    public async Task DeliversSignalsAndCleansSubscriptionsOnAnIsolatedBus()
    {
        await using var bus = await PrivateBus.StartAsync();
        await using var service = await FakeUDisksService.StartAsync(bus.Address);
        await using var transport = await TmdsUDisksTransport.ConnectAsync(bus.Address);

        using var cancellation = new CancellationTokenSource(Timeout);
        var changes = await transport.SubscribeChangesAsync(cancellation.Token);
        await using var enumerator = changes.GetAsyncEnumerator(cancellation.Token);
        foreach (var kind in Enum.GetValues<UDisksChangeKind>())
        {
            await service.EmitChangeAsync(kind, VolumePath);
            Assert.True(await enumerator.MoveNextAsync().AsTask().WaitAsync(Timeout));
            Assert.Equal(kind, enumerator.Current.Kind);
            Assert.Equal(VolumePath, enumerator.Current.ObjectPath);
        }
        var refreshed = await transport.GetManagedObjectsAsync().AsTask().WaitAsync(Timeout);
        Assert.Equal("/run/media/changed", Assert.Single(refreshed.Objects.Single(item => item.ObjectPath == VolumePath)
            .Interface("org.freedesktop.UDisks2.Filesystem").Property("MountPoints").AsStringArray()));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            enumerator.MoveNextAsync().AsTask().WaitAsync(Timeout));
    }

    [LinuxFact]
    public async Task PreservesAuthorizationErrorsAndDoesNotSendCancelledOperations()
    {
        await using var bus = await PrivateBus.StartAsync();
        await using var service = await FakeUDisksService.StartAsync(bus.Address);
        await using var transport = await TmdsUDisksTransport.ConnectAsync(bus.Address);
        await using var client = new UDisksClient(transport);
        foreach (var code in new[] { "NotAuthorized", "NotAuthorizedDismissed" })
        {
            service.MountError = "org.freedesktop.UDisks2.Error." + code;
            var error = await Assert.ThrowsAsync<UDisksClientException>(() =>
                client.MountAsync(VolumePath).AsTask().WaitAsync(Timeout));
            Assert.Equal(service.MountError, error.Code);
            Assert.Equal("Native authorization failed.", error.Message);
        }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.EjectAsync(DrivePath, cancellation.Token).AsTask());
        Assert.Equal(["Mount", "Mount"], service.Methods);
    }

    [LinuxFact]
    public async Task ServiceDisappearanceFailsAndPrivateBusShutdownReapsItsProcess()
    {
        var bus = await PrivateBus.StartAsync();
        try
        {
            await using var transport = await TmdsUDisksTransport.ConnectAsync(bus.Address);
            var service = await FakeUDisksService.StartAsync(bus.Address);
            await service.DisposeAsync();
            var error = await Assert.ThrowsAsync<UDisksTransportException>(() =>
                transport.GetManagedObjectsAsync().AsTask().WaitAsync(Timeout));
            Assert.Equal("org.freedesktop.DBus.Error.ServiceUnknown", error.Code);
        }
        finally
        {
            await bus.DisposeAsync();
        }
        Assert.True(bus.ExitedOnDispose);
        Assert.False(bus.SocketDirectoryExists);
    }

    public sealed class LinuxFactAttribute : FactAttribute
    {
        public LinuxFactAttribute()
        {
            if (!OperatingSystem.IsLinux())
                Skip = "Requires Linux and dbus-daemon; never uses the host system bus.";
        }
    }

    private sealed class PrivateBus : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly DirectoryInfo _directory;

        private PrivateBus(Process process, string address, DirectoryInfo directory)
        {
            _process = process;
            Address = address;
            _directory = directory;
        }

        public string Address { get; }
        public bool HasExited => _process.HasExited;
        public bool ExitedOnDispose { get; private set; }
        public bool SocketDirectoryExists => Directory.Exists(_directory.FullName);

        public static async ValueTask<PrivateBus> StartAsync()
        {
            var directory = Directory.CreateTempSubdirectory("pspman-test-bus-");
            Process? process = null;
            try
            {
                process = Process.Start(new ProcessStartInfo("dbus-daemon",
                ["--session", "--nofork", "--nopidfile", "--print-address=1",
                    "--address=unix:path=" + System.IO.Path.Combine(directory.FullName, "bus")])
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }) ?? throw new InvalidOperationException("Could not start dbus-daemon.");
                var address = await process.StandardOutput.ReadLineAsync().WaitAsync(Timeout)
                    ?? throw new InvalidOperationException("The private D-Bus did not publish an address.");
                return new PrivateBus(process, address, directory);
            }
            catch
            {
                if (process is not null)
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().WaitAsync(Timeout);
                    process.Dispose();
                }
                directory.Delete(recursive: true);
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync().WaitAsync(Timeout);
            ExitedOnDispose = _process.HasExited;
            _process.Dispose();
            _directory.Delete(recursive: true);
        }
    }

    private sealed class FakeUDisksService : IAsyncDisposable
    {
        private DBusConnection _connection = null!;
        private FakeUDisksHandler _handler = null!;

        private FakeUDisksService() { }

        public IReadOnlyList<string> Methods => _handler.Methods;
        public Exception? ConnectionException { get; private set; }
        public string? MountError
        {
            get => _handler.MountError;
            set => _handler.MountError = value;
        }

        public static async ValueTask<FakeUDisksService> StartAsync(string address)
        {
            var service = new FakeUDisksService();
            var options = new DBusConnectionOptions(address)
            {
                OnException = context => service.ConnectionException = context.Exception
            };
            var connection = new DBusConnection(options);
            try
            {
                await connection.ConnectAsync().AsTask().WaitAsync(Timeout);
                if (!await connection.TryRequestNameAsync("org.freedesktop.UDisks2").WaitAsync(Timeout))
                    throw new InvalidOperationException("The fake service could not own the UDisks2 name.");
                var handler = new FakeUDisksHandler(connection);
                connection.AddMethodHandler(handler);
                service._connection = connection;
                service._handler = handler;
                return service;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        public ValueTask EmitChangeAsync(UDisksChangeKind kind, string objectPath) =>
            _handler.EmitChangeAsync(kind, objectPath);

        public ValueTask DisposeAsync()
        {
            _connection.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeUDisksHandler(DBusConnection connection) : IPathMethodHandler
    {
        private readonly ConcurrentQueue<string> _methods = new();

        public string Path => "/org/freedesktop/UDisks2";
        public bool HandlesChildPaths => true;
        public IReadOnlyList<string> Methods => _methods.ToArray();
        public string? MountError { get; set; }
        private string _mountRoot = "/run/media/fake";

        public ValueTask HandleMethodAsync(MethodContext context)
        {
            var member = context.Request.MemberAsString ?? string.Empty;
            _methods.Enqueue(member);
            if (member != "GetManagedObjects")
            {
                Assert.Equal("a{sv}", context.Request.SignatureAsString);
                Assert.Equal(member is "Mount" or "Unmount" ? VolumePath : DrivePath,
                    context.Request.PathAsString);
                Assert.Equal(member is "Mount" or "Unmount"
                    ? "org.freedesktop.UDisks2.Filesystem" : "org.freedesktop.UDisks2.Drive",
                    context.Request.InterfaceAsString);
                Assert.Empty(context.Request.GetBodyReader().ReadDictionaryOfStringToVariantValue());
            }
            if (member == "Mount" && MountError is { } error)
            {
                context.ReplyError(error, "Native authorization failed.");
                return ValueTask.CompletedTask;
            }
            if (member == "PowerOff")
            {
                context.ReplyError("org.freedesktop.UDisks2.Error.Busy", "The fake device is busy.");
                return ValueTask.CompletedTask;
            }

            var writer = context.CreateReplyWriter(member == "GetManagedObjects"
                ? "a{oa{sa{sv}}}" : member == "Mount" ? "s" : string.Empty);
            try
            {
                if (member == "GetManagedObjects")
                    WriteInventory(ref writer, _mountRoot);
                else if (member == "Mount")
                    writer.WriteString("/run/media/fake");
                context.Reply(writer.CreateMessage());
            }
            finally { writer.Dispose(); }
            return ValueTask.CompletedTask;
        }

        private static void WriteInventory(ref MessageWriter writer, string mountRoot)
        {
            var objects = writer.WriteDictionaryStart();
            writer.WriteDictionaryEntryStart();
            writer.WriteObjectPath(DrivePath);
            var interfaces = writer.WriteDictionaryStart();
            writer.WriteDictionaryEntryStart();
            writer.WriteString("org.freedesktop.UDisks2.Drive");
            var properties = writer.WriteDictionaryStart();
            writer.WriteDictionaryEntryStart();
            writer.WriteString("CanPowerOff");
            writer.WriteVariantBool(true);
            writer.WriteDictionaryEntryStart();
            writer.WriteString("MediaCompatibility");
            writer.WriteSignature("as");
            writer.WriteArray(new[] { "optical_dvd" });
            writer.WriteDictionaryEnd(properties);
            writer.WriteDictionaryEnd(interfaces);
            writer.WriteDictionaryEntryStart();
            writer.WriteObjectPath(VolumePath);
            interfaces = writer.WriteDictionaryStart();
            writer.WriteDictionaryEntryStart();
            writer.WriteString("org.freedesktop.UDisks2.Block");
            properties = writer.WriteDictionaryStart();
            writer.WriteDictionaryEntryStart();
            writer.WriteString("Drive");
            writer.WriteVariantObjectPath(DrivePath);
            writer.WriteDictionaryEntryStart();
            writer.WriteString("Device");
            writer.WriteSignature("ay");
            writer.WriteArray(System.Text.Encoding.UTF8.GetBytes("/dev/fake1\0"));
            writer.WriteDictionaryEnd(properties);
            writer.WriteDictionaryEntryStart();
            writer.WriteString("org.freedesktop.UDisks2.Filesystem");
            properties = writer.WriteDictionaryStart();
            writer.WriteDictionaryEntryStart();
            writer.WriteString("MountPoints");
            writer.WriteSignature("aay");
            var roots = writer.WriteArrayStart(DBusType.Array);
            writer.WriteArray(System.Text.Encoding.UTF8.GetBytes(mountRoot + "\0"));
            writer.WriteArrayEnd(roots);
            writer.WriteDictionaryEnd(properties);
            writer.WriteDictionaryEnd(interfaces);
            writer.WriteDictionaryEnd(objects);
        }

        public ValueTask EmitChangeAsync(UDisksChangeKind kind, string objectPath)
        {
            using var writer = connection.GetMessageWriter();
            writer.WriteSignalHeader(destination: null,
                path: kind == UDisksChangeKind.PropertiesChanged ? objectPath : Path,
                @interface: kind == UDisksChangeKind.PropertiesChanged
                    ? "org.freedesktop.DBus.Properties" : "org.freedesktop.DBus.ObjectManager",
                member: kind.ToString(), signature: kind switch
                {
                    UDisksChangeKind.InterfacesAdded => "oa{sa{sv}}",
                    UDisksChangeKind.InterfacesRemoved => "oas",
                    _ => "sa{sv}as"
                });
            if (kind == UDisksChangeKind.PropertiesChanged)
            {
                _mountRoot = "/run/media/changed";
                writer.WriteString("org.freedesktop.UDisks2.Filesystem");
                var changed = writer.WriteDictionaryStart();
                writer.WriteDictionaryEntryStart();
                writer.WriteString("MountPoints");
                writer.WriteSignature("aay");
                var roots = writer.WriteArrayStart(DBusType.Array);
                writer.WriteArray(System.Text.Encoding.UTF8.GetBytes("/run/media/changed\0"));
                writer.WriteArrayEnd(roots);
                writer.WriteDictionaryEnd(changed);
                writer.WriteArray(System.Array.Empty<string>());
            }
            else
            {
                writer.WriteObjectPath(objectPath);
                if (kind == UDisksChangeKind.InterfacesAdded)
                {
                    var interfaces = writer.WriteDictionaryStart();
                    writer.WriteDictionaryEnd(interfaces);
                }
                else
                    writer.WriteArray(new[] { "org.freedesktop.UDisks2.Filesystem" });
            }
            Assert.True(connection.TrySendMessage(writer.CreateMessage()));
            return ValueTask.CompletedTask;
        }
    }
}
