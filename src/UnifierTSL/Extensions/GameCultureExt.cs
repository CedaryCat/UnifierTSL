using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terraria.Localization;

namespace UnifierTSL.Extensions
{
    public static class GameCultureExt
    {
        public static CultureInfo RedirectedCultureInfo(this GameCulture culture) {
            return culture.CultureInfo.Name == "zh-Hans" ? new CultureInfo("zh-CN") : culture.CultureInfo;
        }
    }
}
