using Atelier.Session;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Atelier.Session.Roslyn
{
    internal sealed class SubmissionProjectChain
    {
        private readonly FrontendConfiguration configuration;
        private readonly ProjectId draftProjectId = ProjectId.CreateNewId(AtelierIds.ReplProjectName);
        private readonly DocumentId draftDocumentId;
        private Solution committedSolution;
        private ImmutableArray<SubmissionProjectNode> committedNodes = [];

        public SubmissionProjectChain(AdhocWorkspace workspace, FrontendConfiguration configuration) {
            this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            draftDocumentId = DocumentId.CreateNewId(draftProjectId, "AtelierSubmission.csx");
            committedSolution = (workspace ?? throw new ArgumentNullException(nameof(workspace))).CurrentSolution;
        }

        public Document CreateDraftDocument(
            IReadOnlyList<CommittedSubmission> committedHistory,
            SyntheticDocument syntheticDocument) {

            EnsureCommittedHistory(committedHistory);
            var solution = AddSubmissionProject(
                committedSolution,
                draftProjectId,
                draftDocumentId,
                AtelierIds.ReplProjectName,
                syntheticDocument.Text,
                LastCommittedProjectId);
            return solution.GetDocument(draftDocumentId)
                ?? throw new InvalidOperationException(GetString("Synthetic Atelier document is missing."));
        }

        private ProjectId? LastCommittedProjectId => committedNodes.Length == 0 ? null : committedNodes[^1].ProjectId;

        // Roslyn carries interactive state through a linear chain of submission projects.
        // A history rollback trims the suffix, then new submissions grow from that prefix.
        private void EnsureCommittedHistory(IReadOnlyList<CommittedSubmission> committedHistory) {
            var matchedPrefixLength = CountMatchingPrefix(committedHistory);
            TrimCommittedHistory(matchedPrefixLength);

            var builder = committedNodes.ToBuilder();
            var previousProjectId = LastCommittedProjectId;
            for (var index = matchedPrefixLength; index < committedHistory.Count; index++) {
                var submission = committedHistory[index];
                var name = CreateSubmissionName(submission.Revision);
                var projectId = ProjectId.CreateNewId(name);
                var documentId = DocumentId.CreateNewId(projectId, name + ".csx");
                committedSolution = AddSubmissionProject(
                    committedSolution,
                    projectId,
                    documentId,
                    name,
                    submission.Text,
                    previousProjectId);
                builder.Add(new SubmissionProjectNode(submission, projectId));
                previousProjectId = projectId;
            }

            committedNodes = builder.ToImmutable();
        }

        private int CountMatchingPrefix(IReadOnlyList<CommittedSubmission> committedHistory) {
            var count = Math.Min(committedHistory.Count, committedNodes.Length);
            for (var index = 0; index < count; index++) {
                if (committedHistory[index] != committedNodes[index].Submission) {
                    return index;
                }
            }

            return count;
        }

        private void TrimCommittedHistory(int length) {
            for (var index = committedNodes.Length - 1; index >= length; index--) {
                committedSolution = committedSolution.RemoveProject(committedNodes[index].ProjectId);
            }

            if (length < committedNodes.Length) {
                committedNodes = committedNodes.RemoveRange(length, committedNodes.Length - length);
            }
        }

        private Solution AddSubmissionProject(
            Solution target,
            ProjectId projectId,
            DocumentId documentId,
            string name,
            string text,
            ProjectId? previousProjectId) {
            return target
                .AddProject(CreateSubmissionProjectInfo(projectId, name, previousProjectId))
                .AddDocument(documentId, name + ".csx", SourceText.From(text ?? string.Empty), filePath: name + ".csx");
        }

        private ProjectInfo CreateSubmissionProjectInfo(
            ProjectId projectId,
            string name,
            ProjectId? previousProjectId) {

            var projectReferences = previousProjectId is { } id
                ? [new ProjectReference(id)]
                : ImmutableArray<ProjectReference>.Empty;
            return ProjectInfo.Create(
                id: projectId,
                version: VersionStamp.Create(),
                name: name,
                assemblyName: name,
                language: LanguageNames.CSharp,
                parseOptions: configuration.ParseOptions,
                compilationOptions: configuration.CompilationOptions,
                projectReferences: projectReferences,
                metadataReferences: configuration.MetadataReferences,
                isSubmission: true,
                hostObjectType: configuration.GlobalsType);
        }

        private static string CreateSubmissionName(long revision) {
            return $"{AtelierIds.ReplProjectName}.{Math.Max(0, revision)}";
        }

        private readonly record struct SubmissionProjectNode(CommittedSubmission Submission, ProjectId ProjectId);
    }
}
