using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using MobiusEditor.Interface;

namespace MobiusEditor.Utility
{
    /// <summary>
    /// A map's TILEPATCHES, as the launcher reads them: DATA\XML\TILEPATCHES\MAPS\&lt;game&gt;\&lt;key&gt;_MAPPATCHES.XML
    /// places named patches at positions relative to the map bounds' top-left; each patch's _PATCH.XML lists
    /// one texture per cell under DATA\ART\TEXTURES\SRGB\&lt;DirectoryPath&gt;. The key is the map file's stem.
    /// </summary>
    public static class TilePatchIndex
    {
        private const string MapsRoot = "DATA\\XML\\TILEPATCHES\\MAPS\\";
        private const string PatchesRoot = "DATA\\XML\\TILEPATCHES\\PATCHES\\";
        private const string TexturesRoot = "DATA\\ART\\TEXTURES\\SRGB\\";

        /// <summary>Bounds-relative cell → texture path, for every patched cell whose first frame exists; empty when the map has no index.</summary>
        public static Dictionary<Point, string> Load(IArchiveManager archives, string gameFolder, string mapKey)
        {
            Dictionary<Point, string> cells = new Dictionary<Point, string>();
            XElement index = ReadXml(archives, MapsRoot + gameFolder + "\\" + mapKey.ToUpperInvariant() + "_MAPPATCHES.XML");
            if (index == null)
            {
                return cells;
            }
            foreach (XElement entry in index.Descendants("PatchPositions").Elements("Entry"))
            {
                string patchName = (string)entry.Element("PatchName");
                if (patchName == null || !int.TryParse((string)entry.Element("X"), out int px) || !int.TryParse((string)entry.Element("Y"), out int py))
                {
                    continue;
                }
                XElement patch = ReadXml(archives, PatchesRoot + patchName.ToUpperInvariant() + "_PATCH.XML");
                string directory = (string)patch?.Descendants("DirectoryPath").FirstOrDefault();
                if (directory == null)
                {
                    continue;
                }
                foreach (XElement tile in patch.Descendants("Tiles").Elements("Entry"))
                {
                    string texture = (string)tile.Element("TextureName");
                    if (texture == null || !int.TryParse((string)tile.Element("X"), out int tx) || !int.TryParse((string)tile.Element("Y"), out int ty))
                    {
                        continue;
                    }
                    string path = TexturesRoot + directory.TrimEnd('\\') + "\\" + texture + "-00.DDS";
                    if (archives.FileExists(path))
                    {
                        cells[new Point(px + tx, py + ty)] = path;
                    }
                }
            }
            return cells;
        }

        private static XElement ReadXml(IArchiveManager archives, string path)
        {
            if (!archives.FileExists(path))
            {
                return null;
            }
            using (Stream stream = archives.OpenFile(path))
            {
                try
                {
                    return stream == null ? null : XDocument.Load(stream).Root;
                }
                catch (System.Xml.XmlException)
                {
                    return null;
                }
            }
        }
    }
}
