using System.Runtime.InteropServices;
using CaMgr.Api.CaInterop;

namespace CaMgr.Api.Services;

public sealed record ViewRestriction(string Column, int SeekOp, object? Value);

/// <summary>Well-known CA database column names (certsrv DB schema).</summary>
public static class DbCol
{
    public const string RequestId = "Request.RequestID";
    public const string Disposition = "Request.Disposition";
    public const string CommonName = "Request.CommonName";
    public const string RequesterName = "Request.RequesterName";
    public const string SubmittedWhen = "Request.SubmittedWhen";
    public const string ResolvedWhen = "Request.ResolvedWhen";
    public const string DispositionMessage = "Request.DispositionMessage";
    public const string SerialNumber = "SerialNumber";
    public const string RawCertificate = "RawCertificate";
    public const string RawRequest = "Request.RawRequest";
    public const string NotBefore = "NotBefore";
    public const string NotAfter = "NotAfter";
    public const string CertificateTemplate = "CertificateTemplate";
    public const string RevokedWhen = "Request.RevokedEffectiveWhen";
    public const string RevokedReason = "Request.RevokedReason";
    public const string CallerName = "Request.CallerName";
    public const string Upn = "Request.UserPrincipalName";
}

public sealed class CaRow
{
    public int RequestId { get; set; }
    public Dictionary<string, object?> Values { get; set; } = new();
}

/// <summary>
/// Wraps ICertView2 to run queries against the CA database with typed rows.
/// All COM work executes on the dedicated STA scheduler.
/// </summary>
public sealed class CaDbService(CaContext ca, StaComScheduler sta, ILogger<CaDbService> log)
{
    public Task<List<CaRow>> QueryAsync(
        IReadOnlyList<string> columns,
        IEnumerable<ViewRestriction>? restrictions = null,
        int maxRows = 200,
        bool ascending = false,
        Func<CaRow, bool>? postFilter = null)
        => sta.InvokeAsync(() => Query(columns, restrictions, maxRows, ascending, postFilter));

    public async Task<CaRow?> GetRowAsync(int requestId, IReadOnlyList<string> columns)
    {
        var rows = await QueryAsync(columns, [new ViewRestriction(DbCol.RequestId, CaConst.CVR_SEEK_EQ, requestId)], 1);
        return rows.FirstOrDefault();
    }

