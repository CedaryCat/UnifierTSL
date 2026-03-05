namespace UnifierTSL.ConsoleClient.Shell
{
    public sealed class ReadLineRenderSnapshot
    {
        private ReadLineSnapshotPayload payload = ReadLineSnapshotPayload.CreatePlain();
        private ReadLinePagingState paging = new();

        public ReadLineSnapshotPayload Payload {
            get => payload;
            set => payload = value ?? ReadLineSnapshotPayload.CreatePlain();
        }

        public ReadLinePagingState Paging {
            get => paging;
            set => paging = value ?? new ReadLinePagingState();
        }

        public static ReadLineRenderSnapshot CreatePlain(string? prompt = null)
        {
            return new ReadLineRenderSnapshot {
                Payload = ReadLineSnapshotPayload.CreatePlain(prompt),
                Paging = new ReadLinePagingState(),
            };
        }

        public static ReadLineRenderSnapshot CreateCommandLine(string? prompt = null)
        {
            return new ReadLineRenderSnapshot {
                Payload = ReadLineSnapshotPayload.CreateCommandLine(prompt),
                Paging = new ReadLinePagingState(),
            };
        }
    }
}
