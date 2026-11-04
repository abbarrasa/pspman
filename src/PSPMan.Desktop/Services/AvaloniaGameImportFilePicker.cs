using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace PSPMan.Desktop.Services;

public sealed class AvaloniaGameImportFilePicker : IGameImportFilePicker
{
    private readonly Func<Task<IReadOnlyList<string?>>> _chooseLocalPaths;

    public AvaloniaGameImportFilePicker(TopLevel topLevel)
        : this(() => ChooseLocalPathsAsync(topLevel))
    {
        ArgumentNullException.ThrowIfNull(topLevel);
    }

    internal AvaloniaGameImportFilePicker(Func<Task<IReadOnlyList<string?>>> chooseLocalPaths)
    {
        _chooseLocalPaths = chooseLocalPaths ?? throw new ArgumentNullException(nameof(chooseLocalPaths));
    }

    public async Task<string?> PickFileAsync()
    {
        var selectedPaths = await _chooseLocalPaths();
        if (selectedPaths.Count == 0)
        {
            return null;
        }

        var localPath = selectedPaths[0];
        if (string.IsNullOrWhiteSpace(localPath) || !Path.IsPathFullyQualified(localPath))
        {
            throw new InvalidOperationException("The selected file has no usable local path.");
        }

        return localPath;
    }

    private static async Task<IReadOnlyList<string?>> ChooseLocalPathsAsync(TopLevel topLevel)
    {
        var storageProvider = topLevel.StorageProvider;
        if (!storageProvider.CanOpen)
        {
            throw new InvalidOperationException("File selection is unavailable on this platform.");
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose game catalog CSV",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("CSV files")
            {
                Patterns = ["*.csv"],
                MimeTypes = ["text/csv"],
                AppleUniformTypeIdentifiers = ["public.comma-separated-values-text"],
            }],
        });
        return files.Select(file => file.TryGetLocalPath()).ToArray();
    }
}
