using System.IO;
using System.Linq;
using MobiusEditor.Headless;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using Xunit;
using RA = MobiusEditor.RedAlert;

namespace MobiusCore.Tests
{
    /// <summary>
    /// Mission-pattern expansion: a JSON spec (Newtonsoft, // comments tolerated) holding a
    /// flat pattern list expands into raw triggers/teamtypes applied through the trigger and
    /// teamtype editors. v1 patterns: win, lose, reinforce, attack-wave, plus the raw escape
    /// hatch of literal INI rows parsed by the plugin's own load path (index references
    /// resolving within the pattern's own rows). Errors are collected spec-wide; nothing is
    /// committed unless every pattern builds cleanly and the expanded triggers pass
    /// CheckTriggers without fatals.
    /// </summary>
    public class MissionExpanderTests
    {
        private static IGamePlugin LoadRA() =>
            EditorHost.Shared.Load(Path.Combine(TestPaths.MapEdits, "scm05ea.ini"), out _);

        private static MissionSpec Parse(string json)
        {
            MissionSpec spec = MissionSpec.Parse(json, out string[] errors);
            Assert.Empty(errors);
            return spec;
        }

        [Fact]
        public void ParsesJsonWithCommentsIntoAFlatPatternList()
        {
            MissionSpec spec = Parse(@"{
                // design note: the win condition
                ""patterns"": [
                    { ""pattern"": ""win"", ""name"": ""win1"", ""house"": ""USSR"", ""enemy"": ""Greece"" }
                ]
            }");
            MissionPattern p = Assert.Single(spec.Patterns);
            Assert.Equal("win", p.Pattern);
            Assert.Equal("win1", p.Name);
            Assert.Equal("USSR", p.House);
            Assert.Equal("Greece", p.Enemy);
        }

        [Fact]
        public void MalformedJsonIsAnErrorNotAnException()
        {
            MissionSpec spec = MissionSpec.Parse("{ not json", out string[] errors);
            Assert.Null(spec);
            Assert.NotEmpty(errors);
        }

        [Fact]
        public void WinPatternBuildsTheCanonicalWinTrigger()
        {
            IGamePlugin plugin = LoadRA();
            MissionSpec spec = Parse(@"{ ""patterns"": [
                { ""pattern"": ""win"", ""name"": ""win1"", ""house"": ""USSR"", ""enemy"": ""Greece"" } ] }");
            Assert.True(MissionExpander.Expand(plugin, spec, out string[] errors, out _), string.Join("; ", errors));
            Trigger t = plugin.Map.Triggers.Single(x => x.Name == "win1");
            Assert.Equal("Greece", t.House);
            Assert.Equal(TriggerPersistentType.Volatile, t.PersistentType);
            Assert.Equal(RA.EventTypes.TEVENT_ALL_DESTROYED, t.Event1.EventType);
            Assert.Equal(plugin.Map.HouseTypes.First(h => h.Name == "Greece").ID, t.Event1.Data);
            Assert.Equal(RA.ActionTypes.TACTION_WIN, t.Action1.ActionType);
            Assert.Equal(plugin.Map.HouseTypes.First(h => h.Name == "USSR").ID, t.Action1.Data);
        }

