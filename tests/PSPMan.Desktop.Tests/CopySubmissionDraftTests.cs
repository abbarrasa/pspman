using System.Collections.Immutable;
using PSPMan.Core.Devices;
using PSPMan.Core.Games;
using PSPMan.Core.Transfers;
using PSPMan.Desktop.ViewModels;
using static PSPMan.Desktop.Tests.GameCopyTestContext;

namespace PSPMan.Desktop.Tests;

public sealed class CopySubmissionDraftTests
{
    internal static GameRecord Game(GameMedium medium = GameMedium.Usb) => new(Guid.NewGuid(), "Game", medium,
        medium == GameMedium.Dvd ? 1 : null, "/folder/game.zip", GameFormat.Iso, 3, null);
    internal static DeviceSnapshot Optical(string name) => Device(name) with { Medium = DeviceMedium.Optical, Capabilities = DeviceCapabilities.OpticalSource | DeviceCapabilities.SafeRemoval };
    internal static DeviceSessionSnapshot Observation(params DeviceSnapshot[] devices) => new(new(1, [.. devices]), new(),
        ImmutableDictionary<string, long>.Empty, ImmutableDictionary<string, DeviceOperationError>.Empty);

    [Fact]
    public void SeedingUsesPreferencesWithoutWritingThemAndClearedChoiceDoesNotFallBack()
    {
        var game = Game(); var source = Device("source"); var other = Device("other"); var psp = Device("psp", true);
        var snapshot = Observation(source, other, psp) with { Associations = DeviceAssociationRules.ChooseVolume(new(), SourceGroup.Removable, source.Volumes[0].Key) };
        var before = snapshot.Associations;
        var draft = new CopySubmissionViewModel(game, snapshot, MountedPathSyntax.Linux);
        Assert.True(draft.IsReady); Assert.Equal(source.Key, draft.SelectedSource!.Device.Key);
        Assert.Equal("/psp", draft.SelectedPspRoot);
        draft.SelectedSource = draft.Sources.Single(option => option.Device.Key == other.Key);
        Assert.Equal(other.Volumes[0].Key, draft.SelectedVolume!.Volume.Key);
        Assert.Same(before, snapshot.Associations);
        draft.SelectedPsp = null;
        Assert.Null(draft.Prepare(Guid.NewGuid(), out var error)); Assert.Equal(GameCopyErrorCode.DestinationUnavailable, error!.Code);
        draft.Refresh(snapshot); Assert.Null(draft.SelectedPsp); Assert.False(draft.IsReady);
    }

    [Fact]
    public void TwoDraftsChooseIndependentReadersAndPspsWithoutAssociationMutation()
    {
        var snapshot = Observation(Optical("dvd1"), Optical("dvd2"), Device("psp1", true), Device("psp2", true));
        var first = new CopySubmissionViewModel(Game(GameMedium.Dvd), snapshot, MountedPathSyntax.Linux);
        var second = new CopySubmissionViewModel(Game(GameMedium.Dvd), snapshot, MountedPathSyntax.Linux);
        Assert.Null(first.SelectedSource); Assert.Null(first.SelectedPsp); Assert.False(first.IsReady);
        first.SelectedSource = first.Sources[0]; first.SelectedPsp = first.Psps[0];
        second.SelectedSource = second.Sources[1]; second.SelectedPsp = second.Psps[1];
        var a = first.Prepare(Guid.NewGuid(), out var firstError)!;
        var b = second.Prepare(Guid.NewGuid(), out var secondError)!;
        Assert.Null(firstError); Assert.Null(secondError);
        Assert.NotEqual(a.SourceDevice, b.SourceDevice); Assert.NotEqual(a.DestinationDevice, b.DestinationDevice);
        Assert.Null(snapshot.Associations.OpticalSource); Assert.Empty(snapshot.Associations.PspDestinations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovalOrMediaReplacementPreservesMissingChoiceUntilExplicitReselection(bool replace)
    {
        var source = Device("source"); var psp = Device("psp", true);
        var snapshot = Observation(source, psp);
        var draft = new CopySubmissionViewModel(Game(), snapshot, MountedPathSyntax.Linux);
        var changed = snapshot with { Inventory = new(2, replace ? [source with { MediaGeneration = "g2" }, psp] : [psp]) };
        draft.Refresh(changed); Assert.False(draft.IsReady);
        Assert.Equal("g1", draft.SelectedSource!.Device.MediaGeneration);
        Assert.Contains("unavailable", draft.SelectedSource.Label);
        if (replace)
        {
            draft.SelectedSource = draft.Sources.Single(option => option.Device.MediaGeneration == "g2");
            Assert.True(draft.IsReady); Assert.Equal("g2", draft.SelectedSource.Device.MediaGeneration);
        }
    }

    [Fact]
    public void PspVolumeReplacementAndProviderErrorBlockReadiness()
    {
        var source = Device("source"); var psp = Device("psp", true); var snapshot = Observation(source, psp);
        var draft = new CopySubmissionViewModel(Game(), snapshot, MountedPathSyntax.Linux);
        var replacement = psp with { Volumes = [psp.Volumes[0] with { Identity = new("replacement", DeviceIdentityConfidence.Persistent) }] };
        var changed = snapshot with { Inventory = new(2, [source, replacement]) };
        draft.Refresh(changed); Assert.False(draft.IsReady); Assert.Contains("unavailable", draft.SelectedPsp!.Label);
        draft.SelectedPsp = draft.Psps.Single(option => option.Device.Volumes[0].Identity.PersistentId == "replacement");
        Assert.True(draft.IsReady);
        draft.Refresh(changed with { ProviderErrors = snapshot.ProviderErrors.Add("copy-ui", new("Unavailable", "Provider lost")) });
        Assert.False(draft.IsReady); Assert.Contains("service", draft.Status);
    }

    [Fact]
    public void MultipleVolumesRequireExplicitVolumeAndPathValidationStillUsesCoreRules()
    {
        var source = Device("source"); source = source with { Volumes = [source.Volumes[0], source.Volumes[0] with { Key = new(source.Key, "second"), Root = "/other" }] };
        var draft = new CopySubmissionViewModel(Game(), Observation(source, Device("psp", true)), MountedPathSyntax.Linux);
        Assert.NotNull(draft.SelectedSource); Assert.Null(draft.SelectedVolume); Assert.False(draft.IsReady);
        draft.SelectedVolume = draft.Volumes[1]; Assert.True(draft.IsReady);
        draft.UpdateGame(draft.Game with { Location = "invalid" }); Assert.False(draft.IsReady);
        Assert.Contains("invalid", draft.Status);
    }

    [Fact]
    public void EmptyInventoryRequiresChoicesAndDoesNotSelectNewDevicesOnRefresh()
    {
        var draft = new CopySubmissionViewModel(Game(), Observation(), MountedPathSyntax.Linux);
        Assert.Empty(draft.Sources); Assert.Empty(draft.Psps); Assert.False(draft.IsReady);
        draft.Refresh(Observation(Device("source"), Device("psp", true)));
        Assert.Null(draft.SelectedSource); Assert.Null(draft.SelectedPsp); Assert.False(draft.IsReady);
    }
}
