namespace Winland;

public static partial class ConfigParser
{
    private sealed partial class Parser
    {
        // Curves are checked in config order, so an animation can only name a curve defined above it.
        private readonly AnimationBook _animations = new();

        private bool TryAnimationLine(string lhs, string rhs, int lineNumber)
        {
            var keyword = Qualify(lhs) switch
            {
                "bezier" or "animations:bezier" => AnimationBook.BezierKeyword,
                "animation" or "animations:animation" => AnimationBook.AnimationKeyword,
                _ => null,
            };

            if (keyword is null)
                return false;

            _settings.Add(new Setting(keyword, rhs));
            var warning = keyword == AnimationBook.BezierKeyword ? _animations.AddBezier(rhs) : _animations.SetAnimation(rhs);
            if (warning is not null)
                Warn(lineNumber, warning);

            return true;
        }
    }
}
