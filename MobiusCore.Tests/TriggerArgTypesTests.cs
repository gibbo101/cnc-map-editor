using System.Collections.Generic;
using System.IO;
using System.Linq;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using Xunit;
using RA = MobiusEditor.RedAlert;
using TD = MobiusEditor.TiberianDawn;

namespace MobiusCore.Tests
{
    /// <summary>
    /// The structured event/action parameter-type tables, ported from the fork's
    /// TriggersDialog control-coercion switches (the de-facto spec of which argument each
    /// trigger event/action carries, which field holds it, its legal range, and its
    /// dropdown option list). Coercion semantics are pinned exactly: RA no-arg EVENTS blank
    /// Data and Team, no-arg ACTIONS leave Data alone; list-backed values snap to the first
    /// option when invalid; team references canonicalize to the list's casing while trigger
    /// references keep their own casing when matched case-insensitively.
    /// </summary>
    public class TriggerArgTypesTests
    {
        private static IGamePlugin LoadRA() =>
            EditorHost.Shared.Load(Path.Combine(TestPaths.MapEdits, "scm05ea.ini"), out _);

        private static IGamePlugin LoadTD() =>
            EditorHost.SharedFor(GameType.TiberianDawn).Load(TiberianDawnSessionTests.CommunityMap, out _);

        private static readonly Dictionary<string, TriggerArgType> RaEventArgTypes = new Dictionary<string, TriggerArgType>
        {
            { RA.EventTypes.TEVENT_NONE, TriggerArgType.None },
            { RA.EventTypes.TEVENT_SPIED, TriggerArgType.None },
            { RA.EventTypes.TEVENT_DISCOVERED, TriggerArgType.None },
            { RA.EventTypes.TEVENT_ATTACKED, TriggerArgType.None },
            { RA.EventTypes.TEVENT_DESTROYED, TriggerArgType.None },
            { RA.EventTypes.TEVENT_ANY, TriggerArgType.None },
            { RA.EventTypes.TEVENT_MISSION_TIMER_EXPIRED, TriggerArgType.None },
            { RA.EventTypes.TEVENT_NOFACTORIES, TriggerArgType.None },
            { RA.EventTypes.TEVENT_EVAC_CIVILIAN, TriggerArgType.None },
            { RA.EventTypes.TEVENT_FAKES_DESTROYED, TriggerArgType.None },
            { RA.EventTypes.TEVENT_ALL_BRIDGES_DESTROYED, TriggerArgType.None },
            { RA.EventTypes.TEVENT_LEAVES_MAP, TriggerArgType.TeamType },
            { RA.EventTypes.TEVENT_PLAYER_ENTERED, TriggerArgType.HouseType },
            { RA.EventTypes.TEVENT_CROSS_HORIZONTAL, TriggerArgType.HouseType },
            { RA.EventTypes.TEVENT_CROSS_VERTICAL, TriggerArgType.HouseType },
            { RA.EventTypes.TEVENT_ENTERS_ZONE, TriggerArgType.HouseType },
            { RA.EventTypes.TEVENT_LOW_POWER, TriggerArgType.HouseType },
            { RA.EventTypes.TEVENT_THIEVED, TriggerArgType.HouseType },
            { RA.EventTypes.TEVENT_HOUSE_DISCOVERED, TriggerArgType.HouseType },
            { RA.EventTypes.TEVENT_BUILDINGS_DESTROYED, TriggerArgType.HouseType },
            { RA.EventTypes.TEVENT_UNITS_DESTROYED, TriggerArgType.HouseType },
            { RA.EventTypes.TEVENT_ALL_DESTROYED, TriggerArgType.HouseType },
            { RA.EventTypes.TEVENT_BUILDING_EXISTS, TriggerArgType.BuildingType },
            { RA.EventTypes.TEVENT_BUILD, TriggerArgType.BuildingType },
            { RA.EventTypes.TEVENT_BUILD_UNIT, TriggerArgType.UnitType },
            { RA.EventTypes.TEVENT_BUILD_INFANTRY, TriggerArgType.InfantryType },
            { RA.EventTypes.TEVENT_BUILD_AIRCRAFT, TriggerArgType.AircraftType },
            { RA.EventTypes.TEVENT_NUNITS_DESTROYED, TriggerArgType.Number },
            { RA.EventTypes.TEVENT_NBUILDINGS_DESTROYED, TriggerArgType.Number },
            { RA.EventTypes.TEVENT_CREDITS, TriggerArgType.Number },
            { RA.EventTypes.TEVENT_TIME, TriggerArgType.Number },
            { RA.EventTypes.TEVENT_GLOBAL_SET, TriggerArgType.GlobalNumber },
            { RA.EventTypes.TEVENT_GLOBAL_CLEAR, TriggerArgType.GlobalNumber },
        };

