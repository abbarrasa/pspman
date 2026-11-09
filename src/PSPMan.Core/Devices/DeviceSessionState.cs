namespace PSPMan.Core.Devices;

/// <summary>Explicit source choices for one execution; never persisted or shared between sessions.</summary>
public sealed record DeviceSessionState(DeviceKey? OpticalSource = null, DeviceKey? RemovableSource = null);
