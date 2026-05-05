using Atelier.Presentation.Prompt;
using Atelier.Session;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using UnifierTSL.Surface.Prompting.Model;

namespace Atelier.Session.Roslyn
{
    internal sealed class ReplWorkspace : IDisposable
    {
        private readonly AdhocWorkspace workspace;
        private readonly SubmissionProjectChain submissionChain;

        public ReplWorkspace(FrontendConfiguration configuration) {
            workspace = RoslynHost.CreateWorkspace();
            submissionChain = new SubmissionProjectChain(
                workspace,
                configuration ?? throw new ArgumentNullException(nameof(configuration)));
        }

        public async Task<WorkspaceAnalysis> AnalyzeAsync(
            IReadOnlyList<CommittedSubmission> committedHistory,
            SyntheticDocument syntheticDocument,
            CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();

            var document = submissionChain.CreateDraftDocument(committedHistory, syntheticDocument);
            var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var isCompleteSubmission = root is null || SyntaxFactory.IsCompleteSubmission(root.SyntaxTree);
            var diagnostics = await GetDiagnosticsAsync(document, sourceText, syntheticDocument, cancellationToken).ConfigureAwait(false);
            var highlights = await CreateHighlightsAsync(document, syntheticDocument, cancellationToken).ConfigureAwait(false);
            var signatureHelp = await SignatureHelpProvider.GetSignatureHelpAsync(document, syntheticDocument, cancellationToken).ConfigureAwait(false);
            var completion = await CompletionProvider.GetCompletionInfoAsync(document, sourceText, syntheticDocument, cancellationToken).ConfigureAwait(false);
            return new WorkspaceAnalysis(
                diagnostics,
                highlights,
                signatureHelp,
                completion,
                isCompleteSubmission);
        }

        public void Dispose() {
            workspace.Dispose();
        }

        private static async Task<ImmutableArray<DiagnosticInfo>> GetDiagnosticsAsync(
            Document document,
            SourceText sourceText,
            SyntheticDocument syntheticDocument,
            CancellationToken cancellationToken) {
            var compilation = await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null) {
                return [];
            }

            var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
            return [.. compilation
                .GetDiagnostics(cancellationToken)
                .Where(diagnostic => diagnostic.Severity != DiagnosticSeverity.Hidden)
                .Where(diagnostic => diagnostic.Location == Location.None || diagnostic.Location.SourceTree == syntaxTree)
                .Select(diagnostic => TryCreateDiagnosticInfo(diagnostic, sourceText, syntheticDocument, out var info) ? info : null)
                .Where(static info => info is not null)
                .Cast<DiagnosticInfo>()];
        }

        private static bool TryCreateDiagnosticInfo(
            Diagnostic diagnostic,
            SourceText sourceText,
            SyntheticDocument syntheticDocument,
            out DiagnosticInfo? info) {
            var displayText = BuildDiagnosticDisplayText(diagnostic, sourceText, syntheticDocument, out var draftStart, out var draftLength);
            if (diagnostic.Location != Location.None && diagnostic.Location.IsInSource && draftStart is null) {
                info = null;
                return false;
            }

            info = new DiagnosticInfo(diagnostic.Id, diagnostic.Severity, diagnostic.GetMessage(), draftStart, draftLength, displayText);
            return true;
        }