    private List<CaRow> Query(
        IReadOnlyList<string> columns,
        IEnumerable<ViewRestriction>? restrictions,
        int maxRows,
        bool ascending,
        Func<CaRow, bool>? postFilter)
    {
        var view = ca.CreateView();
        var result = new List<CaRow>(Math.Min(maxRows, 4096));
        try
        {
            view.OpenConnection(ca.Config);

            var idx = columns.Select(c => (name: c, idx: Col(view, c))).ToList();

            view.SetResultColumnCount(idx.Count);
            foreach (var (_, i) in idx) view.SetResultColumn(i);

            var ridIdx = -1;
            try { ridIdx = view.GetColumnIndex(CaConst.CVRC_COLUMN_SCHEMA, DbCol.RequestId); } catch { /* never missing */ }

            var appliedAny = false;
            if (restrictions is not null)
            {
                foreach (var r in restrictions)
                {
                    var c = Col(view, r.Column);
                    int sort = CaConst.CVR_SORT_NONE;
                    if (c == ridIdx) sort = ascending ? CaConst.CVR_SORT_ASCEND : CaConst.CVR_SORT_DESCEND;
                    if (r.Value is null)
                    {
                        var nullVal = (object?)null;
                        view.SetRestriction(c, r.SeekOp, sort, ref nullVal);
                    }
                    else
                    {
                        var v = r.Value is DateTime dt ? dt : r.Value;
                        view.SetRestriction(c, r.SeekOp, sort, ref v);
                    }
                    appliedAny = true;
                }
            }
            if (ridIdx >= 0 && !appliedAny)
            {
                // queries without any restriction misbehave; always pin a deterministic order
                var one = (object?)1;
                view.SetRestriction(ridIdx, CaConst.CVR_SEEK_GE, ascending ? CaConst.CVR_SORT_ASCEND : CaConst.CVR_SORT_DESCEND, ref one);
            }

            var rows = view.OpenView();
            IEnumCERTVIEWCOLUMN? colsEnum = null;
            try
            {
                // row.Next() returns the 1-based row index; 0 means enumeration finished
                while (result.Count < maxRows)
                {
                    int haveRow;
                    try { haveRow = rows.Next(); }
                    catch (COMException ex)
                    {
                        log.LogError(ex, "Row enumeration failed");
                        break;
                    }
                    if (haveRow <= 0) break;

                    var row = new CaRow();
                    colsEnum = rows.EnumCertViewColumn();
                    // column.Next() returns the 0-based column index; -1 means enumeration finished
                    while (true)
                    {
                        int colIdx;
                        try { colIdx = colsEnum.Next(); }
                        catch (COMException) { break; }
                        if (colIdx < 0) break;
                        var name = colsEnum.GetName();
                        object? value;
                        try { value = colsEnum.GetValue(CaConst.CV_OUT_BASE64); }
                        catch { value = null; }
                        if (string.IsNullOrEmpty(name)) continue;
                        if (name.Equals(DbCol.RequestId, StringComparison.OrdinalIgnoreCase) && value is not null)
                            row.RequestId = Convert.ToInt32(value);
                        row.Values[name] = Normalize(value);
                    }
                    // every enumerator holds a CA database session — must release before the next row
                    Marshal.FinalReleaseComObject(colsEnum);
                    colsEnum = null;
                    if (postFilter is null || postFilter(row)) result.Add(row);
                }
            }
            finally
            {
                if (colsEnum is not null)
                    try { Marshal.FinalReleaseComObject(colsEnum); } catch { }
                Marshal.FinalReleaseComObject(rows);
            }

            return result;
        }
        finally
        {
            Marshal.FinalReleaseComObject(view);
        }
    }

    private int Col(ICertView2 view, string column)
    {
        try
        {
            return view.GetColumnIndex(CaConst.CVRC_COLUMN_SCHEMA, column);
        }
        catch (COMException ex)
        {
            log.LogWarning(ex, "Column {Column} not found in CA schema", column);
            throw new InvalidOperationException($"CA 数据库不包含列 {column}");
        }
    }

    private static object? Normalize(object? v) => v switch
    {
        DateTime dt => DateTime.SpecifyKind(dt, DateTimeKind.Local),
        string s when s.Length == 0 => null,
        _ => v
    };

    public static string? Str(CaRow? row, string col) =>
        row is not null && row.Values.TryGetValue(col, out var v) ? v?.ToString() : null;

    public static int? Int(CaRow? row, string col) =>
        row is not null && row.Values.TryGetValue(col, out var v) && v is not null && v.GetType().IsPrimitive
            ? Convert.ToInt32(v) : null;

    public static DateTime? Date(CaRow? row, string col) =>
        row is not null && row.Values.TryGetValue(col, out var v) && v is DateTime dt ? dt : null;

    /// <summary>RawCertificate / RawRequest values come back base64-encoded (CV_OUT_BASE64).</summary>
    public static byte[]? Bytes(CaRow? row, string col)
    {
        if (row is null || !row.Values.TryGetValue(col, out var v) || v is not string b64 || b64.Length == 0) return null;
        try
        {
            var cleaned = b64.Replace("\r", "").Replace("\n", "")
                .Replace("-----BEGIN CERTIFICATE-----", "").Replace("-----END CERTIFICATE-----", "")
                .Replace("-----BEGIN NEW CERTIFICATE REQUEST-----", "").Replace("-----END NEW CERTIFICATE REQUEST-----", "")
                .Trim();
            return Convert.FromBase64String(cleaned);
        }
        catch { return null; }
    }
}
