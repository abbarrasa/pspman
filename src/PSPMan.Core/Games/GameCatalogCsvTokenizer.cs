using System.Text;

namespace PSPMan.Core.Games;

public sealed record GameCatalogCsvRecord(long StartLine, IReadOnlyList<string> Cells);

public sealed class GameCatalogCsvTokenizer
{
    private readonly StringBuilder cell = new();
    private readonly List<string> cells = [];
    private long currentLine = 1;
    private long recordStartLine = 1;
    private bool skipLineFeed;
    private bool hasRecord;
    private bool completed;
    private bool failed;
    private bool blankLine = true;
    private FieldState fieldState;

    private enum FieldState
    {
        Unquoted,
        Quoted,
        AfterQuote
    }

    public void Push(ReadOnlySpan<char> characters, Action<GameCatalogCsvRecord> emit)
    {
        ArgumentNullException.ThrowIfNull(emit);
        ThrowIfCompleted();

        foreach (var character in characters)
        {
            if (skipLineFeed)
            {
                skipLineFeed = false;
                if (character == '\n')
                {
                    if (fieldState == FieldState.Quoted)
                    {
                        cell.Append(character);
                    }
                    continue;
                }
            }

            if (character is not (' ' or '\t' or '\r' or '\n'))
            {
                blankLine = false;
            }

            if (fieldState == FieldState.Quoted)
            {
                if (character == '"')
                {
                    fieldState = FieldState.AfterQuote;
                }
                else
                {
                    cell.Append(character);
                    if (character is '\r' or '\n')
                    {
                        currentLine++;
                        skipLineFeed = character == '\r';
                    }
                }
                continue;
            }

            if (fieldState == FieldState.AfterQuote)
            {
                if (character == '"')
                {
                    cell.Append(character);
                    fieldState = FieldState.Quoted;
                    continue;
                }
                if (character is not (';' or '\r' or '\n'))
                {
                    Fail("Unexpected character after a closing quote.");
                }
                fieldState = FieldState.Unquoted;
            }

            if (character is '\r' or '\n')
            {
                currentLine++;
                skipLineFeed = character == '\r';
                EmitRecord(emit);
                recordStartLine = currentLine;
            }
            else
            {
                hasRecord = true;
                if (character == ';')
                {
                    FinishCell();
                }
                else if (character == '"')
                {
                    if (cell.Length != 0)
                    {
                        Fail("Unexpected quote in an unquoted field.");
                    }
                    fieldState = FieldState.Quoted;
                }
                else
                {
                    cell.Append(character);
                }
            }
        }
    }

    public void Complete(Action<GameCatalogCsvRecord> emit)
    {
        ArgumentNullException.ThrowIfNull(emit);
        ThrowIfCompleted();
        if (fieldState == FieldState.Quoted)
        {
            Fail("Unterminated quoted field.");
        }
        completed = true;
        if (hasRecord)
        {
            EmitRecord(emit);
        }
    }

    private void FinishCell()
    {
        cells.Add(cell.ToString());
        cell.Clear();
        fieldState = FieldState.Unquoted;
    }

    private void EmitRecord(Action<GameCatalogCsvRecord> emit)
    {
        if (blankLine)
        {
            cell.Clear();
            hasRecord = false;
            return;
        }
        FinishCell();
        var record = new GameCatalogCsvRecord(recordStartLine, Array.AsReadOnly(cells.ToArray()));
        cells.Clear();
        hasRecord = false;
        blankLine = true;
        emit(record);
    }

    private void Fail(string reason)
    {
        failed = true;
        throw new FormatException($"CSV record starting on line {recordStartLine}: {reason}");
    }

    private void ThrowIfCompleted()
    {
        if (failed)
        {
            throw new InvalidOperationException("The CSV tokenizer cannot continue after a structure error.");
        }
        if (completed)
        {
            throw new InvalidOperationException("The CSV tokenizer has already been completed.");
        }
    }
}
