namespace Lumui.Cli.Security;

internal static partial class WindowsDataProtector
{
    private const UInt32 UiForbidden = 0x1;

    public static Byte[] Protect(Byte[] data, Byte[] entropy) => Transform(data, entropy, true);

    public static Byte[] Unprotect(Byte[] data, Byte[] entropy) => Transform(data, entropy, false);

    private static Byte[] Transform(Byte[] data, Byte[] entropy, Boolean protect)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows data protection is unavailable on this system.");
        }
        GCHandle dataHandle = default;
        GCHandle entropyHandle = default;
        DataBlob output = default;
        IntPtr description = IntPtr.Zero;
        try
        {
            DataBlob input = Pin(data, ref dataHandle);
            DataBlob optionalEntropy = Pin(entropy, ref entropyHandle);
            Boolean succeeded = protect
                ? CryptProtectData(
                    ref input,
                    null,
                    ref optionalEntropy,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    UiForbidden,
                    out output)
                : CryptUnprotectData(
                    ref input,
                    out description,
                    ref optionalEntropy,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    UiForbidden,
                    out output);
            if (!succeeded)
            {
                throw new CryptographicException(
                    $"Windows data protection failed with error {Marshal.GetLastWin32Error()}.");
            }
            Byte[] result = new Byte[checked((Int32)output.Size)];
            if (result.Length > 0)
            {
                Marshal.Copy(output.Data, result, 0, result.Length);
            }
            return result;
        }
        finally
        {
            if (!protect && output.Data != IntPtr.Zero)
            {
                for (UInt32 index = 0; index < output.Size; index++)
                {
                    Marshal.WriteByte(output.Data, checked((Int32)index), 0);
                }
            }
            if (output.Data != IntPtr.Zero)
            {
                LocalFree(output.Data);
            }
            if (description != IntPtr.Zero)
            {
                LocalFree(description);
            }
            if (entropyHandle.IsAllocated)
            {
                entropyHandle.Free();
            }
            if (dataHandle.IsAllocated)
            {
                dataHandle.Free();
            }
        }
    }

    private static DataBlob Pin(Byte[] value, ref GCHandle handle)
    {
        if (value.Length == 0)
        {
            return default;
        }
        handle = GCHandle.Alloc(value, GCHandleType.Pinned);
        return new DataBlob
        {
            Size = checked((UInt32)value.Length),
            Data = handle.AddrOfPinnedObject()
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public UInt32 Size;
        public IntPtr Data;
    }

    [LibraryImport("crypt32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial Boolean CryptProtectData(
        ref DataBlob dataIn,
        String? dataDescription,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr promptStructure,
        UInt32 flags,
        out DataBlob dataOut);

    [LibraryImport("crypt32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial Boolean CryptUnprotectData(
        ref DataBlob dataIn,
        out IntPtr dataDescription,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr promptStructure,
        UInt32 flags,
        out DataBlob dataOut);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr memory);
}
