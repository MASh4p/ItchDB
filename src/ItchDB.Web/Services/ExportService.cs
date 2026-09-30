using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.JSInterop;

namespace ItchDB.Web.Services;

public sealed record ExportColumn<T>(string Header, Func<T, object?> Value);

public enum ExportFormat
{
    Csv,
    Excel,
}

/// <summary>Downloads the rows a table currently shows (with the page's filters) as CSV or Excel.</summary>
public sealed class ExportService(IJSRuntime js)
{
    public async Task DownloadAsync<T>(string baseName, IEnumerable<T> rows, IReadOnlyList<ExportColumn<T>> columns, ExportFormat format)
    {
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmm", CultureInfo.InvariantCulture);
        var list = rows.ToList();
        byte[] bytes;
        string name, mime;
        if (format == ExportFormat.Csv)
        {
            bytes = Csv(list, columns);
            name = $"{baseName}_{stamp}.csv";
            mime = "text/csv";
        }
        else
        {
            bytes = Excel(baseName, list, columns);
            name = $"{baseName}_{stamp}.xlsx";
            mime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        }
        using var stream = new MemoryStream(bytes);
        using var reference = new DotNetStreamReference(stream);
        await js.InvokeVoidAsync("itchDownload", name, reference, mime);
    }

    /// <summary>Semicolon separated with a BOM: that is what Excel with Russian regional settings opens without an import wizard.</summary>
    private static byte[] Csv<T>(List<T> rows, IReadOnlyList<ExportColumn<T>> columns)
    {
        using var ms = new MemoryStream();
        using (var writer = new StreamWriter(ms, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
        using (var csv = new CsvWriter(writer, new CsvConfiguration(Fmt.Ru) { Delimiter = ";" }))
        {
            foreach (var c in columns) csv.WriteField(c.Header);
            csv.NextRecord();
            foreach (var row in rows)
            {
                foreach (var c in columns) csv.WriteField(Plain(c.Value(row)));
                csv.NextRecord();
            }
        }
        return ms.ToArray();
    }

    private static byte[] Excel<T>(string sheetName, List<T> rows, IReadOnlyList<ExportColumn<T>> columns)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(sheetName.Length > 31 ? sheetName[..31] : sheetName);
        for (var c = 0; c < columns.Count; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = columns[c].Header;
            cell.Style.Font.Bold = true;
        }
        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < columns.Count; c++)
            {
                var cell = ws.Cell(r + 2, c + 1);
                switch (Plain(columns[c].Value(rows[r])))
                {
                    case null: break;
                    case double d: cell.Value = d; break;
                    case int i: cell.Value = i; break;
                    case long l: cell.Value = l; break;
                    case decimal m: cell.Value = m; break;
                    case DateTime t: cell.Value = t; break;
                    case bool b: cell.Value = b ? "да" : "нет"; break;
                    case var other: cell.Value = other.ToString(); break;
                }
            }
        }
        if (rows.Count > 0) ws.Range(1, 1, rows.Count + 1, columns.Count).SetAutoFilter();
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(1, Math.Min(rows.Count + 1, 300));
        foreach (var col in ws.ColumnsUsed()) if (col.Width > 60) col.Width = 60;
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static object? Plain(object? v) => v switch
    {
        double d when !double.IsFinite(d) => null,
        DateTime t => Fmt.ToLocal(t),
        _ => v,
    };
}
