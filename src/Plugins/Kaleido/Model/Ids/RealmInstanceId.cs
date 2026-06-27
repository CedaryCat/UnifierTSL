namespace Kaleido.Model.Ids
{
    public readonly record struct RealmInstanceId(string Value)
    {
        public override string ToString() => Value;
        public static implicit operator string(RealmInstanceId id) => id.Value;
        public static implicit operator RealmInstanceId(string value) => new(value);
    }
}