        private static readonly Dictionary<string, TriggerArgType> RaActionArgTypes = new Dictionary<string, TriggerArgType>
        {
            { RA.ActionTypes.TACTION_NONE, TriggerArgType.None },
            { RA.ActionTypes.TACTION_WINLOSE, TriggerArgType.None },
            { RA.ActionTypes.TACTION_ALLOWWIN, TriggerArgType.None },
            { RA.ActionTypes.TACTION_REVEAL_ALL, TriggerArgType.None },
            { RA.ActionTypes.TACTION_START_TIMER, TriggerArgType.None },
            { RA.ActionTypes.TACTION_STOP_TIMER, TriggerArgType.None },
            { RA.ActionTypes.TACTION_CREEP_SHADOW, TriggerArgType.None },
            { RA.ActionTypes.TACTION_DESTROY_OBJECT, TriggerArgType.None },
            { RA.ActionTypes.TACTION_LAUNCH_NUKES, TriggerArgType.None },
            { RA.ActionTypes.TACTION_CREATE_TEAM, TriggerArgType.TeamType },
            { RA.ActionTypes.TACTION_DESTROY_TEAM, TriggerArgType.TeamType },
            { RA.ActionTypes.TACTION_REINFORCEMENTS, TriggerArgType.TeamType },
            { RA.ActionTypes.TACTION_WIN, TriggerArgType.HouseType },
            { RA.ActionTypes.TACTION_LOSE, TriggerArgType.HouseType },
            { RA.ActionTypes.TACTION_BEGIN_PRODUCTION, TriggerArgType.HouseType },
            { RA.ActionTypes.TACTION_FIRE_SALE, TriggerArgType.HouseType },
            { RA.ActionTypes.TACTION_AUTOCREATE, TriggerArgType.HouseType },
            { RA.ActionTypes.TACTION_ALL_HUNT, TriggerArgType.HouseType },
            { RA.ActionTypes.TACTION_FORCE_TRIGGER, TriggerArgType.Trigger },
            { RA.ActionTypes.TACTION_DESTROY_TRIGGER, TriggerArgType.Trigger },
            { RA.ActionTypes.TACTION_DZ, TriggerArgType.Waypoint },
            { RA.ActionTypes.TACTION_REVEAL_SOME, TriggerArgType.Waypoint },
            { RA.ActionTypes.TACTION_REVEAL_ZONE, TriggerArgType.Waypoint },
            { RA.ActionTypes.TACTION_1_SPECIAL, TriggerArgType.SuperWeapon },
            { RA.ActionTypes.TACTION_FULL_SPECIAL, TriggerArgType.SuperWeapon },
            { RA.ActionTypes.TACTION_PLAY_MUSIC, TriggerArgType.MusicTheme },
            { RA.ActionTypes.TACTION_PLAY_MOVIE, TriggerArgType.Movie },
            { RA.ActionTypes.TACTION_PLAY_SOUND, TriggerArgType.Sound },
            { RA.ActionTypes.TACTION_PLAY_SPEECH, TriggerArgType.Speech },
            { RA.ActionTypes.TACTION_PREFERRED_TARGET, TriggerArgType.PreferredTarget },
            { RA.ActionTypes.TACTION_BASE_BUILDING, TriggerArgType.BooleanSwitch },
            { RA.ActionTypes.TACTION_TEXT_TRIGGER, TriggerArgType.TextMessage },
            { RA.ActionTypes.TACTION_ADD_TIMER, TriggerArgType.Number },
            { RA.ActionTypes.TACTION_SUB_TIMER, TriggerArgType.Number },
            { RA.ActionTypes.TACTION_SET_TIMER, TriggerArgType.Number },
            { RA.ActionTypes.TACTION_SET_GLOBAL, TriggerArgType.GlobalNumber },
            { RA.ActionTypes.TACTION_CLEAR_GLOBAL, TriggerArgType.GlobalNumber },
        };

