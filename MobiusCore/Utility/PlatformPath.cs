//
// Copyright 2020 Electronic Arts Inc.
//
// The Command & Conquer Map Editor and corresponding source code is free
// software: you can redistribute it and/or modify it under the terms of
// the GNU General Public License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
//
// The Command & Conquer Map Editor and corresponding source code is distributed
// in the hope that it will be useful, but with permitted additional restrictions
// under Section 7 of the GPL. See the GNU General Public License in LICENSE.TXT
// distributed with this program. You should have received a copy of the
// GNU General Public License along with permitted additional restrictions
// with this program. If not, see https://github.com/electronicarts/CnC_Remastered_Collection
using System;
using System.IO;

namespace MobiusEditor.Utility
{
    /// <summary>
    /// Maps Windows-style paths onto case-sensitive filesystems: separators are normalised
    /// and each component is resolved case-insensitively against the disk. On Windows this
    /// is a pass-through. Only use on paths meant for the local filesystem, never on
    /// archive-internal (MEG/MIX) names.
    /// </summary>
    public static class PlatformPath
    {
        private static readonly bool isWindows = Path.DirectorySeparatorChar == '\\';

        /// <summary>
        /// Returns a path that exists on disk matching the given path case-insensitively,
        /// or the separator-normalised input if no match exists.
        /// </summary>
        public static string Resolve(string path)
        {
            if (isWindows || String.IsNullOrEmpty(path))
            {
                return path;
            }
            string p = path.Replace('\\', '/');
            if (File.Exists(p) || Directory.Exists(p))
            {
                return p;
            }
            string full;
            try
            {
                full = Path.GetFullPath(p);
            }
            catch (Exception)
            {
                return p;
            }
            string current = "/";
            foreach (string part in full.Split('/'))
            {
                if (part.Length == 0)
                {
                    continue;
                }
                string candidate = Path.Combine(current, part);
                if (!File.Exists(candidate) && !Directory.Exists(candidate))
                {
                    string match = null;
                    try
                    {
                        foreach (string entry in Directory.EnumerateFileSystemEntries(current))
                        {
                            string name = Path.GetFileName(entry);
                            if (String.Equals(name, part, StringComparison.OrdinalIgnoreCase))
                            {
                                match = name;
                                break;
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // Unreadable directory; fall through to no-match.
                    }
                    if (match == null)
                    {
                        return p;
                    }
                    candidate = Path.Combine(current, match);
                }
                current = candidate;
            }
            return current;
        }
    }
}
