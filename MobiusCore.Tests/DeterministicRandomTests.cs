using System;
using System.Drawing;
using MobiusEditor.Utility;
using Xunit;

namespace MobiusCore.Tests
{
    /// <summary>Resource variant selection must give the same result on every runtime and every run.</summary>
    public class DeterministicRandomTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(42)]
        [InlineData(-7)]
        [InlineData(int.MaxValue)]
        [InlineData(int.MinValue)]
        public void MatchesTheSeededFrameworkAlgorithm(int seed)
        {
            // .NET keeps the seeded System.Random sequence compatible with .NET Framework; ours must equal it.
            Random reference = new Random(seed);
            DeterministicRandom ours = new DeterministicRandom(seed);
            for (int i = 0; i < 1000; i++) Assert.Equal(reference.Next(7), ours.Next(7));
            for (int i = 0; i < 100; i++) Assert.Equal(reference.Next(), ours.Next());
        }

        [Fact]
        public void KnownFrameworkValues()
        {
            Assert.Equal(1559595546, new DeterministicRandom(0).Next());
        }

        [Fact]
        public void CellSeedMatchesTheMonoEditorsPointHash()
        {
            // Values observed from System.Drawing.Point.GetHashCode under mono 6.8 (corefx System.Drawing).
            Assert.Equal(172, DeterministicRandom.CellSeed(new Point(5, 9)));
            Assert.Equal(1291, DeterministicRandom.CellSeed(new Point(40, 35)));
        }
    }
}
