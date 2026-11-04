using System.Text;
using PSPMan.Core.Games;

namespace PSPMan.Infraestructure.Games;

public sealed class FileGameCatalogImportSource : IGameCatalogImportSource
{
    public IGameCatalogImportReader Open(string sourcePath)
    {
        try
        {
            return new Reader(new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, bufferSize: 4096, FileOptions.SequentialScan));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new GameCatalogImportReadException("Unable to open the CSV source.", exception);
        }
    }

    private sealed class Reader(Stream stream) : IGameCatalogImportReader
    {
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

        public void Dispose() => reader.Dispose();
    }
}
