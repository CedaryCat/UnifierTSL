namespace Kaleido.Model.Ids
{
    public readonly record struct RealmId(string Value)
    {
        public override string ToString() => Value;
        public static implicit operator string(RealmId id) => id.Value;
        public static implicit operator RealmId(string value) => new(value);
    }
}
