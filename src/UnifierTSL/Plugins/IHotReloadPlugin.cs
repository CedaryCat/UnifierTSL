namespace UnifierTSL.Plugins
{
    /// <summary>
    /// Optional plugin contract for cooperative hot-reload handoff.
    /// </summary>
    public interface IHotReloadPlugin
    {
        ValueTask<HotReloadExportResult> ExportHotReloadStateAsync(HotReloadExportContext context, CancellationToken cancellationToken = default);
        Task<HotReloadPrepareResult> PrepareHotReloadAsync(HotReloadPrepareContext context, CancellationToken cancellationToken = default);
        Task<HotReloadActivateResult> ActivateHotReloadAsync(HotReloadActivateContext context, CancellationToken cancellationToken = default);
        Task<HotReloadAbortResult> ResumeAfterHotReloadAbortAsync(HotReloadAbortContext context, CancellationToken cancellationToken = default);
    }
}
