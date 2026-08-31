//
// The Red Alert event/action parameter-type tables, ported from the fork's TriggersDialog
// control-coercion switches. For each event/action type: the kind of parameter it carries,
// the dropdown option list built from live map state, the spinner range for free numbers,
// and the coercion the dialog applies when (re)binding a value — no-argument EVENTS blank
// Data and Team, no-argument ACTIONS leave the fields alone; list-backed values snap to
// the first option when invalid; team references canonicalize to the list's casing while
// trigger references keep their own casing when matched case-insensitively.
using System;
using System.Collections.Generic;
using System.Linq;
using MobiusEditor.Model;
using MobiusEditor.Utility;

namespace MobiusEditor.RedAlert
{
    public static class TriggerArgTypes
    {
        public static TriggerArgType ForEvent(string eventType)
        {
            switch (eventType)
            {
                case EventTypes.TEVENT_LEAVES_MAP:
                    return TriggerArgType.TeamType;
                case EventTypes.TEVENT_PLAYER_ENTERED:
                case EventTypes.TEVENT_CROSS_HORIZONTAL:
                case EventTypes.TEVENT_CROSS_VERTICAL:
                case EventTypes.TEVENT_ENTERS_ZONE:
                case EventTypes.TEVENT_LOW_POWER:
                case EventTypes.TEVENT_THIEVED:
                case EventTypes.TEVENT_HOUSE_DISCOVERED:
                case EventTypes.TEVENT_BUILDINGS_DESTROYED:
                case EventTypes.TEVENT_UNITS_DESTROYED:
                case EventTypes.TEVENT_ALL_DESTROYED:
                    return TriggerArgType.HouseType;
                case EventTypes.TEVENT_BUILDING_EXISTS:
                case EventTypes.TEVENT_BUILD:
                    return TriggerArgType.BuildingType;
                case EventTypes.TEVENT_BUILD_UNIT:
                    return TriggerArgType.UnitType;
                case EventTypes.TEVENT_BUILD_INFANTRY:
                    return TriggerArgType.InfantryType;
                case EventTypes.TEVENT_BUILD_AIRCRAFT:
                    return TriggerArgType.AircraftType;
                case EventTypes.TEVENT_NUNITS_DESTROYED:
                case EventTypes.TEVENT_NBUILDINGS_DESTROYED:
                case EventTypes.TEVENT_CREDITS:
                case EventTypes.TEVENT_TIME:
                    return TriggerArgType.Number;
                case EventTypes.TEVENT_GLOBAL_SET:
                case EventTypes.TEVENT_GLOBAL_CLEAR:
                    return TriggerArgType.GlobalNumber;
                default:
                    return TriggerArgType.None;
            }
        }

        public static TriggerArgType ForAction(string actionType)
        {
            switch (actionType)
            {
                case ActionTypes.TACTION_CREATE_TEAM:
                case ActionTypes.TACTION_DESTROY_TEAM:
                case ActionTypes.TACTION_REINFORCEMENTS:
                    return TriggerArgType.TeamType;
                case ActionTypes.TACTION_WIN:
                case ActionTypes.TACTION_LOSE:
                case ActionTypes.TACTION_BEGIN_PRODUCTION:
                case ActionTypes.TACTION_FIRE_SALE:
                case ActionTypes.TACTION_AUTOCREATE:
                case ActionTypes.TACTION_ALL_HUNT:
                    return TriggerArgType.HouseType;
                case ActionTypes.TACTION_FORCE_TRIGGER:
                case ActionTypes.TACTION_DESTROY_TRIGGER:
                    return TriggerArgType.Trigger;
                case ActionTypes.TACTION_DZ:
                case ActionTypes.TACTION_REVEAL_SOME:
                case ActionTypes.TACTION_REVEAL_ZONE:
                    return TriggerArgType.Waypoint;
                case ActionTypes.TACTION_1_SPECIAL:
                case ActionTypes.TACTION_FULL_SPECIAL:
                    return TriggerArgType.SuperWeapon;
                case ActionTypes.TACTION_PLAY_MUSIC:
                    return TriggerArgType.MusicTheme;
                case ActionTypes.TACTION_PLAY_MOVIE:
                    return TriggerArgType.Movie;
                case ActionTypes.TACTION_PLAY_SOUND:
                    return TriggerArgType.Sound;
                case ActionTypes.TACTION_PLAY_SPEECH:
                    return TriggerArgType.Speech;
                case ActionTypes.TACTION_PREFERRED_TARGET:
                    return TriggerArgType.PreferredTarget;
                case ActionTypes.TACTION_BASE_BUILDING:
                    return TriggerArgType.BooleanSwitch;
                case ActionTypes.TACTION_TEXT_TRIGGER:
                    return TriggerArgType.TextMessage;
                case ActionTypes.TACTION_ADD_TIMER:
                case ActionTypes.TACTION_SUB_TIMER:
                case ActionTypes.TACTION_SET_TIMER:
                    return TriggerArgType.Number;
                case ActionTypes.TACTION_SET_GLOBAL:
                case ActionTypes.TACTION_CLEAR_GLOBAL:
                    return TriggerArgType.GlobalNumber;
                default:
                    return TriggerArgType.None;
            }
        }

        public static (long Min, long Max)? Range(TriggerArgType argType)
        {
            switch (argType)
            {
                case TriggerArgType.Number:
                    return (0L, int.MaxValue);
                case TriggerArgType.GlobalNumber:
                    return (0L, Constants.HighestGlobal);
                default:
                    return null;
            }
        }