        private static readonly Dictionary<string, TriggerArgType> TdEventArgTypes = new Dictionary<string, TriggerArgType>
        {
            { TD.EventTypes.EVENT_NONE, TriggerArgType.None },
            { TD.EventTypes.EVENT_PLAYER_ENTERED, TriggerArgType.None },
            { TD.EventTypes.EVENT_DISCOVERED, TriggerArgType.None },
            { TD.EventTypes.EVENT_ATTACKED, TriggerArgType.None },
            { TD.EventTypes.EVENT_DESTROYED, TriggerArgType.None },
            { TD.EventTypes.EVENT_ANY, TriggerArgType.None },
            { TD.EventTypes.EVENT_HOUSE_DISCOVERED, TriggerArgType.None },
            { TD.EventTypes.EVENT_UNITS_DESTROYED, TriggerArgType.None },
            { TD.EventTypes.EVENT_BUILDINGS_DESTROYED, TriggerArgType.None },
            { TD.EventTypes.EVENT_ALL_DESTROYED, TriggerArgType.None },
            { TD.EventTypes.EVENT_NOFACTORIES, TriggerArgType.None },
            { TD.EventTypes.EVENT_EVAC_CIVILIAN, TriggerArgType.None },
            { TD.EventTypes.EVENT_CREDITS, TriggerArgType.Number },
            { TD.EventTypes.EVENT_TIME, TriggerArgType.Number },
            { TD.EventTypes.EVENT_NBUILDINGS_DESTROYED, TriggerArgType.Number },
            { TD.EventTypes.EVENT_NUNITS_DESTROYED, TriggerArgType.Number },
            { TD.EventTypes.EVENT_BUILD, TriggerArgType.BuildingType },
        };

        [Fact]
        public void EveryRaEventTypeMatchesTheDialogTable()
        {
            IGamePlugin plugin = LoadRA();
            Assert.Equal(RaEventArgTypes.Count, RA.EventTypes.GetTypes().Count());
            foreach (string eventType in RA.EventTypes.GetTypes())
            {
                Assert.Equal(RaEventArgTypes[eventType], plugin.GetEventArgType(eventType));
            }
        }

        [Fact]
        public void EveryRaActionTypeMatchesTheDialogTable()
        {
            IGamePlugin plugin = LoadRA();
            Assert.Equal(RaActionArgTypes.Count, RA.ActionTypes.GetTypes().Count());
            foreach (string actionType in RA.ActionTypes.GetTypes())
            {
                Assert.Equal(RaActionArgTypes[actionType], plugin.GetActionArgType(actionType));
            }
        }

        [Fact]
        public void EveryTdEventTypeMatchesTheDialogTableAndTdActionsCarryNoArgument()
        {
            IGamePlugin plugin = LoadTD();
            Assert.Equal(TdEventArgTypes.Count, TD.EventTypes.GetTypes().Count());
            foreach (string eventType in TD.EventTypes.GetTypes())
            {
                Assert.Equal(TdEventArgTypes[eventType], plugin.GetEventArgType(eventType));
            }
            foreach (string actionType in TD.ActionTypes.GetTypes())
            {
                Assert.Equal(TriggerArgType.None, plugin.GetActionArgType(actionType));
            }
        }

        [Fact]
        public void UnknownTypesFallBackToNone()
        {
            IGamePlugin plugin = LoadRA();
            Assert.Equal(TriggerArgType.None, plugin.GetEventArgType("Bogus Event"));
            Assert.Equal(TriggerArgType.None, plugin.GetActionArgType("Bogus Action"));
            Assert.Equal(TriggerArgType.None, plugin.GetEventArgType(null));
            Assert.Equal(TriggerArgType.None, plugin.GetActionArgType(null));
        }

