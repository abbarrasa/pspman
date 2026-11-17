using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PSPMan.Desktop.ViewModels;

/// <summary>One input lease. Commands retain this lease, never a replacement prompt.</summary>
public sealed partial class CopyInputViewModel : ViewModelBase
{
    internal CopyInputViewModel(bool password, string message, string path, string warning,
        Action<CopyInputViewModel, string> respond)
    {
        IsPassword = password; Message = message; Path = path; Warning = warning;
        RetryCommand = new RelayCommand(() => respond(this, "retry"));
        OverwriteCommand = new RelayCommand(() => respond(this, "overwrite"));
        SkipCommand = new RelayCommand(() => respond(this, "skip"));
        CancelCommand = new RelayCommand(() => respond(this, "cancel"));
    }
    public bool IsPassword { get; }
    public bool IsOverwrite => !IsPassword;
    public string Message { get; }
    public string Path { get; }
    public string Warning { get; }
    [ObservableProperty] private string _password = "";
    public IRelayCommand RetryCommand { get; }
    public IRelayCommand OverwriteCommand { get; }
    public IRelayCommand SkipCommand { get; }
    public IRelayCommand CancelCommand { get; }
}
