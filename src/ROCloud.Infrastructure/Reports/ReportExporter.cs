using System.Globalization;
using System.Reflection;
using ClosedXML.Excel;
using CsvHelper;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Infrastructure.Reports;

/// <summary>Serialises report rows to CSV (CsvHelper) or XLSX (ClosedXML) (guide §12).</summary>
public class ReportExporter : IReportExporter
{
    public byte[] ToCsv<T>(IEnumerable<T> rows)
    {
        using var ms = new MemoryStream();
        using (var writer = new StreamWriter(ms, leaveOpen: true))
        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            csv.WriteRecords(rows);
        }
        return ms.ToArray();
    }

    public byte[] ToXlsx<T>(IEnumerable<T> rows, string sheetName)
    {
        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(Sanitize(sheetName));

        for (var c = 0; c < props.Length; c++)
            sheet.Cell(1, c + 1).Value = props[c].Name;

        var row = 2;
        foreach (var item in rows)
        {
            for (var c = 0; c < props.Length; c++)
                sheet.Cell(row, c + 1).Value = ToCellValue(props[c].GetValue(item));
            row++;
        }

        sheet.Row(1).Style.Font.Bold = true;
        sheet.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>Maps a CLR value to a type ClosedXML understands (numbers/bool/DateTime), else text.</summary>
    private static XLCellValue ToCellValue(object? value) => value switch
    {
        null => Blank.Value,
        bool b => b,
        DateOnly d => d.ToDateTime(TimeOnly.MinValue),
        DateTime dt => dt,
        decimal m => m,
        double db => db,
        float f => f,
        int i => i,
        long l => l,
        short s => s,
        _ => value.ToString() ?? string.Empty
    };

    /// <summary>Excel sheet names cannot exceed 31 chars or contain : \ / ? * [ ].</summary>
    private static string Sanitize(string name)
    {
        var cleaned = new string(name.Where(ch => !"\\/?*[]:".Contains(ch)).ToArray());
        return cleaned.Length <= 31 ? cleaned : cleaned[..31];
    }
}
