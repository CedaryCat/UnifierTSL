using UnifierTSL.Commanding.Bindings;
using UnifierTSL.Commanding.Builtin;
using UnifierTSL.Commanding.Endpoints;
using UnifierTSL.Commanding.Prompting;
using UnifierTSL.Events.Handlers;

namespace UnifierTSL.Commanding.Composition
{
    internal static class CommandingBootstrap
    {
        private static int installed;
        private static CommandInstallHandle? commandRegistration;

        public static void Install() {
            if (Interlocked.Exchange(ref installed, 1) != 0) {
                return;
            }

            commandRegistration = CommandSystem.Install(ConfigureCoreCommands);
            CommandPrompting.Install();
        }

        private static void ConfigureCoreCommands(CommandRegistrationBuilder context) {
            context.AddControllerGroup<BuiltinCommandController>();
            context.AddBindings(CommandCommonParameterRules.Configure);
            context.AddEndpointBinding<TerminalCommandEndpoint>(TerminalCommandEndpoint.BindAction);
            context.AddOutcomeWriter<MessageSender, TerminalCommandOutcomeWriter>();
        }
    }
}
