using System.Diagnostics;
using System.Text;

namespace Borderus.Services;

internal static class KeyboardBacklightService
{
    public static async Task<string> DiagnoseAsync()
    {
        const string script = """
            $ErrorActionPreference='SilentlyContinue'
            $system=Get-CimInstance Win32_ComputerSystem
            "COMPUTER: $($system.Manufacturer) $($system.Model)"
            $bios=Get-CimInstance Win32_BIOS
            "BIOS: $($bios.SMBIOSBIOSVersion) $($bios.ReleaseDate)"
            $devices=Get-CimInstance Win32_PnPEntity | Where-Object {
              $_.PNPDeviceID -match '^(ACPI|HID|USB)' -and
              ($_.Name -match 'Honor|Huawei|Keyboard|Hotkey|WMI|Control|Backlight|Lighting' -or
               $_.Manufacturer -match 'Honor|Huawei')
            } | Select-Object Name,Manufacturer,PNPDeviceID,Service
            if($devices){'DEVICES:';$devices|ForEach-Object{"- $($_.Name) | $($_.Manufacturer) | $($_.Service) | $($_.PNPDeviceID)"}}
            else{'DEVICES: no matching ACPI/HID control device exposed'}
            $namespaces=Get-CimInstance -Namespace root -ClassName __Namespace | Select-Object -ExpandProperty Name
            $vendorNs=$namespaces | Where-Object {$_ -match 'Honor|Huawei|WMI'}
            if($vendorNs){'VENDOR WMI NAMESPACES:';$vendorNs|ForEach-Object{"- root\$_"}}
            else{'VENDOR WMI NAMESPACES: none'}
            $wmiClasses=Get-CimClass -Namespace root\wmi | Where-Object {
              $_.CimClassName -match 'Keyboard|Backlight|Lighting|Hotkey|Huawei|Honor'
            } | Select-Object -ExpandProperty CimClassName
            if($wmiClasses){'RELEVANT root\wmi CLASSES:';$wmiClasses|ForEach-Object{"- $_"}}
            else{'RELEVANT root\wmi CLASSES: none'}
            "RESULT: diagnosis only; no input was injected and no firmware command was sent."
            "If no vendor interface is listed, control requires a discovered vendor driver protocol or an EC/ACPI kernel driver."
            """;

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command {Quote(script)}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        using var process = Process.Start(startInfo);
        if (process is null) return "Failed to start Windows hardware diagnostics.";
        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return string.IsNullOrWhiteSpace(output) ? $"Diagnostics failed: {error}" : output.Trim();
    }

    private static string Quote(string value) => '"' + value.Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", ";") + '"';
}
