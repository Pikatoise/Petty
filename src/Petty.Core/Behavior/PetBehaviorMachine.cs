using Petty.Core.Domain;

namespace Petty.Core.Behavior;

/// <summary>
/// Explicit finite-state machine for the pet's behaviour. It only decides *what*
/// the pet does; animation playback and window movement are driven by the app from
/// the current state. Priorities: exit &gt; user play &gt; eating &gt; hunger &gt; sleep &gt; idle.
/// </summary>
public sealed class PetBehaviorMachine
{
    private PetTuning _tuning;
    private readonly Random _random;
    private TimeSpan _stateElapsed;
    private TimeSpan _wanderCountdown;
    private TimeSpan _awakeElapsed;
    private TimeSpan _awakeDuration;
    private bool _resumeSleep;
    private TimeSpan _interactionDuration;
    private TimeSpan _sleepRemaining;

    public PetBehaviorMachine(PetTuning tuning, Random? random = null)
    {
        _tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
        _random = random ?? new Random();
        ScheduleWander();
        ScheduleAwake();
    }

#if DEBUG
    public void SetTuning(PetTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        bool wanderChanged = _tuning.WanderMinInterval != tuning.WanderMinInterval || _tuning.WanderMaxInterval != tuning.WanderMaxInterval;
        bool awakeChanged = _tuning.AwakeMinDuration != tuning.AwakeMinDuration || _tuning.AwakeMaxDuration != tuning.AwakeMaxDuration;
        _tuning = tuning;
        if (awakeChanged)
        {
            ScheduleAwake();
        }

        if (wanderChanged && State is PetActivity.Idle)
        {
            ScheduleWander();
        }
    }

    public bool RequestDebugActivity(PetActivity activity)
    {
        if (activity is not (PetActivity.Idle or PetActivity.Walking or PetActivity.Running or PetActivity.Sleeping) ||
            State is not (PetActivity.Idle or PetActivity.Walking or PetActivity.Running or PetActivity.Sleeping or PetActivity.Tired) ||
            State == activity)
        {
            return false;
        }

        MoveTo(activity, "debug-request");
        return true;
    }
#endif

    public PetActivity State { get; private set; } = PetActivity.Idle;

    public TimeSpan StateElapsed => _stateElapsed;

    public TimeSpan InteractionDuration => _interactionDuration;

    public event EventHandler<BehaviorTransition>? Transitioned;

    public void StartEntering() => MoveTo(PetActivity.Entering, "app-start");

    public void StartIdle() => MoveTo(PetActivity.Idle, "restore");

    public bool CanPlay => State is PetActivity.Idle or PetActivity.Walking or PetActivity.Running or PetActivity.Tired;

    public bool RequestPlay() => CanPlay && RequestInteraction(PetActivity.Playing);

    public bool RequestPet() => RequestInteraction(PetActivity.Petting);

    private bool RequestInteraction(PetActivity activity)
    {
        if (State is not (PetActivity.Idle or PetActivity.Walking or PetActivity.Running or PetActivity.Sleeping or PetActivity.Tired))
        {
            return false;
        }

        _resumeSleep = State is PetActivity.Sleeping;
        MoveTo(activity, activity is PetActivity.Playing ? "user-play" : "user-pet");
        return true;
    }

    public void CancelInteraction()
    {
        if (State is PetActivity.Playing or PetActivity.Petting)
        {
            MoveTo(_resumeSleep ? PetActivity.Sleeping : PetActivity.Idle, "scene-cancelled");
        }
    }

    public bool RequestExit()
    {
        if (State is PetActivity.Exiting)
        {
            return false;
        }

        MoveTo(PetActivity.Exiting, "user-exit");
        return true;
    }

    /// <summary>Responds to a click without interrupting sleep or an action.</summary>
    public void OnUserInteraction()
    {
        if (State is PetActivity.Tired or PetActivity.Idle)
        {
            MoveTo(PetActivity.Idle, "user-interaction");
        }
    }

    /// <summary>
    /// The pet reached the point it was walking to. The view reports this because the
    /// state machine deliberately knows nothing about screen geometry; without it a
    /// walk would always end on a fixed timer and the pet would only ever shuffle a
    /// short distance instead of crossing the desktop.
    /// </summary>
    public void NotifyArrived()
    {
        if (State is PetActivity.Entering or PetActivity.Walking or PetActivity.Running)
        {
            MoveTo(PetActivity.Idle, "arrived");
        }
    }

    public void ArriveAtBowl(Bowl bowl)
    {
        if (State is not PetActivity.SeekingFood)
        {
            return;
        }

        MoveTo(
            bowl.HasFood ? PetActivity.Eating : PetActivity.Tired,
            bowl.HasFood ? "bowl-reached" : "bowl-empty");
    }

    public void BowlEmptied()
    {
        if (State is PetActivity.Eating)
        {
            MoveTo(PetActivity.Idle, "bowl-emptied");
        }
    }

