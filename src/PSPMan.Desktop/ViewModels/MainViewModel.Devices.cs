using PSPMan.Core.Devices;

namespace PSPMan.Desktop.ViewModels;

public partial class MainViewModel
{
    private readonly MountedPathSyntax _mountedPathSyntax;

    /// <summary>Caller-owned device state; optional for existing catalog-only consumers.</summary>
    public DevicesViewModel? Devices { get; }

    private void UpdateSelectedGameDevices() => Devices?.SetSelectedGame(SelectedGame?.Id,
        SelectedGame?.Medium, SelectedGame?.Location, _mountedPathSyntax);
}
