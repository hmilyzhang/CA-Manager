using System.Runtime.InteropServices;

namespace CaMgr.Api.CaInterop;

/// <summary>
/// AD CS COM interop declarations. Interface GUIDs and vtable order extracted from
/// certadm.dll / certcli.dll type libraries (dscom tlbdump); constants verified against
/// CertCli.h / CertSrv.h / CertView.h (Windows SDK) and live behaviour on Windows Server 2025.
/// Method names are irrelevant to marshaling — only declaration ORDER matters (dual interfaces).
/// </summary>
internal static class CaConst
{
    // ---- ICertAdmin2::GetCAProperty property IDs (CertCli.h) ----
    public const int CR_PROP_FILEVERSION = 1;            // String
    public const int CR_PROP_PRODUCTVERSION = 2;         // String
    public const int CR_PROP_EXITCOUNT = 3;              // Long
    public const int CR_PROP_EXITDESCRIPTION = 4;        // String, Indexed
    public const int CR_PROP_POLICYDESCRIPTION = 5;      // String
    public const int CR_PROP_CANAME = 6;                 // String
    public const int CR_PROP_SANITIZEDCANAME = 7;        // String
    public const int CR_PROP_SHAREDFOLDER = 8;           // String
    public const int CR_PROP_PARENTCA = 9;               // String
    public const int CR_PROP_CATYPE = 10;                // Long
    public const int CR_PROP_CASIGCERTCOUNT = 11;        // Long
    public const int CR_PROP_CASIGCERT = 12;             // Binary, Indexed
    public const int CR_PROP_CASIGCERTCHAIN = 13;        // Binary, Indexed (PKCS7)
    public const int CR_PROP_CAXCHGCERTCOUNT = 14;       // Long
    public const int CR_PROP_CAXCHGCERT = 15;            // Binary, Indexed
    public const int CR_PROP_CAXCHGCHAIN = 16;           // Binary, Indexed (PKCS7)
    public const int CR_PROP_CACERTSTATE = 19;           // Long, Indexed
    public const int CR_PROP_CRLSTATE = 20;              // Long, Indexed
    public const int CR_PROP_DNSNAME = 22;               // String
    public const int CR_PROP_ROLESEPARATIONENABLED = 23; // Long
    public const int CR_PROP_KRACERTCOUNT = 25;          // Long
    public const int CR_PROP_KRACERT = 26;               // Binary, Indexed
    public const int CR_PROP_ADVANCEDSERVER = 28;        // Long
    public const int CR_PROP_TEMPLATES = 29;             // String (array: name, oid, name, oid, ...)
    public const int CR_PROP_BASECRL = 17;               // Binary, Indexed (PKCS7 with CRL)
    public const int CR_PROP_DELTACRL = 18;              // Binary, Indexed
    public const int CR_PROP_BASECRLPUBLISHSTATUS = 30;  // Long, Indexed
    public const int CR_PROP_DELTACRLPUBLISHSTATUS = 31; // Long, Indexed
    public const int CR_PROP_CASIGCERTCRLCHAIN = 32;     // Binary, Indexed
    public const int CR_PROP_CACERTSTATUSCODE = 34;      // Long, Indexed
    public const int CR_PROP_CERTCDPURLS = 41;           // String, Indexed
    public const int CR_PROP_CERTAIAURLS = 42;           // String, Indexed

    // ---- property types ----
    public const int PROPTYPE_LONG = 1;
    public const int PROPTYPE_DATE = 2;
    public const int PROPTYPE_BINARY = 3;
    public const int PROPTYPE_STRING = 4;

    // ---- CA_DISP (CR_PROP_CRLSTATE values) ----
    public const int CA_DISP_VALID = 0;
    public const int CA_DISP_REVOKED = 1;
    public const int CA_DISP_INVALID = 2;
    public const int CA_DISP_ERROR = 3;

    // ---- database dispositions (CertSrv.h DB_DISP_*) ----
    public const int DB_DISP_ACTIVE = 8;        // queue: being processed
    public const int DB_DISP_PENDING = 9;       // queue: taken under submission
    public const int DB_DISP_FOREIGN = 12;      // log: archived foreign cert
    public const int DB_DISP_CA_CERT = 15;      // log: CA cert
    public const int DB_DISP_CA_CERT_CHAIN = 16;
    public const int DB_DISP_KRA_CERT = 17;
    public const int DB_DISP_ISSUED = 20;       // log: issued
    public const int DB_DISP_REVOKED = 21;      // log: issued and revoked
    public const int DB_DISP_ERROR = 30;        // failed log
    public const int DB_DISP_DENIED = 31;       // failed log

