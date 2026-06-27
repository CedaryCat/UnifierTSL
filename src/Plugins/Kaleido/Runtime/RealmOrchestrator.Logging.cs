namespace Kaleido.Runtime
{
    public sealed partial class RealmOrchestrator
    {
        private void LogError(string category, string message, Exception ex) {
            try {
                logger.Error(category: category, message: message, ex: ex);
            }
            catch {
            }
        }
    }
}
