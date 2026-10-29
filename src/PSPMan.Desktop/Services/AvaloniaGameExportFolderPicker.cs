using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace PSPMan.Desktop.Services;

public sealed class AvaloniaGameExportFolderPicker : IGameExportFolderPicker
{
    private readonly Func<Task<IReadOnlyList<string?>>> _chooseLocalPaths;

    public AvaloniaGameExportFolderPicker(TopLevel topLevel)
        : this(() => ChooseLocalPathsAsync(topLevel))
    {
        ArgumentNullException.ThrowIfNull(topLevel);
    }

    internal AvaloniaGameExportFolderPicker(Func<Task<IReadOnlyList<string?>>> chooseLocalPaths)
    {
        _chooseLocalPaths = chooseLocalPaths ?? throw new ArgumentNullException(nameof(chooseLocalPaths));
    }

    public async Task<string?> PickFolderAsync()
    {
        var selectedPaths = await _chooseLocalPaths();
        if (selectedPaths.Count == 0)
        {
            return null;
        }

        var localPath = selectedPaths[0];
        if (string.IsNullOrWhiteSpace(localPath) || !Path.IsPathFullyQualified(localPath))
        {
            throw new InvalidOperationException("The selected folder has no usable local path.");
        }

        return localPath;
    }

    private static async Task<IReadOnlyList<string?>> ChooseLocalPathsAsync(TopLevel topLevel)
    {
        var storageProvider = topLevel.StorageProvider;
        if (!storageProvider.CanPickFolder)
        {
            throw new InvalidOperationException("Folder selection is unavailable on this platform.");
        }

        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose export folder",
            AllowMultiple = false,
        });
        return folders.Select(folder => folder.TryGetLocalPath()).ToArray();
    }
}