        [Fact]
        public void FieldOfMapsEachArgTypeToItsCarryingField()
        {
            Assert.Equal(TriggerArgField.None, TriggerArg.FieldOf(TriggerArgType.None));
            Assert.Equal(TriggerArgField.Team, TriggerArg.FieldOf(TriggerArgType.TeamType));
            Assert.Equal(TriggerArgField.Trigger, TriggerArg.FieldOf(TriggerArgType.Trigger));
            Assert.Equal(TriggerArgField.Data, TriggerArg.FieldOf(TriggerArgType.Number));
            Assert.Equal(TriggerArgField.Data, TriggerArg.FieldOf(TriggerArgType.GlobalNumber));
            Assert.Equal(TriggerArgField.Data, TriggerArg.FieldOf(TriggerArgType.HouseType));
            Assert.Equal(TriggerArgField.Data, TriggerArg.FieldOf(TriggerArgType.BuildingType));
            Assert.Equal(TriggerArgField.Data, TriggerArg.FieldOf(TriggerArgType.UnitType));
            Assert.Equal(TriggerArgField.Data, TriggerArg.FieldOf(TriggerArgType.InfantryType));
            Assert.Equal(TriggerArgField.Data, TriggerArg.FieldOf(TriggerArgType.AircraftType));
            Assert.Equal(TriggerArgField.Data, TriggerArg.FieldOf(TriggerArgType.Waypoint));
            Assert.Equal(TriggerArgField.Data, TriggerArg.FieldOf(TriggerArgType.SuperWeapon));
            Assert.Equal(TriggerArgField.Data, TriggerArg.FieldOf(TriggerArgType.MusicTheme));
            Assert.Equal(TriggerArgField.Data, TriggerArg.FieldOf(TriggerArgType.Movie));
            Assert.Equal(TriggerArgField.Data, TriggerArg.FieldOf(TriggerArgType.Sound));
            Assert.Equal(TriggerArgField.Data, TriggerArg.FieldOf(TriggerArgType.Speech));
            Assert.Equal(TriggerArgField.Data, TriggerArg.FieldOf(TriggerArgType.PreferredTarget));
            Assert.Equal(TriggerArgField.Data, TriggerArg.FieldOf(TriggerArgType.BooleanSwitch));
            Assert.Equal(TriggerArgField.Data, TriggerArg.FieldOf(TriggerArgType.TextMessage));
        }

        [Fact]
        public void NumericRangesMatchTheDialogSpinners()
        {
            IGamePlugin plugin = LoadRA();
            Assert.Equal((0L, (long)int.MaxValue), plugin.GetArgRange(TriggerArgType.Number));
            Assert.Equal((0L, (long)RA.Constants.HighestGlobal), plugin.GetArgRange(TriggerArgType.GlobalNumber));
            Assert.Null(plugin.GetArgRange(TriggerArgType.HouseType));
            Assert.Null(plugin.GetArgRange(TriggerArgType.None));
            Assert.Null(plugin.GetArgRange(TriggerArgType.TeamType));
        }

        [Fact]
        public void HouseOptionsLeadWithNoneThenTheMapsHouses()
        {
            IGamePlugin plugin = LoadRA();
            List<(long Value, string Label)> options = plugin.GetArgOptions(TriggerArgType.HouseType);
            Assert.Equal((-1L, House.None), options[0]);
            Assert.Equal(plugin.Map.Houses.Length + 1, options.Count);
            foreach (House house in plugin.Map.Houses)
            {
                Assert.Contains(options, o => o.Value == house.Type.ID && o.Label == house.Type.Name);
            }
        }

        [Fact]
        public void TechnoOptionsUseTypeIdsAndRespectCategory()
        {
            IGamePlugin plugin = LoadRA();
            List<(long Value, string Label)> buildings = plugin.GetArgOptions(TriggerArgType.BuildingType);
            Assert.Equal(plugin.Map.BuildingTypes.Count, buildings.Count);
            Assert.All(buildings, o => Assert.Contains(plugin.Map.BuildingTypes, b => b.ID == o.Value));
            List<(long Value, string Label)> units = plugin.GetArgOptions(TriggerArgType.UnitType);
            Assert.NotEmpty(units);
            Assert.All(units, o => Assert.Contains(plugin.Map.UnitTypes, u => u.IsGroundUnit && !u.IsVessel && u.ID == o.Value));
            List<(long Value, string Label)> infantry = plugin.GetArgOptions(TriggerArgType.InfantryType);
            Assert.Equal(plugin.Map.InfantryTypes.Count, infantry.Count);
            List<(long Value, string Label)> aircraft = plugin.GetArgOptions(TriggerArgType.AircraftType);
            Assert.NotEmpty(aircraft);
            Assert.All(aircraft, o => Assert.Contains(plugin.Map.TeamTechnoTypes.OfType<UnitType>(), u => u.IsAircraft && u.ID == o.Value));
        }