    // ---- request submission dispositions (CR_DISP_*) ----
    public const int CR_DISP_INCOMPLETE = 0;
    public const int CR_DISP_ERROR = 1;
    public const int CR_DISP_DENIED = 2;
    public const int CR_DISP_ISSUED = 3;
    public const int CR_DISP_ISSUED_OUT_OF_BAND = 4;
    public const int CR_DISP_UNDER_SUBMISSION = 5;
    public const int CR_DISP_REVOKED = 6;

    // ---- revocation reasons ----
    public const int CRL_REASON_UNSPECIFIED = 0;
    public const int CRL_REASON_KEY_COMPROMISE = 1;
    public const int CRL_REASON_CA_COMPROMISE = 2;
    public const int CRL_REASON_AFFILIATION_CHANGED = 3;
    public const int CRL_REASON_SUPERSEDED = 4;
    public const int CRL_REASON_CESSATION_OF_OPERATION = 5;
    public const int CRL_REASON_CERTIFICATE_HOLD = 6;
    public const int CRL_REASON_REMOVE_FROM_CRL = 8;
    public const int CRL_REASON_UNREVOKE = unchecked((int)0xFFFFFFFF);

    // ---- ICertView seek/sort (CertView.h) ----
    public const int CVR_SEEK_NONE = 0;
    public const int CVR_SEEK_EQ = 1;
    public const int CVR_SEEK_LT = 2;
    public const int CVR_SEEK_LE = 4;
    public const int CVR_SEEK_GE = 8;
    public const int CVR_SEEK_GT = 16;
    public const int CVR_SORT_NONE = 0;
    public const int CVR_SORT_ASCEND = 1;
    public const int CVR_SORT_DESCEND = 2;

    // ---- GetValue output flags (CertView.h) ----
    public const int CV_OUT_BASE64HEADER = 0;
    public const int CV_OUT_BASE64 = 1;
    public const int CV_OUT_BINARY = 2;

    // ---- ICertGetConfig / schema selectors ----
    public const int CC_DEFAULTCONFIG = 0;
    public const int CVRC_COLUMN_SCHEMA = 0;
    public const int CVRC_TABLE_REQCERT = 0;

    // ---- ICertRequest2::Submit flags ----
    public const int CR_IN_BASE64HEADER = 0;
    public const int CR_IN_BASE64 = 1;
    public const int CR_IN_BINARY = 2;
    public const int CR_IN_ENCODEANY = 0xff;
    public const int CR_IN_PKCS10 = 0x100;
    public const int CR_IN_KEYGEN = 0x200;
    public const int CR_IN_PKCS7 = 0x300;

    // ---- ICertRequest2::GetCertificate flags ----
    public const int CR_OUT_BASE64HEADER = 0;
    public const int CR_OUT_BASE64 = 1;
    public const int CR_OUT_BINARY = 2;
    public const int CR_OUT_CHAIN = 0x100;

    // ---- PublishCRLs flags ----
    public const int CA_CRL_BASE = 0x1;
    public const int CA_CRL_DELTA = 0x2;

    // ---- common CA config registry entries (GetConfigEntry/SetConfigEntry node "") ----
    public const string EntryCRLPeriodUnits = "CRLPeriodUnits";
    public const string EntryCRLPeriod = "CRLPeriod";
    public const string EntryCRLDeltaPeriodUnits = "CRLDeltaPeriodUnits";
    public const string EntryCRLDeltaPeriod = "CRLDeltaPeriod";
}

#region coclasses

[ComImport, Guid("37eabaf0-7fb6-11d0-8817-00a0c903b83c")]
internal class CCertAdmin { }

[ComImport, Guid("a12d0f7a-1e84-11d1-9bd6-00c04fb683fa")]
internal class CCertView { }

[ComImport, Guid("98aff3f0-5524-11d0-8812-00a0c903b83c")]
internal class CCertRequest { }

[ComImport, Guid("c6cc49b0-ce17-11d0-8833-00a0c903b83c")]
internal class CCertGetConfig { }

#endregion

#region interfaces

/// <summary>ICertAdmin2 — vtable order from certadm.dll typelib.</summary>
[ComImport,
 Guid("f7c3ac41-b8ce-4fb4-aa58-3d1dc0e36b39"),
 InterfaceType(ComInterfaceType.InterfaceIsDual)]
public interface ICertAdmin2
{
    [return: MarshalAs(UnmanagedType.I4)]
    int IsValidCertificate([MarshalAs(UnmanagedType.BStr)] string strConfig, [MarshalAs(UnmanagedType.BStr)] string strSerialNumber);

    [return: MarshalAs(UnmanagedType.I4)]
    int GetRevocationReason([MarshalAs(UnmanagedType.BStr)] string strConfig, [MarshalAs(UnmanagedType.BStr)] string strSerialNumber);

