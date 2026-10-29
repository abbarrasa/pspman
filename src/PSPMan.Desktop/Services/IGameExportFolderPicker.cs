using System.Threading.Tasks;

namespace PSPMan.Desktop.Services;

public interface IGameExportFolderPicker
{
    Task<string?> PickFolderAsync();
}
