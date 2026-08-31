using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MobiusCore.Tests;
using MobiusEditor.Model;
using Xunit;

namespace MobiusEditor.Shell.Tests
{
    /// <summary>
    /// The palette search box narrows every palette to entries whose friendly label or INI
    /// name contains the query, case-insensitive. Group headers stay only while at least one
    /// of their entries survives; a blank query restores everything.
    /// </summary>
    public class PaletteFilterTests
    {
        private static MapDocument Open()
        {
            MapDocument doc = new MapDocument(EditorHost.Shared);
            doc.Open(Path.Combine(TestPaths.MapEdits, "scm05ea.ini"));
            return doc;
        }

        [Fact]
        public void BlankQueryMatchesEverything()
        {
            using (MapDocument doc = Open())
            {
                foreach (UnitType unit in doc.AvailableUnits())
                {
                    PaletteItem item = PaletteItem.From(unit);
                    Assert.True(PaletteFilter.Matches(item, null));
                    Assert.True(PaletteFilter.Matches(item, ""));
                    Assert.True(PaletteFilter.Matches(item, "   "));
                }
            }
        }

        [Fact]
        public void MatchesDisplayNameOrIniNameCaseInsensitive()
        {
            using (MapDocument doc = Open())
            {
                UnitType heavyTank = doc.AvailableUnits().First(u => u.Name == "3tnk");
                PaletteItem item = PaletteItem.From(heavyTank);
                // The label is the display name ("Heavy Tank"); the INI name is 3tnk.
                Assert.True(PaletteFilter.Matches(item, "heavy"));
                Assert.True(PaletteFilter.Matches(item, "HEAVY TANK"));
                Assert.True(PaletteFilter.Matches(item, "3TNK"));
                Assert.False(PaletteFilter.Matches(item, "obelisk"));

                TemplateType template = doc.AvailableTemplates().First();
                Assert.True(PaletteFilter.Matches(PaletteItem.From(template), template.Name.ToUpperInvariant()));
                Assert.False(PaletteFilter.Matches(PaletteItem.From(template), template.Name + "zzz"));
            }
        }

        [Fact]
        public void PlainStringRowsMatchBySubstring()
        {
            Assert.True(PaletteFilter.Matches("3: Waypoint 3", "way"));
            Assert.True(PaletteFilter.Matches("3: Waypoint 3", ""));
            Assert.False(PaletteFilter.Matches("3: Waypoint 3", "flare"));
        }

        [Fact]
        public void HeadersSurviveOnlyWithMatchingEntries()
        {
            List<string> rows = new List<string> { "#Fruit", "apple", "banana", "#Veg", "carrot", "#Empty" };
            Func<string, bool> isHeader = r => r.StartsWith("#");

            List<string> all = PaletteFilter.Apply(rows, isHeader, r => PaletteFilter.Matches(r, ""));
            Assert.Equal(new[] { "#Fruit", "apple", "banana", "#Veg", "carrot" }, all);

            List<string> onlyBanana = PaletteFilter.Apply(rows, isHeader, r => PaletteFilter.Matches(r, "ban"));
            Assert.Equal(new[] { "#Fruit", "banana" }, onlyBanana);

            List<string> onlyCarrot = PaletteFilter.Apply(rows, isHeader, r => PaletteFilter.Matches(r, "carrot"));
            Assert.Equal(new[] { "#Veg", "carrot" }, onlyCarrot);

            Assert.Empty(PaletteFilter.Apply(rows, isHeader, r => PaletteFilter.Matches(r, "zzz")));
        }
    }
}
