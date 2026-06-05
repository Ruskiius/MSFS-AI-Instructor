using System.Globalization;

public sealed class TelemetryCsvLogger : IDisposable
{
    private readonly DateTime _startTimeUtc;
    private readonly StreamWriter _writer;
    private readonly object _writeLock = new();

    public TelemetryCsvLogger()
    {
        _startTimeUtc = DateTime.UtcNow;

        Directory.CreateDirectory("Logs");

        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture);
        string filePath = GetUniqueLogFilePath(timestamp);

        _writer = new StreamWriter(filePath);
        WriteHeader();
    }

    public void Write(TelemetrySnapshot telemetry)
    {
        DateTime timestampUtc = telemetry.Timestamp.ToUniversalTime();
        double elapsedSeconds = (timestampUtc - _startTimeUtc).TotalSeconds;

        string row = string.Join(
            ",",
            timestampUtc.ToString("O", CultureInfo.InvariantCulture),
            elapsedSeconds.ToString("F3", CultureInfo.InvariantCulture),
            telemetry.AirspeedIndicatedKnots.ToString("F3", CultureInfo.InvariantCulture),
            telemetry.IndicatedAltitudeFeet.ToString("F3", CultureInfo.InvariantCulture),
            telemetry.HeadingMagneticDegrees.ToString("F3", CultureInfo.InvariantCulture),
            telemetry.VerticalSpeedFeetPerMinute.ToString("F3", CultureInfo.InvariantCulture),
            telemetry.PitchDegrees.ToString("F3", CultureInfo.InvariantCulture),
            telemetry.BankDegrees.ToString("F3", CultureInfo.InvariantCulture),
            telemetry.LatitudeDegrees.ToString("F6", CultureInfo.InvariantCulture),
            telemetry.LongitudeDegrees.ToString("F6", CultureInfo.InvariantCulture),
            telemetry.IsOnGround.ToString(CultureInfo.InvariantCulture));

        lock (_writeLock)
        {
            _writer.WriteLine(row);
            _writer.Flush();
        }
    }

    public void Dispose()
    {
        _writer.Dispose();
    }

    private void WriteHeader()
    {
        _writer.WriteLine(
            "TimestampUtc," +
            "ElapsedSeconds," +
            "IndicatedAirspeedKnots," +
            "IndicatedAltitudeFeet," +
            "HeadingDegrees," +
            "VerticalSpeedFpm," +
            "PitchDegrees," +
            "BankDegrees," +
            "Latitude," +
            "Longitude," +
            "IsOnGround");

        _writer.Flush();
    }

    private static string GetUniqueLogFilePath(string timestamp)
    {
        string filePath = Path.Combine("Logs", $"flight_log_{timestamp}.csv");

        if (!File.Exists(filePath))
        {
            return filePath;
        }

        for (int index = 2; ; index++)
        {
            string alternatePath = Path.Combine("Logs", $"flight_log_{timestamp}_{index}.csv");

            if (!File.Exists(alternatePath))
            {
                return alternatePath;
            }
        }
    }
}
