namespace PSPMan.Desktop.Tests;

public sealed class ProgramDispatchTests
{
    [Fact]
    public void OrdinaryLaunchUsesDesktopStartupOnceAndForwardsArguments()
    {
        var arguments = new[] { "--ordinary-option" };
        var calls = 0;
        var result = ProgramDispatch.Run(arguments, args => { Assert.Same(arguments, args); calls++; return 7; });
        Assert.Equal(7, result);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("--device-operation")]
    [InlineData("--Device-Operation")]
    [InlineData("--pipe")]
    [InlineData("--initiator")]
    [InlineData("--request=bad")]
    public void InvalidHelperArgumentsNeverInvokeDesktopOrCatalogStartup(string argument)
    {
        Assert.Equal(2, ProgramDispatch.Run([argument], _ => throw new Xunit.Sdk.XunitException("Helper entered UI startup")));
        Assert.Equal(2, Program.Main([argument])); // Exercise the real entry point, not just the dispatcher seam.
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void HandledHelperStatusNeverFallsBackToDesktop(int helperStatus)
    {
        var calls = 0;
        Assert.Equal(helperStatus, ProgramDispatch.Run(["--device-operation"],
            _ => throw new Xunit.Sdk.XunitException("Helper entered UI startup"), args => { calls++; return helperStatus; }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void SyntacticallyValidHelperUsesTheHelperBranchWithoutDesktopInitialization()
    {
        string[] args = ["--device-operation", "--pipe", "PSPMan-operation-" + new string('A', 64), "--request",
            "10000000-0000-0000-0000-000000000035", "--initiator", "10", "--initiator-start", "100"];
        var result = ProgramDispatch.Run(args, _ => throw new Xunit.Sdk.XunitException("Helper entered UI startup"),
            input => { Assert.Same(args, input); return 0; });
        Assert.Equal(0, result);
    }
}
