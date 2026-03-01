namespace UnifierTSL.Logging
{
    internal interface ILogHistorySink
    {
        void Write(scoped in LogRecordView record);
    }
}
