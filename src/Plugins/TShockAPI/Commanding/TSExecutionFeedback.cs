using UnifierTSL.Surface.Activities;
using UnifierTSL.Commanding;

namespace TShockAPI.Commanding
{
    internal sealed class TSExecutionFeedback : ICommandExecutionFeedback
    {
        private readonly SurfaceActivityExecutionFeedback? surfaceFeedback;

        public TSExecutionFeedback(ActivityHandle? activity) {
            if (activity is not null) {
                surfaceFeedback = new SurfaceActivityExecutionFeedback(activity);
            }
        }

        public CancellationToken CancellationToken => surfaceFeedback?.CancellationToken ?? default;

        public bool SupportsLiveProgress => surfaceFeedback is not null;

        public bool SupportsBackgroundExecution => false;

        public void SetStatus(string message) {
            surfaceFeedback?.SetStatus(message);
        }

        public void SetProgress(
            long current,
            long total,
            CommandExecutionProgressStyle style = CommandExecutionProgressStyle.Ratio) {
            surfaceFeedback?.SetProgress(current, total, style);
        }

        public void ClearProgress() {
            surfaceFeedback?.ClearProgress();
        }
    }
}
