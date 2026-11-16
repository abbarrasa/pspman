using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using PSPMan.Core.Transfers;
using Native = PSPMan.Infraestructure.Transfers.SevenZipNativeMethods;

namespace PSPMan.Infraestructure.Transfers;

internal sealed class SevenZipNativeSession : IGameArchiveSession
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly IGameArchiveSource source;
    private string? password;
    private readonly ArchiveRuntimeResolver.RuntimeAssets assets;
    private readonly SemaphoreSlim serial = new(1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly AsyncLocal<bool> inCallback = new();
    private readonly object nativeGate = new(), disposalGate = new(), sourceGate = new();
    private readonly Dictionary<ulong, Stream> inputs = [];
    private readonly Dictionary<string, string> hashes = new(StringComparer.Ordinal);
    private Native.Api? api;
    private Native.Callbacks callbacks;
    private nint handle;
    private ulong nextHandle;
    private CancellationToken activeToken;
    private Exception? callbackError;
    private GameCopyError? entryError;
    private ImmutableArray<GameArchiveEntry> entries;
    private string sourceIdentity = "";
    private IGameArchiveExtractionTarget? target;
    private IProgress<ArchiveByteProgress>? progress;
    private int currentIndex = -1;
    private bool entryPrepared;
    private long nativeDecoded, nativeWritten, baselineDecoded, baselineWritten;
    private Task? disposal;
    private int closing;

    internal SevenZipNativeSession(IGameArchiveSource source, string? password, ArchiveRuntimeResolver.RuntimeAssets assets)
    {
        this.source = source; this.password = password; this.assets = assets;
        callbacks = new Native.Callbacks {
            Size = (uint)Marshal.SizeOf<Native.Callbacks>(), Abi = 1,
            Open = OpenPart, Read = Read, Seek = Seek, Close = ClosePart, Password = GetPassword,
            Begin = BeginEntry, Write = Write, End = EndEntry, Progress = ReportProgress
        };
    }

    internal async ValueTask OpenAsync(CancellationToken token)
    {
        await RunAsync(() =>
        {
            sourceIdentity = source.CaptureEvidenceAsync(activeToken).AsTask().GetAwaiter().GetResult();
            source.ValidateAsync(sourceIdentity, activeToken).AsTask().GetAwaiter().GetResult();
            var name = FirstPart(source.ArchiveName);
            // Verify the selected part itself before resolving its first part; no alternative source fallback.
            using (var selected = source.OpenPartAsync(source.ArchiveName, activeToken).AsTask().GetAwaiter().GetResult())
                ValidateInput(selected);
            using var signature = source.OpenPartAsync(name, activeToken).AsTask().GetAwaiter().GetResult();
            ValidateInput(signature);
            var bytes = new byte[8];
            var length = signature.ReadAtLeastAsync(bytes, 7, throwOnEndOfStream: false, activeToken).AsTask().GetAwaiter().GetResult();
            // A split ZIP's final .zip part may start in the middle of payload data, not with PK.
            uint format = length >= 7 && bytes.AsSpan(0, 6).SequenceEqual(new byte[] { 0x52,0x61,0x72,0x21,0x1A,0x07 })
                ? bytes[6] == 0 ? 3u : bytes[6] == 1 ? 0xCCu : 0
                : (length >= 2 && bytes.AsSpan(0, 2).SequenceEqual("PK"u8)) || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? 1u : 0;
            if (format == 0) throw Error(GameCopyErrorCode.UnsupportedArchive, "The source is not a supported ZIP/RAR archive.");
            api = new Native.Api(assets.Bridge);
            if (api.Abi() != 1 || api.Version() != 2606) throw Error(GameCopyErrorCode.UnsupportedArchive, "Native archive ABI/version mismatch.");
            var engine = Utf8.GetBytes(assets.Engine);
            lock (nativeGate) Check(api.Create(engine, (uint)engine.Length, ref callbacks, out handle));
            var encodedName = Utf8.GetBytes(name);
            Check(api.Open(handle, format, encodedName, (uint)encodedName.Length));
            entries = ReadMetadata();
            VerifySources();
            return true;
        }, token).ConfigureAwait(false);
    }

    public async ValueTask<GameArchiveInspection> InspectAsync(CancellationToken cancellationToken)
        => await RunAsync(() =>
        {
            VerifySources();
            if (entries.IsEmpty) throw Error(GameCopyErrorCode.EmptyArchive, "The archive is empty.");
            entries = ArchiveSizeInspector.Complete(entries, (indices, counter) => ExtractCore(indices, counter, null));
            var total = GameCopyRules.SumActualBytes(entries);
            if (total.Error is not null) throw new GameCopyException(total.Error);
            return new GameArchiveInspection(entries, Fingerprint());
        }, cancellationToken).ConfigureAwait(false);

    public async ValueTask ExtractAsync(ImmutableArray<int> indices, IGameArchiveExtractionTarget target,
        IProgress<ArchiveByteProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        await RunAsync(() => { ExtractCore(indices, target, progress); return true; }, cancellationToken).ConfigureAwait(false);
    }

    private void ExtractCore(ImmutableArray<int> indices, IGameArchiveExtractionTarget output, IProgress<ArchiveByteProgress>? observer)
    {
        if (indices.IsDefault || indices.Any(i => i < 0 || i >= entries.Length)
            || indices.Zip(indices.Skip(1)).Any(p => p.First >= p.Second))
            throw new ArgumentException("Extraction indices must be distinct and archive-ordered.", nameof(indices));
        VerifySources();
        target = output; progress = observer;
        nativeDecoded = nativeWritten = 0;
        entryError = null;
        Exception? failure = null;
        try { Check(api!.Extract(handle, indices.Select(i => (uint)i).ToArray(), (uint)indices.Length)); }
        catch (Exception error) { failure = error; }
        finally
        {
            // Native end normally closes the prepared output, including on failure. Never leak if a codec aborts early.
            if (entryPrepared) Guard(() => { CompleteCurrent(CurrentError(2)); return 0; }, GameCopyErrorCode.CloseError, false);
            target = null; progress = null;
        }
        if (callbackError is not null) ExceptionDispatchInfo.Capture(callbackError).Throw();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        VerifySources();
    }

    private async ValueTask<T> RunAsync<T>(Func<T> work, CancellationToken token)
    {
        if (inCallback.Value) throw new InvalidOperationException("Archive session calls cannot reenter a native callback.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        await serial.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref closing) != 0, this);
            activeToken = linked.Token;
            callbackError = null;
            using var cancel = activeToken.Register(CancelNative);
            return await Task.Run(() => { activeToken.ThrowIfCancellationRequested(); return work(); }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (FileNotFoundException error) { throw Error(GameCopyErrorCode.MissingArchive, "The selected archive or its first part was not found.", error); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw Error(GameCopyErrorCode.ReadError, $"Archive source read failed: {error.Message}", error); }
        catch (Exception error) when (error is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        { throw Error(GameCopyErrorCode.UnsupportedArchive, "The bundled archive runtime could not be loaded.", error); }
        finally { serial.Release(); }
    }

    private ImmutableArray<GameArchiveEntry> ReadMetadata()
    {
        Check(api!.Count(handle, out var count));
        if (count > int.MaxValue) throw Error(GameCopyErrorCode.SizeOverflow, "The archive contains too many entries.");
        var builder = ImmutableArray.CreateBuilder<GameArchiveEntry>((int)count);
        for (uint i = 0; i < count; i++)
        {
            activeToken.ThrowIfCancellationRequested();
            var result = api.Metadata(handle, i, out _, [], 0, out var length);
            if (result is not (0 or 8)) Check(result);
            if (length > int.MaxValue) throw Error(GameCopyErrorCode.InvalidName, "The archive entry name is too large.");
            var buffer = new byte[length];
            Check(api.Metadata(handle, i, out var entry, buffer, length, out _));
            if ((entry.Flags & 8) != 0 && entry.Size > long.MaxValue)
                throw Error(GameCopyErrorCode.SizeOverflow, "Actual decompressed size exceeds the supported byte range.");
            builder.Add(new((int)i, Utf8.GetString(buffer), (entry.Flags & 8) != 0 ? (long)entry.Size : null,
                (entry.Flags & 1) != 0, (entry.Flags & 2) != 0, (entry.Flags & 4) != 0));
        }
        return builder.MoveToImmutable();
    }

    private int Guard(Func<int> callback, GameCopyErrorCode errorCode, bool checkCancellation = true)
    {
        var previous = inCallback.Value;
        inCallback.Value = true;
        try { if (checkCancellation) activeToken.ThrowIfCancellationRequested(); return callback(); }
        catch (OperationCanceledException error) when (checkCancellation && activeToken.IsCancellationRequested)
        { Interlocked.CompareExchange(ref callbackError, error, null); return 1; }
        catch (Exception error)
        {
            var cause = error is GameCopyException ? error : Error(errorCode, $"Archive {errorCode}: {error.Message}", error);
            if (errorCode == GameCopyErrorCode.CloseError) Interlocked.Exchange(ref callbackError, cause);
            else Interlocked.CompareExchange(ref callbackError, cause, null);
            return 2;
        }
        finally { inCallback.Value = previous; }
    }
    private int OpenPart(nint context, nint name, uint length, out ulong opened)
    {
        ulong id = 0;
        var result = Guard(() =>
        {
            var leaf = Marshal.PtrToStringUTF8(name, checked((int)length))!;
            ValidateLeaf(leaf);
            Stream stream;
            try { stream = source.OpenPartAsync(leaf, activeToken).AsTask().GetAwaiter().GetResult(); }
            catch (FileNotFoundException) { return 6; }
            try
            {
                ValidateInput(stream);
                var hash = Hash(stream);
                lock (sourceGate)
                {
                    if (hashes.TryGetValue(leaf, out var expected) && expected != hash)
                        throw Error(GameCopyErrorCode.SourceChanged, "An archive part changed.");
                    hashes[leaf] = hash;
                    id = ++nextHandle;
                    inputs.Add(id, stream);
                }
                return 0;
            }
            catch { stream.Dispose(); throw; }
        }, GameCopyErrorCode.ReadError);
        opened = id; return result;
    }
    private Stream Input(ulong id) { lock (sourceGate) return inputs[id]; }
    private int Read(nint context, ulong id, nint data, uint size, out uint read)
    {
        uint actual = 0;
        var result = Guard(() =>
        {
            var buffer = ArrayPool<byte>.Shared.Rent((int)Math.Min(size, 65536));
            try
            {
                actual = (uint)Input(id).ReadAsync(buffer.AsMemory(0, (int)Math.Min(size, 65536)), activeToken).AsTask().GetAwaiter().GetResult();
                Marshal.Copy(buffer, 0, data, (int)actual); return 0;
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }, GameCopyErrorCode.ReadError);
        read = actual; return result;
    }
    private int Seek(nint context, ulong id, long offset, uint origin, out ulong position)
    {
        ulong actual = 0;
        var result = Guard(() =>
        {
            if (origin > 2) throw new IOException("Invalid archive seek origin.");
            actual = checked((ulong)Input(id).Seek(offset, (SeekOrigin)origin)); return 0;
        }, GameCopyErrorCode.ReadError);
        position = actual; return result;
    }
    private void ClosePart(nint context, ulong id) => Guard(() =>
    {
        Stream? stream;
        lock (sourceGate) inputs.Remove(id, out stream);
        stream?.Dispose(); return 0;
    }, GameCopyErrorCode.CloseError, false);
    private int GetPassword(nint context, nint data, uint capacity, out uint length)
    {
        uint actual = 0;
        var result = Guard(() =>
        {
            if (password is null) return 10;
            var bytes = Utf8.GetBytes(password);
            try
            {
                if (bytes.Length > capacity) throw Error(GameCopyErrorCode.UnsupportedArchive, "The archive password exceeds the native input capacity.");
                Marshal.Copy(bytes, 0, data, bytes.Length); actual = (uint)bytes.Length; return 0;
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }, GameCopyErrorCode.ReadError);
        length = actual; return result;
    }
    private Stream? currentOutput;
    private int BeginEntry(nint context, uint index) => Guard(() =>
    {
        currentIndex = checked((int)index);
        baselineDecoded = Interlocked.Read(ref nativeDecoded); baselineWritten = Interlocked.Read(ref nativeWritten);
        var destination = target!.PrepareEntryAsync(entries[currentIndex], activeToken).AsTask().GetAwaiter().GetResult();
        entryPrepared = true;
        currentOutput = destination.Output;
        if (destination.SkipReason is not null)
        {
            if (currentOutput is not null) throw new IOException("A skipped entry cannot own an output stream.");
            return 11;
        }
        if (!entries[currentIndex].IsDirectory && (currentOutput is null || !currentOutput.CanWrite))
            throw new IOException("An extracted file requires a writable controlled stream.");
        return 0;
    }, GameCopyErrorCode.WriteError);
    private int Write(nint context, uint index, nint data, uint size, out uint written)
    {
        uint actual = 0;
        var result = Guard(() =>
        {
            var buffer = ArrayPool<byte>.Shared.Rent(65536);
            try
            {
                while (actual < size)
                {
                    var length = (int)Math.Min(65536, size - actual);
                    Marshal.Copy(data + checked((nint)actual), buffer, 0, length);
                    currentOutput!.WriteAsync(buffer.AsMemory(0, length), activeToken).AsTask().GetAwaiter().GetResult();
                    actual += (uint)length;
                }
                return 0;
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }, GameCopyErrorCode.WriteError);
        written = actual; return result;
    }
    private int EndEntry(nint context, uint index, int code) => Guard(() =>
    {
        entryError = code == 0 ? null : CurrentError(code);
        CompleteCurrent(entryError); return 0;
    }, GameCopyErrorCode.CloseError, false);
    private void CompleteCurrent(GameCopyError? error)
    {
        if (!entryPrepared) return;
        entryPrepared = false;
        try { target!.CompleteEntryAsync(currentIndex, error).AsTask().GetAwaiter().GetResult(); }
        finally { currentOutput = null; currentIndex = -1; }
    }
    private int ReportProgress(nint context, ulong decoded, ulong written) => Guard(() =>
    {
        var d = checked((long)decoded); var w = checked((long)written);
        Interlocked.Exchange(ref nativeDecoded, d); Interlocked.Exchange(ref nativeWritten, w);
        var index = Volatile.Read(ref currentIndex);
        if (index >= 0) progress?.Report(new(index, d - baselineDecoded, w - baselineWritten));
        return 0;
    }, GameCopyErrorCode.WriteError);

    private void VerifySources()
    {
        source.ValidateAsync(sourceIdentity, activeToken).AsTask().GetAwaiter().GetResult();
        foreach (var (name, expected) in hashes.ToArray())
        {
            Stream stream;
            try { stream = source.OpenPartAsync(name, activeToken).AsTask().GetAwaiter().GetResult(); }
            catch (FileNotFoundException error) { throw Error(GameCopyErrorCode.MissingPart, "An archive part is no longer available.", error); }
            using var owned = stream;
            ValidateInput(stream);
            if (Hash(stream) != expected) throw Error(GameCopyErrorCode.SourceChanged, "An archive part changed after inspection.");
        }
    }
    private string Hash(Stream stream)
    {
        var position = stream.Position;
        try { stream.Position = 0; return Convert.ToHexStringLower(SHA256.HashDataAsync(stream, activeToken).AsTask().GetAwaiter().GetResult()); }
        finally { stream.Position = position; }
    }
    private string Fingerprint() => Convert.ToHexStringLower(SHA256.HashData(Utf8.GetBytes(
        sourceIdentity + "\0" + string.Join("\0", hashes.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "\0" + p.Value)))));
    private static void ValidateInput(Stream stream)
    {
        if (!stream.CanRead || !stream.CanSeek || stream.CanWrite) throw new IOException("Archive sources must be read-only and seekable.");
    }
    private static void ValidateLeaf(string name)
    {
        if (string.IsNullOrEmpty(name) || name is "." or ".." || name.IndexOfAny(['/', '\\', ':', '\0']) >= 0)
            throw Error(GameCopyErrorCode.SourceChanged, "An archive requested a part outside its selected source.");
    }
    private static string FirstPart(string name)
    {
        ValidateLeaf(name);
        var part = Regex.Match(name, @"\.part(\d+)\.rar$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (part.Success) return name[..part.Groups[1].Index] + "1".PadLeft(part.Groups[1].Length, '0') + name[(part.Groups[1].Index + part.Groups[1].Length)..];
        if (Regex.IsMatch(name, @"\.r\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return name[..name.LastIndexOf('.')] + ".rar";
        if (Regex.IsMatch(name, @"\.z\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return name[..name.LastIndexOf('.')] + ".zip";
        return name;
    }
    private GameCopyError CurrentError(int code) => callbackError is GameCopyException typed ? typed.Error : new(code switch {
        1 => GameCopyErrorCode.Cancelled,
        3 => GameCopyErrorCode.IncorrectPassword, 4 => GameCopyErrorCode.IndeterminateEncryptedError,
        5 => GameCopyErrorCode.CorruptArchive, 6 => GameCopyErrorCode.MissingPart, 7 or 8 or 9 => GameCopyErrorCode.UnsupportedArchive,
        10 => GameCopyErrorCode.PasswordRequired, _ => GameCopyErrorCode.ReadError
    }, code == 4 ? "Incorrect password or damaged archive" : code == 1 ? "Archive extraction was cancelled." : "Archive processing failed.", currentIndex >= 0 ? currentIndex : null);
    private void Check(int code)
    {
        if (callbackError is not null) ExceptionDispatchInfo.Capture(callbackError).Throw();
        activeToken.ThrowIfCancellationRequested();
        if (code == 1) throw new OperationCanceledException(activeToken);
        if (code != 0) throw new GameCopyException(entryError ?? CurrentError(code));
    }
    private static GameCopyException Error(GameCopyErrorCode code, string message, Exception? inner = null) => new(new(code, message), inner);
    private void CancelNative() { lock (nativeGate) { if (handle != 0) api!.Cancel(handle); } }
    public ValueTask DisposeAsync()
    {
        if (inCallback.Value) throw new InvalidOperationException("Archive disposal cannot reenter a native callback.");
        lock (disposalGate) return new(disposal ??= DisposeCoreAsync());
    }
    private async Task DisposeCoreAsync()
    {
        Interlocked.Exchange(ref closing, 1);
        await lifetime.CancelAsync().ConfigureAwait(false);
        await serial.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (nativeGate)
            {
                callbackError = null; // Previously reported errors are not new disposal failures.
                if (handle != 0) { api!.Destroy(handle); handle = 0; }
                api?.Dispose(); api = null;
            }
            GC.KeepAlive(callbacks);
            password = null;
            if (inputs.Count != 0) throw Error(GameCopyErrorCode.CloseError, "Native archive handles were not released.");
            if (callbackError is GameCopyException error && error.Error.Code == GameCopyErrorCode.CloseError) throw error;
        }
        finally { serial.Release(); lifetime.Dispose(); }
    }
}
