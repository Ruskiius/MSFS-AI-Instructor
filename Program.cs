using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("MSFS SimConnect is only available on Windows.");
    return 1;
}

TelemetrySnapshot? latestTelemetry = null;
bool keepRunning = true;
DateTime? lastPrintedTelemetryTimestamp = null;

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    keepRunning = false;
};

using TelemetryCsvLogger csvLogger = new();
using SimConnectTelemetryService telemetryService = new();

telemetryService.TelemetryReceived += (_, snapshot) =>
{
    csvLogger.Write(snapshot);
    Interlocked.Exchange(ref latestTelemetry, snapshot);
};

telemetryService.ErrorOccurred += (_, message) =>
{
    Console.Error.WriteLine(message);
};

telemetryService.Disconnected += (_, message) =>
{
    Console.Error.WriteLine(message);
    keepRunning = false;
};

try
{
    telemetryService.Connect();
    Console.WriteLine("Connected to MSFS");

    telemetryService.Start();
    Console.WriteLine("Reading aircraft telemetry once per second. Press Ctrl+C to exit.");

    while (keepRunning)
    {
        TelemetrySnapshot? snapshot = Volatile.Read(ref latestTelemetry);

        if (snapshot is null)
        {
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} | Waiting for telemetry...");
        }
        else
        {
            if (lastPrintedTelemetryTimestamp != snapshot.Timestamp)
            {
                PrintTelemetry(snapshot);
                lastPrintedTelemetryTimestamp = snapshot.Timestamp;
            }
        }

        await Task.Delay(TimeSpan.FromSeconds(1));
    }

    return 0;
}
catch (DllNotFoundException)
{
    Console.Error.WriteLine("SimConnect.dll was not found. Install the MSFS SDK or copy SimConnect.dll next to the app.");
    return 1;
}
catch (BadImageFormatException)
{
    Console.Error.WriteLine("SimConnect.dll was found, but it is not the x64 version.");
    return 1;
}
catch (COMException ex)
{
    Console.Error.WriteLine($"Could not connect to MSFS. Is Microsoft Flight Simulator running? HRESULT 0x{ex.ErrorCode:X8}");
    return 1;
}
catch (Win32Exception ex)
{
    Console.Error.WriteLine($"SimConnect error: {ex.Message}");
    return 1;
}

static void PrintTelemetry(TelemetrySnapshot telemetry)
{
    Console.WriteLine(
        $"{telemetry.Timestamp:HH:mm:ss} | " +
        $"IAS {telemetry.AirspeedIndicatedKnots,6:F1} kt | " +
        $"ALT {telemetry.IndicatedAltitudeFeet,8:F0} ft | " +
        $"HDG {telemetry.HeadingMagneticDegrees,6:F1} deg | " +
        $"VS {telemetry.VerticalSpeedFeetPerMinute,7:F0} fpm | " +
        $"PITCH {telemetry.PitchDegrees,6:F1} deg | " +
        $"BANK {telemetry.BankDegrees,6:F1} deg | " +
        $"LAT {telemetry.LatitudeDegrees,10:F6} | " +
        $"LON {telemetry.LongitudeDegrees,11:F6} | " +
        $"GROUND {(telemetry.IsOnGround ? "Yes" : "No")}");
}
