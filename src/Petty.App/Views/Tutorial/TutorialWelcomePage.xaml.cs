using System.Windows.Controls;
using Petty.Core.Animation;

namespace Petty.App.Views.Tutorial;

public partial class TutorialWelcomePage : Page
{
    private readonly TutorialMedia _media;

    internal TutorialWelcomePage(TutorialMedia media)
    {
        _media = media;
        InitializeComponent();
        _ = new TutorialAnimation(this, RenderScene);
        RenderScene(0);
    }

    private void RenderScene(double elapsed)
    {
        double t = elapsed % 10;
        TutorialMedia.ShowPet(PetImage, _media.Sheet("cat"), t < 5 ? PetAnimationMap.Walk : PetAnimationMap.Sleep,
            t, t < 5 ? 80 + t * 34 : 250);
    }
}
