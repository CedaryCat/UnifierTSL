using UnifierTSL.Surface.Activities;
using UnifierTSL.Surface.Status;

namespace UnifierTSL.Commanding
{
    public sealed class SurfaceActivityExecutionFeedback : ICommandExecutionFeedback
    {
        private readonly ActivityHandle activity;

        public SurfaceActivityExecutionFeedback(ActivityHandle activity) {
            ArgumentNullException.ThrowIfNull(activity);
            this.activity = activity;
        }

        public CancellationToken CancellationToken => activity.CancellationToken;

        public bool SupportsLiveProgress => true;

        public bool SupportsBackgroundExecution => false;

        public void SetStatus(string message) {
            activity.Message = message;
        }

        public void SetProgress(
            long current,
            long total,
            CommandExecutionProgressStyle style = CommandExecutionProgressStyle.Ratio) {
            activity.ProgressStyle = style switch {
                CommandExecutionProgressStyle.Percent => ActivityProgressStyle.Percent,
                _ => ActivityProgressStyle.Ratio,
            };
            activity.ProgressEnabled = true;
            activity.ProgressTotal = total;
            activity.ProgressCurrent = current;
        }

        public void ClearProgress() {
            activity.ProgressEnabled = false;
            activity.ProgressTotal = 0;
            activity.ProgressCurrent = 0;
        }
    }
}
