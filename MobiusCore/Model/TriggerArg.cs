//
// The structured description of the parameter a trigger event or action carries: which
// kind of value it is, and which field of the event/action holds it. Per-game tables map
// event/action type strings onto these; consumers get them through IGamePlugin so editing
// UIs and generators stay game-agnostic.
using System.Collections.Generic;
using System.Linq;

namespace MobiusEditor.Model
{
    /// <summary>The kind of parameter a trigger event or action carries.</summary>
    public enum TriggerArgType
    {
        /// <summary>No parameter.</summary>
        None,
        /// <summary>Free number in Data (a count, credits, or tenth-minute time).</summary>
        Number,
        /// <summary>Global flag number in Data, capped at the game's highest global.</summary>
        GlobalNumber,
        /// <summary>House ID in Data; -1 is none.</summary>
        HouseType,
        /// <summary>Building type ID in Data.</summary>
        BuildingType,
        /// <summary>Vehicle type ID in Data.</summary>
        UnitType,
        /// <summary>Infantry type ID in Data.</summary>
        InfantryType,
        /// <summary>Aircraft type ID in Data.</summary>
        AircraftType,
        /// <summary>Teamtype name in the Team field.</summary>
        TeamType,
        /// <summary>Trigger name in the Trigger field.</summary>
        Trigger,
        /// <summary>Waypoint index in Data; -1 is none.</summary>
        Waypoint,
        /// <summary>Special weapon index in Data; -1 is none.</summary>
        SuperWeapon,
        /// <summary>Music theme index in Data; -1 is the no-theme entry.</summary>
        MusicTheme,
        /// <summary>Movie index in Data; -1 is the no-movie entry.</summary>
        Movie,
        /// <summary>Sound effect index in Data.</summary>
        Sound,
        /// <summary>EVA speech index in Data.</summary>
        Speech,
        /// <summary>Attack-quarry value in Data.</summary>
        PreferredTarget,
        /// <summary>Off/On switch in Data.</summary>
        BooleanSwitch,
        /// <summary>Text message ID in Data (1-based).</summary>
        TextMessage,
    }

    /// <summary>Which field of a TriggerEvent/TriggerAction carries the parameter.</summary>
    public enum TriggerArgField
    {
        None,
        Data,
        Team,
        Trigger,
    }

    public static class TriggerArg
    {
        public static TriggerArgField FieldOf(TriggerArgType argType)
        {
            switch (argType)
            {
                case TriggerArgType.None:
                    return TriggerArgField.None;
                case TriggerArgType.TeamType:
                    return TriggerArgField.Team;
                case TriggerArgType.Trigger:
                    return TriggerArgField.Trigger;
                default:
                    return TriggerArgField.Data;
            }
        }

        /// <summary>The value when the option list contains it, otherwise the first option's value.</summary>
        public static long CheckInList(long value, List<(long Value, string Label)> options)
        {
            if (options == null || options.Count == 0)
            {
                return value;
            }
            return options.Any(o => o.Value == value) ? value : options[0].Value;
        }
    }
}
