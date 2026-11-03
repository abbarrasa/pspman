using System.Text;

namespace PSPMan.Core.Games;

public sealed record GameCatalogCsvRecord(long StartLine, IReadOnlyList<string> Cells);

// Incremental foundation for ordinary cells; quoted-field support is added separately.
public sealed class GameCatalogCsvTokenizer
{
    private readonly StringBuilder cell = new();
    private readonly List<string> cells = [];
    private long currentLine = 1;
    private long recordStartLine = 1;
    private bool skipLineFeed;
    private bool hasRecord;
    private bool completed;

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
                    continue;
                }
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
