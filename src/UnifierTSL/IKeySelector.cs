namespace UnifierTSL
{
    public interface IKeySelector<TKey> where TKey : notnull
    {
        TKey Key { get; }
    }
}
