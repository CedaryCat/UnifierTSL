using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnifierTSL.Logging;

namespace ExamplePlugin
{
    public class ExampleTool
    {
        public static void DoSomething(IStandardLogger logger) {
            logger.Info("Say hi!");
        }
    }
}
