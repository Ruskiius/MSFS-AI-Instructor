using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

public sealed class SimConnectTelemetryService : IDisposable
{
    private const int DispatchQueueEmpty = unchecked((int)0x80004005);
    private const uint SimConnectObjectIdUser = 0;
    private const uint SimConnectUnused = 0xFFFFFFFF;
    private static readonly TimeSpan ReadTaskStopTimeout = TimeSpan.FromSeconds(2);

    private IntPtr _simConnectHandle = IntPtr.Zero;
    private CancellationTokenSource? _readCancellation;
    private Task? _readTask;
    private bool _isDisposed;
    private bool _readStopTimeoutReported;

    public event EventHandler<TelemetrySnapshot>? TelemetryReceived;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<string>? Disconnected;

    public void Connect()
    {
        ThrowIfDisposed();

        if (_simConnectHandle != IntPtr.Zero)
        {
            return;
        }

        int result = NativeMethods.SimConnect_Open(
            out _simConnectHandle,
            "MsfsAiInstructor",
            IntPtr.Zero,
            0,
            IntPtr.Zero,
            0);

        ThrowIfFailed(result, "Open SimConnect");

        ConfigureTelemetry();
        RequestTelemetry();
    }

    public void Start()
    {
        ThrowIfDisposed();
        EnsureConnected();

        if (_readTask is not null)
        {
            return;
        }

        _readCancellation = new CancellationTokenSource();
        _readTask = Task.Run(() => ReadDispatchLoopAsync(_readCancellation.Token));
    }

