namespace UnifierTSL.Surface.Activities
{
    public enum ActivityProgressStyle
    {
        Ratio,
        Percent,
    }

    public readonly record struct ActivityDisplayOptions(
        ActivityProgressStyle ProgressStyle = ActivityProgressStyle.Ratio,
        bool HideElapsed = false);
}
