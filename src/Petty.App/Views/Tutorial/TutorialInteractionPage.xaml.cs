using System.Windows;
using System.Windows.Controls;
using Petty.App.Sprites;
using Petty.Core.Animation;
using Petty.Core.Behavior;
using Petty.Core.Runtime;
using static Petty.App.Views.Tutorial.TutorialMedia;

namespace Petty.App.Views.Tutorial;

public partial class TutorialInteractionPage : Page
{
    private readonly TutorialMedia _media;

    internal TutorialInteractionPage(TutorialMedia media)
    {
        _media = media;
        InitializeComponent();
        _ = new TutorialAnimation(this, RenderScene);
        RenderScene(0);
    }

    private void RenderScene(double elapsed)
    {
        SpriteSheet sheet = _media.Sheet("cat");
        double t = elapsed % 10;
        const double left = 150;
        ActionMenu.Visibility = t < 2 ? Visibility.Visible : Visibility.Collapsed;
        PropImage.Visibility = t >= 2 ? Visibility.Visible : Visibility.Collapsed;
        string animation = t < 2 ? PetAnimationMap.Idle : t < 5 ? PetAnimationMap.Pet : PetAnimationMap.Play;
        if (t is >= 2 and < 5)
        {
            (double offset, double height) = ActionScene.Sample(PetActivity.Petting, (t - 2) / 3);
            _media.ShowProp(PropImage, "hand", left + 128 * 0.68 - _media.Prop("hand").Width + offset * Scale / 3,
                Floor - 155 * Scale / 3 + height * Scale / 3 + _media.Prop("hand").LastOpaqueRow * Scale);
        }
        else if (t >= 5)
        {
            SpriteClip play = sheet.GetClip(PetAnimationMap.Play);
            double contactLift = Math.Max(0, (sheet.GroundRow - (sheet.Metadata.PlayContactY ?? 45)) * Scale - _media.Prop("ball").Width * Scale * 0.44);
            PlayScenePose pose = ActionScene.SamplePlay(t - 5, 5, play.Frames.Count, play.FramesPerSecond, 180, contactLift);
            double contact = left + (sheet.Metadata.PlayContactX ?? 58) * Scale + _media.Prop("ball").Width * Scale * 0.44;
            _media.ShowProp(PropImage, "ball", contact - _media.Prop("ball").Width * Scale / 2 + pose.PropOffset, Floor + pose.PropHeight);
            ShowPet(PetImage, sheet, animation, t, left, pose.FrameIndex);
            return;
        }
        ShowPet(PetImage, sheet, animation, t, left);
    }
}
