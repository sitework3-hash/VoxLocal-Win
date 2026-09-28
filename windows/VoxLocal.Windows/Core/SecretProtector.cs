using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace VoxLocal.Win.Core;

public static class SecretProtector
{
    private const uint CryptProtectUiForbidden = 0x1;
    private const uint CryptUnprotectUiForbidden = 0x1;

    public static string Protect(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        var input = Encoding.UTF8.GetBytes(value);
        var blob = new DataBlob(input.Length, input);
        var entropy = new DataBlob(Encoding.UTF8.GetByteCount("VoxLocal.CloudApiKey"), Encoding.UTF8.GetBytes("VoxLocal.CloudApiKey"));
        try
        {
            if (!CryptProtectData(ref blob, "VoxLocal API key", ref entropy, nint.Zero, nint.Zero,
                    CryptProtectUiForbidden, out var protectedBlob))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return Convert.ToBase64String(ReadAndFree(protectedBlob));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
            blob.Dispose();
            entropy.Dispose();
        }
    }

    public static string Unprotect(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        var protectedBytes = Convert.FromBase64String(value);
        var blob = new DataBlob(protectedBytes.Length, protectedBytes);
        var entropy = new DataBlob(Encoding.UTF8.GetByteCount("VoxLocal.CloudApiKey"), Encoding.UTF8.GetBytes("VoxLocal.CloudApiKey"));
        nint description = nint.Zero;
        try
        {
            if (!CryptUnprotectData(ref blob, out description, ref entropy, nint.Zero, nint.Zero,
                    CryptUnprotectUiForbidden, out var plainBlob))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var plainBytes = ReadAndFree(plainBlob);
            try
            {
                return Encoding.UTF8.GetString(plainBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plainBytes);
            }
        }
        finally
        {
            if (description != nint.Zero)
                LocalFree(description);
            blob.Dispose();
            entropy.Dispose();
        }
    }

    private static byte[] ReadAndFree(DataBlob blob)
    {
        var bytes = new byte[blob.Size];
        if (blob.Size > 0)
            Marshal.Copy(blob.Data, bytes, 0, blob.Size);
        if (blob.Data != nint.Zero)
            LocalFree(blob.Data);
        return bytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob : IDisposable
    {
        public int Size;
        public nint Data;

        public DataBlob(int size, byte[] bytes)
        {
            Size = size;
            Data = Marshal.AllocHGlobal(size);
            Marshal.Copy(bytes, 0, Data, size);
        }

        public void Dispose()
        {
            if (Data != nint.Zero)
            {
                Marshal.FreeHGlobal(Data);
                Data = nint.Zero;
            }
        }
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn, string description, ref DataBlob optionalEntropy,
        nint reserved, nint prompt, uint flags, out DataBlob dataOut);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn, out nint description, ref DataBlob optionalEntropy,
        nint reserved, nint prompt, uint flags, out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint handle);
}