    void RevokeCertificate([MarshalAs(UnmanagedType.BStr)] string strConfig,
                           [MarshalAs(UnmanagedType.BStr)] string strSerialNumber,
                           int Reason,
                           double Date);

    void SetRequestAttributes([MarshalAs(UnmanagedType.BStr)] string strConfig, int RequestId, [MarshalAs(UnmanagedType.BStr)] string strAttributes);

    void SetCertificateExtension([MarshalAs(UnmanagedType.BStr)] string strConfig, int RequestId,
        [MarshalAs(UnmanagedType.BStr)] string strExtensionName, int Type, int Flags, [In] ref object pvarValue);

    void DenyRequest([MarshalAs(UnmanagedType.BStr)] string strConfig, int RequestId);

    [return: MarshalAs(UnmanagedType.I4)]
    int ResubmitRequest([MarshalAs(UnmanagedType.BStr)] string strConfig, int RequestId);

    void PublishCRL([MarshalAs(UnmanagedType.BStr)] string strConfig, double Date);

    [return: MarshalAs(UnmanagedType.BStr)]
    string GetCRL([MarshalAs(UnmanagedType.BStr)] string strConfig, int Flags);

    [return: MarshalAs(UnmanagedType.BStr)]
    string ImportCertificate([MarshalAs(UnmanagedType.BStr)] string strConfig, [MarshalAs(UnmanagedType.BStr)] string strCertificate, int Flags);

    void PublishCRLs([MarshalAs(UnmanagedType.BStr)] string strConfig, double Date, int CRLFlags);

    [return: MarshalAs(UnmanagedType.Struct)]
    object GetCAProperty([MarshalAs(UnmanagedType.BStr)] string strConfig, int PropId, int PropIndex, int PropType, int Flags);

    void SetCAProperty([MarshalAs(UnmanagedType.BStr)] string strConfig, int PropId, int PropIndex, int PropType, [In] ref object pvarPropertyValue);

    [return: MarshalAs(UnmanagedType.I4)]
    int GetCAPropertyFlags([MarshalAs(UnmanagedType.BStr)] string strConfig, int PropId);

    [return: MarshalAs(UnmanagedType.BStr)]
    string GetCAPropertyDisplayName([MarshalAs(UnmanagedType.BStr)] string strConfig, int PropId);

    [return: MarshalAs(UnmanagedType.Struct)]
    object GetArchivedKey([MarshalAs(UnmanagedType.BStr)] string strConfig, int RequestId, int Flags);

    [return: MarshalAs(UnmanagedType.Struct)]
    object GetConfigEntry([MarshalAs(UnmanagedType.BStr)] string strConfig, [MarshalAs(UnmanagedType.BStr)] string strNodePath, [MarshalAs(UnmanagedType.BStr)] string strEntryName);

    void SetConfigEntry([MarshalAs(UnmanagedType.BStr)] string strConfig, [MarshalAs(UnmanagedType.BStr)] string strNodePath,
        [MarshalAs(UnmanagedType.BStr)] string strEntryName, [In] ref object pvarEntry);

    void ImportKey([MarshalAs(UnmanagedType.BStr)] string strConfig, [MarshalAs(UnmanagedType.BStr)] string strCertificate);

    [return: MarshalAs(UnmanagedType.I4)]
    int GetMyRoles([MarshalAs(UnmanagedType.BStr)] string strConfig);

    void DeleteRow([MarshalAs(UnmanagedType.BStr)] string strConfig, int Flags, int Table, int RequestId, [MarshalAs(UnmanagedType.BStr)] string strSerial, DateTime Date);
}

/// <summary>ICertView2 — vtable order from certadm.dll typelib.</summary>
[ComImport,
 Guid("d594b282-8851-4b61-9c66-3edadf848863"),
 InterfaceType(ComInterfaceType.InterfaceIsDual)]
public interface ICertView2
{
    void OpenConnection([MarshalAs(UnmanagedType.BStr)] string strConfig);

    [return: MarshalAs(UnmanagedType.Interface)]
    IEnumCERTVIEWCOLUMN EnumCertViewColumn(int fResultColumn);

    [return: MarshalAs(UnmanagedType.I4)]
    int GetColumnCount(int fResultColumn);

    [return: MarshalAs(UnmanagedType.I4)]
    int GetColumnIndex(int fResultColumn, [MarshalAs(UnmanagedType.BStr)] string strColumnName);

    void SetResultColumnCount(int cResultColumn);

    void SetResultColumn(int ColumnIndex);

    void SetRestriction(int ColumnIndex, int SeekOperator, int SortOrder, [In] ref object pvarValue);

    [return: MarshalAs(UnmanagedType.Interface)]
    IEnumCERTVIEWROW OpenView();

    void SetTable(int Table);
}

