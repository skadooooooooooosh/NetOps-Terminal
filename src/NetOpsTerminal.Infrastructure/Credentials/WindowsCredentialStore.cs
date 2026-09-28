using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using NetOpsTerminal.Security.Credentials;

namespace NetOpsTerminal.Infrastructure.Credentials;

public sealed partial class WindowsCredentialStore : ICredentialStore
{
    private const int ErrorNotFound = 1168;
    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistLocalMachine = 2;
    private const int MaximumCredentialBlobBytes = 2_560;
    private const string TargetPrefix = "NetOpsTerminal:";

    public Task<string?> GetSecretAsync(
        string secretId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWindows();
        string targetName = BuildTargetName(secretId);
        if (!NativeMethods.CredRead(
                targetName,
                CredentialTypeGeneric,
                0,
                out IntPtr credentialPointer))
        {
            int error = Marshal.GetLastPInvokeError();
            if (error == ErrorNotFound)
            {
                return Task.FromResult<string?>(null);
            }

            throw new Win32Exception(error, "Windows Credential Manager could not read the requested credential.");
        }

        try
        {
            NativeCredential credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlobSize == 0)
            {
                return Task.FromResult<string?>(string.Empty);
            }

            byte[] bytes = new byte[checked((int)credential.CredentialBlobSize)];
            try
            {
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                return Task.FromResult<string?>(Encoding.UTF8.GetString(bytes));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        finally
        {
            NativeMethods.CredFree(credentialPointer);
        }
    }

    public Task SetSecretAsync(
        string secretId,
        string value,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWindows();
        string targetName = BuildTargetName(secretId);
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > MaximumCredentialBlobBytes)
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw new ArgumentException(
                $"The password exceeds the Windows Credential Manager limit of {MaximumCredentialBlobBytes} UTF-8 bytes.",
                nameof(value));
        }

        IntPtr blob = IntPtr.Zero;
        IntPtr targetNamePointer = IntPtr.Zero;
        IntPtr userNamePointer = IntPtr.Zero;
        try
        {
            targetNamePointer = Marshal.StringToHGlobalUni(targetName);
            userNamePointer = Marshal.StringToHGlobalUni(Environment.UserName);
            if (bytes.Length > 0)
            {
                blob = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, blob, bytes.Length);
            }

            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = targetNamePointer,
                CredentialBlobSize = checked((uint)bytes.Length),
                CredentialBlob = blob,
                Persist = CredentialPersistLocalMachine,
                UserName = userNamePointer,
            };
            if (!NativeMethods.CredWrite(ref credential, 0))
            {
                throw new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    "Windows Credential Manager could not save the credential.");
            }

            return Task.CompletedTask;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            if (blob != IntPtr.Zero)
            {
                for (int index = 0; index < bytes.Length; index++)
                {
                    Marshal.WriteByte(blob, index, 0);
                }

                Marshal.FreeHGlobal(blob);
            }

            if (targetNamePointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(targetNamePointer);
            }

            if (userNamePointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(userNamePointer);
            }
        }
    }

    public Task DeleteSecretAsync(string secretId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWindows();
        string targetName = BuildTargetName(secretId);
        if (!NativeMethods.CredDelete(targetName, CredentialTypeGeneric, 0))
        {
            int error = Marshal.GetLastPInvokeError();
            if (error != ErrorNotFound)
            {
                throw new Win32Exception(
                    error,
                    "Windows Credential Manager could not delete the credential.");
            }
        }

        return Task.CompletedTask;
    }

    private static string BuildTargetName(string secretId)
    {
        if (string.IsNullOrWhiteSpace(secretId)
            || secretId.Length > 200
            || secretId.Any(char.IsControl))
        {
            throw new ArgumentException("A valid secret reference is required.", nameof(secretId));
        }

        return TargetPrefix + secretId;
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Saved credentials require Windows Credential Manager on the current Phase 4 target.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;

        public IntPtr TargetName;
        public IntPtr Comment;
        public NativeFileTime LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;

        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }

    private static partial class NativeMethods
    {
        [LibraryImport(
            "Advapi32.dll",
            EntryPoint = "CredWriteW",
            SetLastError = true,
            StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CredWrite(ref NativeCredential credential, uint flags);

        [LibraryImport(
            "Advapi32.dll",
            EntryPoint = "CredReadW",
            SetLastError = true,
            StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CredRead(
            string target,
            uint type,
            uint flags,
            out IntPtr credential);

        [LibraryImport(
            "Advapi32.dll",
            EntryPoint = "CredDeleteW",
            SetLastError = true,
            StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CredDelete(string target, uint type, uint flags);

        [LibraryImport("Advapi32.dll")]
        internal static partial void CredFree(IntPtr credential);
    }
}
