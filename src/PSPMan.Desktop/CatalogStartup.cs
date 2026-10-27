using System;
using System.IO;
using PSPMan.Core.Games;
using PSPMan.Desktop.ViewModels;
using PSPMan.Infraestructure.Games;

namespace PSPMan.Desktop;

public static class CatalogStartup
{
    public static MainViewModel CreateMainViewModel(string? applicationDataRoot = null)
    {
        try
        {
            var root = applicationDataRoot
                ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new IOException("The local application-data directory is unavailable.");
            }

            var catalogDirectory = Path.Combine(root, "PSPMan");
            Directory.CreateDirectory(catalogDirectory);
            var repository = new SqliteGameRecordRepository(Path.Combine(catalogDirectory, "catalog.db"));
            return new MainViewModel(new GameCatalogService(repository), null);
        }
        catch (InvalidDataException)
        {
            return new MainViewModel(null, "The game catalog is corrupt. Existing data were not changed.");
        }
        catch (Exception)
        {
            return new MainViewModel(null, "Unable to open the game catalog.");
        }
    }
}
