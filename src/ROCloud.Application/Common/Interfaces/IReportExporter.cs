namespace ROCloud.Application.Common.Interfaces;

/// <summary>Serialises a report row set to CSV or XLSX for download (guide §12).</summary>
public interface IReportExporter
{
    byte[] ToCsv<T>(IEnumerable<T> rows);
    byte[] ToXlsx<T>(IEnumerable<T> rows, string sheetName);
}
