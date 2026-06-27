namespace Kaleido.Hosting
{
    public sealed record RealmHostSession(global::UnifierTSL.Servers.ServerContext Server, IRealmDriver Driver);
}
