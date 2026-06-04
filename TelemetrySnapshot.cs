public sealed class TelemetrySnapshot
{
    public TelemetrySnapshot(
        DateTime timestamp,
        double airspeedIndicatedKnots,
        double indicatedAltitudeFeet,
        double headingMagneticDegrees,
        double verticalSpeedFeetPerMinute,
        double pitchDegrees,
        double bankDegrees,
        double latitudeDegrees,
        double longitudeDegrees,
        bool isOnGround)
    {
        Timestamp = timestamp;
        AirspeedIndicatedKnots = airspeedIndicatedKnots;
        IndicatedAltitudeFeet = indicatedAltitudeFeet;
        HeadingMagneticDegrees = headingMagneticDegrees;
        VerticalSpeedFeetPerMinute = verticalSpeedFeetPerMinute;
        PitchDegrees = pitchDegrees;
        BankDegrees = bankDegrees;
        LatitudeDegrees = latitudeDegrees;
        LongitudeDegrees = longitudeDegrees;
        IsOnGround = isOnGround;
    }

    public DateTime Timestamp { get; }
    public double AirspeedIndicatedKnots { get; }
    public double IndicatedAltitudeFeet { get; }
    public double HeadingMagneticDegrees { get; }
    public double VerticalSpeedFeetPerMinute { get; }
    public double PitchDegrees { get; }
    public double BankDegrees { get; }
    public double LatitudeDegrees { get; }
    public double LongitudeDegrees { get; }
    public bool IsOnGround { get; }
}
