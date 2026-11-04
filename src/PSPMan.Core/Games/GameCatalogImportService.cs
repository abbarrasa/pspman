namespace PSPMan.Core.Games;

public sealed class GameCatalogVerifiedImportSession : IDisposable
{
    private readonly IGameCatalogImportReader reader;
    private bool disposed;

    internal GameCatalogVerifiedImportSession(GameCatalogImportPreview preview, IGameCatalogImportReader reader)
    {
        Preview = preview;
        this.reader = reader;
    }

    public GameCatalogImportPreview Preview { get; }

    internal IGameCatalogImportReader Reader
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return reader;
        }
    }

    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            reader.Dispose();
        }
    }
}

public sealed class GameCatalogImportPreview
{
    internal GameCatalogImportPreview(
        string sourcePath,
        GameCatalogImportFingerprint fingerprint,
        GameCatalogImportClassification classification,
        IEnumerable<GameCatalogImportRowError> rowErrors)
    {
        SourcePath = sourcePath;
        Fingerprint = fingerprint;
        NewGames = classification.NewGames;
        ModifiedGames = classification.ModifiedGames;
        RowErrors = Array.AsReadOnly(rowErrors.ToArray());
    }

    public string SourcePath { get; }
    public GameCatalogImportFingerprint Fingerprint { get; }
    public IReadOnlyList<GameCatalogImportCandidate> NewGames { get; }
    public IReadOnlyList<GameCatalogImportModification> ModifiedGames { get; }
    public IReadOnlyList<GameCatalogImportRowError> RowErrors { get; }
    public int NewCount => NewGames.Count;
    public int ModifiedCount => ModifiedGames.Count;
    public int ErrorCount => RowErrors.Count;
}

public sealed class GameCatalogImportService
{
    private readonly IGameRecordRepository repository;
    private readonly IGameCatalogImportSource source;

    public GameCatalogImportService(IGameRecordRepository repository, IGameCatalogImportSource source)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.source = source ?? throw new ArgumentNullException(nameof(source));
    }

    /// <summary>Reopens and verifies a confirmed preview. The caller must dispose a successful session.</summary>
    public GameCatalogResult<GameCatalogVerifiedImportSession> PrepareConfirmedImport(
        GameCatalogImportPreview preview, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reader = source.Open(preview.SourcePath);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                reader.VerifyUnchanged(preview.Fingerprint);
                cancellationToken.ThrowIfCancellationRequested();
                return GameCatalogResult<GameCatalogVerifiedImportSession>.Success(new(preview, reader));
            }
            catch
            {
                reader.Dispose();
                throw;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return GameCatalogResult<GameCatalogVerifiedImportSession>.Failure("The CSV import confirmation was cancelled.");
        }
        catch (Exception exception) when (exception is GameCatalogImportReadException or GameCatalogImportSourceChangedException)
        {
            return GameCatalogResult<GameCatalogVerifiedImportSession>.Failure(exception.Message);
        }
        catch (Exception)
        {
            return GameCatalogResult<GameCatalogVerifiedImportSession>.Failure("Unable to verify the confirmed CSV import source.");
        }
    }

    public GameCatalogResult<GameCatalogImportPreview> Preview(string sourcePath, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            GameRecord[] catalogSnapshot;
            try
            {
                catalogSnapshot = repository.List().ToArray();
            }
            catch (InvalidDataException)
            {
                return GameCatalogResult<GameCatalogImportPreview>.Corrupt();
            }
            cancellationToken.ThrowIfCancellationRequested();
            using var reader = source.Open(sourcePath);
            cancellationToken.ThrowIfCancellationRequested();
            var fingerprint = reader.CaptureFingerprint();
            cancellationToken.ThrowIfCancellationRequested();
            var candidates = new Dictionary<GameCatalogImportIdentity, GameCatalogImportCandidate>();
            var rowErrors = new List<GameCatalogImportRowError>();
            GameCatalogCsvHeaderMap? headerMap = null;
            var tokenizer = new GameCatalogCsvTokenizer();

            void EvaluateRecord(GameCatalogCsvRecord record)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (headerMap is null)
                {
                    if (!GameCatalogImportRules.TryMapHeadings(record.Cells, out headerMap, out var error))
                    {
                        throw new FormatException($"CSV header starting on line {record.StartLine}: {error}");
                    }
                    return;
                }

                if (!GameCatalogImportRules.SelectLastValid(record, headerMap, candidates, out var rowError))
                {
                    rowErrors.Add(rowError);
                }
            }

            var buffer = new char[4096];
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = reader.Read(buffer);
                cancellationToken.ThrowIfCancellationRequested();
                if (count == 0)
                {
                    break;
                }
                tokenizer.Push(buffer.AsSpan(0, count), EvaluateRecord);
            }
            tokenizer.Complete(EvaluateRecord);
            if (headerMap is null)
            {
                throw new FormatException("The CSV source does not contain a header.");
            }

            reader.VerifyUnchanged(fingerprint);
            cancellationToken.ThrowIfCancellationRequested();
            var classification = GameCatalogImportRules.ClassifyCandidates(candidates, catalogSnapshot);
            cancellationToken.ThrowIfCancellationRequested();
            return GameCatalogResult<GameCatalogImportPreview>.Success(
                new(sourcePath, fingerprint, classification, rowErrors));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return GameCatalogResult<GameCatalogImportPreview>.Failure("The CSV import preview was cancelled.");
        }
        catch (Exception exception) when (exception is FormatException or GameCatalogImportReadException or GameCatalogImportSourceChangedException)
        {
            return GameCatalogResult<GameCatalogImportPreview>.Failure(exception.Message);
        }
        catch (Exception)
        {
            return GameCatalogResult<GameCatalogImportPreview>.Failure("Unable to prepare the CSV import preview.");
        }
    }
}
