using Avalonia;
using Avalonia.Themes.Fluent;

namespace PSPMan.Desktop.Tests;

public sealed class TestApplication : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}

[CollectionDefinition("Headless UI", DisableParallelization = true)]
public sealed class HeadlessUiCollection;
