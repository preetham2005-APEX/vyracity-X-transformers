using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace VeyraCity.World.Data
{
    /// <summary>
    /// A parsed CSV file: header plus rows, with typed, header-driven accessors that fail loudly
    /// (FormatException naming the file, line and column) instead of returning silent defaults.
    /// </summary>
    public sealed class CsvTable
    {
        public readonly string Source;
        public readonly string[] Header;
        public readonly List<string[]> Rows;
        /// <summary>1-based line number in the file of each row (rows can span lines inside quotes).</summary>
        public readonly List<int> RowLines;
        readonly Dictionary<string, int> index = new Dictionary<string, int>(StringComparer.Ordinal);

        public CsvTable(string source, string[] header, List<string[]> rows, List<int> rowLines)
        {
            Source = source; Header = header; Rows = rows; RowLines = rowLines;
            for (int i = 0; i < header.Length; i++)
            {
                if (index.ContainsKey(header[i])) throw new FormatException($"{source}: duplicate column '{header[i]}'");
                index[header[i]] = i;
            }
        }

        public bool Has(string column) => index.ContainsKey(column);
        public int Count => Rows.Count;

        /// <summary>Throws unless every named column exists.</summary>
        public void Require(params string[] columns)
        {
            var missing = new List<string>();
            foreach (var c in columns) if (!index.ContainsKey(c)) missing.Add(c);
            if (missing.Count > 0) throw new FormatException($"{Source}: missing column(s) {string.Join(", ", missing)}; header is [{string.Join(", ", Header)}]");
        }

        public string Str(int row, string column)
        {
            if (!index.TryGetValue(column, out int c)) throw new FormatException($"{Source}: no column '{column}'");
            return Rows[row][c];
        }

        /// <summary>Optional string: null when the column does not exist.</summary>
        public string StrOpt(int row, string column) => index.TryGetValue(column, out int c) ? Rows[row][c] : null;

        public float Float(int row, string column)
        {
            string s = Str(row, column);
            if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) || float.IsNaN(v) || float.IsInfinity(v))
                throw new FormatException($"{Source} line {RowLines[row]}: column '{column}' is not a number: '{s}'");
            return v;
        }

        /// <summary>Optional number: <paramref name="fallback"/> when the column is missing or the cell is empty.</summary>
        public float FloatOpt(int row, string column, float fallback)
        {
            if (!index.ContainsKey(column) || Str(row, column).Length == 0) return fallback;
            return Float(row, column);
        }

        public int Int(int row, string column)
        {
            string s = Str(row, column);
            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                throw new FormatException($"{Source} line {RowLines[row]}: column '{column}' is not an integer: '{s}'");
            return v;
        }
    }

    /// <summary>RFC 4180 CSV reader: quoted fields with commas, doubled quotes, CR/LF/CRLF line ends, a leading BOM, newlines inside quotes.</summary>
    public static class VeyraCsv
    {
        public static CsvTable Parse(string text, string source)
        {
            if (text == null) throw new ArgumentNullException(nameof(text), $"{source}: no text");
            var records = ReadRecords(text, source, out var lines);
            if (records.Count == 0) throw new FormatException($"{source}: empty file (no header)");
            var header = records[0];
            for (int i = 0; i < header.Length; i++) header[i] = header[i].Trim();
            var rows = new List<string[]>(records.Count - 1);
            var rowLines = new List<int>(records.Count - 1);
            for (int r = 1; r < records.Count; r++)
            {
                var rec = records[r];
                if (rec.Length == 1 && rec[0].Length == 0) continue; // blank line
                if (rec.Length != header.Length)
                    throw new FormatException($"{source} line {lines[r]}: {rec.Length} fields, header has {header.Length}");
                rows.Add(rec);
                rowLines.Add(lines[r]);
            }
            return new CsvTable(source, header, rows, rowLines);
        }

        static List<string[]> ReadRecords(string text, string source, out List<int> startLines)
        {
            var records = new List<string[]>();
            startLines = new List<int>();
            var fields = new List<string>();
            var sb = new StringBuilder();
            int i = 0, n = text.Length, line = 1, recordLine = 1;
            if (n > 0 && text[0] == '﻿') i = 1;
            bool inQuotes = false, fieldStarted = false, any = false;
            while (i < n)
            {
                char ch = text[i];
                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < n && text[i + 1] == '"') { sb.Append('"'); i += 2; continue; }
                        inQuotes = false; i++; continue;
                    }
                    if (ch == '\n') line++;
                    sb.Append(ch); i++; continue;
                }
                if (ch == '"' && !fieldStarted) { inQuotes = true; fieldStarted = true; any = true; i++; continue; }
                if (ch == ',') { fields.Add(sb.ToString()); sb.Clear(); fieldStarted = false; any = true; i++; continue; }
                if (ch == '\r' || ch == '\n')
                {
                    fields.Add(sb.ToString()); sb.Clear(); fieldStarted = false;
                    if (any || fields.Count > 1 || fields[0].Length > 0) { records.Add(fields.ToArray()); startLines.Add(recordLine); }
                    fields.Clear(); any = false;
                    if (ch == '\r' && i + 1 < n && text[i + 1] == '\n') i++;
                    i++; line++; recordLine = line; continue;
                }
                sb.Append(ch); fieldStarted = true; any = true; i++;
            }
            if (inQuotes) throw new FormatException($"{source}: unterminated quoted field starting on line {recordLine}");
            if (any || sb.Length > 0 || fields.Count > 0)
            {
                fields.Add(sb.ToString());
                records.Add(fields.ToArray());
                startLines.Add(recordLine);
            }
            return records;
        }
    }
}
