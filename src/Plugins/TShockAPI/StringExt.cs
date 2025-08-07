using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TShockAPI
{
    public static class StringExt
    {
        //Can't name it Format :(
        public static string SFormat(this string str, params object[] args) {
            return string.Format(str, args);
        }

        /// <summary>
        /// Wraps the string representation of an object with a Terraria color code for the given color
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="color"></param>
        /// <returns></returns>
        public static string Color(this object obj, string color) {
            return $"[c/{color}:{obj}]";
        }
    }
}
