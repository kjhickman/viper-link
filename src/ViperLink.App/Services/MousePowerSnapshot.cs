using System;

namespace ViperLink.App.Services;

public sealed record MousePowerSnapshot(
    DateTimeOffset Timestamp,
    string DeviceDisplayName,
    int? BatteryPercent,
    bool? IsCharging,
    bool IsSuccessful,
    string ResultDetail,
    string Diagnostics,
    string? LogFilePath = null);