        [Fact]
        public void LosePatternTargetsTheLosingHouseItself()
        {
            IGamePlugin plugin = LoadRA();
            MissionSpec spec = Parse(@"{ ""patterns"": [
                { ""pattern"": ""lose"", ""name"": ""lose"", ""house"": ""USSR"" } ] }");
            Assert.True(MissionExpander.Expand(plugin, spec, out string[] errors, out _), string.Join("; ", errors));
            Trigger t = plugin.Map.Triggers.Single(x => x.Name == "lose");
            Assert.Equal("USSR", t.House);
            Assert.Equal(RA.EventTypes.TEVENT_ALL_DESTROYED, t.Event1.EventType);
            long ussr = plugin.Map.HouseTypes.First(h => h.Name == "USSR").ID;
            Assert.Equal(ussr, t.Event1.Data);
            Assert.Equal(RA.ActionTypes.TACTION_LOSE, t.Action1.ActionType);
            Assert.Equal(ussr, t.Action1.Data);
        }

        [Fact]
        public void ReinforcePatternBuildsTheTeamAndItsTimeTrigger()
        {
            IGamePlugin plugin = LoadRA();
            MissionSpec spec = Parse(@"{ ""patterns"": [
                { ""pattern"": ""reinforce"", ""name"": ""rnf1"", ""house"": ""USSR"",
                  ""units"": [""E1:3"", ""BADR:1""], ""at"": 4, ""to"": 7, ""after"": 30, ""repeat"": true } ] }");
            Assert.True(MissionExpander.Expand(plugin, spec, out string[] errors, out _), string.Join("; ", errors));
            TeamType team = plugin.Map.TeamTypes.Single(x => x.Name == "rnf1");
            Assert.Equal("USSR", team.House.Name);
            Assert.True(team.IsReinforcable);
            Assert.Equal(4, team.Origin);
            Assert.Equal(new[] { ("E1", (byte)3), ("BADR", (byte)1) },
                team.Classes.Select(c => (c.Type.Name.ToUpperInvariant(), c.Count)).ToArray());
            TeamTypeMission move = Assert.Single(team.Missions);
            Assert.Equal(RA.TeamMissionTypes.Move.ID, move.Mission.ID);
            Assert.Equal(7, move.Argument);
            Trigger t = plugin.Map.Triggers.Single(x => x.Name == "rnf1");
            Assert.Equal("USSR", t.House);
            Assert.Equal(TriggerPersistentType.Persistent, t.PersistentType);
            Assert.Equal(RA.EventTypes.TEVENT_TIME, t.Event1.EventType);
            Assert.Equal(30, t.Event1.Data);
            Assert.Equal(RA.ActionTypes.TACTION_REINFORCEMENTS, t.Action1.ActionType);
            Assert.Equal("rnf1", t.Action1.Team);
        }

        [Fact]
        public void AttackWavePatternBuildsAPersistentCreateTeamLoop()
        {
            IGamePlugin plugin = LoadRA();
            MissionSpec spec = Parse(@"{ ""patterns"": [
                { ""pattern"": ""attack-wave"", ""name"": ""atk1"", ""house"": ""USSR"",
                  ""units"": [""3TNK:2"", ""E1:3""], ""every"": 40, ""target"": ""Buildings"", ""at"": 5 } ] }");
            Assert.True(MissionExpander.Expand(plugin, spec, out string[] errors, out _), string.Join("; ", errors));
            TeamType team = plugin.Map.TeamTypes.Single(x => x.Name == "atk1");
            Assert.Equal(5, team.Origin);
            Assert.False(team.IsReinforcable);
            TeamTypeMission attack = Assert.Single(team.Missions);
            Assert.Equal(RA.TeamMissionTypes.Attack.ID, attack.Mission.ID);
            Assert.Equal(RA.TeamMissionTypes.Attack.DropdownOptions.First(op => op.Label == "Buildings").Value, attack.Argument);
            Trigger t = plugin.Map.Triggers.Single(x => x.Name == "atk1");
            Assert.Equal(TriggerPersistentType.Persistent, t.PersistentType);
            Assert.Equal(RA.EventTypes.TEVENT_TIME, t.Event1.EventType);
            Assert.Equal(40, t.Event1.Data);
            Assert.Equal(RA.ActionTypes.TACTION_CREATE_TEAM, t.Action1.ActionType);
            Assert.Equal("atk1", t.Action1.Team);
        }

        [Fact]
        public void RawPatternParsesLiteralRowsAndResolvesIndicesWithinThePattern()
        {
            IGamePlugin plugin = LoadRA();
            // Rows in the game's own INI encoding (shape of scu01ea): the trigger's action
            // references team index 0, which must resolve to the pattern's own first team.
            MissionSpec spec = Parse(@"{ ""patterns"": [
                { ""pattern"": ""raw"",
                  ""teamtypes"": { ""para1"": ""2,0,7,0,0,16,-1,3,E1:2,E2:3,BADR:1,0"" },
                  ""triggers"": { ""rspd"": ""0,5,0,1,6,-1,0,0,-1,0,7,0,-1,-1,0,-1,-1,-1"" } } ] }");
            Assert.True(MissionExpander.Expand(plugin, spec, out string[] errors, out _), string.Join("; ", errors));
            TeamType team = plugin.Map.TeamTypes.Single(x => x.Name == "para1");
            Assert.Equal(3, team.Classes.Count);
            Assert.Equal(16, team.Origin);
            Trigger t = plugin.Map.Triggers.Single(x => x.Name == "rspd");
            Assert.Equal(RA.EventTypes.TEVENT_ATTACKED, t.Event1.EventType);
            Assert.Equal(RA.ActionTypes.TACTION_REINFORCEMENTS, t.Action1.ActionType);
            Assert.Equal("para1", t.Action1.Team);
        }

        [Fact]
        public void ExpansionIsAllOrNothingAcrossPatterns()
        {
            IGamePlugin plugin = LoadRA();
            int triggersBefore = plugin.Map.Triggers.Count;
            int teamsBefore = plugin.Map.TeamTypes.Count;
            MissionSpec spec = Parse(@"{ ""patterns"": [
                { ""pattern"": ""win"", ""name"": ""win1"", ""house"": ""USSR"", ""enemy"": ""Greece"" },
                { ""pattern"": ""lose"", ""name"": ""lose"", ""house"": ""Klingons"" } ] }");
            Assert.False(MissionExpander.Expand(plugin, spec, out string[] errors, out _));
            Assert.Contains(errors, e => e.Contains("Klingons"));
            Assert.Equal(triggersBefore, plugin.Map.Triggers.Count);
            Assert.Equal(teamsBefore, plugin.Map.TeamTypes.Count);
        }

        [Fact]
        public void UnknownPatternUnitAndCollidingNameAreErrors()
        {
            IGamePlugin plugin = LoadRA();
            MissionSpec spec = Parse(@"{ ""patterns"": [
                { ""pattern"": ""teleport"", ""name"": ""tp01"" } ] }");
            Assert.False(MissionExpander.Expand(plugin, spec, out string[] errors, out _));
            Assert.Contains(errors, e => e.Contains("teleport"));

            spec = Parse(@"{ ""patterns"": [
                { ""pattern"": ""reinforce"", ""name"": ""rnf1"", ""house"": ""USSR"", ""units"": [""ZZZZ:1""], ""after"": 10 } ] }");
            Assert.False(MissionExpander.Expand(plugin, spec, out errors, out _));
            Assert.Contains(errors, e => e.Contains("ZZZZ"));

            spec = Parse(@"{ ""patterns"": [
                { ""pattern"": ""win"", ""name"": ""win1"", ""house"": ""USSR"", ""enemy"": ""Greece"" },
                { ""pattern"": ""lose"", ""name"": ""win1"", ""house"": ""USSR"" } ] }");
            Assert.False(MissionExpander.Expand(plugin, spec, out errors, out _));
            Assert.NotEmpty(errors);
        }

        [Fact]
        public void TiberianDawnMapsAreRefusedForNow()
        {
            EditorSession session = EditorHost.SharedFor(GameType.TiberianDawn);
            IGamePlugin plugin = session.Load(TiberianDawnSessionTests.CommunityMap, out _);
            MissionSpec spec = Parse(@"{ ""patterns"": [
                { ""pattern"": ""lose"", ""name"": ""lose"", ""house"": ""BadGuy"" } ] }");
            Assert.False(MissionExpander.Expand(plugin, spec, out string[] errors, out _));
            Assert.Contains(errors, e => e.Contains("Red Alert"));
        }

        [Fact]
        public void ExpandedMapSurvivesASaveLoadRoundTrip()
        {
            IGamePlugin plugin = LoadRA();
            MissionSpec spec = Parse(@"{ ""patterns"": [
                { ""pattern"": ""win"", ""name"": ""win1"", ""house"": ""USSR"", ""enemy"": ""Greece"" },
                { ""pattern"": ""reinforce"", ""name"": ""rnf1"", ""house"": ""USSR"",
                  ""units"": [""E1:3""], ""at"": 4, ""after"": 30 } ] }");
            Assert.True(MissionExpander.Expand(plugin, spec, out string[] errors, out _), string.Join("; ", errors));
            string outDir = TestPaths.Output("mission-expand");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "expanded.ini");
            plugin.Save(outPath, FileType.INI);
            IGamePlugin reloaded = EditorHost.Shared.Load(outPath, out _);
            Trigger t = reloaded.Map.Triggers.Single(x => x.Name == "rnf1");
            Assert.Equal(RA.ActionTypes.TACTION_REINFORCEMENTS, t.Action1.ActionType);
            Assert.Equal("rnf1", t.Action1.Team);
            Assert.Equal(TriggerPersistentType.Volatile, t.PersistentType);
            Assert.Single(reloaded.Map.TeamTypes, x => x.Name == "rnf1");
            Assert.NotNull(reloaded.Map.Triggers.SingleOrDefault(x => x.Name == "win1"));
        }
    }
}
