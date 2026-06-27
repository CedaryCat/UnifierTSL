namespace Kaleido.Model.Ids
{
    public readonly record struct RealmKey(string Value)
    {
        public override string ToString() => Value;
        public static implicit operator string(RealmKey key) => key.Value;
        public static implicit operator RealmKey(string value) => new(value);
    }
}
