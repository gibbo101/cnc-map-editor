//
// The Tiberian Dawn event/action parameter-type tables, ported from the fork's
// TriggersDialog coercion switch (shared by Sole Survivor). TD trigger actions carry no
// parameter at all; events carry either nothing, a free number, or a building type.
// Coercing a no-argument TD event blanks Data but — unlike Red Alert — leaves Team alone,
// matching the dialog.
using System.Collections.Generic;
using System.Linq;
using MobiusEditor.Model;
using MobiusEditor.Utility;

namespace MobiusEditor.TiberianDawn
{
    public static class TriggerArgTypes
    {
        public static TriggerArgType ForEvent(string eventType)
        {
            switch (eventType)
            {
                case EventTypes.EVENT_TIME:
                case EventTypes.EVENT_CREDITS:
                case EventTypes.EVENT_NUNITS_DESTROYED:
                case EventTypes.EVENT_NBUILDINGS_DESTROYED:
                    return TriggerArgType.Number;
                case EventTypes.EVENT_BUILD:
                    return TriggerArgType.BuildingType;
                default:
                    return TriggerArgType.None;
            }
        }

        public static (long Min, long Max)? Range(TriggerArgType argType)
        {
            switch (argType)
            {
                case TriggerArgType.Number:
                case TriggerArgType.GlobalNumber:
                    return (0L, int.MaxValue);
                default:
                    return null;
            }
        }

        public static List<(long Value, string Label)> Options(Map map, TriggerArgType argType)
        {
            switch (argType)
            {
                case TriggerArgType.BuildingType:
                    return map.BuildingTypes.Select(t => ((long)t.ID, t.DisplayNameWithTheaterInfo)).ToList();
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
                    break;
                case TriggerArgType.Number:
                    evt.Data = evt.Data.Restrict(0, int.MaxValue);
                    break;
                default:
                    evt.Data = TriggerArg.CheckInList(evt.Data, Options(map, argType));
                    break;
            }
        }
    }
}
