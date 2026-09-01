using System.IO;
using System.Linq;
using MobiusEditor.Headless;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using Xunit;
using Xunit.Abstractions;

namespace MobiusCore.Tests
{
    /// <summary>
    /// The shore catalog classifies RA's shore templates from their per-icon land data.
    /// The game's own equivalence groups pin the convention: sh04–sh07 are the
    /// water-to-the-south straights, sh25–sh28 water-to-the-north.
    /// </summary>
    public class ShoreCatalogTests
    {
        private readonly ITestOutputHelper output;
        public ShoreCatalogTests(ITestOutputHelper output) { this.output = output; }

        private static IGamePlugin Open()
        {
            return EditorHost.Shared.Load(Path.Combine(TestPaths.MapEdits, "scm05ea.ini"), out _);
        }

        [Fact]
        public void DumpClassifications()
        {
            IGamePlugin plugin = Open();
            foreach (ShorePiece piece in ShoreCatalog.Build(plugin.Map.TemplateTypes))
            {
                output.WriteLine(piece.ToString());
            }
        }

        [Fact]
        public void KnownStraightsClassifyByTheGamesOwnGroups()
        {
            IGamePlugin plugin = Open();
            var catalog = ShoreCatalog.Build(plugin.Map.TemplateTypes).ToDictionary(p => p.Template.Name);
            foreach (string name in new[] { "sh04", "sh05", "sh06", "sh07" })
            {
                Assert.Equal(ShoreKind.Straight, catalog[name].Kind);
                Assert.Equal(ShoreSide.South, catalog[name].WaterSide);
            }
            foreach (string name in new[] { "sh25", "sh26", "sh27", "sh28" })
            {
                Assert.Equal(ShoreKind.Straight, catalog[name].Kind);
                Assert.Equal(ShoreSide.North, catalog[name].WaterSide);
            }
            // sh16 is the clean west-water straight (W|B|C in every row).
            Assert.Equal(ShoreKind.Straight, catalog["sh16"].Kind);
            Assert.Equal(ShoreSide.West, catalog["sh16"].WaterSide);
        }

        [Fact]
        public void EveryDirectionHasAStraightAndEveryDiagonalARun()
        {
            IGamePlugin plugin = Open();
            var catalog = ShoreCatalog.Build(plugin.Map.TemplateTypes);
            foreach (ShoreSide side in new[] { ShoreSide.North, ShoreSide.South, ShoreSide.East, ShoreSide.West })
            {
                Assert.Contains(catalog, p => p.Kind == ShoreKind.Straight && p.WaterSide == side);
            }
            foreach (ShoreSide side in new[] { ShoreSide.NorthEast, ShoreSide.NorthWest, ShoreSide.SouthEast, ShoreSide.SouthWest })
            {
                Assert.Contains(catalog, p => p.Kind == ShoreKind.Diagonal && p.WaterSide == side);
            }
        }
    }
}
