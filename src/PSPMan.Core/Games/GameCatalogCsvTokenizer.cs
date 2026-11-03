using System.Text;

namespace PSPMan.Core.Games;

public sealed record GameCatalogCsvRecord(long StartLine, IReadOnlyList<string> Cells);

// Blank-line filtering and malformed-CSV reporting are added separately.
public sealed class GameCatalogCsvTokenizer
{
    private readonly StringBuilder cell = new();
    private readonly List<string> cells = [];
    private long currentLine = 1;
    private long recordStartLine = 1;
    private bool skipLineFeed;
    private bool hasRecord;
    private bool completed;
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
                else if (character == '"' && cell.Length == 0)
                {
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
        FinishCell();
        var record = new GameCatalogCsvRecord(recordStartLine, Array.AsReadOnly(cells.ToArray()));
        cells.Clear();
        hasRecord = false;
        emit(record);
    }

    private void ThrowIfCompleted()
    {
        if (completed)
        {
            throw new InvalidOperationException("The CSV tokenizer has already been completed.");
        }
    }
}
