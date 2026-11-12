using PSPMan.Core.Devices;

namespace PSPMan.Desktop.ViewModels;

public partial class MainViewModel
{
    private readonly MountedPathSyntax _mountedPathSyntax;

    /// <summary>Caller-owned device state; optional for existing catalog-only consumers.</summary>
    public DevicesViewModel? Devices { get; private set; }
    public bool HasDevices => Devices is not null;

    /// <summary>Called by the application owner on the UI context; does not replace catalog state.</summary>
    internal void AttachDevices(DevicesViewModel? devices)
    {
        if (ReferenceEquals(Devices, devices)) return;
        Devices = devices;
        UpdateSelectedGameDevices();
        OnPropertyChanged(nameof(Devices));
        OnPropertyChanged(nameof(HasDevices));
    }

    private void UpdateSelectedGameDevices() => Devices?.SetSelectedGame(SelectedGame?.Id,
        SelectedGame?.Medium, SelectedGame?.Location, _mountedPathSyntax);
}
