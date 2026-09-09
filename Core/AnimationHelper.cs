using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;


namespace LeitostrapV7.Core;


public static class AnimationHelper
{
    public static void FadeIn(FrameworkElement element, int milliseconds = 200)
    {
        var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(milliseconds));
        animation.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
        element.BeginAnimation(UIElement.OpacityProperty, animation);
    }


    public static void FadeOut(FrameworkElement element, int milliseconds = 200)
    {
        var animation = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(milliseconds));
        animation.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn };
        element.BeginAnimation(UIElement.OpacityProperty, animation);
    }


    public static void SlideInFromBottom(FrameworkElement element, int milliseconds = 280, double distance = 18)
    {
        var transform = element.RenderTransform as TranslateTransform;
        if (transform == null)
        {
            transform = new TranslateTransform();
            element.RenderTransform = transform;
        }


        var opacityAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(milliseconds));
        opacityAnim.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };


        var slideAnim = new DoubleAnimation(distance, 0, TimeSpan.FromMilliseconds(milliseconds));
        slideAnim.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };


        element.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
        transform.BeginAnimation(TranslateTransform.YProperty, slideAnim);
    }


    public static void SlideInFromLeft(FrameworkElement element, int milliseconds = 280, double distance = 40)
    {
        var transform = element.RenderTransform as TranslateTransform;
        if (transform == null)
        {
            transform = new TranslateTransform();
            element.RenderTransform = transform;
        }


        var opacityAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(milliseconds));
        opacityAnim.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };


        var slideAnim = new DoubleAnimation(-distance, 0, TimeSpan.FromMilliseconds(milliseconds));
        slideAnim.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };


        element.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
        transform.BeginAnimation(TranslateTransform.XProperty, slideAnim);
    }


    public static void AnimateContentSwitch(ContentControl contentControl, object newContent, int milliseconds = 280)
    {
        if (contentControl.Content == null)
        {
            contentControl.Content = newContent;
            FadeIn(contentControl, milliseconds);
            return;
        }


        int fadeOutMs = (int)(milliseconds * 0.53);
        int fadeInMs = milliseconds;


        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(fadeOutMs));
        fadeOut.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn };


        fadeOut.Completed += (s, e) =>
        {
            contentControl.Content = newContent;


            var transform = contentControl.RenderTransform as TranslateTransform;
            if (transform == null)
            {
                transform = new TranslateTransform();
                contentControl.RenderTransform = transform;
            }


            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(fadeInMs));
            fadeIn.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };


            var slideIn = new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(fadeInMs));
            slideIn.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };


            contentControl.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            transform.BeginAnimation(TranslateTransform.YProperty, slideIn);
        };


        contentControl.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }
}
