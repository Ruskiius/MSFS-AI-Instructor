public sealed class FlightStateDetector
{
    private const double GroundStoppedAirspeedKnots = 5.0;
    private const double ClimbVerticalSpeedFpm = 300.0;
    private const double DescentVerticalSpeedFpm = -300.0;
    private const double InitialClimbMaxSeconds = 20.0;
    private const double InitialClimbMaxAglFeet = 500.0;
    private const double ApproachMaxAglFeet = 1_000.0;
    private const double ApproachMaxAirspeedKnots = 120.0;
    private const double LandingRollMaxSeconds = 120.0;

    private FlightState _previousState = FlightState.Unknown;
    private bool _hasPreviousSnapshot;
    private bool _wasOnGround;
    private bool _hasTakenOff;
    private DateTime? _takeoffTime;
    private DateTime? _landingTime;
    private double? _departureFieldElevationFeet;

    public FlightState Detect(TelemetrySnapshot snapshot)
    {
        RecordDepartureFieldElevationIfNeeded(snapshot);

        bool justBecameAirborne = _hasPreviousSnapshot && _wasOnGround && !snapshot.IsOnGround;
        bool justTouchedDown = _hasPreviousSnapshot && !_wasOnGround && snapshot.IsOnGround;

        if (justBecameAirborne)
        {
            _hasTakenOff = true;
            _takeoffTime = snapshot.Timestamp;
            _landingTime = null;
        }

        if (justTouchedDown)
        {
            _landingTime = snapshot.Timestamp;
        }

        double? estimatedAglFeet = EstimateAglFeet(snapshot);
        FlightState detectedState = DetectState(snapshot, justBecameAirborne, justTouchedDown, estimatedAglFeet);

        _previousState = detectedState;
        _wasOnGround = snapshot.IsOnGround;
        _hasPreviousSnapshot = true;

        return detectedState;
    }

    private FlightState DetectState(
        TelemetrySnapshot snapshot,
        bool justBecameAirborne,
        bool justTouchedDown,
        double? estimatedAglFeet)
    {
        if (snapshot.IsOnGround)
        {
            if (snapshot.AirspeedIndicatedKnots < GroundStoppedAirspeedKnots)
            {
                return FlightState.OnGround;
            }

            if (IsLandingRoll(snapshot, justTouchedDown))
            {
                return FlightState.LandingRoll;
            }

            return _hasTakenOff ? FlightState.OnGround : FlightState.TakeoffRoll;
        }

        if (justBecameAirborne || IsInitialClimb(snapshot, estimatedAglFeet))
        {
            return FlightState.InitialClimb;
        }

        if (snapshot.VerticalSpeedFeetPerMinute > ClimbVerticalSpeedFpm)
        {
            return FlightState.Climb;
        }

        if (snapshot.VerticalSpeedFeetPerMinute < DescentVerticalSpeedFpm)
        {
            if (IsApproach(snapshot, estimatedAglFeet))
            {
                return FlightState.Approach;
            }

            return FlightState.Descent;
        }

        return FlightState.LevelFlight;
    }

    private void RecordDepartureFieldElevationIfNeeded(TelemetrySnapshot snapshot)
    {
        if (_departureFieldElevationFeet is not null)
        {
            return;
        }

        if (snapshot.IsOnGround && snapshot.AirspeedIndicatedKnots < GroundStoppedAirspeedKnots)
        {
            _departureFieldElevationFeet = snapshot.IndicatedAltitudeFeet;
        }
    }

    private bool IsInitialClimb(TelemetrySnapshot snapshot, double? estimatedAglFeet)
    {
        if (_takeoffTime is null)
        {
            return false;
        }

        double secondsSinceTakeoff = (snapshot.Timestamp - _takeoffTime.Value).TotalSeconds;

        if (secondsSinceTakeoff <= InitialClimbMaxSeconds)
        {
            return true;
        }

        return estimatedAglFeet is >= 0 and <= InitialClimbMaxAglFeet &&
            snapshot.VerticalSpeedFeetPerMinute > 0;
    }

    private bool IsApproach(TelemetrySnapshot snapshot, double? estimatedAglFeet)
    {
        if (estimatedAglFeet is null)
        {
            return false;
        }

        return estimatedAglFeet <= ApproachMaxAglFeet &&
            snapshot.AirspeedIndicatedKnots <= ApproachMaxAirspeedKnots;
    }

    private bool IsLandingRoll(TelemetrySnapshot snapshot, bool justTouchedDown)
    {
        if (justTouchedDown)
        {
            return true;
        }

        if (_previousState == FlightState.LandingRoll)
        {
            return true;
        }

        if (_landingTime is null)
        {
            return false;
        }

        double secondsSinceLanding = (snapshot.Timestamp - _landingTime.Value).TotalSeconds;

        return secondsSinceLanding <= LandingRollMaxSeconds &&
            snapshot.AirspeedIndicatedKnots >= GroundStoppedAirspeedKnots;
    }

    private double? EstimateAglFeet(TelemetrySnapshot snapshot)
    {
        if (_departureFieldElevationFeet is null)
        {
            return null;
        }

        return snapshot.IndicatedAltitudeFeet - _departureFieldElevationFeet.Value;
    }
}
