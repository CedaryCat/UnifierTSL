namespace UnifierTSL.ConsoleClient.Shell
{
    public sealed class ReadLineSemanticSnapshot
    {
        private ReadLineSnapshotPayload payload = ReadLineSnapshotPayload.CreatePlain();

        public ReadLineSnapshotPayload Payload {
            get => payload;
            set => payload = value ?? ReadLineSnapshotPayload.CreatePlain();
        }
    }
}