        [Fact]
        public void WaypointOptionsLeadWithNoneAndCoverEverySlot()
        {
            IGamePlugin plugin = LoadRA();
            List<(long Value, string Label)> options = plugin.GetArgOptions(TriggerArgType.Waypoint);
            Assert.Equal((-1L, Waypoint.None), options[0]);
            Assert.Equal(plugin.Map.Waypoints.Length + 1, options.Count);
            Assert.Equal(0L, options[1].Value);
        }

        [Fact]
        public void MediaOptionsMatchTheDialogLists()
        {
            IGamePlugin plugin = LoadRA();
            List<(long Value, string Label)> super = plugin.GetArgOptions(TriggerArgType.SuperWeapon);
            Assert.Equal(RA.ActionDataTypes.SuperTypes.Length + 1, super.Count);
            Assert.Contains(super, o => o.Value == -1 && o.Label == "None");
            Assert.Equal(super.OrderBy(o => o.Label).ToList(), super);
            List<(long Value, string Label)> music = plugin.GetArgOptions(TriggerArgType.MusicTheme);
            Assert.Equal(-1L, music[0].Value);
            Assert.Equal(plugin.Map.ThemeTypes.Count, music.Count);
            List<(long Value, string Label)> movies = plugin.GetArgOptions(TriggerArgType.Movie);
            Assert.Equal(-1L, movies[0].Value);
            Assert.Equal(plugin.Map.MovieTypes.Count, movies.Count);
            List<(long Value, string Label)> sounds = plugin.GetArgOptions(TriggerArgType.Sound);
            Assert.Equal((-1L, "None"), sounds[0]);
            Assert.All(sounds.Skip(1), o => Assert.False(string.Equals(RA.ActionDataTypes.VocNames[o.Value], "x", System.StringComparison.OrdinalIgnoreCase)));
            List<(long Value, string Label)> speech = plugin.GetArgOptions(TriggerArgType.Speech);
            Assert.Equal((-1L, "None"), speech[0]);
            Assert.All(speech.Skip(1), o => Assert.False(string.Equals(RA.ActionDataTypes.VoxNames[o.Value], "none", System.StringComparison.OrdinalIgnoreCase)));
            List<(long Value, string Label)> targets = plugin.GetArgOptions(TriggerArgType.PreferredTarget);
            Assert.Equal(RA.TeamMissionTypes.Attack.DropdownOptions.Select(o => ((long)o.Value, o.Label)).ToList(), targets);
            Assert.Equal(new List<(long, string)> { (0, "Off"), (1, "On") }, plugin.GetArgOptions(TriggerArgType.BooleanSwitch));
            List<(long Value, string Label)> text = plugin.GetArgOptions(TriggerArgType.TextMessage);
            Assert.Equal(RA.ActionDataTypes.TextDesc.Length, text.Count);
            Assert.Equal(1L, text[0].Value);
            Assert.StartsWith("001 ", text[0].Label);
        }

        [Fact]
        public void OptionFreeArgTypesHaveNoOptionList()
        {
            IGamePlugin plugin = LoadRA();
            Assert.Null(plugin.GetArgOptions(TriggerArgType.None));
            Assert.Null(plugin.GetArgOptions(TriggerArgType.Number));
            Assert.Null(plugin.GetArgOptions(TriggerArgType.GlobalNumber));
            Assert.Null(plugin.GetArgOptions(TriggerArgType.TeamType));
            Assert.Null(plugin.GetArgOptions(TriggerArgType.Trigger));
        }

        [Fact]
        public void CoercingANoArgEventBlanksDataAndTeam()
        {
            IGamePlugin plugin = LoadRA();
            TriggerEvent evt = new TriggerEvent { EventType = RA.EventTypes.TEVENT_DESTROYED, Data = 5, Team = "ghost" };
            plugin.CoerceEventArg(evt);
            Assert.Equal(0, evt.Data);
            Assert.Equal(TeamType.None, evt.Team);
        }

        [Fact]
        public void CoercingListBackedEventDataSnapsInvalidValuesToTheFirstOption()
        {
            IGamePlugin plugin = LoadRA();
            TriggerEvent evt = new TriggerEvent { EventType = RA.EventTypes.TEVENT_PLAYER_ENTERED, Data = 9999 };
            plugin.CoerceEventArg(evt);
            Assert.Equal(-1, evt.Data);
            long validHouse = plugin.Map.Houses[0].Type.ID;
            evt.Data = validHouse;
            plugin.CoerceEventArg(evt);
            Assert.Equal(validHouse, evt.Data);
        }

