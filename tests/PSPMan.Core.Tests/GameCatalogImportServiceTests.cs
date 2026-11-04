using PSPMan.Core.Games;

namespace PSPMan.Core.Tests;

public class GameCatalogImportServiceTests
{
    private const string Header = "Title;Medium;DvdNumber;Location;Format;SizeBytes;Notes\r\n";
    private const string ValidRow = "Game;Usb;;/GAMES/game.rar;ISO;1024;\r\n";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OrdinaryCreateOrUpdateFailureIsReportedAndLaterCandidatesAreSaved(bool failCreate)
    {
        var original = new GameRecord(Guid.NewGuid(), "Original", GameMedium.Usb, null,
            failCreate ? "/GAMES/later.rar" : "/GAMES/fail.rar", GameFormat.Iso, 1024, "old");
        var repository = new WritableRepository([original]);
        repository.BeforeWrite = (_, record) =>
        {
            if (record.Location == "/GAMES/fail.rar") throw new IOException("Simulated storage failure.");
        };
        var source = new TestSource(Header + "First;Usb;;/GAMES/first.rar;ISO;1024;\n"
            + "Failed;Usb;;/GAMES/fail.rar;CHD;2048;\n"
            + "Later;Usb;;/GAMES/later.rar;CHD;2048;\n", 1);
        var service = new GameCatalogImportService(repository, source);

        var result = service.Apply(ConfirmImportPreview(service, source));

        Assert.False(result.IsSuccess);
        Assert.False(result.IsCorrupt);
        Assert.NotNull(result.Error);
        Assert.Equal(failCreate ? 1 : 2, result.CreatedCount);
        Assert.Equal(failCreate ? 1 : 0, result.UpdatedCount);
        Assert.Equal(1, result.FailedCount);
        var error = Assert.Single(result.SaveErrors);
        Assert.Equal(3, error.StartLine);
        Assert.Contains("Simulated storage failure.", error.Message);
        Assert.Equal(3, repository.Attempts.Count);
        Assert.Equal(new[] { "First", "Later" }, result.SavedRecords.Select(record => record.Title));
        Assert.Equal(repository.Writes.Select(write => write.Record), result.SavedRecords);
        if (!failCreate) Assert.Same(original, repository.Records[original.Id]);
        Assert.True(source.ConfirmationReader!.Disposed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CorruptionDuringCreateOrUpdateStopsBeforeLaterWritesAndRetainsEarlierSaves(bool failCreate)
    {
        GameRecord[] existing = failCreate ? [] :
            [new(Guid.NewGuid(), "Original", GameMedium.Usb, null, "/GAMES/fail.rar", GameFormat.Iso, 1024, "old")];
        var repository = new WritableRepository(existing);
        repository.BeforeWrite = (_, record) =>
        {
            if (record.Location == "/GAMES/fail.rar") throw new InvalidDataException("Corrupt catalog.");
        };
        var source = new TestSource(Header + "First;Usb;;/GAMES/first.rar;ISO;1024;\n"
            + "Failed;Usb;;/GAMES/fail.rar;CHD;2048;\n"
            + "Unattempted;Usb;;/GAMES/later.rar;ISO;1024;\n", 1);
        var service = new GameCatalogImportService(repository, source);

        var result = service.Apply(ConfirmImportPreview(service, source));

        Assert.False(result.IsSuccess);
        Assert.True(result.IsCorrupt);
        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(3, Assert.Single(result.SaveErrors).StartLine);
        Assert.Equal(2, repository.Attempts.Count);
        Assert.Equal("First", Assert.Single(result.SavedRecords).Title);
        Assert.DoesNotContain(repository.Records.Values, record => record.Title == "Unattempted");
        if (!failCreate) Assert.Same(existing[0], repository.Records[existing[0].Id]);
        Assert.True(source.ConfirmationReader!.Disposed);
    }

    [Fact]
    public void AllOrdinaryFailuresAreCountedWithoutAnyFalseSavedRecord()
    {
        var repository = new WritableRepository([])
        {
            BeforeWrite = (_, _) => throw new IOException("No storage available.")
        };
        var source = new TestSource(Header + "One;Usb;;/one.rar;ISO;1;\nTwo;Usb;;/two.rar;ISO;1;\n", 1);
        var service = new GameCatalogImportService(repository, source);

        var result = service.Apply(ConfirmImportPreview(service, source));

        Assert.False(result.IsSuccess);
        Assert.False(result.IsCorrupt);
        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(2, result.FailedCount);
        Assert.Equal(new long[] { 2, 3 }, result.SaveErrors.Select(error => error.StartLine));
        Assert.Empty(result.SavedRecords);
        Assert.Empty(repository.Records);
        Assert.Equal(2, repository.Attempts.Count);
        Assert.Throws<NotSupportedException>(() => ((IList<GameCatalogImportRowError>)result.SaveErrors).Clear());
    }

    [Fact]
    public void OrdinaryFailuresRemainReportedIfACorruptWriteLaterStopsTheImport()
    {
        var repository = new WritableRepository([]);
        repository.BeforeWrite = (_, record) =>
        {
            if (record.Title == "Ordinary") throw new IOException("Storage failure.");
            if (record.Title == "Corrupt") throw new InvalidDataException("Catalog corruption.");
        };
        var source = new TestSource(Header + "Ordinary;Usb;;/one.rar;ISO;1;\nSaved;Usb;;/two.rar;ISO;1;\n"
            + "Corrupt;Usb;;/three.rar;ISO;1;\nUnattempted;Usb;;/four.rar;ISO;1;\n", 1);
        var service = new GameCatalogImportService(repository, source);

        var result = service.Apply(ConfirmImportPreview(service, source));

        Assert.True(result.IsCorrupt);
        Assert.Equal(2, result.FailedCount);
        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(new long[] { 2, 4 }, result.SaveErrors.Select(error => error.StartLine));
        Assert.Equal("Saved", Assert.Single(result.SavedRecords).Title);
        Assert.Equal(3, repository.Attempts.Count);
    }

    [Fact]
    public void SourceVerificationErrorsAreNotCountedAsFailedWritesOrCatalogCorruption()
    {
        var repository = new WritableRepository([]);
        var source = new TestSource(Header + ValidRow, 1);
        var service = new GameCatalogImportService(repository, source);
        var session = ConfirmImportPreview(service, source);
        source.ConfirmationReader!.VerificationFailure = new InvalidDataException("Invalid source data.");

        var result = service.Apply(session);

        Assert.False(result.IsSuccess);
        Assert.False(result.IsCorrupt);
        Assert.Equal(0, result.FailedCount);
        Assert.Empty(repository.Attempts);
    }

    [Fact]
    public void CooperativeCancellationIsNotSwallowedAsAnOrdinarySaveFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var repository = new WritableRepository([]);
        repository.BeforeWrite = (_, record) =>
        {
            if (record.Title == "Cancel")
            {
                cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
            }
        };
        var source = new TestSource(Header + "Saved;Usb;;/one.rar;ISO;1;\nCancel;Usb;;/two.rar;ISO;1;\n"
            + "Unattempted;Usb;;/three.rar;ISO;1;\n", 1);
        var service = new GameCatalogImportService(repository, source);

        var result = service.Apply(ConfirmImportPreview(service, source), cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.False(result.IsCorrupt);
        Assert.Equal("The CSV import was cancelled.", result.Error);
        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(2, repository.Attempts.Count);
        Assert.Equal("Saved", Assert.Single(result.SavedRecords).Title);
        Assert.True(source.ConfirmationReader!.Disposed);
    }

    [Fact]
    public void AppliesOnlyFinalChangedCandidatesInSourceOrderWithCorrectIdsAndCounts()
    {
        var editedId = Guid.NewGuid();
        var sameId = Guid.NewGuid();
        var repository = new WritableRepository([
            new(editedId, "Original", GameMedium.Usb, null, "/GAMES/edit.rar", GameFormat.Iso, 1024, "old notes"),
            new(sameId, "Same", GameMedium.Usb, null, "/GAMES/same.rar", GameFormat.Iso, 1024, null)]);
        var source = new TestSource(Header
            + "First;Usb;;/GAMES/new.rar;ISO;1024;superseded\r\n"
            + "Edited;Usb;;/GAMES/edit.rar;CHD;2048;\r\n"
            + "Same;Usb;;/GAMES/same.rar;ISO;1024;\r\n"
            + "Last;Usb;;/GAMES/new.rar;CHD;4096;last notes\r\n"
            + "Invalid later;Usb;;/GAMES/new.rar;ISO;bad;\r\n"
            + "Other;MemoryCard;;/GAMES/offline.rar;ISO;2048;\r\n", 1);
        var service = new GameCatalogImportService(repository, source);
        var session = ConfirmImportPreview(service, source);
        Assert.Empty(repository.Writes);

        var result = service.Apply(session);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, result.CreatedCount);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(new[] { "update", "create", "create" }, repository.Writes.Select(write => write.Operation));
        Assert.Equal(repository.Writes.Select(write => write.Record), result.SavedRecords);
        var edit = repository.Records[editedId];
        Assert.Equal(new GameRecord(editedId, "Edited", GameMedium.Usb, null,
            "/GAMES/edit.rar", GameFormat.Chd, 2048, ""), edit);
        var added = repository.Writes.Where(write => write.Operation == "create").Select(write => write.Record).ToArray();
        Assert.Equal(new[] { "Last", "Other" }, added.Select(record => record.Title));
        Assert.Equal(2, added.Select(record => record.Id).Distinct().Count());
        Assert.All(added, record => Assert.DoesNotContain(record.Id, new[] { Guid.Empty, editedId, sameId }));
        Assert.Null(repository.Records[sameId].Notes);
        Assert.Equal(1, repository.ListCalls);
        Assert.True(source.ConfirmationReader!.Disposed);
        Assert.Equal(2, source.ConfirmationReader.VerifyCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ApplyingIdenticalMatchesDoesNotCreateOrUpdateRecords(string? notes)
    {
        var saved = new GameRecord(Guid.NewGuid(), "Game", GameMedium.Usb, null,
            "/GAMES/game.rar", GameFormat.Iso, 1024, notes);
        var repository = new WritableRepository([saved]);
        var source = new TestSource(Header + ValidRow, 1);
        var service = new GameCatalogImportService(repository, source);
        var session = ConfirmImportPreview(service, source);

        var result = service.Apply(session);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Empty(result.SavedRecords);
        Assert.Empty(repository.Writes);
        Assert.Same(saved, repository.Records[saved.Id]);
        Assert.True(source.ConfirmationReader!.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosedOrAlreadyUsedSessionsCannotWriteAgain(bool alreadyUsed)
    {
        var repository = new WritableRepository([]);
        var source = new TestSource(Header + ValidRow, 1);
        var service = new GameCatalogImportService(repository, source);
        var session = ConfirmImportPreview(service, source);
        if (alreadyUsed)
        {
            Assert.True(service.Apply(session).IsSuccess);
        }
        else
        {
            session.Dispose();
        }

        var result = service.Apply(session);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Empty(result.SavedRecords);
        Assert.Equal(alreadyUsed ? 1 : 0, repository.Writes.Count);
        Assert.Equal(1, source.ConfirmationReader!.DisposeCalls);
    }

    [Fact]
    public void SourceChangeAfterPreparationStopsBeforeTheFirstWrite()
    {
        var repository = new WritableRepository([]);
        var source = new TestSource(Header + ValidRow, 1);
        var service = new GameCatalogImportService(repository, source);
        var session = ConfirmImportPreview(service, source);
        source.ConfirmationReader!.Changed = true;

        var result = service.Apply(session);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Empty(repository.Writes);
        Assert.True(source.ConfirmationReader.Disposed);
    }

    private static GameCatalogVerifiedImportSession ConfirmImportPreview(GameCatalogImportService service, TestSource source)
    {
        var previewResult = service.Preview("virtual.csv");
        Assert.True(previewResult.IsSuccess, previewResult.Error);
        source.UseFreshConfirmationReader();
        var confirmation = service.PrepareConfirmedImport(previewResult.Value!);
        Assert.True(confirmation.IsSuccess, confirmation.Error);
        return Assert.IsType<GameCatalogVerifiedImportSession>(confirmation.Value);
    }


    [Fact]
    public void ConfirmingAnUnchangedPreviewKeepsTheVerifiedReaderOpenUntilSessionDisposal()
    {
        var repository = new ReadOnlyRepository([]);
        var source = new TestSource(Header + ValidRow, 1);
        var service = new GameCatalogImportService(repository, source);
        var preview = Assert.IsType<GameCatalogImportPreview>(service.Preview("chosen.csv").Value);
        var confirmationReader = new TestReader(Header + ValidRow, 1) { RequireEndBeforeVerification = false };
        source.ConfirmationReader = confirmationReader;

        var result = service.PrepareConfirmedImport(preview);

        Assert.True(result.IsSuccess, result.Error);
        var session = Assert.IsType<GameCatalogVerifiedImportSession>(result.Value);
        using (session)
        {
            Assert.Same(preview, session.Preview);
            Assert.False(confirmationReader.Disposed);
            Assert.Equal(0, confirmationReader.CharactersRead);
            Assert.Equal(preview.Fingerprint, confirmationReader.LastVerifiedFingerprint);
            Assert.Equal(1, confirmationReader.VerifyCalls);
            Assert.Equal(2, source.OpenCalls);
            Assert.Equal(preview.SourcePath, source.OpenedPath);
            Assert.Equal(1, repository.ListCalls);
            Assert.Equal(0, repository.WriteCalls);
        }
        session.Dispose();
        Assert.True(confirmationReader.Disposed);
        Assert.Equal(1, confirmationReader.DisposeCalls);
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("missing")]
    [InlineData("unreadable")]
    [InlineData("unverifiable")]
    public void FailedConfirmationReturnsNoSessionAndNeverWrites(string failure)
    {
        var repository = new ReadOnlyRepository([]);
        var source = new TestSource(Header + ValidRow, 1);
        var service = new GameCatalogImportService(repository, source);
        var preview = Assert.IsType<GameCatalogImportPreview>(service.Preview("chosen.csv").Value);
        var confirmationReader = new TestReader(Header + ValidRow, 1) { RequireEndBeforeVerification = false };
        source.ConfirmationReader = confirmationReader;
        switch (failure)
        {
            case "changed": confirmationReader.Fingerprint = new("different-content"); break;
            case "missing": source.OpenFailure = new GameCatalogImportReadException("Source is missing.", new FileNotFoundException()); break;
            case "unreadable": source.OpenFailure = new GameCatalogImportReadException("Source is unreadable.", new UnauthorizedAccessException()); break;
            case "unverifiable": confirmationReader.VerificationFailure = new GameCatalogImportReadException("Source cannot be verified.", new IOException()); break;
        }

        var result = service.PrepareConfirmedImport(preview);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.NotNull(result.Error);
        Assert.False(result.IsCorrupt);
        Assert.Equal(failure is "changed" or "unverifiable", confirmationReader.Disposed);
        Assert.Equal(0, repository.WriteCalls);
        Assert.Equal(1, repository.ListCalls);
    }

    [Theory]
    [InlineData("before")]
    [InlineData("open")]
    [InlineData("verify")]
    public void CancelledConfirmationDoesNotTransferReaderOwnershipOrWrite(string stage)
    {
        var repository = new ReadOnlyRepository([]);
        var source = new TestSource(Header + ValidRow, 1);
        var service = new GameCatalogImportService(repository, source);
        var preview = Assert.IsType<GameCatalogImportPreview>(service.Preview("chosen.csv").Value);
        var confirmationReader = new TestReader(Header + ValidRow, 1) { RequireEndBeforeVerification = false };
        source.ConfirmationReader = confirmationReader;
        using var cancellation = new CancellationTokenSource();
        if (stage == "before") cancellation.Cancel();
        if (stage == "open") source.AfterOpen = cancellation.Cancel;
        if (stage == "verify") confirmationReader.AfterVerify = cancellation.Cancel;

        var result = service.PrepareConfirmedImport(preview, cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal("The CSV import confirmation was cancelled.", result.Error);
        Assert.Equal(stage != "before", confirmationReader.Disposed);
        Assert.Equal(stage == "before" ? 1 : 2, source.OpenCalls);
        Assert.Equal(stage == "verify" ? 1 : 0, confirmationReader.VerifyCalls);
        Assert.Equal(0, repository.WriteCalls);
    }

    [Fact]
    public void AlreadyCancelledPreviewDoesNotAccessRepositoryOrSource()
    {
        var repository = new ReadOnlyRepository([]);
        var source = new TestSource(Header + ValidRow, 1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = new GameCatalogImportService(repository, source).Preview("chosen.csv", cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal("The CSV import preview was cancelled.", result.Error);
        Assert.Equal(0, repository.ListCalls);
        Assert.Equal(0, source.OpenCalls);
        Assert.Equal(0, repository.WriteCalls);
    }

    [Theory]
    [InlineData("catalog")]
    [InlineData("open")]
    [InlineData("capture")]
    [InlineData("read")]
    [InlineData("eof")]
    [InlineData("verify")]
    public void CancellationBetweenOperationsDiscardsPreviewAndDisposesAnyOpenedReader(string stage)
    {
        using var cancellation = new CancellationTokenSource();
        var repository = new ReadOnlyRepository([]);
        var source = new TestSource(Header + ValidRow, 1);
        switch (stage)
        {
            case "catalog": repository.AfterList = cancellation.Cancel; break;
            case "open": source.AfterOpen = cancellation.Cancel; break;
            case "capture": source.Reader.AfterCapture = cancellation.Cancel; break;
            case "read": source.Reader.AfterRead = count => { if (count > 0) cancellation.Cancel(); }; break;
            case "eof": source.Reader.AfterRead = count => { if (count == 0) cancellation.Cancel(); }; break;
            case "verify": source.Reader.AfterVerify = cancellation.Cancel; break;
        }

        var result = new GameCatalogImportService(repository, source).Preview("chosen.csv", cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal("The CSV import preview was cancelled.", result.Error);
        Assert.False(result.IsCorrupt);
        Assert.Equal(stage == "catalog" ? 0 : 1, source.OpenCalls);
        Assert.Equal(stage != "catalog", source.Reader.Disposed);
        Assert.Equal(0, repository.WriteCalls);
    }

    [Theory]
    [InlineData(Header)]
    [InlineData(Header + " \t\r\n\r\n")]
    [InlineData("Title;Medium;DvdNumber;Location;Format;SizeBytes;Notes")]
    public void HeaderOnlySourceHasZeroProposedChangesAndErrors(string csv)
    {
        var repository = new ReadOnlyRepository([
            new(Guid.NewGuid(), "Saved", GameMedium.Usb, null, "/saved.rar", GameFormat.Iso, 1, null)]);
        var source = new TestSource(csv, 1);

        var result = new GameCatalogImportService(repository, source).Preview("chosen.csv");

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(result.Value);
        Assert.Equal(0, result.Value.NewCount);
        Assert.Equal(0, result.Value.ModifiedCount);
        Assert.Equal(0, result.Value.ErrorCount);
        Assert.Equal(1, repository.ListCalls);
        Assert.Equal(0, repository.WriteCalls);
        Assert.True(source.Reader.Disposed);
    }

    [Theory]
    [InlineData("", "does not contain a header")]
    [InlineData(" \t\n\r\n", "does not contain a header")]
    [InlineData("Title;Notes\n", "Missing required CSV headings")]
    [InlineData("Title;Medium;DvdNumber;Location;Format;SizeBytes;Notes;Unknown Heading\n", "must not contain whitespace")]
    [InlineData(Header + ValidRow + "\"unfinished", "Unterminated quoted field")]
    [InlineData(Header + ValidRow + "Bad\"quote;Usb", "Unexpected quote")]
    public void FatalCsvErrorsDiscardAllProposalsAndReleaseTheReader(string csv, string reason)
    {
        var repository = new ReadOnlyRepository([]);
        var source = new TestSource(csv, 1);

        var result = new GameCatalogImportService(repository, source).Preview("chosen.csv");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.False(result.IsCorrupt);
        Assert.Contains(reason, result.Error);
        Assert.True(source.Reader.Disposed);
        Assert.Equal(0, repository.WriteCalls);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("capture")]
    [InlineData("read")]
    [InlineData("verify")]
    public void SourceFailuresNeverReturnAPartialPreviewOrMarkTheCatalogCorrupt(string stage)
    {
        var repository = new ReadOnlyRepository([]);
        var source = new TestSource(Header + ValidRow + ValidRow, 1);
        var failure = new GameCatalogImportReadException("Simulated source failure.", new IOException());
        switch (stage)
        {
            case "open": source.OpenFailure = failure; break;
            case "capture": source.Reader.CaptureFailure = failure; break;
            case "read":
                source.Reader.ReadFailure = failure;
                source.Reader.FailAtPosition = Header.Length + ValidRow.Length;
                break;
            case "verify": source.Reader.VerificationFailure = failure; break;
        }

        var result = new GameCatalogImportService(repository, source).Preview("chosen.csv");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(failure.Message, result.Error);
        Assert.False(result.IsCorrupt);
        Assert.Equal(stage != "open", source.Reader.Disposed);
        if (stage == "read") Assert.Equal(Header.Length + ValidRow.Length, source.Reader.CharactersRead);
        Assert.Equal(0, repository.WriteCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CatalogCorruptionIsDistinguishedFromOrdinaryReadFailures(bool corrupt)
    {
        var repository = new ReadOnlyRepository([])
        {
            ListFailure = corrupt ? new InvalidDataException("Corrupt catalog.") : new IOException("Unavailable catalog.")
        };
        var source = new TestSource(Header + ValidRow, 1);

        var result = new GameCatalogImportService(repository, source).Preview("chosen.csv");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(corrupt, result.IsCorrupt);
        Assert.NotNull(result.Error);
        Assert.Equal(0, source.OpenCalls);
        Assert.Equal(0, repository.WriteCalls);
    }

    [Fact]
    public void InvalidSourceDataIsNotMistakenForCatalogCorruption()
    {
        var repository = new ReadOnlyRepository([]);
        var source = new TestSource(Header + ValidRow, 1) { OpenFailure = new InvalidDataException("Source data error.") };

        var result = new GameCatalogImportService(repository, source).Preview("chosen.csv");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.False(result.IsCorrupt);
        Assert.Equal(0, repository.WriteCalls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(4096)]
    public void PreviewsFinalValidCandidatesAndRowErrorsWithoutWriting(int chunkSize)
    {
        var editedId = Guid.NewGuid();
        GameRecord[] existing =
        [
            new(Guid.NewGuid(), "Same", GameMedium.Usb, null, "/GAMES/same.rar", GameFormat.Iso, 1024, null),
            new(editedId, "Original", GameMedium.Usb, null, "/GAMES/edit.rar", GameFormat.Iso, 1024, "old notes")
        ];
        var repository = new ReadOnlyRepository(existing);
        var source = new TestSource(Header
            + "New first;Usb;;/GAMES/new.rar;ISO;1024;old\r\n"
            + "Same;Usb;;/GAMES/same.rar;ISO;1024;\r\n"
            + "Edited;Usb;;/GAMES/edit.rar;CHD;2048;\r\n"
            + "New last;Usb;;/GAMES/new.rar;CHD;3072;\"two\r\nlines\"\r\n"
            + "Invalid last;Usb;;/GAMES/new.rar;ISO;bad;\r\n"
            + "short\r\n"
            + "Other;MemoryCard;;/GAMES/other.rar;ISO;2048;\r\n", chunkSize);
        var service = new GameCatalogImportService(repository, source);

        var result = service.Preview("chosen.csv");

        Assert.True(result.IsSuccess, result.Error);
        var preview = Assert.IsType<GameCatalogImportPreview>(result.Value);
        Assert.Equal(2, preview.NewCount);
        Assert.Equal(1, preview.ModifiedCount);
        Assert.Equal(2, preview.ErrorCount);
        Assert.Equal(new long[] { 5, 9 }, preview.NewGames.Select(candidate => candidate.StartLine));
        Assert.Equal("New last", preview.NewGames[0].Input.Title);
        Assert.Equal("two\r\nlines", preview.NewGames[0].Input.Notes);
        var edit = Assert.Single(preview.ModifiedGames);
        Assert.Equal(editedId, edit.ExistingId);
        Assert.Equal(4, edit.StartLine);
        Assert.Equal("", edit.Input.Notes);
        Assert.Equal(new long[] { 7, 8 }, preview.RowErrors.Select(error => error.StartLine));
        Assert.Equal("SizeBytes", Assert.Single(preview.RowErrors[0].ValidationErrors).Field);
        Assert.Equal("chosen.csv", preview.SourcePath);
        Assert.Equal(source.Reader.Fingerprint, preview.Fingerprint);
        Assert.Equal(1, repository.ListCalls);
        Assert.Equal(0, repository.WriteCalls);
        Assert.Equal(1, source.OpenCalls);
        Assert.Equal("chosen.csv", source.OpenedPath);
        Assert.Equal(1, source.Reader.CaptureCalls);
        Assert.Equal(1, source.Reader.VerifyCalls);
        Assert.True(source.Reader.EndReached);
        Assert.True(source.Reader.Disposed);
        Assert.Equal(existing, repository.Records);
    }

    [Fact]
    public void IdenticalMatchesAreAbsentFromEveryPreviewCount()
    {
        var repository = new ReadOnlyRepository([
            new(Guid.NewGuid(), "Same", GameMedium.Usb, null, "/GAMES/same.rar", GameFormat.Iso, 1024, null)]);
        var source = new TestSource(Header + "Same;Usb;;/GAMES/same.rar;ISO;1024;\r\n", 1);

        var result = new GameCatalogImportService(repository, source).Preview("chosen.csv");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(0, result.Value.NewCount);
        Assert.Equal(0, result.Value.ModifiedCount);
        Assert.Equal(0, result.Value.ErrorCount);
        Assert.Empty(result.Value.NewGames);
        Assert.Empty(result.Value.ModifiedGames);
        Assert.Empty(result.Value.RowErrors);
        Assert.Equal(0, repository.WriteCalls);
    }

    [Fact]
    public void CombinesReorderedRepeatedAndExtraColumnsWithFormulaProtection()
    {
        var repository = new ReadOnlyRepository([]);
        var source = new TestSource("Notes;Title;Medium;DvdNumber;Location;Format;SizeBytes;Ignored;TITLE\n"
            + "\"\t=SUM(1;2)\";superseded;Usb;;/GAMES/offline.rar;ISO;1024;discarded;\"\t+Game\"\n", 1);

        var result = new GameCatalogImportService(repository, source).Preview("chosen.csv");

        Assert.True(result.IsSuccess, result.Error);
        var preview = Assert.IsType<GameCatalogImportPreview>(result.Value);
        var added = Assert.Single(preview.NewGames);
        Assert.Equal("+Game", added.Input.Title);
        Assert.Equal("=SUM(1;2)", added.Input.Notes);
        Assert.Equal(0, preview.ErrorCount);
        Assert.Equal(0, repository.WriteCalls);
    }

    [Fact]
    public void DoesNotPublishAPreviewWhenFinalSourceVerificationFails()
    {
        var repository = new ReadOnlyRepository([]);
        var source = new TestSource(Header + "Game;Usb;;/GAMES/game.rar;ISO;1024;\n", 7);
        source.Reader.Changed = true;

        var result = new GameCatalogImportService(repository, source).Preview("chosen.csv");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.NotNull(result.Error);
        Assert.Equal(1, source.Reader.VerifyCalls);
        Assert.True(source.Reader.Disposed);
        Assert.Equal(0, repository.WriteCalls);
    }

    [Fact]
    public void PreviewCollectionsCannotBeModifiedByTheCaller()
    {
        var source = new TestSource(Header + "Game;Usb;;/GAMES/game.rar;ISO;1024;\nshort\n", 7);
        var result = new GameCatalogImportService(new ReadOnlyRepository([]), source).Preview("chosen.csv");
        var preview = Assert.IsType<GameCatalogImportPreview>(result.Value);

        Assert.Throws<NotSupportedException>(() => ((IList<GameCatalogImportCandidate>)preview.NewGames).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<GameCatalogImportModification>)preview.ModifiedGames).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<GameCatalogImportRowError>)preview.RowErrors).Clear());
    }

    private sealed class WritableRepository(IEnumerable<GameRecord> records) : IGameRecordRepository
    {
        public Dictionary<Guid, GameRecord> Records { get; } = records.ToDictionary(record => record.Id);
        public List<(string Operation, GameRecord Record)> Writes { get; } = [];
        public List<(string Operation, GameRecord Record)> Attempts { get; } = [];
        public Action<string, GameRecord>? BeforeWrite { get; set; }
        public int ListCalls { get; private set; }
        public IReadOnlyList<GameRecord> List()
        {
            ListCalls++;
            return Records.Values.ToArray();
        }
        public GameRecord? GetById(Guid id) => Records.GetValueOrDefault(id);
        public void Create(GameRecord record)
        {
            Attempts.Add(("create", record));
            BeforeWrite?.Invoke("create", record);
            Records.Add(record.Id, record);
            Writes.Add(("create", record));
        }
        public void Update(GameRecord record)
        {
            Attempts.Add(("update", record));
            BeforeWrite?.Invoke("update", record);
            Assert.True(Records.ContainsKey(record.Id));
            Records[record.Id] = record;
            Writes.Add(("update", record));
        }
        public void Delete(Guid id) => throw new InvalidOperationException("Import must not delete catalog records.");
    }

    private sealed class ReadOnlyRepository(IReadOnlyList<GameRecord> records) : IGameRecordRepository
    {
        public IReadOnlyList<GameRecord> Records { get; } = records;
        public int ListCalls { get; private set; }
        public int WriteCalls { get; private set; }
        public Exception? ListFailure { get; set; }
        public Action? AfterList { get; set; }
        public IReadOnlyList<GameRecord> List()
        {
            ListCalls++;
            Assert.Equal(1, ListCalls);
            if (ListFailure is not null) throw ListFailure;
            AfterList?.Invoke();
            return Records;
        }
        public GameRecord? GetById(Guid id) => throw new InvalidOperationException("Preview must use the catalog snapshot.");
        public void Create(GameRecord record) => RejectWrite();
        public void Update(GameRecord record) => RejectWrite();
        public void Delete(Guid id) => RejectWrite();
        private void RejectWrite()
        {
            WriteCalls++;
            throw new InvalidOperationException("Preview must never write to the catalog.");
        }
    }

    private sealed class TestSource(string text, int chunkSize) : IGameCatalogImportSource
    {
        public TestReader Reader { get; } = new(text, chunkSize);
        public int OpenCalls { get; private set; }
        public string? OpenedPath { get; private set; }
        public Exception? OpenFailure { get; set; }
        public Action? AfterOpen { get; set; }
        public TestReader? ConfirmationReader { get; set; }
        public void UseFreshConfirmationReader() => ConfirmationReader = new(text, chunkSize) { RequireEndBeforeVerification = false };
        public IGameCatalogImportReader Open(string sourcePath)
        {
            OpenCalls++;
            OpenedPath = sourcePath;
            if (OpenFailure is not null) throw OpenFailure;
            AfterOpen?.Invoke();
            return OpenCalls > 1 && ConfirmationReader is not null ? ConfirmationReader : Reader;
        }
    }

    private sealed class TestReader(string text, int chunkSize) : IGameCatalogImportReader
    {
        private int position;
        public GameCatalogImportFingerprint Fingerprint { get; set; } = new("test-content-fingerprint");
        public bool RequireEndBeforeVerification { get; set; } = true;
        public GameCatalogImportFingerprint? LastVerifiedFingerprint { get; private set; }
        public int DisposeCalls { get; private set; }
        public bool Disposed { get; private set; }
        public bool EndReached { get; private set; }
        public bool Changed { get; set; }
        public int CaptureCalls { get; private set; }
        public int VerifyCalls { get; private set; }
        public int CharactersRead => position;
        public int? FailAtPosition { get; set; }
        public Exception? ReadFailure { get; set; }
        public Exception? CaptureFailure { get; set; }
        public Exception? VerificationFailure { get; set; }
        public Action<int>? AfterRead { get; set; }
        public Action? AfterCapture { get; set; }
        public Action? AfterVerify { get; set; }
        public int Read(Span<char> buffer)
        {
            Assert.False(Disposed);
            if (FailAtPosition is int failurePosition && position >= failurePosition && ReadFailure is not null)
                throw ReadFailure;
            var count = Math.Min(Math.Min(chunkSize, buffer.Length), text.Length - position);
            text.AsSpan(position, count).CopyTo(buffer);
            position += count;
            EndReached = count == 0;
            AfterRead?.Invoke(count);
            return count;
        }
        public GameCatalogImportFingerprint CaptureFingerprint()
        {
            Assert.Equal(0, position);
            CaptureCalls++;
            if (CaptureFailure is not null) throw CaptureFailure;
            AfterCapture?.Invoke();
            return Fingerprint;
        }
        public void VerifyUnchanged(GameCatalogImportFingerprint expected)
        {
            VerifyCalls++;
            if (RequireEndBeforeVerification) Assert.True(EndReached);
            LastVerifiedFingerprint = expected;
            if (VerificationFailure is not null) throw VerificationFailure;
            if (Changed || Fingerprint != expected) throw new GameCatalogImportSourceChangedException();
            AfterVerify?.Invoke();
        }
        public void Dispose()
        {
            DisposeCalls++;
            Disposed = true;
        }
    }
}
