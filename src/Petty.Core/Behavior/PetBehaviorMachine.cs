using Petty.Core.Domain;

namespace Petty.Core.Behavior;

/// <summary>
/// Explicit finite-state machine for the pet's behaviour. It only decides *what*
/// the pet does; animation playback and window movement are driven by the app from
/// the current state. Priorities: exit &gt; user play &gt; eating &gt; hunger &gt; sleep &gt; idle.
/// </summary>
public sealed class PetBehaviorMachine
{
    private readonly PetTuning _tuning;
    private readonly Random _random;
    private TimeSpan _stateElapsed;
    private TimeSpan _wanderCountdown;

    public PetBehaviorMachine(PetTuning tuning, Random? random = null)
    {
        _tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
        _random = random ?? new Random();
        ScheduleWander();
    }

    public PetActivity State { get; private set; } = PetActivity.Idle;

    public TimeSpan StateElapsed => _stateElapsed;

    public event EventHandler<BehaviorTransition>? Transitioned;

    public void StartEntering() => MoveTo(PetActivity.Entering, "app-start");

    public void StartIdle() => MoveTo(PetActivity.Idle, "restore");

    public bool RequestPlay()
    {
        if (State is PetActivity.Exiting) return false;
        MoveTo(PetActivity.Playing, "user-play");
        return true;
    }

    public bool RequestExit()
    {
        if (State is PetActivity.Exiting) return false;
        MoveTo(PetActivity.Exiting, "user-exit");
        return true;
    }

    /// <summary>Wakes the pet up on hover/click without cancelling play, eating or exit.</summary>
    public void OnUserInteraction()
    {
        if (State is PetActivity.Sleeping or PetActivity.Tired or PetActivity.Idle)
            MoveTo(PetActivity.Idle, "user-interaction");
    }

    /// <summary>
    /// The pet reached the point it was walking to. The view reports this because the
    /// state machine deliberately knows nothing about screen geometry; without it a
    /// walk would always end on a fixed timer and the pet would only ever shuffle a
    /// short distance instead of crossing the desktop.
    /// </summary>
    public void NotifyArrived()
    {
        if (State is PetActivity.Walking or PetActivity.Running)
            MoveTo(PetActivity.Idle, "arrived");
    }

    public void ArriveAtBowl(Bowl bowl)
    {
        if (State is not PetActivity.SeekingFood) return;
        MoveTo(
            bowl.HasFood ? PetActivity.Eating : PetActivity.Tired,
            bowl.HasFood ? "bowl-reached" : "bowl-empty");
    }

    public void BowlEmptied()
    {
        if (State is PetActivity.Eating) MoveTo(PetActivity.Idle, "bowl-emptied");
    }

    public void Update(TimeSpan delta, Needs needs, Bowl bowl)
    {
        if (delta <= TimeSpan.Zero) return;
        _stateElapsed += delta;

        switch (State)
        {
            case PetActivity.Entering:
                if (_stateElapsed >= _tuning.EnterDuration) MoveTo(PetActivity.Idle, "entered");
                break;

            case PetActivity.Exiting:
                break;

            case PetActivity.Playing:
                if (_stateElapsed >= _tuning.PlayDuration) MoveTo(PetActivity.Idle, "play-finished");
                break;

            case PetActivity.Eating:
                if (!bowl.HasFood) MoveTo(PetActivity.Idle, "bowl-emptied");
                else if (needs.Satiety >= Needs.Maximum) MoveTo(PetActivity.Idle, "fed");
                else if (_stateElapsed >= _tuning.MaxEatDuration) MoveTo(PetActivity.Idle, "eat-timeout");
                break;

            case PetActivity.SeekingFood:
                if (!bowl.HasFood) MoveTo(PetActivity.Tired, "bowl-empty");
                else if (needs.Satiety > _tuning.SatietyRecoveryThreshold) MoveTo(PetActivity.Idle, "not-hungry-anymore");
                else if (_stateElapsed >= _tuning.SeekTimeout) MoveTo(PetActivity.Idle, "seek-timeout");
                break;

            case PetActivity.Sleeping:
                if (needs.IsHungry(_tuning) && bowl.HasFood) MoveTo(PetActivity.SeekingFood, "hungry");
                else if (_stateElapsed >= _tuning.SleepDuration) MoveTo(PetActivity.Idle, "woke-up");
                break;

            case PetActivity.Tired:
                if (needs.IsHungry(_tuning) && bowl.HasFood) MoveTo(PetActivity.SeekingFood, "hungry");
                else if (needs.Satiety > _tuning.TiredThreshold + 10) MoveTo(PetActivity.Idle, "recovered");
                break;

            case PetActivity.Walking:
                if (HungerOverride(needs, bowl)) break;
                if (_stateElapsed >= _tuning.WalkDuration) MoveTo(PetActivity.Idle, "walk-done");
                break;

            case PetActivity.Running:
                if (HungerOverride(needs, bowl)) break;
                if (_stateElapsed >= _tuning.RunDuration) MoveTo(PetActivity.Idle, "run-done");
                break;

            case PetActivity.Idle:
                if (HungerOverride(needs, bowl)) break;
                if (needs.IsStarving(_tuning))
                {
                    MoveTo(PetActivity.Tired, "tired");
                    break;
                }

                if (_stateElapsed >= _tuning.SleepAfterIdle && needs.Happiness > _tuning.SleepHappinessThreshold)
                {
                    MoveTo(PetActivity.Sleeping, "sleepy");
                    break;
                }

                _wanderCountdown -= delta;
                if (_wanderCountdown <= TimeSpan.Zero)
                {
                    MoveTo(
                        _random.NextDouble() < _tuning.RunChance ? PetActivity.Running : PetActivity.Walking,
                        "wander");
                }

                break;
        }
    }

    private bool HungerOverride(Needs needs, Bowl bowl)
    {
        if (!needs.IsHungry(_tuning)) return false;
        if (bowl.HasFood)
        {
            MoveTo(PetActivity.SeekingFood, "hungry");
            return true;
        }

        if (needs.IsStarving(_tuning))
        {
            MoveTo(PetActivity.Tired, "starving");
            return true;
        }

        return false;
    }

    private void MoveTo(PetActivity next, string reason)
    {
        if (next == State) return;

        var previous = State;
        State = next;
        _stateElapsed = TimeSpan.Zero;

        if (next is PetActivity.Idle) ScheduleWander();

        Transitioned?.Invoke(this, new BehaviorTransition(previous, next, reason));
    }

    private void ScheduleWander()
    {
        var min = _tuning.WanderMinInterval.TotalMilliseconds;
        var max = Math.Max(min, _tuning.WanderMaxInterval.TotalMilliseconds);
        _wanderCountdown = TimeSpan.FromMilliseconds(min + (_random.NextDouble() * (max - min)));
    }
}
