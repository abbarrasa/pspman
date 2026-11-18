using System;
using CommunityToolkit.Mvvm.Input;

namespace PSPMan.Desktop.ViewModels;

/// <summary>One close decision; retired commands cannot answer a later request.</summary>
public sealed class CopyCloseConfirmationViewModel : ViewModelBase
{
    internal CopyCloseConfirmationViewModel(string destination,
        Action<CopyCloseConfirmationViewModel, bool> respond)
    {
        Destination = destination;
        AcceptCommand = new RelayCommand(() => respond(this, true));
        KeepOpenCommand = new RelayCommand(() => respond(this, false));
    }
    public string Message => "Close PSPMan and stop all active copies? Files already written will remain on each PSP and may be incomplete or incorrect. Overwritten files will not be restored.";
    public string Destination { get; }
    public IRelayCommand AcceptCommand { get; }
    public IRelayCommand KeepOpenCommand { get; }
}
