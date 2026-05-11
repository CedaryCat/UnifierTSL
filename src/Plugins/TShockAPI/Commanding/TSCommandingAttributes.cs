using UnifierTSL.Commanding;
using UnifierTSL.Servers;

namespace TShockAPI.Commanding
{
    public readonly record struct PageRefSourceContext(
        ServerContext? Server,
        CommandInvocationContext? InvocationContext = null);

    public interface IPageRefSource<TSelf> where TSelf : IPageRefSource<TSelf>
    {
        static abstract int? GetPageCount(PageRefSourceContext context);
    }

    public enum PageRefUpperBoundBehavior : byte
    {
        AllowOverflow,
        ValidateKnownCount,
    }

    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public abstract class PageRefAttribute : CommandBindingAttribute
    {
        protected PageRefAttribute(Type sourceType) {
            ArgumentNullException.ThrowIfNull(sourceType);
            if (!ImplementsPageRefSource(sourceType)) {
                throw new ArgumentException(
                    $"Page ref source type '{sourceType.FullName}' must implement '{typeof(IPageRefSource<>).FullName}' on itself.",
                    nameof(sourceType));
            }

            SourceType = sourceType;
        }

        internal Type SourceType { get; }

        public string InvalidTokenMessage { get; set; } = nameof(DefaultInvalidTokenMessage);

        public PageRefUpperBoundBehavior UpperBoundBehavior { get; set; } = PageRefUpperBoundBehavior.AllowOverflow;

        private static string DefaultInvalidTokenMessage(params object?[] args) =>
            GetString("\"{0}\" is not a valid page number.", args);

        private static bool ImplementsPageRefSource(Type sourceType) {
            return sourceType
                .GetInterfaces()
                .Any(candidate =>
                    candidate.IsGenericType
                    && candidate.GetGenericTypeDefinition() == typeof(IPageRefSource<>)
                    && candidate.GenericTypeArguments[0] == sourceType);
        }
    }

    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class PageRefAttribute<TSource>() : PageRefAttribute(typeof(TSource))
        where TSource : IPageRefSource<TSource>;

    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class CommandRefAttribute : CommandBindingAttribute
    {
        public bool Recursive { get; set; }

        public bool AcceptOptionalPrefix { get; set; } = true;

        public bool InsertPrefix { get; set; }
    }
}
