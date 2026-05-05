using System.Globalization;
using UnifierTSL.Commanding;
using UnifierTSL.Commanding.Bindings;

namespace TShockAPI.Commanding
{
    public enum TSTileCoordinateAxis : byte
    {
        X,
        Y,
    }

    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class TileCoordinateAttribute(TSTileCoordinateAxis axis) : CommandBindingAttribute
    {
        public TSTileCoordinateAxis Axis { get; } = axis;

        public string InvalidTokenMessage { get; set; } = nameof(DefaultInvalidTokenMessage);

        private static string DefaultInvalidTokenMessage =>
            GetString("The destination coordinates provided don't look like valid numbers.");
    }

    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    internal sealed class BuffDurationAttribute(int maxSeconds) : CommandBindingAttribute
    {
        public int MaxSeconds { get; } = maxSeconds;
    }

    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    internal sealed class WindSpeedAttribute : CommandBindingAttribute
    {
        public float MinimumMph { get; set; } = -40f;

        public float MaximumMph { get; set; } = 40f;
    }

    internal static class TSCommandValueBindings
    {
        public static void Configure(ICommandBindingRegistry builder) {
            ArgumentNullException.ThrowIfNull(builder);

            builder.AddBindingRule<TileCoordinateAttribute, int>(
                BindTileCoordinate,
                static _ => CommonParamPrompts.IntegerValue);
            builder.AddBindingRule<BuffDurationAttribute, int>(
                BindBuffDuration,
                static _ => CommonParamPrompts.IntegerValue);
            builder.AddBindingRule<BuffDurationAttribute, int?>(
                BindNullableBuffDuration,
                static _ => CommonParamPrompts.IntegerValue);
            builder.AddBindingRule<WindSpeedAttribute, float>(BindWindSpeed);
        }

        private static CommandParamBindingResult BindTileCoordinate(
            CommandParamBindingContext context,
            TileCoordinateAttribute attribute) {
            if (context.UserIndex >= context.UserArguments.Length) {
                return CommandParamBindingResult.Mismatch();
            }

            var raw = context.UserArguments[context.UserIndex].Trim();
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var coordinate)) {
                return Failure(attribute, raw);
            }

            var server = context.InvocationContext.Server;
            if (server is null) {
                return CommandParamBindingResult.Failure(CommandOutcome.Error(
                    GetString("You must use this command in sepcific server.")));
            }

            var maxInclusive = attribute.Axis == TSTileCoordinateAxis.X
                ? server.Main.maxTilesX - 1
                : server.Main.maxTilesY - 1;
            return CommandParamBindingResult.Success(Math.Clamp(coordinate, 0, maxInclusive));
        }

        private static CommandParamBindingResult BindBuffDuration(
            CommandParamBindingContext context,
            BuffDurationAttribute attribute) {
            if (context.UserIndex >= context.UserArguments.Length) {
                return CommandParamBindingResult.Mismatch();
            }

            var raw = context.UserArguments[context.UserIndex].Trim();
            var durationSeconds = 0;
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out durationSeconds)) {
                durationSeconds = 0;
            }

            if (durationSeconds < 0 || durationSeconds > attribute.MaxSeconds) {
                durationSeconds = attribute.MaxSeconds;
            }

            return CommandParamBindingResult.Success(durationSeconds);
        }

        private static CommandParamBindingResult BindNullableBuffDuration(
            CommandParamBindingContext context,
            BuffDurationAttribute attribute) {
            var result = BindBuffDuration(context, attribute);
            return !result.IsSuccess
                ? result
                : CommandParamBindingResult.SuccessMany(
                    result.Candidates.Select(static candidate =>
                        new BindingCandidate((int?)candidate.Value!, candidate.ConsumedTokens)),
                    result.FailureOutcome,
                    result.FailureConsumedTokens);
        }

        private static CommandParamBindingResult BindWindSpeed(
            CommandParamBindingContext context,
            WindSpeedAttribute attribute) {
            if (context.UserIndex >= context.UserArguments.Length) {
                return CommandParamBindingResult.Mismatch();
            }

            var raw = context.UserArguments[context.UserIndex].Trim();
            if (!float.TryParse(raw, out var mph)
                || mph < attribute.MinimumMph
                || mph > attribute.MaximumMph) {
                return InvalidWindSpeed();
            }

            return CommandParamBindingResult.Success(mph);
        }

        private static CommandParamBindingResult InvalidWindSpeed() {
            return CommandParamBindingResult.Failure(CommandOutcome.Error(
                GetString("Invalid wind speed (must be between -40 and 40).")));
        }

        private static CommandParamBindingResult Failure(TileCoordinateAttribute attribute, string rawToken) {
            return CommandParamBindingResult.Failure(CommandOutcome.Error(CommandAttributeText.Invoke(
                attribute,
                nameof(TileCoordinateAttribute.InvalidTokenMessage),
                attribute.InvalidTokenMessage,
                rawToken)));
        }
    }
}
