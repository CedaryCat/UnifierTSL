namespace UnifierTSL.FileSystem
{
    public interface IFileMonitorHandle : IDisposable
    {
        Task InternalModifyAsync(Func<Task> modification);
        void InternalModify(Action modification);
    }

}
