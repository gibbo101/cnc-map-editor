//
// The palette search box: an entry matches when its friendly label or INI name contains
// the query, case-insensitive; a blank query matches everything. Apply keeps a group
// header only while at least one of its entries survives.
using System;
using System.Collections.Generic;

namespace MobiusEditor.Shell
{
    public static class PaletteFilter
    {
        public static bool Matches(PaletteItem item, string query)
        {
            if (item == null) return false;
            return Matches(item.Label, query) || Matches(item.IniName, query);
        }

        public static bool Matches(string text, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            return text != null && text.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public static List<T> Apply<T>(IReadOnlyList<T> rows, Func<T, bool> isHeader, Func<T, bool> matches)
        {
            List<T> result = new List<T>();
            foreach (T row in rows)
            {
                if (isHeader(row) || matches(row)) result.Add(row);
            }
            // A header still pending at the end gathered no entries; the same holds for one
            // directly followed by another header.
            for (int i = result.Count - 1; i >= 0; i--)
            {
                if (isHeader(result[i]) && (i == result.Count - 1 || isHeader(result[i + 1]))) result.RemoveAt(i);
            }
            return result;
        }
    }
}
