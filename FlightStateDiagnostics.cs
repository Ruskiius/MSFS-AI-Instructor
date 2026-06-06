public sealed class FlightStateDiagnostics
{
    public FlightStateDiagnostics(
        double? estimatedAglFeet,
        double? departureFieldElevationFeet,
        double? secondsSinceTakeoff,
        double? secondsSinceLanding,
        FlightState previousFlightState)
    {
        EstimatedAglFeet = estimatedAglFeet;
        DepartureFieldElevationFeet = departureFieldElevationFeet;
        SecondsSinceTakeoff = secondsSinceTakeoff;
        SecondsSinceLanding = secondsSinceLanding;
        PreviousFlightState = previousFlightState;
    }

    public double? EstimatedAglFeet { get; }
    public double? DepartureFieldElevationFeet { get; }
    public double? SecondsSinceTakeoff { get; }
    public double? SecondsSinceLanding { get; }
    public FlightState PreviousFlightState { get; }
}
