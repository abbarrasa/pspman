namespace PSPMan.Core.Games;

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

    public GameCatalogResult<GameCatalogImportPreview> Preview(string sourcePath)
    {
        try
        {
            var catalogSnapshot = repository.List().ToArray();
            using var reader = source.Open(sourcePath);
            var fingerprint = reader.CaptureFingerprint();
            var candidates = new Dictionary<GameCatalogImportIdentity, GameCatalogImportCandidate>();
            var rowErrors = new List<GameCatalogImportRowError>();
            GameCatalogCsvHeaderMap? headerMap = null;
            var tokenizer = new GameCatalogCsvTokenizer();

            void EvaluateRecord(GameCatalogCsvRecord record)
            {
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
            int count;
            while ((count = reader.Read(buffer)) != 0)
            {
                tokenizer.Push(buffer.AsSpan(0, count), EvaluateRecord);
            }
            tokenizer.Complete(EvaluateRecord);
            if (headerMap is null)
            {
                throw new FormatException("The CSV source does not contain a header.");
            }

            reader.VerifyUnchanged(fingerprint);
            var classification = GameCatalogImportRules.ClassifyCandidates(candidates, catalogSnapshot);
            return GameCatalogResult<GameCatalogImportPreview>.Success(
                new(sourcePath, fingerprint, classification, rowErrors));
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
