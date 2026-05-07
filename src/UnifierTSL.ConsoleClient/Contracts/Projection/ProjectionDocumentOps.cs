namespace UnifierTSL.Contracts.Projection {
    public static class ProjectionDocumentOps {
        public static ProjectionScope GetScope(ProjectionSnapshotPayload payload) {
            return payload.Body switch {
                ProjectionFullSnapshotBody full => full.Document.Scope,
                ProjectionUpdateSnapshotBody update => update.Scope,
                _ => throw new InvalidOperationException($"Unsupported projection snapshot body type: {payload.Body.GetType().FullName}."),
            };
        }

        public static ProjectionDocument ApplySnapshot(ProjectionDocument? current, ProjectionSnapshotPayload payload) {
            return payload.Body switch {
                ProjectionFullSnapshotBody full => BindStyles(full.Document),
                ProjectionUpdateSnapshotBody update => current is not null
                    ? ApplyUpdate(current, update.Scope, update.Patch)
                    : throw new InvalidOperationException("Projection update cannot be applied before the initial full snapshot."),
                _ => throw new InvalidOperationException($"Unsupported projection snapshot body type: {payload.Body.GetType().FullName}."),
            };
        }

        public static ProjectionDocument ApplyUpdate(
            ProjectionDocument current,
            ProjectionScope scope,
            ProjectionDocumentPatch patch) {
            ArgumentNullException.ThrowIfNull(current);
            ArgumentNullException.ThrowIfNull(scope);
            ArgumentNullException.ThrowIfNull(patch);
            if (!ScopesEqual(current.Scope, scope)) {
                throw new InvalidOperationException("Projection update scope does not match the current document scope.");
            }

            return BindStyles(new ProjectionDocument {
                Scope = current.Scope,
                Definition = current.Definition,
                State = new ProjectionDocumentState {
                    Nodes = MergeNodes(current.Definition, current.State.Nodes, patch.Nodes),
                    Focus = patch.Focus ?? current.State.Focus,
                    Selection = patch.Selection ?? current.State.Selection,
                    Styles = patch.Styles ?? current.State.Styles,
                },
            });
        }

        private static ProjectionNodeState[] MergeNodes(
            ProjectionDocumentDefinition definition,
            ProjectionNodeState[] currentNodes,
            ProjectionNodePatch[] patches) {
            var definitionsByNodeId = (definition.Nodes ?? [])
                .ToDictionary(static node => node.NodeId, StringComparer.Ordinal);
            var merged = new Dictionary<string, ProjectionNodeState>(StringComparer.Ordinal);
            foreach (var state in currentNodes ?? []) {
                if (!string.IsNullOrWhiteSpace(state.NodeId)) {
                    if (!definitionsByNodeId.TryGetValue(state.NodeId, out var nodeDefinition)) {
                        throw new InvalidOperationException($"Projection document state referenced undefined node '{state.NodeId}'.");
                    }

                    EnsureNodeKindMatchesDefinition(nodeDefinition, state);
                    merged[state.NodeId] = state;
                }
            }

            foreach (var patch in patches ?? []) {
                var state = patch.State ?? throw new InvalidOperationException("Projection node patch is missing replacement state.");
                if (string.IsNullOrWhiteSpace(state.NodeId)) {
                    throw new InvalidOperationException("Projection node patch replacement state is missing NodeId.");
                }

                if (!definitionsByNodeId.TryGetValue(state.NodeId, out var nodeDefinition)) {
                    throw new InvalidOperationException($"Projection node patch targeted undefined node '{state.NodeId}'.");
                }

                EnsureNodeKindMatchesDefinition(nodeDefinition, state);
                merged[state.NodeId] = state;
            }

            return [.. (definition.Nodes ?? [])
                .Select(static node => node.NodeId)
                .Where(merged.ContainsKey)
                .Select(nodeId => merged[nodeId])];
        }

        private static void EnsureNodeKindMatchesDefinition(ProjectionNodeDefinition definition, ProjectionNodeState state) {
            if (definition.Kind == state.Kind) {
                return;
            }

            throw new InvalidOperationException(
                $"Projection node state kind '{state.Kind}' does not match definition kind '{definition.Kind}' for node '{state.NodeId}'.");
        }

        private static bool ScopesEqual(ProjectionScope left, ProjectionScope right) {
            return left.Kind == right.Kind
                && string.Equals(left.ScopeId, right.ScopeId, StringComparison.Ordinal)
                && string.Equals(left.DocumentKind, right.DocumentKind, StringComparison.Ordinal);
        }

        private static ProjectionDocument BindStyles(ProjectionDocument document) {
            var styles = ProjectionStyleDictionaryOps.WithSlots(document.State.Styles);
            return new ProjectionDocument {
                Scope = document.Scope,
                Definition = BindStyles(document.Definition, styles),
                State = BindStyles(document.State, styles),
            };
        }

        private static ProjectionDocumentDefinition BindStyles(
            ProjectionDocumentDefinition definition,
            ProjectionStyleDictionary styles) {
            return new ProjectionDocumentDefinition {
                RootNodeIds = definition.RootNodeIds,
                Nodes = definition.Nodes,
                MarkerCatalog = [.. (definition.MarkerCatalog ?? []).Select(item => new ProjectionMarkerCatalogItem {
                    Key = item.Key,
                    VariantKey = item.VariantKey,
                    DisplayText = item.DisplayText,
                    Style = ProjectionStyleDictionaryOps.Resolve(styles, item.Style?.Key),
                })],
                Traits = definition.Traits,
            };
        }

        private static ProjectionDocumentState BindStyles(
            ProjectionDocumentState state,
            ProjectionStyleDictionary styles) {
            return new ProjectionDocumentState {
                Nodes = [.. (state.Nodes ?? []).Select(node => BindStyles(node, styles))],
                Focus = state.Focus,
                Selection = state.Selection,
                Styles = styles,
            };
        }

        private static ProjectionNodeState BindStyles(
            ProjectionNodeState node,
            ProjectionStyleDictionary styles) {
            return node switch {
                ContainerProjectionNodeState container => new ContainerProjectionNodeState {
                    NodeId = container.NodeId,
                    State = container.State,
                },
                TextProjectionNodeState text => new TextProjectionNodeState {
                    NodeId = text.NodeId,
                    State = new TextNodeState {
                        Content = BindStyles(text.State.Content, styles),
                        Animation = BindStyles(text.State.Animation, styles),
                    },
                },
                EditableTextProjectionNodeState editable => new EditableTextProjectionNodeState {
                    NodeId = editable.NodeId,
                    State = new EditableTextNodeState {
                        BufferText = editable.State.BufferText,
                        CaretIndex = editable.State.CaretIndex,
                        ExpectedClientBufferRevision = editable.State.ExpectedClientBufferRevision,
                        RemoteRevision = editable.State.RemoteRevision,
                        Markers = editable.State.Markers,
                        Decorations = [.. (editable.State.Decorations ?? []).Select(decoration => new ProjectionInlineDecoration {
                            Kind = decoration.Kind,
                            StartIndex = decoration.StartIndex,
                            Length = decoration.Length,
                            Style = ProjectionStyleDictionaryOps.Resolve(styles, decoration.Style?.Key),
                            Content = BindStyles(decoration.Content, styles),
                        })],
                        Submit = editable.State.Submit,
                    },
                },
                CollectionProjectionNodeState collection => new CollectionProjectionNodeState {
                    NodeId = collection.NodeId,
                    State = new CollectionNodeState {
                        Items = [.. (collection.State.Items ?? []).Select(item => new ProjectionCollectionItem {
                            ItemId = item.ItemId,
                            Label = BindStyles(item.Label, styles),
                            SecondaryLabel = BindStyles(item.SecondaryLabel, styles),
                            TrailingLabel = BindStyles(item.TrailingLabel, styles),
                            Summary = BindStyles(item.Summary, styles),
                            Detail = BindStyles(item.Detail, styles),
                            PrimaryEdit = item.PrimaryEdit,
                            IsEnabled = item.IsEnabled,
                        })],
                        TotalItemCount = collection.State.TotalItemCount,
                        WindowOffset = collection.State.WindowOffset,
                        PageSize = collection.State.PageSize,
                        IsPaged = collection.State.IsPaged,
                    },
                },
                PropertySetProjectionNodeState propertySet => new PropertySetProjectionNodeState {
                    NodeId = propertySet.NodeId,
                    State = new PropertySetNodeState {
                        Properties = [.. (propertySet.State.Properties ?? []).Select(property => new ProjectionPropertyValue {
                            PropertyKey = property.PropertyKey,
                            Label = BindStyles(property.Label, styles),
                            Value = BindStyles(property.Value, styles),
                            Style = ProjectionStyleDictionaryOps.Resolve(styles, property.Style?.Key),
                            IsVisible = property.IsVisible,
                        })],
                    },
                },
                DetailProjectionNodeState detail => new DetailProjectionNodeState {
                    NodeId = detail.NodeId,
                    State = new DetailNodeState {
                        ContextItemId = detail.State.ContextItemId,
                        Heading = BindStyles(detail.State.Heading, styles),
                        Summary = BindStyles(detail.State.Summary, styles),
                        Lines = [.. (detail.State.Lines ?? []).Select(line => BindStyles(line, styles))],
                    },
                },
                _ => throw new InvalidOperationException($"Unsupported projection node state type: {node.GetType().FullName}."),
            };
        }

        private static ProjectionTextAnimation? BindStyles(
            ProjectionTextAnimation? animation,
            ProjectionStyleDictionary styles) {
            return animation is null
                ? null
                : new ProjectionTextAnimation {
                    FrameStepTicks = animation.FrameStepTicks,
                    Frames = [.. (animation.Frames ?? []).Select(frame => BindStyles(frame, styles))],
                };
        }

        private static ProjectionTextBlock BindStyles(
            ProjectionTextBlock? block,
            ProjectionStyleDictionary styles) {
            return new ProjectionTextBlock {
                Lines = [.. (block?.Lines ?? []).Select(line => new ProjectionTextLine {
                    Style = ProjectionStyleDictionaryOps.Resolve(styles, line.Style?.Key),
                    Spans = [.. (line.Spans ?? []).Select(span => new ProjectionTextSpan {
                        Text = span.Text,
                        Style = ProjectionStyleDictionaryOps.Resolve(styles, span.Style?.Key),
                    })],
                })],
            };
        }
    }
}
