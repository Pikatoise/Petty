namespace Petty.Assets;

public sealed record AnimationDefinition(string Name, double FramesPerSecond, bool Loop, IReadOnlyList<CatPose> Poses);

public static class Animations
{
    public static IReadOnlyList<AnimationDefinition> CreateAll() =>
    [
        new("idle", 5, true, Idle()),
        new("walk", 9, true, Walk()),
        new("run", 14, true, Run()),
        new("eat", 7, true, Eat()),
        new("sleep", 3, true, Sleep()),
        new("play", 11, true, Play()),
        new("tired", 4, true, Tired()),
        new("exit", 9, false, Exit()),
    ];

    private static List<CatPose> Idle()
    {
        var poses = new List<CatPose>(4);

        for (var i = 0; i < 4; i++)
        {
            var phase = i / 4.0 * Math.PI * 2;
            var breath = Math.Sin(phase);

            poses.Add(new CatPose
            {
                BodyY = 36.5 - (0.5 * breath),
                BodyRy = 8.5 + (0.25 * breath),
                HeadY = 23.5 - (0.5 * breath),
                TailSway = breath,
                Eyes = i == 2 ? EyeShape.Blink : EyeShape.Open,
            });
        }

        return poses;
    }

    private static List<CatPose> Walk()
    {
        var poses = new List<CatPose>(8);

        for (var i = 0; i < 8; i++)
        {
            var phase = i / 8.0 * Math.PI * 2;
            var swing = Math.Sin(phase);
            var bob = Math.Abs(Math.Cos(phase));

            poses.Add(new CatPose
            {
                BodyY = 36.5 - (0.6 * bob),
                HeadY = 23.5 - (0.6 * bob),
                FrontLegDx = 2.2 * swing,
                BackLegDx = -2.2 * swing,
                FrontLift = (int)Math.Round(Math.Max(0, swing) * 2),
                BackLift = (int)Math.Round(Math.Max(0, -swing) * 2),
                TailSway = swing,
            });
        }

        return poses;
    }

    private static List<CatPose> Run()
    {
        var poses = new List<CatPose>(8);

        for (var i = 0; i < 8; i++)
        {
            var phase = i / 8.0 * Math.PI * 2;
            var swing = Math.Sin(phase);
            var bob = Math.Abs(Math.Cos(phase));

            poses.Add(new CatPose
            {
                BodyX = 28,
                BodyY = 37.5 - (1.5 * bob),
                BodyRx = 12.5,
                BodyRy = 8,
                HeadX = 46,
                HeadY = 26 - (0.8 * bob),
                FrontLegDx = 5 * swing,
                BackLegDx = -5 * swing,
                FrontLift = (int)Math.Round(Math.Max(0, swing) * 5),
                BackLift = (int)Math.Round(Math.Max(0, -swing) * 5),
                TailSway = swing * 0.6,
                TailLift = -0.15,
                EarsBack = true,
            });
        }

        return poses;
    }

    private static List<CatPose> Eat()
    {
        var poses = new List<CatPose>(6);

        for (var i = 0; i < 6; i++)
        {
            var chewing = i % 2 == 0;

            poses.Add(new CatPose
            {
                BodyX = 28,
                BodyY = 38,
                BodyRx = 11,
                BodyRy = 8,
                HeadX = 47,
                HeadY = 40 - (chewing ? 0.6 : 0),
                HeadR = 9.5,
                FrontLegDx = 5,
                BackLegDx = -1,
                TailLift = 0.7,
                TailSway = ((i % 3) - 1) * 0.4,
                Mouth = chewing ? MouthShape.Open : MouthShape.Closed,
                Eyes = i < 2 ? EyeShape.Open : EyeShape.Half,
            });
        }

        return poses;
    }

    private static List<CatPose> Sleep()
    {
        var poses = new List<CatPose>(4);

        for (var i = 0; i < 4; i++)
        {
            var phase = i / 4.0 * Math.PI * 2;
            var breath = Math.Sin(phase);

            poses.Add(new CatPose
            {
                Curled = true,
                BodyX = 30,
                BodyY = 46 - (0.4 * breath),
                BodyRx = 13,
                BodyRy = 5.5 + (0.2 * breath),
                HeadX = 41,
                HeadY = 43 - (0.4 * breath),
                HeadR = 8,
                Eyes = EyeShape.Closed,
                ZzzPhase = i,
            });
        }

        return poses;
    }

    private static List<CatPose> Play()
    {
        return
        [
            new CatPose { BodyY = 39, BodyRy = 7, HeadX = 43, HeadY = 29, TailLift = 1 },
            new CatPose { BodyY = 40, BodyRy = 6.5, HeadX = 43, HeadY = 30, TailLift = 1, Eyes = EyeShape.Happy },
            new CatPose
            {
                BodyY = 38, HeadY = 27, FrontLift = 6, FrontLegDx = 4, PawOut = true, TailLift = 1,
            },
            new CatPose
            {
                BodyY = 38, HeadY = 26, FrontLift = 8, FrontLegDx = 6, PawOut = true, TailLift = 1,
            },
            new CatPose
            {
                WholeLift = 3, BodyY = 36, BodyRy = 7.5, HeadY = 26,
                FrontLift = 3, BackLift = 2, TailLift = 1,
            },
            new CatPose
            {
                WholeLift = 4, BodyY = 36, BodyRy = 7.5, HeadY = 25,
                FrontLift = 4, BackLift = 3, TailLift = 1, Eyes = EyeShape.Happy,
            },
            new CatPose
            {
                WholeLift = 1, BodyY = 38, BodyRy = 7, HeadY = 28,
                FrontLift = 2, BackLift = 1,
            },
            new CatPose
            {
                BodyY = 36.5, BodyRy = 8, HeadY = 26, Eyes = EyeShape.Happy,
            },
        ];
    }

    private static List<CatPose> Tired()
    {
        var poses = new List<CatPose>(4);

        for (var i = 0; i < 4; i++)
        {
            var sway = Math.Sin(i / 4.0 * Math.PI * 2) * 0.3;

            poses.Add(new CatPose
            {
                BodyX = 29,
                BodyY = 38.5,
                BodyRx = 11,
                BodyRy = 8,
                HeadX = 43,
                HeadY = 28.5,
                HeadR = 9.3,
                EarDroop = true,
                Eyes = EyeShape.Half,
                TailLift = -0.7,
                TailSway = sway,
            });
        }

        return poses;
    }

    private static List<CatPose> Exit()
    {
        var poses = new List<CatPose>(6);

        for (var i = 0; i < 6; i++)
        {
            var phase = i / 6.0 * Math.PI * 2;
            var swing = Math.Sin(phase);
            var bob = Math.Abs(Math.Cos(phase));

            poses.Add(new CatPose
            {
                BodyX = 30,
                BodyY = 36 - (0.6 * bob),
                HeadX = 46,
                HeadY = 24.5 - (0.5 * bob),
                FrontLegDx = 2.8 * swing,
                BackLegDx = -2.8 * swing,
                FrontLift = (int)Math.Round(Math.Max(0, swing) * 2),
                BackLift = (int)Math.Round(Math.Max(0, -swing) * 2),
                TailLift = 0.5,
                TailSway = swing * 0.5,
            });
        }

        return poses;
    }
}
