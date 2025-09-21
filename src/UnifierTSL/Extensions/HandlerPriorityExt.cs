using UnifierTSL.Events.Core;

namespace UnifierTSL.Extensions
{
    public static class HandlerPriorityExt
    {
        private const int highestLimit = byte.MinValue;
        private const int lowestLimit = byte.MaxValue;
        public static HandlerPriority Lower(this HandlerPriority priority, byte step = 1) {
            int p = (byte)priority;
            if (p + step > lowestLimit) {
                return (HandlerPriority)lowestLimit;
            }
            return (HandlerPriority)p;
        }
        public static HandlerPriority Higher(this HandlerPriority priority, byte step = 1) {
            int p = (byte)priority;
            if (p - step < highestLimit) {
                return (HandlerPriority)highestLimit;
            }
            return (HandlerPriority)p;
        }
    }
}