        public static List<(long Value, string Label)> Options(Map map, TriggerArgType argType)
        {
            switch (argType)
            {
                case TriggerArgType.HouseType:
                    return (-1L, House.None).Yield()
                        .Concat(map.Houses.Select(h => ((long)h.Type.ID, h.Type.Name))).ToList();
                case TriggerArgType.BuildingType:
                    return map.BuildingTypes.Select(t => ((long)t.ID, t.DisplayNameWithTheaterInfo)).ToList();
                case TriggerArgType.UnitType:
                    return map.UnitTypes.OfType<VehicleType>().Select(t => ((long)t.ID, t.DisplayName)).ToList();
                case TriggerArgType.InfantryType:
                    return map.InfantryTypes.Select(t => ((long)t.ID, t.DisplayName)).ToList();
                case TriggerArgType.AircraftType:
                    return map.TeamTechnoTypes.OfType<AircraftType>().Select(t => ((long)t.ID, t.DisplayName)).ToList();
                case TriggerArgType.Waypoint:
                    return (-1L, Waypoint.None).Yield()
                        .Concat(map.Waypoints.Select((wp, i) => ((long)i, wp.ToString()))).ToList();
                case TriggerArgType.SuperWeapon:
                    return (-1L, "None").Yield()
                        .Concat(ActionDataTypes.SuperTypes.Select((t, i) => ((long)i, t)))
                        .OrderBy(t => t.Item2).ToList();
                case TriggerArgType.MusicTheme:
                    return NoneEntryFirst(map.ThemeTypes.Select((t, i) => ((long)(i - 1), t))
                        .OrderBy(t => t.Item2));
                case TriggerArgType.Movie:
                    return NoneEntryFirst(map.MovieTypes.Select((t, i) => ((long)(i - 1), t))
                        .OrderBy(t => t.Item2, new ExplorerComparer()));
                case TriggerArgType.Sound:
                    return (-1L, "None").Yield()
                        .Concat(ActionDataTypes.VocDesc.Select((t, i) => ((long)i, t + " (" + ActionDataTypes.VocNames[i] + ")"))
                            .Where(t => !string.Equals(ActionDataTypes.VocNames[(int)t.Item1], "x", StringComparison.OrdinalIgnoreCase))).ToList();
                case TriggerArgType.Speech:
                    return (-1L, "None").Yield()
                        .Concat(ActionDataTypes.VoxDesc.Select((t, i) => ((long)i, t + " (" + ActionDataTypes.VoxNames[i] + ")"))
                            .Where(t => !string.Equals(ActionDataTypes.VoxNames[(int)t.Item1], "none", StringComparison.OrdinalIgnoreCase))).ToList();
                case TriggerArgType.PreferredTarget:
                    return TeamMissionTypes.Attack.DropdownOptions.Select(t => ((long)t.Value, t.Label)).ToList();
                case TriggerArgType.BooleanSwitch:
                    return new List<(long, string)> { (0L, "Off"), (1L, "On") };
                case TriggerArgType.TextMessage:
                    return ActionDataTypes.TextDesc.Select((t, i) => ((long)i + 1, (i + 1).ToString("000") + " " + t)).ToList();
                default:
                    return null;
            }
        }

        public static void CoerceEvent(Map map, TriggerEvent evt)
        {
            if (evt == null)
            {
                return;
            }
            TriggerArgType argType = ForEvent(evt.EventType);
            switch (argType)
            {
                case TriggerArgType.None:
                    evt.Data = 0;
                    evt.Team = TeamType.None;
                    break;
                case TriggerArgType.TeamType:
                    evt.Data = 0;
                    evt.Team = MatchTeam(map, evt.Team);
                    break;
                case TriggerArgType.Number:
                case TriggerArgType.GlobalNumber:
                    (long min, long max) = Range(argType).Value;
                    evt.Data = evt.Data.Restrict(min, max);
                    break;
                default:
                    evt.Data = TriggerArg.CheckInList(evt.Data, Options(map, argType));
                    break;
            }
        }

        public static void CoerceAction(Map map, TriggerAction act, IEnumerable<Trigger> currentTriggers)
        {
            if (act == null)
            {
                return;
            }
            TriggerArgType argType = ForAction(act.ActionType);
            switch (argType)
            {
                case TriggerArgType.None:
                    break;
                case TriggerArgType.TeamType:
                    act.Team = MatchTeam(map, act.Team);
                    break;
                case TriggerArgType.Trigger:
                    bool known = Trigger.None.Yield().Concat((currentTriggers ?? Enumerable.Empty<Trigger>()).Select(t => t.Name))
                        .Contains(act.Trigger, StringComparer.OrdinalIgnoreCase);
                    act.Trigger = known ? act.Trigger : Trigger.None;
                    break;
                case TriggerArgType.Number:
                case TriggerArgType.GlobalNumber:
                    (long min, long max) = Range(argType).Value;
                    act.Data = act.Data.Restrict(min, max);
                    break;
                default:
                    act.Data = TriggerArg.CheckInList(act.Data, Options(map, argType));
                    break;
            }
        }

        internal static string MatchTeam(Map map, string team)
        {
            return TeamType.None.Yield().Concat(map.TeamTypes.Select(t => t.Name))
                .FirstOrDefault(tm => tm.Equals(team, StringComparison.OrdinalIgnoreCase)) ?? TeamType.None;
        }

        private static List<(long Value, string Label)> NoneEntryFirst(IEnumerable<(long Value, string Label)> sorted)
        {
            List<(long Value, string Label)> options = sorted.ToList();
            int noneIndex = options.FindIndex(t => t.Value == -1);
            if (noneIndex > 0)
            {
                (long Value, string Label) none = options[noneIndex];
                options.RemoveAt(noneIndex);
                options.Insert(0, none);
            }
            return options;
        }
    }
}
