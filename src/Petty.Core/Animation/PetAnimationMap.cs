using Petty.Core.Behavior;

namespace Petty.Core.Animation;

/// <summary>
/// The only place that decides which sprite clip represents a behaviour state.
/// Keeping it here (instead of in the window) means the mapping can be unit-tested
/// and the behaviour machine never depends on animation names.
/// </summary>
public static class PetAnimationMap
{
    public const string Idle = "idle";
    public const string Walk = "walk";
    public const string Run = "run";
    public const string Eat = "eat";
    public const string Sleep = "sleep";
    public const string Play = "play";
    public const string Tired = "tired";
    public const string Exit = "exit";

    public static string ClipFor(PetActivity activity) => activity switch
    {
        PetActivity.Entering => Walk,
        PetActivity.Walking => Walk,
        PetActivity.SeekingFood => Walk,
        PetActivity.Running => Run,
        PetActivity.Eating => Eat,
        PetActivity.Sleeping => Sleep,
        PetActivity.Playing => Play,
        PetActivity.Tired => Tired,
        PetActivity.Exiting => Exit,
        _ => Idle,
    };
}
