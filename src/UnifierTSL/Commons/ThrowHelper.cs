using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace UnifierTSL.Commons
{
    internal static class TSLThrowHelper
    {
        internal static void ThrowIfNull([NotNull] object? argument, [CallerArgumentExpression("argument")] string? paramName = null) {
            if (argument == null) {
                Throw(paramName);
            }
        }

        [DoesNotReturn]
        private static void Throw(string paramName) {
            throw new ArgumentNullException(paramName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [return: NotNull]
        public static string IfNullOrWhitespace([NotNull] string? argument, [CallerArgumentExpression("argument")] string paramName = "") {
            if (string.IsNullOrWhiteSpace(argument)) {
                if (argument == null) {
                    throw new ArgumentNullException(paramName);
                }

                throw new ArgumentException(paramName, "Argument is whitespace");
            }

            return argument;
        }
    }
}
