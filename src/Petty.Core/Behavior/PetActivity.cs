namespace Petty.Core.Behavior;

public enum PetActivity
{
    Entering,
    Idle,
    Walking,
    Running,
    SeekingFood,
    Eating,
    Sleeping,
    Playing,
    Petting,
    Tired,
    Exiting,
}

public readonly record struct BehaviorTransition(PetActivity From, PetActivity To, string Reason);
