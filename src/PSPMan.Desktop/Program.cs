using Avalonia;
using System;
using System.Runtime.CompilerServices;

namespace PSPMan.Desktop;

sealed class Program
{
    // Only the fixed helper bootstrap may run before Avalonia; it does not initialize UI/catalog services.
    [STAThread]
    public static int Main(string[] args) => ProgramDispatch.Run(args, StartDesktop);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int StartDesktop(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
