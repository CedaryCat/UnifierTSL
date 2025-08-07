using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnifierTSL.Publisher
{
    public static class CLIHelper
    {
        /// <summary>
        /// --key1 value1 --key2 value2
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        public static Dictionary<string, List<string>> ParseArguements(string[] args) {
            string? key = null;
            string value = "";
            Dictionary<string, List<string>> dictionary = new Dictionary<string, List<string>>();
            for (int i = 0; i < args.Length; i++) {
                if (args[i].Length == 0) {
                    continue;
                }

                if (args[i][0] == '-' || args[i][0] == '+') {
                    if (key is not null) {
                        if (!dictionary.TryGetValue(key.ToLower(), out var values)) {
                            dictionary.Add(key.ToLower(), values = []);
                        }
                        values.Add(value);
                    }

                    key = args[i];
                    value = "";
                }
                else {
                    if (value != "") {
                        value += " ";
                    }

                    value += args[i];
                }
            }

            if (key is not null) {
                if (!dictionary.TryGetValue(key.ToLower(), out var values)) {
                    dictionary.Add(key.ToLower(), values = []);
                }
                values.Add(value);
            }

            return dictionary;
        }
        /// <summary>
        /// --key1 sub1:val1 sub2:val2 --key2 sub3:val3
        /// </summary>
        /// <param name="input"></param>
        /// <param name="result"></param>
        /// <returns></returns>
        public static bool TryParseSubArguements(string input, [NotNullWhen(true)] out Dictionary<string, string>? result) {
            result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var sb = new StringBuilder();
            string? currentKey = null;
            bool inQuotes = false;
            bool readingValue = false;
            bool hasColon = false;

            for (int i = 0; i <= input.Length; i++) {
                char c = i < input.Length ? input[i] : ' '; // simulate trailing whitespace

                if (c == '"' && (i == 0 || input[i - 1] != '\\')) {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (!inQuotes && c == ':') {
                    if (!readingValue) {
                        currentKey = sb.ToString().Trim();
                        sb.Clear();
                        readingValue = true;
                        hasColon = true;
                        continue;
                    }
                    else if (currentKey is not null) {
                        var keySb = new StringBuilder();
                        for (int j = sb.Length - 1; j >= 0; j--) {
                            if (char.IsWhiteSpace(sb[j])) {
                                break;
                            }
                            keySb.Insert(0, sb[j]);
                            sb.Remove(j, 1);
                        }
                        result[currentKey] = sb.ToString().Trim();
                        currentKey = keySb.ToString().Trim();
                        sb.Clear();
                        readingValue = true;
                        hasColon = true;
                        continue;
                    }
                }

                if (!inQuotes && char.IsWhiteSpace(c)) {
                    if (sb.Length == 0 && !readingValue) continue;
                }

                sb.Append(c);
            }

            if (currentKey is not null && sb.Length > 0) {
                result[currentKey] = sb.ToString().Trim();
            }

            if (!hasColon) {
                result = null;
                return false;
            }

            return true;
        }
    }
}