[ComImport,
 Guid("d1157f4c-5af2-11d1-9bdc-00c04fb683fa"),
 InterfaceType(ComInterfaceType.InterfaceIsDual)]
public interface IEnumCERTVIEWROW
{
    [return: MarshalAs(UnmanagedType.I4)]
    int Next();

    [return: MarshalAs(UnmanagedType.Interface)]
    IEnumCERTVIEWCOLUMN EnumCertViewColumn();

    [return: MarshalAs(UnmanagedType.Interface)]
    object EnumCertViewAttribute(int Flags);

    [return: MarshalAs(UnmanagedType.Interface)]
    object EnumCertViewExtension(int Flags);

    void Skip(int celt);

    void Reset();

    [return: MarshalAs(UnmanagedType.Interface)]
    IEnumCERTVIEWROW Clone();

    [return: MarshalAs(UnmanagedType.I4)]
    int GetMaxIndex();
}

[ComImport,
 Guid("9c735be2-57a5-11d1-9bdb-00c04fb683fa"),
 InterfaceType(ComInterfaceType.InterfaceIsDual)]
public interface IEnumCERTVIEWCOLUMN
{
    [return: MarshalAs(UnmanagedType.I4)]
    int Next();

    [return: MarshalAs(UnmanagedType.BStr)]
    string GetName();

    [return: MarshalAs(UnmanagedType.BStr)]
    string GetDisplayName();

    // named GetColumnType: vtable slot is what matters, name must not collide with object.GetType
    [return: MarshalAs(UnmanagedType.I4)]
    int GetColumnType();

    [return: MarshalAs(UnmanagedType.I4)]
    int IsIndexed();

    [return: MarshalAs(UnmanagedType.I4)]
    int GetMaxLength();

    [return: MarshalAs(UnmanagedType.Struct)]
    object GetValue(int Flags);

    void Skip(int celt);

    void Reset();

    [return: MarshalAs(UnmanagedType.Interface)]
    IEnumCERTVIEWCOLUMN Clone();
}

/// <summary>ICertRequest2 — vtable order from certcli.dll typelib.</summary>
[ComImport,
 Guid("a4772988-4a85-4fa9-824e-b5cf5c16405a"),
 InterfaceType(ComInterfaceType.InterfaceIsDual)]
public interface ICertRequest2
{
    [return: MarshalAs(UnmanagedType.I4)]
    int Submit(int Flags,
               [MarshalAs(UnmanagedType.BStr)] string strRequest,
               [MarshalAs(UnmanagedType.BStr)] string strAttributes,
               [MarshalAs(UnmanagedType.BStr)] string strConfig);

    [return: MarshalAs(UnmanagedType.I4)]
    int RetrievePending(int RequestId, [MarshalAs(UnmanagedType.BStr)] string strConfig);

    [return: MarshalAs(UnmanagedType.I4)]
    int GetLastStatus();

    [return: MarshalAs(UnmanagedType.I4)]
    int GetRequestId();

    [return: MarshalAs(UnmanagedType.BStr)]
    string GetDispositionMessage();

    [return: MarshalAs(UnmanagedType.BStr)]
    string GetCACertificate(int fExchangeCertificate, [MarshalAs(UnmanagedType.BStr)] string strConfig, int Flags);

    [return: MarshalAs(UnmanagedType.BStr)]
    string GetCertificate(int Flags);

    [return: MarshalAs(UnmanagedType.BStr)]
    string GetIssuedCertificate([MarshalAs(UnmanagedType.BStr)] string strConfig, int RequestId, [MarshalAs(UnmanagedType.BStr)] string strSerialNumber);

    [return: MarshalAs(UnmanagedType.BStr)]
    string GetErrorMessageText(int hrMessage, int LocaleId);

    [return: MarshalAs(UnmanagedType.Struct)]
    object GetCAProperty([MarshalAs(UnmanagedType.BStr)] string strConfig, int PropId, int PropIndex, int PropType, int Flags);

    [return: MarshalAs(UnmanagedType.I4)]
    int GetCAPropertyFlags([MarshalAs(UnmanagedType.BStr)] string strConfig, int PropId);

    [return: MarshalAs(UnmanagedType.BStr)]
    string GetCAPropertyDisplayName([MarshalAs(UnmanagedType.BStr)] string strConfig, int PropId);

    [return: MarshalAs(UnmanagedType.Struct)]
    object GetFullResponseProperty(int PropId, int PropIndex, int PropType, int Flags);
}

[ComImport,
 Guid("c7ea09c0-ce17-11d0-8833-00a0c903b83c"),
 InterfaceType(ComInterfaceType.InterfaceIsDual)]
public interface ICertGetConfig
{
    [return: MarshalAs(UnmanagedType.BStr)]
    string GetConfig(int Flags);
}

#endregion
