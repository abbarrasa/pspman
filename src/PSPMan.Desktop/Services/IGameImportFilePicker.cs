using System.Threading.Tasks;

namespace PSPMan.Desktop.Services;

public interface IGameImportFilePicker
{
    Task<string?> PickFileAsync();
}
