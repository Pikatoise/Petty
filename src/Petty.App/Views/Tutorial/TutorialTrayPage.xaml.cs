using System.Windows;
using System.Windows.Controls;
using Petty.Core.Animation;

namespace Petty.App.Views.Tutorial;

public partial class TutorialTrayPage : Page
{
    private readonly TutorialMedia _media;

    internal TutorialTrayPage(TutorialMedia media)
    {
        _media = media;
        InitializeComponent();
        TrayImage.Source = media.Prop("tray-cat").Image;
        _ = new TutorialAnimation(this, RenderScene);
        RenderScene(0);
    }

    private void RenderScene(double elapsed)
    {
        double t = elapsed % 10;
        TrayIllustration.Visibility = t % 4 < 1 ? Visibility.Collapsed : Visibility.Visible;
        TutorialMedia.ShowPet(PetImage, _media.Sheet("dog"), PetAnimationMap.Idle, t, 70);
    }
}
