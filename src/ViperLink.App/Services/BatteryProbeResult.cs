namespace ViperLink.App.Services;

public sealed record BatteryProbeResult(
    string BatteryHeader,
    string DeviceHeader,
    string ResultHeader,
    string DiagnosticsHeader,
    string ToolTipText,
    string? LogFilePath = null);