    public void Stop()
    {
        StopReadTask();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        bool readTaskStopped = StopReadTask();

        if (_simConnectHandle == IntPtr.Zero)
        {
            return;
        }

        if (readTaskStopped)
        {
            CloseSimConnectHandle();
            return;
        }

        Task? pendingReadTask = _readTask;
        CancellationTokenSource? pendingReadCancellation = _readCancellation;

        if (pendingReadTask is null)
        {
            return;
        }

        _ = pendingReadTask.ContinueWith(
            _ =>
            {
                CloseSimConnectHandle();
                pendingReadCancellation?.Dispose();

                if (ReferenceEquals(_readCancellation, pendingReadCancellation))
                {
                    _readCancellation = null;
                }

                if (ReferenceEquals(_readTask, pendingReadTask))
                {
                    _readTask = null;
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task ReadDispatchLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _simConnectHandle != IntPtr.Zero)
            {
                int result = NativeMethods.SimConnect_GetNextDispatch(_simConnectHandle, out IntPtr messagePointer, out _);

                if (cancellationToken.IsCancellationRequested || _isDisposed)
                {
                    break;
                }

                if (result == 0)
                {
                    if (!ProcessDispatchMessage(messagePointer))
                    {
                        break;
                    }

                    continue;
                }

                if (result == DispatchQueueEmpty || result > 0)
                {
                    await Task.Delay(50, cancellationToken);
                    continue;
                }

                ThrowIfFailed(result, "Read SimConnect dispatch message");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is COMException or Win32Exception)
        {
            Disconnected?.Invoke(this, $"SimConnect disconnected: {ex.Message}");
        }
    }

    private void ConfigureTelemetry()
    {
        AddTelemetryDatum("AIRSPEED INDICATED", "Knots");
        AddTelemetryDatum("INDICATED ALTITUDE", "Feet");
        AddTelemetryDatum("PLANE HEADING DEGREES MAGNETIC", "Degrees");
        AddTelemetryDatum("VERTICAL SPEED", "Feet per minute");
        AddTelemetryDatum("PLANE PITCH DEGREES", "Degrees");
        AddTelemetryDatum("PLANE BANK DEGREES", "Degrees");
        AddTelemetryDatum("PLANE LATITUDE", "Degrees");
        AddTelemetryDatum("PLANE LONGITUDE", "Degrees");
        AddTelemetryDatum("SIM ON GROUND", "Bool");
    }

    private void AddTelemetryDatum(string name, string units)
    {
        int result = NativeMethods.SimConnect_AddToDataDefinition(
            _simConnectHandle,
            DataDefinitionId.AircraftTelemetry,
            name,
            units,
            SimConnectDataType.Float64,
            0,
            SimConnectUnused);

        ThrowIfFailed(result, $"Add telemetry datum '{name}'");
    }

    private void RequestTelemetry()
    {
        int result = NativeMethods.SimConnect_RequestDataOnSimObject(
            _simConnectHandle,
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

    private bool ProcessDispatchMessage(IntPtr messagePointer)
    {
        SimConnectRecv message = Marshal.PtrToStructure<SimConnectRecv>(messagePointer);

        switch (message.Id)
        {
            case SimConnectRecvId.Open:
                return true;

            case SimConnectRecvId.Quit:
                Disconnected?.Invoke(this, "SimConnect disconnected.");
                return false;

            case SimConnectRecvId.Exception:
                SimConnectRecvException exception = Marshal.PtrToStructure<SimConnectRecvException>(messagePointer);
                ErrorOccurred?.Invoke(this, $"SimConnect exception received. Exception={exception.Exception}, SendId={exception.SendId}, Index={exception.Index}");
                return true;

            case SimConnectRecvId.SimObjectData:
                ReadTelemetry(messagePointer);
                return true;

            default:
                return true;
        }
    }

    private void ReadTelemetry(IntPtr messagePointer)
    {
        SimConnectRecvSimObjectData message = Marshal.PtrToStructure<SimConnectRecvSimObjectData>(messagePointer);

        if (message.RequestId != DataRequestId.AircraftTelemetry)
        {
            return;
        }

        int dataOffset = Marshal.OffsetOf<SimConnectRecvSimObjectData>(nameof(SimConnectRecvSimObjectData.Data)).ToInt32();
        IntPtr dataPointer = IntPtr.Add(messagePointer, dataOffset);
        AircraftTelemetryData data = Marshal.PtrToStructure<AircraftTelemetryData>(dataPointer);

        TelemetrySnapshot snapshot = new(
            DateTime.Now,
            data.AirspeedIndicated,
            data.IndicatedAltitude,
            data.HeadingMagnetic,
            data.VerticalSpeed,
            data.PitchDegrees,
            data.BankDegrees,
            data.Latitude,
            data.Longitude,
            data.SimOnGround >= 0.5);

        TelemetryReceived?.Invoke(this, snapshot);
    }

    private void EnsureConnected()
    {
        if (_simConnectHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Call Connect before starting telemetry.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    private bool StopReadTask()
    {
        Task? readTask = _readTask;
        CancellationTokenSource? readCancellation = _readCancellation;

        readCancellation?.Cancel();

        if (readTask is null)
        {
            readCancellation?.Dispose();
            _readCancellation = null;
            return true;
        }

        try
        {
            if (!readTask.Wait(ReadTaskStopTimeout))
            {
                ReportReadStopTimeout();
                return false;
            }
        }
        catch (AggregateException ex) when (AllInnerExceptionsAreCancellation(ex))
        {
        }

        readCancellation?.Dispose();

        if (ReferenceEquals(_readCancellation, readCancellation))
        {
            _readCancellation = null;
        }

        if (ReferenceEquals(_readTask, readTask))
        {
            _readTask = null;
        }

        _readStopTimeoutReported = false;
        return true;
    }

    private void ReportReadStopTimeout()
    {
        if (_readStopTimeoutReported)
        {
            return;
        }

        _readStopTimeoutReported = true;
        ErrorOccurred?.Invoke(this, "Timed out waiting for SimConnect read loop to stop; preserving the native handle until the loop exits.");
    }

    private void CloseSimConnectHandle()
    {
        IntPtr handle = Interlocked.Exchange(ref _simConnectHandle, IntPtr.Zero);

        if (handle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            NativeMethods.SimConnect_Close(handle);
        }
        catch (Exception ex) when (ex is COMException or Win32Exception or SEHException)
        {
        }
    }

    private static void ThrowIfFailed(int result, string operation)
    {
        if (result < 0)
        {
            throw new COMException($"{operation} failed with HRESULT 0x{result:X8}.", result);
        }
    }

    private static bool AllInnerExceptionsAreCancellation(AggregateException ex)
    {
        return ex.InnerExceptions.All(innerException => innerException is TaskCanceledException or OperationCanceledException);
    }

    private enum DataDefinitionId : uint
    {
        AircraftTelemetry = 0
    }

    private enum DataRequestId : uint
    {
        AircraftTelemetry = 0
    }

    private enum SimConnectDataType : uint
    {
        Float64 = 4
    }

    private enum SimConnectPeriod : uint
    {
        Second = 4
    }

    private enum SimConnectRecvId : uint
    {
        Exception = 1,
        Open = 2,
        Quit = 3,
        SimObjectData = 8
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AircraftTelemetryData
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
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SimConnectRecv
    {
        public uint Size;
        public uint Version;
        public SimConnectRecvId Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SimConnectRecvException
    {
        public uint Size;
        public uint Version;
        public SimConnectRecvId Id;
        public uint Exception;
        public uint SendId;
        public uint Index;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SimConnectRecvSimObjectData
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

    private static class NativeMethods
    {
        [DllImport("SimConnect.dll", CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern int SimConnect_Open(
            out IntPtr phSimConnect,
            string szName,
            IntPtr hWnd,
            uint userEventWin32,
            IntPtr hEventHandle,
            uint configIndex);

        [DllImport("SimConnect.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern int SimConnect_Close(IntPtr hSimConnect);

        [DllImport("SimConnect.dll", CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern int SimConnect_AddToDataDefinition(
            IntPtr hSimConnect,
            DataDefinitionId defineId,
            string datumName,
            string unitsName,
            SimConnectDataType datumType,
            float epsilon,
            uint datumId);

        [DllImport("SimConnect.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern int SimConnect_RequestDataOnSimObject(
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
        public static extern int SimConnect_GetNextDispatch(
            IntPtr hSimConnect,
            out IntPtr data,
            out uint dataSize);
    }
}
