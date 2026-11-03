using System.Windows;
using System.Windows.Controls;
using Petty.App.Sprites;
using Petty.Core.Animation;
using static Petty.App.Views.Tutorial.TutorialMedia;

namespace Petty.App.Views.Tutorial;

public partial class TutorialFeedingPage : Page
{
    private readonly TutorialMedia _media;

    internal TutorialFeedingPage(TutorialMedia media)
    {
        _media = media;
        InitializeComponent();
        _ = new TutorialAnimation(this, RenderScene);
        RenderScene(0);
    }

    private void RenderScene(double elapsed)
    {
        double t = elapsed % 10;
        SpriteSheet sheet = _media.Sheet("dog");
        string animation = t < 1.8 ? PetAnimationMap.Idle : t < 4 ? PetAnimationMap.Walk : PetAnimationMap.Eat;
        double eatingLeft = 307 + _media.Prop("bowl-full").Width * Scale / 2 - (sheet.Metadata.SpeciesId == "dog" ? 50 : 54) * Scale;
        double left = t < 1.8 ? 70 : t < 4 ? 70 + (t - 1.8) / 2.2 * (eatingLeft - 70) : eatingLeft;
        string level = t < 0.5 ? "bowl-empty" : t < 0.8 ? "bowl-25" : t < 1.1 ? "bowl-50" : t < 1.4 ? "bowl-75" : "bowl-full";
        _media.ShowProp(PropImage, level, 307, Floor);
        PourImage.Visibility = t is >= 0.3 and < 1.5 ? Visibility.Visible : Visibility.Collapsed;
        if (PourImage.Visibility == Visibility.Visible)
        {
            string pour = $"pour-{Math.Min(5, (int)((t - 0.3) / 0.2))}";
            _media.ShowProp(PourImage, pour, 307, Floor - _media.Prop(level).LastOpaqueRow * Scale - _media.Prop(pour).Height * Scale + 13 * Scale + _media.Prop(pour).LastOpaqueRow * Scale);
        }
        ShowPet(PetImage, sheet, animation, t, left);
    }
}
