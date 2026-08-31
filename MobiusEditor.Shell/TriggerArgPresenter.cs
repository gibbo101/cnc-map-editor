//
// Turns a trigger event/action into the value control a dialog shows for it: nothing, a
// number spinner with a range, a Data-bound option list, or a name list (teams from the
// map, triggers from the working list being edited). The value is coerced into validity
// first — the headless equivalent of the fork dialog's Update*Controls switches, built on
// the plugin's structured arg-type tables.
using System.Collections.Generic;
using System.Linq;
using MobiusEditor.Interface;
using MobiusEditor.Model;
using MobiusEditor.Utility;

namespace MobiusEditor.Shell
{
    public enum TriggerArgControl
    {
        None,
        Number,
        DataList,
        NameList,
    }

    public sealed class TriggerArgPresentation
    {
        public TriggerArgType ArgType { get; internal set; }
        public TriggerArgControl Control { get; internal set; }
        /// <summary>Number: inclusive spinner range and the current (coerced) value.</summary>
        public long Min { get; internal set; }
        public long Max { get; internal set; }
        public long Value { get; internal set; }
        /// <summary>DataList: the options; Value is the selected one.</summary>
        public IReadOnlyList<(long Value, string Label)> Options { get; internal set; }
        /// <summary>NameList: the names; Name is the selected one.</summary>
        public IReadOnlyList<string> Names { get; internal set; }
        public string Name { get; internal set; }
    }

    public static class TriggerArgPresenter
    {
        /// <summary>Coerces the event for its type and describes its value control.</summary>
        public static TriggerArgPresentation ForEvent(IGamePlugin plugin, TriggerEvent evt)
        {
            plugin.CoerceEventArg(evt);
            return Present(plugin, plugin.GetEventArgType(evt.EventType), evt.Data, evt.Team, null, null);
        }

        /// <summary>Coerces the action for its type and describes its value control; trigger references list the working set.</summary>
        public static TriggerArgPresentation ForAction(IGamePlugin plugin, TriggerAction act, IEnumerable<Trigger> workingTriggers)
        {
            List<Trigger> working = workingTriggers?.ToList() ?? new List<Trigger>();
            plugin.CoerceActionArg(act, working);
            return Present(plugin, plugin.GetActionArgType(act.ActionType), act.Data, act.Team, act.Trigger, working);
        }

        /// <summary>
        /// The value control for a teamtype order's argument, per its TeamMissionArgType —
        /// the fork's MissionItemControl switch. Waypoint orders share the trigger waypoint
        /// option list; options-list orders use the mission's own dropdown; numeric kinds get
        /// a spinner with the fork's ranges. The returned Value is the argument coerced into
        /// validity (clamped, or snapped to the first option).
        /// </summary>
        public static TriggerArgPresentation ForTeamMission(IGamePlugin plugin, TeamMission mission, int argument)
        {
            TriggerArgPresentation p = new TriggerArgPresentation();
            switch (mission.ArgType)
            {
                case TeamMissionArgType.None:
                    p.Control = TriggerArgControl.None;
                    break;
                case TeamMissionArgType.Waypoint:
                    p.Control = TriggerArgControl.DataList;
                    p.Options = plugin.GetArgOptions(TriggerArgType.Waypoint);
                    p.Value = TriggerArg.CheckInList(argument, p.Options.ToList());
                    break;
                case TeamMissionArgType.OptionsList:
                    p.Control = TriggerArgControl.DataList;
                    p.Options = mission.DropdownOptions.Select(o => ((long)o.Value, o.Label)).ToList();
                    p.Value = TriggerArg.CheckInList(argument, p.Options.ToList());
                    break;
                case TeamMissionArgType.GlobalNumber:
                    (long gMin, long gMax) = plugin.GetArgRange(TriggerArgType.GlobalNumber) ?? (0L, int.MaxValue);
                    p.Control = TriggerArgControl.Number;
                    p.Min = gMin;
                    p.Max = gMax;
                    p.Value = ((long)argument).Restrict(gMin, gMax);
                    break;
                case TeamMissionArgType.MapCell:
                    p.Control = TriggerArgControl.Number;
                    p.Min = 0;
                    p.Max = plugin.Map.Metrics.Length - 1;
                    p.Value = ((long)argument).Restrict(p.Min, p.Max);
                    break;
                default:
                    // Number, Time, MissionNumber, Tarcom: a plain non-negative spinner.
                    p.Control = TriggerArgControl.Number;
                    p.Min = 0;
                    p.Max = int.MaxValue;
                    p.Value = ((long)argument).Restrict(p.Min, p.Max);
                    break;
            }
            return p;
        }

        private static TriggerArgPresentation Present(IGamePlugin plugin, TriggerArgType argType, long data, string team, string trigger, List<Trigger> working)
        {
            TriggerArgPresentation p = new TriggerArgPresentation { ArgType = argType };
            switch (TriggerArg.FieldOf(argType))
            {
                case TriggerArgField.None:
                    p.Control = TriggerArgControl.None;
                    break;
                case TriggerArgField.Team:
                    p.Control = TriggerArgControl.NameList;
                    p.Names = TeamType.None.Yield().Concat(plugin.Map.TeamTypes.Select(t => t.Name)).ToList();
                    p.Name = team;
                    break;
                case TriggerArgField.Trigger:
                    p.Control = TriggerArgControl.NameList;
                    p.Names = Trigger.None.Yield().Concat(working.Select(t => t.Name)).ToList();
                    p.Name = trigger;
                    break;
                default:
                    (long Min, long Max)? range = plugin.GetArgRange(argType);
                    if (range.HasValue)
                    {
                        p.Control = TriggerArgControl.Number;
                        p.Min = range.Value.Min;
                        p.Max = range.Value.Max;
                        p.Value = data;
                    }
                    else
                    {
                        p.Control = TriggerArgControl.DataList;
                        p.Options = plugin.GetArgOptions(argType);
                        p.Value = data;
                    }
                    break;
            }
            return p;
        }
    }
}
