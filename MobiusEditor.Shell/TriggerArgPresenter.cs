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
