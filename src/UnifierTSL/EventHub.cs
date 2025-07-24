using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnifierTSL.Events.Handlers;

namespace UnifierTSL
{
    public class EventHub
    {
        public readonly ChatHandler Chat = new();
        public readonly CoordinatorEventBridge Coordinator = new();
        public readonly GameEventBridge Game = new();
        public readonly NetplayEventBridge Netplay = new();
        public readonly ServerEventBridge Server = new();
    }
}
