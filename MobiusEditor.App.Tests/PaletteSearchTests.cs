using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MobiusCore.Tests;
using MobiusEditor.Model;
using Xunit;

namespace MobiusEditor.App.Tests
{
    /// <summary>
    /// The palette search box narrows every palette at once — by display name or INI name —
    /// keeps a selected brush that survives the filter, drops group headers that lose all
    /// their entries, and restores everything when cleared.
    /// </summary>
    public class PaletteSearchTests
    {
        private static MainWindow Open()
        {
            string map = Path.Combine(TestPaths.MapEdits, "scm05ea.ini");
            MainWindow window = new MainWindow(new[] { map, "--game", TestPaths.GameDir, "--mod", TestPaths.ModDir });
            window.Show();
            Pump.UntilMapReady(window);
            return window;
        }

        [AvaloniaFact]
        public void SearchNarrowsPalettesAndClearRestores()
        {
            MainWindow window = Open();
            TextBox search = window.FindControl<TextBox>("PaletteSearch");
            ListBox units = window.FindControl<ListBox>("UnitPalette");
            int allUnits = units.Items.OfType<PaletteEntry>().Count();

            search.Text = "heavy tank";
            Dispatcher.UIThread.RunJobs();
            var matches = units.Items.OfType<PaletteEntry>().ToList();
            Assert.NotEmpty(matches);
            Assert.True(matches.Count < allUnits);
            Assert.All(matches, m => Assert.Contains("heavy tank", m.Label, System.StringComparison.OrdinalIgnoreCase));

            // INI names match too, and other palettes narrow at the same time.
            search.Text = "3tnk";
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(units.Items.OfType<PaletteEntry>(), m => (m.Type as UnitType)?.Name == "3tnk");
            ListBox buildings = window.FindControl<ListBox>("BuildingPalette");
            Assert.Empty(buildings.Items.OfType<PaletteEntry>());
            Assert.Empty(buildings.Items.OfType<PaletteHeader>());

            search.Text = "";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(allUnits, units.Items.OfType<PaletteEntry>().Count());
        }

        [AvaloniaFact]
        public void SurvivingSelectionKeepsItsBrush()
        {
            MainWindow window = Open();
            TextBox search = window.FindControl<TextBox>("PaletteSearch");
            ListBox units = window.FindControl<ListBox>("UnitPalette");
            PaletteEntry heavyTank = units.Items.OfType<PaletteEntry>().First(p => (p.Type as UnitType)?.Name == "3tnk");
            units.SelectedItem = heavyTank;
            Dispatcher.UIThread.RunJobs();

            search.Text = "heavy";
            Dispatcher.UIThread.RunJobs();
            Assert.Same(heavyTank, units.SelectedItem);

            search.Text = "";
            Dispatcher.UIThread.RunJobs();
            Assert.Same(heavyTank, units.SelectedItem);
        }

        [AvaloniaFact]
        public void HeadersOnlySurviveWithEntries()
        {
            MainWindow window = Open();
            TextBox search = window.FindControl<TextBox>("PaletteSearch");
            ListBox units = window.FindControl<ListBox>("UnitPalette");

            // "3tnk" is Soviet: the Allies header must go, the Soviets header stays.
            search.Text = "3tnk";
            Dispatcher.UIThread.RunJobs();
            var headers = units.Items.OfType<PaletteHeader>().Select(h => h.Label).ToList();
            Assert.Contains("Soviets", headers);
            Assert.DoesNotContain("Allies", headers);
        }
    }
}
