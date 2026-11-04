namespace PSPMan.Core.Games;

public sealed class GameCatalogVerifiedImportSession : IDisposable
{
    private readonly IGameCatalogImportReader reader;
    private bool disposed;
    private bool consumed;

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

    internal IGameCatalogImportReader BeginApply()
    {
        var currentReader = Reader;
        if (consumed)
        {
            throw new InvalidOperationException("The verified import session has already been used.");
        }
        consumed = true;
        return currentReader;
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

public sealed class GameCatalogImportApplyResult
{
    internal GameCatalogImportApplyResult(int createdCount, int updatedCount,
        IEnumerable<GameRecord> savedRecords, string? error,
        IEnumerable<GameCatalogImportRowError>? saveErrors = null, bool isCorrupt = false)
    {
        CreatedCount = createdCount;
        UpdatedCount = updatedCount;
        SavedRecords = Array.AsReadOnly(savedRecords.ToArray());
        Error = error;
        SaveErrors = Array.AsReadOnly(saveErrors?.ToArray() ?? []);
        IsCorrupt = isCorrupt;
    }

    public int CreatedCount { get; }
    public int UpdatedCount { get; }
    public IReadOnlyList<GameRecord> SavedRecords { get; }
    public string? Error { get; }
    public IReadOnlyList<GameCatalogImportRowError> SaveErrors { get; }
    public int FailedCount => SaveErrors.Count;
    public bool IsCorrupt { get; }
    public bool IsSuccess => Error is null && FailedCount == 0 && !IsCorrupt;
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

    /// <summary>Consumes and closes a verified session, applying only its proposed changes.</summary>
    public GameCatalogImportApplyResult Apply(
        GameCatalogVerifiedImportSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var savedRecords = new List<GameRecord>();
        var createdCount = 0;
        var updatedCount = 0;
        var saveErrors = new List<GameCatalogImportRowError>();
        var isCorrupt = false;
        const string corruptMessage = "The game catalog is corrupt. Catalog changes are disabled.";
        try
        {
            var reader = session.BeginApply();
            using (session)
            {
                cancellationToken.ThrowIfCancellationRequested();
                reader.VerifyUnchanged(session.Preview.Fingerprint);
                cancellationToken.ThrowIfCancellationRequested();
                var operations = session.Preview.NewGames.Select(candidate =>
                        (candidate.StartLine, ExistingId: (Guid?)null, candidate.Input))
                    .Concat(session.Preview.ModifiedGames.Select(modification =>
                        (modification.StartLine, ExistingId: (Guid?)modification.ExistingId, modification.Input)))
                    .OrderBy(operation => operation.StartLine)
                    .ToArray();

                var operationIndex = 0;
                GameCatalogCsvHeaderMap? headerMap = null;
                var tokenizer = new GameCatalogCsvTokenizer();

                void ApplyRecord(GameCatalogCsvRecord sourceRecord)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (headerMap is null)
                    {
                        if (!GameCatalogImportRules.TryMapHeadings(sourceRecord.Cells, out headerMap, out var error))
                        {
                            throw new FormatException($"CSV header starting on line {sourceRecord.StartLine}: {error}");
                        }
                        return;
                    }
                    if (operationIndex == operations.Length) return;
                    var operation = operations[operationIndex];
                    if (sourceRecord.StartLine < operation.StartLine) return;
                    if (sourceRecord.StartLine != operation.StartLine
                        || !GameCatalogImportRules.TryMapSourceRow(sourceRecord, headerMap, out var input, out _)
                        || input != operation.Input)
                    {
                        throw new GameCatalogImportSourceChangedException();
                    }

                    // Check even when several logical rows were already buffered by the reader.
                    reader.VerifyUnchanged(session.Preview.Fingerprint);
                    cancellationToken.ThrowIfCancellationRequested();
                    operationIndex++;
                    var record = new GameRecord(operation.ExistingId ?? Guid.NewGuid(), input.Title,
                        input.Medium, input.DvdNumber, input.Location, input.Format, input.SizeBytes, input.Notes);
                    try
                    {
                        if (operation.ExistingId is null)
                        {
                            repository.Create(record);
                            createdCount++;
                        }
                        else
                        {
                            repository.Update(record);
                            updatedCount++;
                        }
                        savedRecords.Add(record);
                    }
                    catch (InvalidDataException)
                    {
                        saveErrors.Add(new(operation.StartLine, corruptMessage, []));
                        isCorrupt = true;
                        throw;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        var action = operation.ExistingId is null ? "create" : "update";
                        saveErrors.Add(new(operation.StartLine,
                            $"Unable to {action} the game record: {exception.Message}", []));
                    }
                }

                var buffer = new char[4096];
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var count = reader.Read(buffer);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (count == 0) break;
                    tokenizer.Push(buffer.AsSpan(0, count), ApplyRecord);
                }
                tokenizer.Complete(ApplyRecord);
                if (headerMap is null)
                {
                    throw new FormatException("The CSV source does not contain a header.");
                }
                if (operationIndex != operations.Length)
                {
                    throw new GameCatalogImportSourceChangedException();
                }
                reader.VerifyUnchanged(session.Preview.Fingerprint);
                cancellationToken.ThrowIfCancellationRequested();
            }

            return new(createdCount, updatedCount, savedRecords,
                saveErrors.Count == 0 ? null : "Some game records could not be saved.", saveErrors);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(createdCount, updatedCount, savedRecords, "The CSV import was cancelled.", saveErrors, isCorrupt);
        }
        catch (InvalidDataException) when (isCorrupt)
        {
            return new(createdCount, updatedCount, savedRecords, corruptMessage, saveErrors, isCorrupt: true);
        }
        catch (Exception exception) when (exception is FormatException or GameCatalogImportReadException or GameCatalogImportSourceChangedException)
        {
            return new(createdCount, updatedCount, savedRecords, exception.Message, saveErrors, isCorrupt);
        }
        catch (Exception)
        {
            return new(createdCount, updatedCount, savedRecords, "Unable to apply the confirmed CSV import.", saveErrors, isCorrupt);
        }
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
