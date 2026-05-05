using Atelier.Session;
using Atelier.Session.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Atelier.Presentation.Window.Formatting {
    internal static class RoslynFormattingWorkspace {
        private static readonly Lock Sync = new();
        private static AdhocWorkspace? workspace;

        public static void Initialize() {
            lock (Sync) {
                workspace ??= RoslynHost.CreateWorkspace();
            }
        }

        public static ImmutableArray<SourceEdit> GetFormattedTextChanges(SyntaxNode root, TextSpan span) {
            lock (Sync) {
                if (workspace is null) {
                    throw new InvalidOperationException(GetString("Atelier Roslyn formatting workspace is not initialized."));
                }

                return TextEditPlan.ToSourceEdits(Formatter.GetFormattedTextChanges(
                    root,
                    span,
                    workspace,
                    workspace.Options,
                    CancellationToken.None));
            }
        }

        public static void Dispose() {
            AdhocWorkspace? workspaceToDispose;
            lock (Sync) {
                workspaceToDispose = workspace;
                workspace = null;
            }

            workspaceToDispose?.Dispose();
        }
    }
}
