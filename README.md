# ViperLink

Initial Avalonia spike for a tray-only Razer mouse battery utility.

## Current spike

- Targets `.NET 10`
- Uses `Avalonia 12.0.1`
- Uses `HidSharp 2.6.4` for cross-platform HID access
- Starts as a tray app with no main window
- Enumerates `Razer` HID devices (`VID 0x1532`)
- Attempts a vendor feature-report battery read using the current best-known `0x07 / 0x80` power command

The tray menu currently shows:

- battery status
- selected device
- last probe result
- a short diagnostics line
- refresh now
- quit

## Run

```bash
dotnet run --project src/ViperLink.App/ViperLink.App.csproj
```

## Notes

- This is a spike, not the final architecture.
- The tray icon is still static; the battery percentage is shown in the tooltip and menu.
- The battery protocol is based on public OpenRazer and related community research, but still needs validation against real `Razer Viper Ultimate` hardware on Windows and macOS.
- Wireless and wired modes may enumerate as different HID devices.
