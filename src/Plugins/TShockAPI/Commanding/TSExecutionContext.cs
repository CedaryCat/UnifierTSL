using UnifierTSL.Surface.Activities;
using System.Diagnostics.CodeAnalysis;
using UnifierTSL.Surface.Status;
using UnifierTSL.Commanding;

namespace TShockAPI.Commanding
{
    public sealed class TSExecutionContext : TerminalExecutionContext
    {
        private ActivityHandle? consoleActivity;

        [SetsRequiredMembers]
        public TSExecutionContext(
            CommandExecutor executor,
            bool silent,
            CommandInvocationTarget target) {
            Executor = executor;
            Silent = silent;
            Target = target;
            Server = executor.SourceServer;
            ExecutionFeedback = new TSExecutionFeedback(activity: null);
        }

        public CommandExecutor Executor { get; }
        public TSPlayer? Player => Executor.InGamePlayer;
        public bool Silent { get; }
        public ActivityHandle? ConsoleActivity {
            get => consoleActivity;
            set {
                consoleActivity = value;
                ExecutionFeedback = new TSExecutionFeedback(value);
            }
        }
    }
}