        [Fact]
        public void CoercingNumericEventDataClampsToTheSpinnerRange()
        {
            IGamePlugin plugin = LoadRA();
            TriggerEvent evt = new TriggerEvent { EventType = RA.EventTypes.TEVENT_CREDITS, Data = -5 };
            plugin.CoerceEventArg(evt);
            Assert.Equal(0, evt.Data);
            evt = new TriggerEvent { EventType = RA.EventTypes.TEVENT_GLOBAL_SET, Data = 99 };
            plugin.CoerceEventArg(evt);
            Assert.Equal(RA.Constants.HighestGlobal, evt.Data);
        }

        [Fact]
        public void CoercingATeamEventCanonicalizesCasingAndBlanksData()
        {
            IGamePlugin plugin = LoadRA();
            plugin.Map.TeamTypes.Add(new TeamType { Name = "alfa", House = plugin.Map.HouseTypes.First() });
            try
            {
                TriggerEvent evt = new TriggerEvent { EventType = RA.EventTypes.TEVENT_LEAVES_MAP, Data = 3, Team = "ALFA" };
                plugin.CoerceEventArg(evt);
                Assert.Equal("alfa", evt.Team);
                Assert.Equal(0, evt.Data);
                evt.Team = "ghost";
                plugin.CoerceEventArg(evt);
                Assert.Equal(TeamType.None, evt.Team);
            }
            finally
            {
                plugin.Map.TeamTypes.RemoveAll(t => t.Name == "alfa");
            }
        }

        [Fact]
        public void CoercingANoArgActionLeavesDataAlone()
        {
            IGamePlugin plugin = LoadRA();
            TriggerAction act = new TriggerAction { ActionType = RA.ActionTypes.TACTION_REVEAL_ALL, Data = 7 };
            plugin.CoerceActionArg(act, Enumerable.Empty<Trigger>());
            Assert.Equal(7, act.Data);
        }

        [Fact]
        public void CoercingATriggerActionChecksTheWorkingListButKeepsTheGivenCasing()
        {
            IGamePlugin plugin = LoadRA();
            Trigger[] working = { new Trigger { Name = "abcd" } };
            TriggerAction act = new TriggerAction { ActionType = RA.ActionTypes.TACTION_FORCE_TRIGGER, Trigger = "ABCD" };
            plugin.CoerceActionArg(act, working);
            Assert.Equal("ABCD", act.Trigger);
            act.Trigger = "ghost";
            plugin.CoerceActionArg(act, working);
            Assert.Equal(Trigger.None, act.Trigger);
        }

        [Fact]
        public void CoercingListBackedActionDataSnapsInvalidValuesToTheFirstOption()
        {
            IGamePlugin plugin = LoadRA();
            TriggerAction act = new TriggerAction { ActionType = RA.ActionTypes.TACTION_BASE_BUILDING, Data = 5 };
            plugin.CoerceActionArg(act, Enumerable.Empty<Trigger>());
            Assert.Equal(0, act.Data);
            act = new TriggerAction { ActionType = RA.ActionTypes.TACTION_SET_GLOBAL, Data = 99 };
            plugin.CoerceActionArg(act, Enumerable.Empty<Trigger>());
            Assert.Equal(RA.Constants.HighestGlobal, act.Data);
        }

        [Fact]
        public void TdCoercionBlanksNoArgEventDataAndLeavesActionsAlone()
        {
            IGamePlugin plugin = LoadTD();
            TriggerEvent evt = new TriggerEvent { EventType = TD.EventTypes.EVENT_ATTACKED, Data = 5, Team = "keep" };
            plugin.CoerceEventArg(evt);
            Assert.Equal(0, evt.Data);
            Assert.Equal("keep", evt.Team);
            evt = new TriggerEvent { EventType = TD.EventTypes.EVENT_BUILD, Data = 9999 };
            plugin.CoerceEventArg(evt);
            Assert.Equal(plugin.Map.BuildingTypes.First().ID, evt.Data);
            TriggerAction act = new TriggerAction { ActionType = TD.ActionTypes.ACTION_WIN, Data = 7 };
            plugin.CoerceActionArg(act, Enumerable.Empty<Trigger>());
            Assert.Equal(7, act.Data);
        }
    }
}
