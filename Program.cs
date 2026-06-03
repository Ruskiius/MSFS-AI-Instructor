using System.ComponentModel;
using System.Runtime.InteropServices;

const int DispatchQueueEmpty = unchecked((int)0x80004005);
const uint SimConnectObjectIdUser = 0;
const uint SimConnectUnused = 0xFFFFFFFF;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("MSFS SimConnect is only available on Windows.");
    return 1;
}

IntPtr simConnectHandle = IntPtr.Zero;
bool keepRunning = true;

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    keepRunning = false;
};

try
{
    int result = NativeMethods.SimConnect_Open(
        out simConnectHandle,
        "MsfsAiInstructor",
        IntPtr.Zero,
        0,
        IntPtr.Zero,
        0);

    ThrowIfFailed(result, "Open SimConnect");

    Console.WriteLine("Connected to MSFS");
    ConfigureTelemetry(simConnectHandle);
    RequestTelemetry(simConnectHandle);

    Console.WriteLine("Reading aircraft telemetry once per second. Press Ctrl+C to exit.");

    while (keepRunning)
    {
        result = NativeMethods.SimConnect_GetNextDispatch(simConnectHandle, out IntPtr messagePointer, out _);

        if (result == 0)
        {
            keepRunning = ProcessDispatchMessage(messagePointer);
            continue;
        }

        if (result == DispatchQueueEmpty || result > 0)
        {
            Thread.Sleep(50);
            continue;
        }

        ThrowIfFailed(result, "Read SimConnect dispatch message");
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
catch (COMException ex) when (simConnectHandle == IntPtr.Zero)
{
    Console.Error.WriteLine($"Could not connect to MSFS. Is Microsoft Flight Simulator running? {FormatComException(ex)}");
    return 1;
}
catch (Exception ex) when (ex is COMException or Win32Exception)
{
    Console.Error.WriteLine($"SimConnect error: {ex.Message}");
    return 1;
}
finally
{
    if (simConnectHandle != IntPtr.Zero)
    {
        NativeMethods.SimConnect_Close(simConnectHandle);
    }
}

static void ConfigureTelemetry(IntPtr simConnectHandle)
{
    AddTelemetryDatum(simConnectHandle, "AIRSPEED INDICATED", "Knots");
    AddTelemetryDatum(simConnectHandle, "INDICATED ALTITUDE", "Feet");
    AddTelemetryDatum(simConnectHandle, "PLANE HEADING DEGREES MAGNETIC", "Degrees");
    AddTelemetryDatum(simConnectHandle, "VERTICAL SPEED", "Feet per minute");
    AddTelemetryDatum(simConnectHandle, "PLANE PITCH DEGREES", "Degrees");
    AddTelemetryDatum(simConnectHandle, "PLANE BANK DEGREES", "Degrees");
    AddTelemetryDatum(simConnectHandle, "PLANE LATITUDE", "Degrees");
    AddTelemetryDatum(simConnectHandle, "PLANE LONGITUDE", "Degrees");
    AddTelemetryDatum(simConnectHandle, "SIM ON GROUND", "Bool");
}

static void AddTelemetryDatum(IntPtr simConnectHandle, string name, string units)
{
    int result = NativeMethods.SimConnect_AddToDataDefinition(
        simConnectHandle,
        DataDefinitionId.AircraftTelemetry,
        name,
        units,
        SimConnectDataType.Float64,
        0,
        SimConnectUnused);

    ThrowIfFailed(result, $"Add telemetry datum '{name}'");
}

static void RequestTelemetry(IntPtr simConnectHandle)
{
    int result = NativeMethods.SimConnect_RequestDataOnSimObject(
        simConnectHandle,
        DataRequestId.AircraftTelemetry,
        DataDefinitionId.AircraftTelemetry,
        SimConnectObjectIdUser,
        SimConnectPeriod.Second,
        0,
        0,
        0,
        0);

    ThrowIfFailed(result, "Request aircraft telemetry");
}

static bool ProcessDispatchMessage(IntPtr messagePointer)
{
    SimConnectRecv message = Marshal.PtrToStructure<SimConnectRecv>(messagePointer);

    switch (message.Id)
    {
        case SimConnectRecvId.Open:
            return true;

        case SimConnectRecvId.Quit:
            Console.Error.WriteLine("SimConnect disconnected.");
            return false;

        case SimConnectRecvId.Exception:
            SimConnectRecvException exception = Marshal.PtrToStructure<SimConnectRecvException>(messagePointer);
            Console.Error.WriteLine($"SimConnect exception received. Exception={exception.Exception}, SendId={exception.SendId}, Index={exception.Index}");
            return true;

        case SimConnectRecvId.SimObjectData:
            PrintTelemetry(messagePointer);
            return true;

        default:
            return true;
    }
}

static void PrintTelemetry(IntPtr messagePointer)
{
    SimConnectRecvSimObjectData message = Marshal.PtrToStructure<SimConnectRecvSimObjectData>(messagePointer);

    if (message.RequestId != DataRequestId.AircraftTelemetry)
    {
        return;
    }

    int dataOffset = Marshal.OffsetOf<SimConnectRecvSimObjectData>(nameof(SimConnectRecvSimObjectData.Data)).ToInt32();
    IntPtr dataPointer = IntPtr.Add(messagePointer, dataOffset);
    AircraftTelemetry telemetry = Marshal.PtrToStructure<AircraftTelemetry>(dataPointer);

    Console.WriteLine(
        $"{DateTime.Now:HH:mm:ss} | " +
        $"IAS {telemetry.AirspeedIndicated,6:F1} kt | " +
        $"ALT {telemetry.IndicatedAltitude,8:F0} ft | " +
        $"HDG {telemetry.HeadingMagnetic,6:F1} deg | " +
        $"VS {telemetry.VerticalSpeed,7:F0} fpm | " +
        $"PITCH {telemetry.PitchDegrees,6:F1} deg | " +
        $"BANK {telemetry.BankDegrees,6:F1} deg | " +
        $"LAT {telemetry.Latitude,10:F6} | " +
        $"LON {telemetry.Longitude,11:F6} | " +
        $"GROUND {(telemetry.OnGround ? "Yes" : "No")}");
}

static void ThrowIfFailed(int result, string operation)
{
    if (result < 0)
    {
        throw new COMException($"{operation} failed with HRESULT 0x{result:X8}.", result);
    }
}

static string FormatComException(COMException ex) => $"HRESULT 0x{ex.ErrorCode:X8}";

internal enum DataDefinitionId : uint
{
    AircraftTelemetry = 0
}

internal enum DataRequestId : uint
{
    AircraftTelemetry = 0
}

internal enum SimConnectDataType : uint
{
    Float64 = 4
}

internal enum SimConnectPeriod : uint
{
    Never = 0,
    Once = 1,
    VisualFrame = 2,
    SimFrame = 3,
    Second = 4
}

internal enum SimConnectRecvId : uint
{
    Null = 0,
    Exception = 1,
    Open = 2,
    Quit = 3,
    SimObjectData = 8
}

[StructLayout(LayoutKind.Sequential)]
internal struct AircraftTelemetry
{
    public double AirspeedIndicated;
    public double IndicatedAltitude;
    public double HeadingMagnetic;
    public double VerticalSpeed;
    public double PitchDegrees;
    public double BankDegrees;
    public double Latitude;
    public double Longitude;
    public double SimOnGround;

    public readonly bool OnGround => SimOnGround >= 0.5;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SimConnectRecv
{
    public uint Size;
    public uint Version;
    public SimConnectRecvId Id;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SimConnectRecvException
{
    public uint Size;
    public uint Version;
    public SimConnectRecvId Id;
    public uint Exception;
    public uint SendId;
    public uint Index;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SimConnectRecvSimObjectData
{
    public uint Size;
    public uint Version;
    public SimConnectRecvId Id;
    public DataRequestId RequestId;
    public uint ObjectId;
    public DataDefinitionId DefineId;
    public uint Flags;
    public uint EntryNumber;
    public uint OutOf;
    public uint DefineCount;
    public uint Data;
}

internal static class NativeMethods
{
    [DllImport("SimConnect.dll", CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    internal static extern int SimConnect_Open(
        out IntPtr phSimConnect,
        string szName,
        IntPtr hWnd,
        uint userEventWin32,
        IntPtr hEventHandle,
        uint configIndex);

    [DllImport("SimConnect.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    internal static extern int SimConnect_Close(IntPtr hSimConnect);

    [DllImport("SimConnect.dll", CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    internal static extern int SimConnect_AddToDataDefinition(
        IntPtr hSimConnect,
        DataDefinitionId defineId,
        string datumName,
        string unitsName,
        SimConnectDataType datumType,
        float epsilon,
        uint datumId);

    [DllImport("SimConnect.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    internal static extern int SimConnect_RequestDataOnSimObject(
        IntPtr hSimConnect,
        DataRequestId requestId,
        DataDefinitionId defineId,
        uint objectId,
        SimConnectPeriod period,
        uint flags,
        uint origin,
        uint interval,
        uint limit);

    [DllImport("SimConnect.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    internal static extern int SimConnect_GetNextDispatch(
        IntPtr hSimConnect,
        out IntPtr data,
        out uint dataSize);
}