        private static string BuildDiagnosticDisplayText(
            Diagnostic diagnostic,
            SourceText sourceText,
            SyntheticDocument syntheticDocument,
            out int? draftStart,
            out int? draftLength) {
            draftStart = null;
            draftLength = null;

            var severity = diagnostic.Severity.ToString().ToLowerInvariant();
            if (diagnostic.Location == Location.None || !diagnostic.Location.IsInSource) {
                return $"{severity} {diagnostic.Id}: {diagnostic.GetMessage()}";
            }

            var span = diagnostic.Location.SourceSpan;
            if (span.End < syntheticDocument.DraftStart || span.Start > syntheticDocument.DraftEnd) {
                return $"{severity} {diagnostic.Id}: {diagnostic.GetMessage()}";
            }

            var draftSourceStart = Math.Clamp(span.Start - syntheticDocument.DraftStart, 0, syntheticDocument.DraftLength);
            var draftSourceEnd = Math.Clamp(span.End - syntheticDocument.DraftStart, draftSourceStart, syntheticDocument.DraftLength);
            if (!syntheticDocument.TryMapDraftSourceSpan(
                    draftSourceStart,
                    draftSourceEnd - draftSourceStart,
                    out var encodedStart,
                    out var encodedLength)) {
                return $"{severity} {diagnostic.Id}: {diagnostic.GetMessage()}";
            }

            draftStart = encodedStart;
            draftLength = encodedLength;

            var draftBaseLine = sourceText.Length == 0
                ? 0
                : sourceText.Lines.GetLineFromPosition(Math.Min(syntheticDocument.DraftStart, sourceText.Length - 1)).LineNumber;
            var linePosition = sourceText.Lines.GetLinePosition(syntheticDocument.DraftStart + draftSourceStart);
            var line = linePosition.Line - draftBaseLine + 1;
            var column = linePosition.Character + 1;
            return $"{severity} {diagnostic.Id} L{line}:C{column}: {diagnostic.GetMessage()}";
        }

        private static async Task<ImmutableArray<PromptHighlightSpan>> CreateHighlightsAsync(
            Document document,
            SyntheticDocument syntheticDocument,
            CancellationToken cancellationToken) {
            var builder = ImmutableArray.CreateBuilder<PromptHighlightSpan>();
            await AppendClassifiedHighlightsAsync(builder, document, syntheticDocument, cancellationToken).ConfigureAwait(false);

            return [.. builder
                .OrderBy(span => span.StartIndex)
                .ThenBy(span => span.Length)];
        }

        private static async Task AppendClassifiedHighlightsAsync(
            ImmutableArray<PromptHighlightSpan>.Builder builder,
            Document document,
            SyntheticDocument syntheticDocument,
            CancellationToken cancellationToken) {
            if (syntheticDocument.DraftLength <= 0) {
                return;
            }

            try {
                var draftSpan = TextSpan.FromBounds(syntheticDocument.DraftStart, syntheticDocument.DraftEnd);
                var classifiedSpans = await Classifier.GetClassifiedSpansAsync(document, draftSpan, cancellationToken).ConfigureAwait(false);
                foreach (var classifiedSpanGroup in classifiedSpans.GroupBy(static span => span.TextSpan)) {
                    var styleId = classifiedSpanGroup
                        .Select(static span => RoslynStyleResolver.ResolveClassificationStyleId(span.ClassificationType))
                        .FirstOrDefault(static styleId => !string.IsNullOrWhiteSpace(styleId));
                    if (string.IsNullOrWhiteSpace(styleId)) {
                        continue;
                    }

                    if (!TryMapDraftHighlightSpan(classifiedSpanGroup.Key, syntheticDocument, out var start, out var length)) {
                        continue;
                    }

                    builder.Add(new PromptHighlightSpan(start, length, styleId));
                }
            }
            catch (OperationCanceledException) {
                throw;
            }
            catch {
            }
        }

        private static bool TryMapDraftHighlightSpan(TextSpan span, SyntheticDocument syntheticDocument, out int start, out int length) {
            if (span.End <= syntheticDocument.DraftStart || span.Start >= syntheticDocument.DraftEnd) {
                start = 0;
                length = 0;
                return false;
            }

            start = Math.Clamp(span.Start - syntheticDocument.DraftStart, 0, syntheticDocument.DraftLength);
            var end = Math.Clamp(span.End - syntheticDocument.DraftStart, start, syntheticDocument.DraftLength);
            length = end - start;
            return length > 0;
        }

    }
}
