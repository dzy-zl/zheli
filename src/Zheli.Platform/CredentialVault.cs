using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
namespace Zheli.Platform;

public static class CredentialVault
{
    private const string Target="Zheli/DeepSeek";
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags,Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist,AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias,UserName;
    }
    [DllImport("advapi32.dll",EntryPoint="CredWriteW",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern bool Write(ref Credential credential,uint flags);
    [DllImport("advapi32.dll",EntryPoint="CredReadW",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern bool Read(string target,uint type,uint flags,out IntPtr ptr);
    [DllImport("advapi32.dll",EntryPoint="CredDeleteW",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern bool Delete(string target,uint type,uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr ptr);
    public static void Set(string secret)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (string.IsNullOrWhiteSpace(secret) || Encoding.Unicode.GetByteCount(secret)>2560) throw new ArgumentException("密钥为空或过长。");
        var blob=Marshal.StringToCoTaskMemUni(secret);
        try
        {
            var value=new Credential {Type=1,TargetName=Target,CredentialBlob=blob,CredentialBlobSize=(uint)Encoding.Unicode.GetByteCount(secret),Persist=2,UserName="DeepSeek"};
            if(!Write(ref value,0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { Marshal.ZeroFreeCoTaskMemUnicode(blob); }
    }
    public static string? Get()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if(!Read(Target,1,0,out var ptr))
        {
            var error=Marshal.GetLastWin32Error(); if(error==1168) return null;
            throw new Win32Exception(error);
        }
        try { var c=Marshal.PtrToStructure<Credential>(ptr); return Marshal.PtrToStringUni(c.CredentialBlob,(int)c.CredentialBlobSize/2); }
        finally { CredFree(ptr); }
    }
    public static void Remove()
    {
        if(!Delete(Target,1,0) && Marshal.GetLastWin32Error()!=1168) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
}
