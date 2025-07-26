using System.Diagnostics;
using System.Runtime.InteropServices;

namespace UnifierTSL.Logging.LogTrace
{
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public readonly struct TraceContext(Guid correlationId, TraceId traceId, SpanId spanId)
    {
        [FieldOffset(00)] public readonly Guid CorrelationId = correlationId;
        [FieldOffset(16)] public readonly TraceId TraceId = traceId;
        [FieldOffset(32)] public readonly SpanId SpanId = spanId;
        public override string ToString()
            => $"[CorrelationId: {CorrelationId}] [TraceId: {TraceId}] [SpanId: {SpanId}]";
    }
}
