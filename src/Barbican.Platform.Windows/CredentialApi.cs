// Mirrors: Win32 wincred.h (CredReadW, CredWriteW, CredDeleteW, CredFree, CREDENTIALW, CRED_TYPE_GENERIC = 1, CRED_PERSIST_LOCAL_MACHINE = 2,
// ERROR_NOT_FOUND = 1168); spikes/s5-credentials/read-format.ps1 used the same calls. Hand-written LibraryImport declarations instead of
// CsWin32: four functions and one struct, with no generator version to track (FABLE-REVIEW "M1 stream: accounts").

using System.Runtime.InteropServices;

namespace Barbican.Platform.Windows;

/// <summary>The Credential Manager calls.</summary>
internal static unsafe partial class CredentialApi
{
    public const uint TypeGeneric = 1;
    public const uint PersistLocalMachine = 2;
    public const int ErrorNotFound = 1168;

    /// <summary>The largest blob a generic credential holds (CRED_MAX_CREDENTIAL_BLOB_SIZE: 5 * 512 bytes).</summary>
    public const int MaxBlob = 5 * 512;

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CredRead(string target, uint type, uint flags, out Credential* credential);

    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CredWrite(Credential* credential, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CredDelete(string target, uint type, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredFree")]
    public static partial void CredFree(void* buffer);

    /// <summary>CREDENTIALW.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Credential
    {
        public uint Flags;
        public uint Type;
        public char* TargetName;
        public char* Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public byte* CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public void* Attributes;
        public char* TargetAlias;
        public char* UserName;
    }
}
