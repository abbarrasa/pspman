using System.Text;
using System.Security.Cryptography;
using PSPMan.Core.Games;

namespace PSPMan.Infraestructure.Games;

public sealed class FileGameCatalogImportSource : IGameCatalogImportSource
{
    private readonly Func<string, Stream> openRead;

    public FileGameCatalogImportSource() : this(path => new FileStream(path, FileMode.Open, FileAccess.Read,
        FileShare.Read, bufferSize: 4096, FileOptions.SequentialScan))
    {
    }

    internal FileGameCatalogImportSource(Func<string, Stream> openRead)
    {
        this.openRead = openRead ?? throw new ArgumentNullException(nameof(openRead));
    }

    public IGameCatalogImportReader Open(string sourcePath)
    {
        try
        {
            var path = Path.GetFullPath(sourcePath);
            var stream = openRead(path);
            try
            {
                if (!stream.CanRead || !stream.CanSeek)
                {
                    throw new IOException("The CSV source must support reading and seeking.");
                }
                return new Reader(stream, path, openRead);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new GameCatalogImportReadException("Unable to open the CSV source.", exception);
        }
    }

    private static GameCatalogImportFingerprint HashContent(Stream stream)
    {
        var position = stream.Position;
        try
        {
            stream.Position = 0;
            return new(Convert.ToHexString(SHA256.HashData(stream)));
        }
        finally
        {
            stream.Position = position;
        }
    }

    private sealed class Reader(Stream stream, string sourcePath, Func<string, Stream> openRead) : IGameCatalogImportReader
    {
        private bool disposed;
        // A UTF-8 preamble is consumed, but UTF-16/32 detection must remain disabled.
        private readonly StreamReader reader = new(stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: false, bufferSize: 4096);

        public int Read(Span<char> buffer)
        {
            try
            {
                return reader.Read(buffer);
            }
            catch (DecoderFallbackException exception)
            {
                throw new GameCatalogImportReadException("The CSV source is not valid UTF-8.", exception);
            }
            catch (IOException exception)
            {
                throw new GameCatalogImportReadException("Unable to read the CSV source.", exception);
            }
        }

        public GameCatalogImportFingerprint CaptureFingerprint()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            try
            {
                return HashContent(stream);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                throw new GameCatalogImportReadException("Unable to fingerprint the CSV source.", exception);
            }
        }

        public void VerifyUnchanged(GameCatalogImportFingerprint expected)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ArgumentNullException.ThrowIfNull(expected);
            if (CaptureFingerprint() != expected)
            {
                throw new GameCatalogImportSourceChangedException();
            }

            GameCatalogImportFingerprint current;
            try
            {
                using var currentStream = openRead(sourcePath);
                current = HashContent(currentStream);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                throw new GameCatalogImportReadException("Unable to verify the CSV source.", exception);
            }

            if (current != expected)
            {
                throw new GameCatalogImportSourceChangedException();
            }
        }

        public void Dispose()
        {
            disposed = true;
            reader.Dispose();
        }
    }
}
