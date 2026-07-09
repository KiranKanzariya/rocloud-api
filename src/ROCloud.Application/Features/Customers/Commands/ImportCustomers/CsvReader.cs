namespace ROCloud.Application.Features.Customers.Commands.ImportCustomers;

/// <summary>
/// Minimal RFC-4180 CSV reader (no external dependency in the Application layer). Handles quoted
/// fields, escaped quotes (""), commas/newlines inside quotes, and CRLF/LF line endings. Returns one
/// dictionary per data row keyed by the lower-cased header names; missing cells are empty strings.
/// </summary>
internal static class CsvReader
{
    public static List<Dictionary<string, string>> Parse(string content)
    {
        var rows = SplitRecords(content);
        var result = new List<Dictionary<string, string>>();
        if (rows.Count == 0) return result;

        var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();

        for (var r = 1; r < rows.Count; r++)
        {
            var cells = rows[r];
            // Skip fully blank lines.
            if (cells.Count == 1 && string.IsNullOrWhiteSpace(cells[0])) continue;

            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var c = 0; c < header.Count; c++)
                dict[header[c]] = c < cells.Count ? cells[c].Trim() : string.Empty;
            result.Add(dict);
        }

        return result;
    }

    /// <summary>Splits the whole document into records, each a list of field strings.</summary>
    private static List<List<string>> SplitRecords(string content)
    {
        // Strip a UTF-8 BOM if present (Excel adds one).
        if (content.Length > 0 && content[0] == '﻿') content = content[1..];

        var records = new List<List<string>>();
        var fields = new List<string>();
        var field = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < content.Length; i++)
        {
            var ch = content[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(ch);
                continue;
            }

            switch (ch)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break; // handled with \n
                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    records.Add(fields);
                    fields = new List<string>();
                    break;
                default:
                    field.Append(ch);
                    break;
            }
        }

        // Flush the trailing field/record (file may not end with a newline).
        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            records.Add(fields);
        }

        return records;
    }
}