    public void Update(TimeSpan delta, Needs needs, Bowl bowl)
    {
        if (delta <= TimeSpan.Zero)
        {
            return;
        }

        _stateElapsed += delta;
        if (State is not (PetActivity.Entering or PetActivity.Exiting or PetActivity.Sleeping) && !_resumeSleep)
        {
            _awakeElapsed += delta;
        }

        switch (State)
        {
            case PetActivity.Entering:
                if (_stateElapsed >= TimeSpan.FromTicks(Math.Max(_tuning.EnterDuration.Ticks, TimeSpan.FromSeconds(20).Ticks)))
                {
                    MoveTo(PetActivity.Idle, "enter-timeout");
                }

                break;

            case PetActivity.Exiting:
                break;

            case PetActivity.Playing:
                if (_stateElapsed >= _interactionDuration)
                {
                    MoveTo(PetActivity.Idle, "play-finished");
                }

                break;

            case PetActivity.Petting:
                if (_stateElapsed >= _interactionDuration)
                {
                    MoveTo(_resumeSleep ? PetActivity.Sleeping : PetActivity.Idle, "pet-finished");
                }

                break;

            case PetActivity.Eating:
                if (!bowl.HasFood)
                {
                    MoveTo(PetActivity.Idle, "bowl-emptied");
                }
                else if (needs.Satiety >= Needs.Maximum)
                {
                    MoveTo(PetActivity.Idle, "fed");
                }
                else if (_stateElapsed >= _tuning.MaxEatDuration)
                {
                    MoveTo(PetActivity.Idle, "eat-timeout");
                }

                break;

            case PetActivity.SeekingFood:
                if (!bowl.HasFood)
                {
                    MoveTo(PetActivity.Tired, "bowl-empty");
                }
                else if (needs.Satiety > _tuning.SatietyRecoveryThreshold)
                {
                    MoveTo(PetActivity.Idle, "not-hungry-anymore");
                }
                else if (_stateElapsed >= _tuning.SeekTimeout)
                {
                    MoveTo(PetActivity.Idle, "seek-timeout");
                }

                break;

            case PetActivity.Sleeping:
                _sleepRemaining -= delta;
                if (needs.IsHungry(_tuning) && bowl.HasFood)
                {
                    MoveTo(PetActivity.SeekingFood, "hungry");
                }
                else if (_sleepRemaining <= TimeSpan.Zero)
                {
                    MoveTo(PetActivity.Idle, "woke-up");
                }

                break;

            case PetActivity.Tired:
                if (needs.IsHungry(_tuning) && bowl.HasFood)
                {
                    MoveTo(PetActivity.SeekingFood, "hungry");
                }
                else if (needs.Satiety > _tuning.TiredThreshold + 10 && needs.Happiness > Needs.Minimum)
                {
                    MoveTo(PetActivity.Idle, "recovered");
                }
                else
                {
                    TrySleep();
                }

                break;

            case PetActivity.Walking:
                if (NeedsOverride(needs, bowl))
                {
                    break;
                }

                if (TrySleep())
                {
                    break;
                }

                if (_stateElapsed >= _tuning.WalkDuration)
                {
                    MoveTo(PetActivity.Idle, "walk-done");
                }

                break;

            case PetActivity.Running:
                if (NeedsOverride(needs, bowl))
                {
                    break;
                }

                if (TrySleep())
                {
                    break;
                }

                if (_stateElapsed >= _tuning.RunDuration)
                {
                    MoveTo(PetActivity.Idle, "run-done");
                }

                break;

            case PetActivity.Idle:
                if (NeedsOverride(needs, bowl))
                {
                    break;
                }

                if (TrySleep())
                {
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

    private bool TrySleep()
    {
        if (_awakeElapsed < _awakeDuration)
        {
            return false;
        }

        _awakeElapsed = TimeSpan.Zero;
        if (_tuning.SleepChance < 1 && (_tuning.SleepChance <= 0 || _random.NextDouble() >= _tuning.SleepChance))
        {
            ScheduleAwake();
            return false;
        }
        MoveTo(PetActivity.Sleeping, "sleepy");
        return true;
    }

    private bool NeedsOverride(Needs needs, Bowl bowl)
    {
        if (needs.IsHungry(_tuning) && bowl.HasFood)
        {
            MoveTo(PetActivity.SeekingFood, "hungry");
            return true;
        }

        if (needs.IsStarving(_tuning) || needs.Happiness <= Needs.Minimum)
        {
            MoveTo(PetActivity.Tired, needs.IsStarving(_tuning) ? "starving" : "unhappy");
            return true;
        }

        return false;
    }

    private void MoveTo(PetActivity next, string reason)
    {
        if (next == State)
        {
            return;
        }

        PetActivity previous = State;
        State = next;
        _stateElapsed = TimeSpan.Zero;
        if (next is PetActivity.Sleeping && !_resumeSleep)
        {
            _sleepRemaining = SampleDuration(_tuning.SleepMinDuration, _tuning.SleepMaxDuration);
        }

        if ((previous is PetActivity.Sleeping && next is not PetActivity.Petting) || previous is PetActivity.Eating)
        {
            _awakeElapsed = TimeSpan.Zero;
            ScheduleAwake();
        }
        if (next is not PetActivity.Petting)
        {
            _resumeSleep = false;
        }

        if (next is PetActivity.Playing)
        {
            _interactionDuration = _tuning.PlayDuration;
        }
        else if (next is PetActivity.Petting)
        {
            _interactionDuration = _tuning.PetDuration;
        }

        if (next is PetActivity.Idle)
        {
            ScheduleWander();
        }

        Transitioned?.Invoke(this, new BehaviorTransition(previous, next, reason));
    }

    private TimeSpan SampleDuration(TimeSpan min, TimeSpan max) => max <= min ? min :
        TimeSpan.FromTicks(min.Ticks + (long)((max.Ticks - min.Ticks) * _random.NextDouble()));

    private void ScheduleAwake() => _awakeDuration = SampleDuration(_tuning.AwakeMinDuration, _tuning.AwakeMaxDuration);

    private void ScheduleWander()
    {
        double min = _tuning.WanderMinInterval.TotalMilliseconds;
        double max = Math.Max(min, _tuning.WanderMaxInterval.TotalMilliseconds);
        _wanderCountdown = TimeSpan.FromMilliseconds(min + (_random.NextDouble() * (max - min)));
    }
}
