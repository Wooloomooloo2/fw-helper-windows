using FwHelper.Helpers;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace FwHelper.Hardware
{
    public enum EcStatus
    {
        Success = 0,
        InvalidCommand = 1,
        Error = 2,
        InvalidParam = 3,
        AccessDenied = 4,
        InvalidResponse = 5,
        InvalidVersion = 6,
        InvalidChecksum = 7,
        InProgress = 8,
        Unavailable = 9,
        Timeout = 10,
        Overflow = 11,
        InvalidHeader = 12,
        RequestTruncated = 13,
        ResponseTooBig = 14,
        BusError = 15,
        Busy = 16,
        DriverError = 0x1000,
        NotConnected = 0x1001,
    }

    /// <summary>
    /// Raw access to the ChromeOS-style embedded controller through Framework's CrosEC Windows driver
    /// (service "CrosEcBus", device \Device\CrosEC). Same interface framework_tool uses.
    /// The driver does not require elevation.
    /// </summary>
    public sealed class CrosEc : IDisposable
    {
        private const string DevicePath = @"\\.\GLOBALROOT\Device\CrosEC";

        // CTL_CODE(0x80EC, 0x801, METHOD_BUFFERED, FILE_READ_DATA | FILE_WRITE_DATA)
        private const uint IOCTL_CROSEC_XCMD = 0x80ECE004;
        // CTL_CODE(0x80EC, 0x802, METHOD_BUFFERED, FILE_READ_DATA)
        private const uint IOCTL_CROSEC_RDMEM = 0x80EC6008;

        // struct { u32 version; u32 command; u32 outsize; u32 insize; u32 result; u8 data[]; }
        private const int CommandHeaderSize = 20;
        // The driver rejects anything larger than this (verified on hardware)
        private const int CommandBufferSize = 248;
        public const int MaxPayload = CommandBufferSize - CommandHeaderSize;

        // struct { u32 offset; u32 bytes; u8 buffer[256]; }
        private const int MemmapBufferSize = 8 + 256;
        public const int MemmapSize = 256;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] inBuffer, int inSize, byte[] outBuffer, int outSize, out int returned, IntPtr overlapped);

        private SafeFileHandle? _handle;
        private readonly object _lock = new();

        public bool IsConnected => _handle is { IsInvalid: false, IsClosed: false };
        public int LastWin32Error { get; private set; }

        public bool Connect()
        {
            lock (_lock)
            {
                if (IsConnected) return true;
                var handle = CreateFile(DevicePath, 0xC0000000 /* GENERIC_READ|WRITE */, 3 /* share R|W */, IntPtr.Zero, 3 /* OPEN_EXISTING */, 0, IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    LastWin32Error = Marshal.GetLastWin32Error();
                    Logger.WriteLine($"CrosEC: can't open driver (error {LastWin32Error})");
                    handle.Dispose();
                    return false;
                }
                _handle = handle;
                return true;
            }
        }

        /// <summary>Read from the EC shared memory map (temps, fans, battery...).</summary>
        public byte[]? ReadMemory(int offset, int length)
        {
            if (offset < 0 || length <= 0 || offset + length > MemmapSize) throw new ArgumentOutOfRangeException(nameof(length));

            lock (_lock)
            {
                if (!IsConnected) return null;
                var buffer = new byte[MemmapBufferSize];
                BitConverter.TryWriteBytes(buffer.AsSpan(0), offset);
                BitConverter.TryWriteBytes(buffer.AsSpan(4), length);

                if (!DeviceIoControl(_handle!, IOCTL_CROSEC_RDMEM, buffer, buffer.Length, buffer, buffer.Length, out _, IntPtr.Zero))
                {
                    LastWin32Error = Marshal.GetLastWin32Error();
                    return null;
                }
                return buffer.AsSpan(8, length).ToArray();
            }
        }

        /// <summary>Send a host command. Response is always exactly <paramref name="responseSize"/> bytes on success.</summary>
        public EcStatus Command(ushort command, byte version, ReadOnlySpan<byte> request, int responseSize, out byte[] response)
        {
            response = new byte[Math.Max(0, responseSize)];
            if (request.Length > MaxPayload || responseSize > MaxPayload) throw new ArgumentOutOfRangeException(nameof(request));

            lock (_lock)
            {
                if (!IsConnected) return EcStatus.NotConnected;

                var buffer = new byte[CommandBufferSize];
                BitConverter.TryWriteBytes(buffer.AsSpan(0), (uint)version);
                BitConverter.TryWriteBytes(buffer.AsSpan(4), (uint)command);
                BitConverter.TryWriteBytes(buffer.AsSpan(8), (uint)request.Length);
                BitConverter.TryWriteBytes(buffer.AsSpan(12), (uint)responseSize);
                BitConverter.TryWriteBytes(buffer.AsSpan(16), 0xFFu);
                request.CopyTo(buffer.AsSpan(CommandHeaderSize));

                if (!DeviceIoControl(_handle!, IOCTL_CROSEC_XCMD, buffer, buffer.Length, buffer, buffer.Length, out _, IntPtr.Zero))
                {
                    LastWin32Error = Marshal.GetLastWin32Error();
                    Logger.WriteLine($"CrosEC: command 0x{command:X4} v{version} ioctl failed ({LastWin32Error})");
                    return EcStatus.DriverError;
                }

                var status = (EcStatus)BitConverter.ToUInt32(buffer, 16);
                if (status == EcStatus.Success)
                    buffer.AsSpan(CommandHeaderSize, responseSize).CopyTo(response);
                return status;
            }
        }

        public EcStatus Command(ushort command, byte version, ReadOnlySpan<byte> request)
            => Command(command, version, request, 0, out _);

        public void Dispose()
        {
            lock (_lock)
            {
                _handle?.Dispose();
                _handle = null;
            }
        }
    }
}
