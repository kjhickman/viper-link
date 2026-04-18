using HidSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ViperLink.App.Services;

public sealed class RazerBatterySpikeService
{
    private const int RazerVendorId = 0x1532;
    private static readonly HashSet<int> PreferredProductIds = [0x007a, 0x007b];
    private const int BatteryReportLength = 90;
    private const byte BatteryCommandClass = 0x07;
    private const byte GetBatteryCommandId = 0x80;
    private static readonly byte[] CandidateTransactionIds = [0x00, 0x1f, 0x3f, 0xff];

    public BatteryProbeResult Probe()
    {
        var timestamp = DateTimeOffset.Now;
        var diagnostics = new StringBuilder();

        IReadOnlyList<HidDevice> razerDevices;
        try
        {
            razerDevices = DeviceList.Local
                .GetHidDevices()
                .Where(device => device.VendorID == RazerVendorId)
                .OrderBy(device => device.ProductID)
                .ThenBy(device => device.DevicePath, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception ex)
        {
            var result = BuildUnavailableResult(timestamp, "enumeration failed", $"HID enumeration failed: {ex.Message}");
            return result with { LogFilePath = WriteDiagnosticsLog(timestamp, diagnostics.ToString(), result) };
        }

        diagnostics.AppendLine($"Detected {razerDevices.Count} Razer HID device(s).");
        foreach (var device in razerDevices)
        {
            diagnostics.AppendLine($"Found {DescribeDevice(device)}");
        }

        if (razerDevices.Count == 0)
        {
            var result = BuildUnavailableResult(timestamp, "no devices", "No Razer HID devices found.");
            return result with { LogFilePath = WriteDiagnosticsLog(timestamp, diagnostics.ToString(), result) };
        }

        var candidateDevices = PrioritizeDevices(razerDevices);
        diagnostics.AppendLine($"Prioritized {candidateDevices.Count} candidate device(s) for probing.");

        foreach (var device in candidateDevices)
        {
            diagnostics.AppendLine($"Trying {DescribeDevice(device)}");

            if (TryReadBattery(device, diagnostics, out var batteryPercent))
            {
                var tooltip = string.Create(
                    CultureInfo.InvariantCulture,
                    $"ViperLink spike\n{device.GetFriendlyName()}: {batteryPercent}%");

                return new BatteryProbeResult(
                    $"Battery: {batteryPercent}%",
                    $"Device: {DescribeDevice(device)}",
                    $"Last probe: success at {timestamp:HH:mm:ss}",
                    TruncateHeader($"Diagnostics: {LastDiagnosticLine(diagnostics)}"),
                    tooltip,
                    WriteDiagnosticsLog(timestamp, diagnostics.ToString(), null));
            }
        }

        var failureResult = new BatteryProbeResult(
            "Battery: unavailable",
            $"Device: tried {candidateDevices.Count}/{razerDevices.Count} Razer HID device(s)",
            $"Last probe: no battery response at {timestamp:HH:mm:ss}",
            TruncateHeader($"Diagnostics: {LastDiagnosticLine(diagnostics)}"),
            "ViperLink spike\nBattery unavailable");
        return failureResult with { LogFilePath = WriteDiagnosticsLog(timestamp, diagnostics.ToString(), failureResult) };
    }

    private static IReadOnlyList<HidDevice> PrioritizeDevices(IReadOnlyList<HidDevice> devices)
    {
        if (OperatingSystem.IsWindows())
        {
            var topLevelCollections = devices
                .Where(device => IsLikelyWindowsTopLevelCollection(device))
                .ToArray();

            if (topLevelCollections.Length > 0)
            {
                return topLevelCollections;
            }
        }

        var preferred = devices
            .Where(device => PreferredProductIds.Contains(device.ProductID) || LooksLikeViperUltimate(device))
            .ToArray();

        if (preferred.Length > 0)
        {
            return preferred;
        }

        var reportSized = devices
            .Where(device => device.GetMaxFeatureReportLength() >= BatteryReportLength)
            .ToArray();

        return reportSized.Length > 0 ? reportSized : devices;
    }

    private static bool TryReadBattery(HidDevice device, StringBuilder diagnostics, out int batteryPercent)
    {
        batteryPercent = 0;

        var reportLength = Math.Max(BatteryReportLength, device.GetMaxFeatureReportLength());
        diagnostics.AppendLine($"Feature report length: {reportLength}");
        if (reportLength < BatteryReportLength)
        {
            diagnostics.AppendLine("Skipped: feature report length is shorter than 90 bytes.");
            return false;
        }

        if (WindowsHidFeatureTransport.IsSupported && IsLikelyWindowsTopLevelCollection(device))
        {
            diagnostics.AppendLine("Using Windows native HID feature transport.");
            return TryReadBatteryViaWindowsHid(device, reportLength, diagnostics, out batteryPercent);
        }

        if (!device.TryOpen(out HidStream stream))
        {
            diagnostics.AppendLine("Open failed.");
            return false;
        }

        using (stream)
        {
            stream.ReadTimeout = 1000;
            stream.WriteTimeout = 1000;

            foreach (var transactionId in CandidateTransactionIds)
            {
                var request = BuildBatteryRequest(reportLength, transactionId);
                var response = new byte[reportLength];

                try
                {
                    stream.SetFeature(request);
                    stream.GetFeature(response);
                }
                catch (Exception ex)
                {
                    diagnostics.AppendLine($"Transaction 0x{transactionId:x2} failed: {ex.Message}");
                    continue;
                }

                diagnostics.AppendLine($"Transaction 0x{transactionId:x2} response: {FormatReport(response)}");

                if (!LooksLikeBatteryResponse(response, transactionId))
                {
                    continue;
                }

                batteryPercent = (int)Math.Round(response[9] * 100.0 / 255.0, MidpointRounding.AwayFromZero);
                diagnostics.AppendLine($"Battery byte {response[9]} parsed as {batteryPercent}%.");
                return true;
            }
        }

        return false;
    }

    private static bool TryReadBatteryViaWindowsHid(HidDevice device, int reportLength, StringBuilder diagnostics, out int batteryPercent)
    {
        batteryPercent = 0;
        var payloadOffset = reportLength == BatteryReportLength + 1 ? 1 : 0;
        var payloadLength = reportLength - payloadOffset;

        foreach (var transactionId in CandidateTransactionIds)
        {
            var requestPayload = BuildBatteryRequest(payloadLength, transactionId);
            var request = new byte[reportLength];
            Array.Copy(requestPayload, 0, request, payloadOffset, requestPayload.Length);

            var response = new byte[reportLength];

            if (!WindowsHidFeatureTransport.TryExchangeFeatureReport(device.DevicePath, request, response, out var error))
            {
                diagnostics.AppendLine($"Transaction 0x{transactionId:x2} failed: {error}");
                continue;
            }

            var responsePayload = response.AsSpan(payloadOffset, payloadLength).ToArray();
            diagnostics.AppendLine($"Transaction 0x{transactionId:x2} response: {FormatReport(responsePayload)}");

            if (!LooksLikeBatteryResponse(responsePayload, transactionId))
            {
                continue;
            }

            batteryPercent = (int)Math.Round(responsePayload[9] * 100.0 / 255.0, MidpointRounding.AwayFromZero);
            diagnostics.AppendLine($"Battery byte {responsePayload[9]} parsed as {batteryPercent}%.");
            return true;
        }

        return false;
    }

    private static byte[] BuildBatteryRequest(int reportLength, byte transactionId)
    {
        var request = new byte[reportLength];
        request[0] = 0x00;
        request[1] = transactionId;
        request[5] = 0x02;
        request[6] = BatteryCommandClass;
        request[7] = GetBatteryCommandId;
        request[88] = CalculateChecksum(request);
        return request;
    }

    private static byte CalculateChecksum(IReadOnlyList<byte> report)
    {
        byte checksum = 0x00;
        for (var index = 2; index <= 87; index++)
        {
            checksum ^= report[index];
        }

        return checksum;
    }

    private static bool LooksLikeBatteryResponse(IReadOnlyList<byte> response, byte transactionId)
    {
        if (response.Count < BatteryReportLength)
        {
            return false;
        }

        var status = response[0];
        if (status is not (0x00 or 0x02 or 0x04))
        {
            return false;
        }

        return response[1] == transactionId
            && response[6] == BatteryCommandClass
            && response[7] == GetBatteryCommandId;
    }

    private static string DescribeDevice(HidDevice device)
    {
        var name = device.GetFriendlyName();
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{name} ({device.VendorID:x4}:{device.ProductID:x4}, feature={device.GetMaxFeatureReportLength()}, path={device.DevicePath})");
    }

    private static string FormatReport(IReadOnlyList<byte> report)
    {
        var bytesToShow = report.Take(BatteryReportLength).Select(value => value.ToString("x2", CultureInfo.InvariantCulture));
        return string.Join(" ", bytesToShow);
    }

    private static BatteryProbeResult BuildUnavailableResult(DateTimeOffset timestamp, string reason, string diagnostics)
    {
        return new BatteryProbeResult(
            "Battery: unavailable",
            "Device: no compatible Razer HID device",
            $"Last probe: {reason} at {timestamp:HH:mm:ss}",
            TruncateHeader($"Diagnostics: {diagnostics}"),
            "ViperLink spike\nBattery unavailable");
    }

    private static string? WriteDiagnosticsLog(DateTimeOffset timestamp, string diagnostics, BatteryProbeResult? result)
    {
        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ViperLink");
            Directory.CreateDirectory(logDirectory);

            var logPath = Path.Combine(logDirectory, "probe.log");
            var builder = new StringBuilder();
            builder.AppendLine($"Timestamp: {timestamp:O}");

            if (result is not null)
            {
                builder.AppendLine($"BatteryHeader: {result.BatteryHeader}");
                builder.AppendLine($"DeviceHeader: {result.DeviceHeader}");
                builder.AppendLine($"ResultHeader: {result.ResultHeader}");
                builder.AppendLine($"DiagnosticsHeader: {result.DiagnosticsHeader}");
            }

            builder.AppendLine("Diagnostics:");
            builder.AppendLine(diagnostics);
            File.WriteAllText(logPath, builder.ToString());
            return logPath;
        }
        catch
        {
            return null;
        }
    }

    private static bool LooksLikeViperUltimate(HidDevice device)
    {
        var name = device.GetFriendlyName();
        return name.Contains("Viper Ultimate", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Viper Ultimate Dongle", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyWindowsTopLevelCollection(HidDevice device)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (device.GetMaxFeatureReportLength() < BatteryReportLength)
        {
            return false;
        }

        if (!PreferredProductIds.Contains(device.ProductID) && !LooksLikeViperUltimate(device))
        {
            return false;
        }

        return !device.DevicePath.Contains("\\kbd", StringComparison.OrdinalIgnoreCase);
    }

    private static string LastDiagnosticLine(StringBuilder diagnostics)
    {
        var lines = diagnostics
            .ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return lines.LastOrDefault() ?? "no details";
    }

    private static string TruncateHeader(string value)
    {
        return value.Length <= 80 ? value : string.Concat(value.AsSpan(0, 77), "...");
    }
}

internal static class HidDeviceExtensions
{
    public static string GetFriendlyName(this HidDevice device)
    {
        var productName = device.GetProductName();
        if (!string.IsNullOrWhiteSpace(productName))
        {
            return productName;
        }

        return "Unknown Razer device";
    }
}
