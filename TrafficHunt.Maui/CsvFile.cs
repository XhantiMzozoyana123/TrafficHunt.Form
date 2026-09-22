namespace TrafficHunt.Maui
{
    using System.Text;

    /// <summary>
    /// Minimal RFC 4180 CSV read/write helpers. Commas, quotes, newlines and non-ASCII
    /// (comment text is full of them) are handled with standard quoting so an exported
    /// file can be round-tripped back through the import option.
    /// </summary>
    public static class CsvFile
    {
        private static readonly char[] LineBreaks = ['\r', '\n'];

        /// <summary>Writes rows (first row = header) as UTF-8 CSV, overwriting the file.</summary>
        public static async Task WriteAsync(string path, IReadOnlyList<IReadOnlyList<string?>> rows)
        {
            var builder = new StringBuilder();

            foreach (var row in rows)
            {
                var cells = row.Select(Escape).ToArray();
                builder.Append(string.Join(',', cells));
                builder.Append("\r\n");
            }

            await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }

        /// <summary>
        /// Reads a CSV file into rows. Blank lines are skipped; the caller treats the
        /// first row as the header.
        /// </summary>
        public static async Task<List<string[]>> ReadAsync(string path)
        {
            var text = await File.ReadAllTextAsync(path);
            var rows = new List<string[]>();
            var cells = new List<string>();
            var current = new StringBuilder();
            var inQuotes = false;
            var i = 0;

            void EndCell()
            {
                cells.Add(current.ToString());
                current.Clear();
            }

            void EndRow()
            {
                EndCell();

                // Skip completely blank lines but keep rows that are just empty cells.
                if (cells.Count > 1 || cells[0].Trim().Length > 0)
                {
                    rows.Add(cells.ToArray());
                }

                cells.Clear();
            }

            while (i < text.Length)
            {
                var c = text[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            current.Append('"');
                            i += 2;
                            continue;
                        }

                        inQuotes = false;
                    }
                    else
                    {
                        current.Append(c);
                    }

                    i++;
                    continue;
                }

                if (c == '"')
                {
                    inQuotes = true;
                    i++;
                }
                else if (c == ',')
                {
                    EndCell();
                    i++;
                }
                else if (c == '\r' || c == '\n')
                {
                    EndRow();
                    while (i < text.Length && Array.IndexOf(LineBreaks, text[i]) >= 0)
                    {
                        i++;
                    }
                }
                else
                {
                    current.Append(c);
                    i++;
                }
            }

            if (current.Length > 0 || cells.Count > 0)
            {
                EndRow();
            }

            return rows;
        }

        private static string Escape(string? value)
        {
            var text = value ?? string.Empty;

            if (text.Contains('"') || text.Contains(',') || text.Contains('\r') || text.Contains('\n'))
            {
                return $"\"{text.Replace("\"", "\"\"")}\"";
            }

            return text;
        }
    }
}