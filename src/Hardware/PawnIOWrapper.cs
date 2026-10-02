using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FwHelper.Hardware
{
    /// <summary>
    /// Minimal client for the PawnIO driver (https://pawnio.eu), adapted from G-Helper.
    /// Requires PawnIO to be installed and the app to run elevated.
    /// </summary>
    public sealed class PawnIOWrapper : IDisposable
    {
        private const int FN_LEN = 32;
        private const uint IOCTL_LOAD = 0xA1B22084;
        private const uint IOCTL_EXECUTE = 0xA1B22104;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(string n, uint acc, uint share, IntPtr sec, uint disp, uint fl, IntPtr tmpl);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle dev, uint code, byte[]? inB, uint inSz, byte[]? outB, uint outSz, out uint ret, IntPtr ovl);

        private SafeFileHandle? _handle;
        private bool _loaded;

        public enum ConnectResult { OK, NotInstalled, AccessDenied, OtherError }

        public bool IsReady => _loaded && _handle is { IsInvalid: false };

        public ConnectResult Connect()
        {
            if (_handle is { IsInvalid: false }) return ConnectResult.OK;
            var h = CreateFile(@"\\?\GLOBALROOT\Device\PawnIO", 0xC0000000u, 0x3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h.IsInvalid)
            {
                int err = Marshal.GetLastWin32Error();
                h.Dispose();
                return err switch
                {
                    2 or 3 => ConnectResult.NotInstalled,
                    5 => ConnectResult.AccessDenied,
                    _ => ConnectResult.OtherError,
                };
            }
            _handle = h;
            return ConnectResult.OK;
        }

        public bool LoadModule(byte[] data)
        {
            if (_handle is null || _handle.IsInvalid) return false;
            _loaded = DeviceIoControl(_handle, IOCTL_LOAD, data, (uint)data.Length, null, 0, out _, IntPtr.Zero);
            return _loaded;
        }

        public bool Execute(string functionName, ulong[]? input, ulong[]? output)
        {
            if (!IsReady) return false;

            byte[] buffer = new byte[FN_LEN + (input?.Length ?? 0) * 8];
            byte[] nameBytes = Encoding.ASCII.GetBytes(functionName);
            Buffer.BlockCopy(nameBytes, 0, buffer, 0, Math.Min(nameBytes.Length, FN_LEN - 1));
            if (input is { Length: > 0 }) Buffer.BlockCopy(input, 0, buffer, FN_LEN, input.Length * 8);

            byte[]? outBuffer = output is { Length: > 0 } ? new byte[output.Length * 8] : null;
            bool ok = DeviceIoControl(_handle!, IOCTL_EXECUTE, buffer, (uint)buffer.Length, outBuffer, (uint)(outBuffer?.Length ?? 0), out uint returned, IntPtr.Zero);
            if (ok && output is not null && outBuffer is not null && returned > 0)
                Buffer.BlockCopy(outBuffer, 0, output, 0, (int)Math.Min(returned, (uint)outBuffer.Length));
            return ok;
        }

        public void Dispose()
        {
            _loaded = false;
            _handle?.Dispose();
            _handle = null;
        }
    }
}
