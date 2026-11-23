using System.Text;

namespace Runesgalder.Sandbox.Worker;

internal sealed class CsvTable : ITable
{
    private static readonly IReadOnlyList<IReadOnlyList<string?>> EmptyRows = [];

    private CsvTable(string name, IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        Name = name;
        Columns = columns;
        Rows = rows;
    }

    public string Name { get; }

    public IReadOnlyList<string> Columns { get; }

    public IReadOnlyList<IReadOnlyList<string?>> Rows { get; }

    public static CsvTable Parse(string name, string text, int maxRows)
    {
        var records = ReadRecords(text);
        if (records.Count == 0)
        {
            throw new CsvFormatException("The CSV file is empty.");
        }

        var columns = records[0];
        var rows = new List<IReadOnlyList<string?>>(Math.Min(records.Count - 1, maxRows));
        for (var i = 1; i < records.Count; i++)
        {
            if (rows.Count >= maxRows)
            {
                throw new CsvFormatException($"The CSV file has more than {maxRows} data rows.");
            }

            var record = records[i];
            if (record.Count != columns.Count)
            {
                throw new CsvFormatException(
                    $"Row {i + 1} has {record.Count} fields but the header has {columns.Count}.");
            }

            rows.Add(record.Select(value => value.Length == 0 ? null : (string?)value).ToArray());
        }

        return new CsvTable(name, columns, rows.Count == 0 ? EmptyRows : rows);
    }

    private static List<List<string>> ReadRecords(string text)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldStarted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"' when !fieldStarted:
                    inQuotes = true;
                    fieldStarted = true;
                    break;
                case ',':
                    record.Add(field.ToString());
                    field.Clear();
                    fieldStarted = false;
                    break;
                case '\r':
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    EndRecord(records, record, field);
                    fieldStarted = false;
                    break;
                case '\n':
                    EndRecord(records, record, field);
                    fieldStarted = false;
                    break;
                default:
                    field.Append(c);
                    fieldStarted = true;
                    break;
            }
        }

        if (inQuotes)
        {
            throw new CsvFormatException("The CSV file ends inside a quoted field.");
        }

        if (fieldStarted || field.Length > 0 || record.Count > 0)
        {
            EndRecord(records, record, field);
        }

        return records;
    }

    private static void EndRecord(List<List<string>> records, List<string> record, StringBuilder field)
    {
        record.Add(field.ToString());
        field.Clear();
        if (!(record.Count == 1 && record[0].Length == 0))
        {
            records.Add([.. record]);
        }

        record.Clear();
    }
}

internal sealed class CsvFormatException(string message, Exception? innerException = null)
    : Exception(message, innerException);
