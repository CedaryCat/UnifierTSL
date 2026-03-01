using Microsoft.Xna.Framework;

namespace UnifierTSL.Extensions
{
    public static class ColorExt
    {
        private static readonly Dictionary<ConsoleColor, Color> _consoleColorMap = new() {
            [ConsoleColor.Black] = new Color(0, 0, 0),
            [ConsoleColor.DarkBlue] = new Color(0, 0, 128),
            [ConsoleColor.DarkGreen] = new Color(0, 128, 0),
            [ConsoleColor.DarkCyan] = new Color(0, 128, 128),
            [ConsoleColor.DarkRed] = new Color(128, 0, 0),
            [ConsoleColor.DarkMagenta] = new Color(128, 0, 128),
            [ConsoleColor.DarkYellow] = new Color(128, 128, 0),
            [ConsoleColor.Gray] = new Color(192, 192, 192),
            [ConsoleColor.DarkGray] = new Color(128, 128, 128),
            [ConsoleColor.Blue] = new Color(0, 0, 255),
            [ConsoleColor.Green] = new Color(0, 255, 0),
            [ConsoleColor.Cyan] = new Color(0, 255, 255),
            [ConsoleColor.Red] = new Color(255, 0, 0),
            [ConsoleColor.Magenta] = new Color(255, 0, 255),
            [ConsoleColor.Yellow] = new Color(255, 255, 0),
            [ConsoleColor.White] = new Color(255, 255, 255)
        };

        /// <summary>
        /// Convert XNA color to the closest console color
        /// </summary>
        public static ConsoleColor ToConsoleColor(this Color color) {
            Color rgb = new(color.R, color.G, color.B);

            ConsoleColor closest = ConsoleColor.Gray;
            double minDistance = double.MaxValue;

            foreach (KeyValuePair<ConsoleColor, Color> kvp in _consoleColorMap) {
                double distance = CalculateDistance(rgb, kvp.Value);
                if (distance < minDistance) {
                    minDistance = distance;
                    closest = kvp.Key;
                }
            }
            return closest;
        }

        /// <summary>
        /// Calculate the Euclidean distance in RGB space
        /// </summary>
        /// <param name="c1"></param>
        /// <param name="c2"></param>
        /// <returns></returns>
        private static double CalculateDistance(Color c1, Color c2) {
            // The human eye is most sensitive to green, next to red, and least to blue
            const double rWeight = 0.3;
            const double gWeight = 0.6;
            const double bWeight = 0.1;

            int dr = c1.R - c2.R;
            int dg = c1.G - c2.G;
            int db = c1.B - c2.B;

            return Math.Sqrt(
                rWeight * dr * dr +
                gWeight * dg * dg +
                bWeight * db * db
            );
        }
    }
}
