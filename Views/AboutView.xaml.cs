using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;


namespace LeitostrapV7.Views
{
    public partial class AboutView : UserControl
    {
        private const string WebsiteUrl = "https://leitostrap.netlify.app/";
        private const string DiscordUrl = "https://discord.gg/Fgec4NtHnu";
        private const string GitHubUrl = "https://github.com/Leitostrap/Leitostrap";


        static AboutView()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        }


        public AboutView()
        {
            InitializeComponent();
        }


        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            LoadAvatars();
            PlayEntranceAnimation();
        }


        private void PlayEntranceAnimation()
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };


            AnimateCard(LogoCard, 0, ease, 300, 0.9, 20);


            AnimateOpacity(DescText, 100, ease, 350);


            AnimateCard(Feature1, 120, ease, 300, 0.92, 15);
            AnimateCard(Feature2, 180, ease, 300, 0.92, 15);
            AnimateCard(Feature3, 240, ease, 300, 0.92, 15);
            AnimateCard(Feature4, 300, ease, 300, 0.92, 15);


            AnimateCard(AvCard, 360, ease, 300, 0.92, 15);


            AnimateOpacity(CreditsHeader, 420, ease, 350);
            AnimateCard(OwnerCard, 460, ease, 350, 0.94, 12);
            AnimateCard(DevsCard, 520, ease, 350, 0.94, 12);
            AnimateCard(ThanksCard, 580, ease, 350, 0.94, 12);


            AnimateOpacity(LinksSection, 640, ease, 400);
        }


        private void AnimateCard(Border card, int delayMs, CubicEase ease, int durationMs, double fromScale, double fromY)
        {
            if (card == null) return;


            var opacityAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(durationMs))
            {
                EasingFunction = ease,
                BeginTime = TimeSpan.FromMilliseconds(delayMs)
            };


            var scaleX = new DoubleAnimation(fromScale, 1, TimeSpan.FromMilliseconds(durationMs))
            {
                EasingFunction = ease,
                BeginTime = TimeSpan.FromMilliseconds(delayMs)
            };
            var scaleY = new DoubleAnimation(fromScale, 1, TimeSpan.FromMilliseconds(durationMs))
            {
                EasingFunction = ease,
                BeginTime = TimeSpan.FromMilliseconds(delayMs)
            };
            var translateY = new DoubleAnimation(fromY, 0, TimeSpan.FromMilliseconds(durationMs))
            {
                EasingFunction = ease,
                BeginTime = TimeSpan.FromMilliseconds(delayMs)
            };


            card.BeginAnimation(OpacityProperty, opacityAnim);


            if (card.RenderTransform is TransformGroup tg && tg.Children.Count >= 2)
            {
                if (tg.Children[0] is ScaleTransform st)
                {
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
                }
                if (tg.Children[1] is TranslateTransform tt)
                {
                    tt.BeginAnimation(TranslateTransform.YProperty, translateY);
                }
            }
        }


        private void AnimateOpacity(UIElement element, int delayMs, CubicEase ease, int durationMs)
        {
            if (element == null) return;
            var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(durationMs))
            {
                EasingFunction = ease,
                BeginTime = TimeSpan.FromMilliseconds(delayMs)
            };
            element.BeginAnimation(OpacityProperty, anim);
        }


        private void Card_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is Border border && border.RenderTransform is TransformGroup tg && tg.Children.Count >= 2)
            {
                if (tg.Children[0] is ScaleTransform st)
                {
                    var sx = new DoubleAnimation(1.03, TimeSpan.FromMilliseconds(200))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    var sy = new DoubleAnimation(1.03, TimeSpan.FromMilliseconds(200))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
                }
                if (tg.Children[1] is TranslateTransform tt)
                {
                    var ty = new DoubleAnimation(-2, TimeSpan.FromMilliseconds(200))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    tt.BeginAnimation(TranslateTransform.YProperty, ty);
                }
            }
        }


        private void Card_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is Border border && border.RenderTransform is TransformGroup tg && tg.Children.Count >= 2)
            {
                if (tg.Children[0] is ScaleTransform st)
                {
                    var sx = new DoubleAnimation(1, TimeSpan.FromMilliseconds(250))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    var sy = new DoubleAnimation(1, TimeSpan.FromMilliseconds(250))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
                }
                if (tg.Children[1] is TranslateTransform tt)
                {
                    var ty = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    tt.BeginAnimation(TranslateTransform.YProperty, ty);
                }
            }
        }


        private void Link_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is Border border && border.RenderTransform is TransformGroup tg && tg.Children.Count >= 2)
            {
                if (tg.Children[0] is ScaleTransform st)
                {
                    var s = new DoubleAnimation(1.06, TimeSpan.FromMilliseconds(180))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, s);
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, s);
                }
                if (tg.Children[1] is TranslateTransform tt)
                {
                    var ty = new DoubleAnimation(-3, TimeSpan.FromMilliseconds(180))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    tt.BeginAnimation(TranslateTransform.YProperty, ty);
                }
            }
        }


        private void Link_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is Border border && border.RenderTransform is TransformGroup tg && tg.Children.Count >= 2)
            {
                if (tg.Children[0] is ScaleTransform st)
                {
                    var s = new DoubleAnimation(1, TimeSpan.FromMilliseconds(220))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, s);
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, s);
                }
                if (tg.Children[1] is TranslateTransform tt)
                {
                    var ty = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    tt.BeginAnimation(TranslateTransform.YProperty, ty);
                }
            }
        }


        private void LoadAvatars()
        {
            var ids = new Dictionary<string, long>
            {
                ["leito"] = 1086319417761202256,
                ["lean"] = 909416193893486602,
                ["winnie"] = 1267942307269836912,
                ["prezone"] = 1459853628071346318,
                ["dem"] = 1491081093292490885,
                ["theo"] = 1081551899397992509,
            };


            var brushes = new Dictionary<string, ImageBrush>
            {
                ["leito"] = AvatarLeito,
                ["lean"] = AvatarLean,
                ["winnie"] = AvatarWinnie,
                ["prezone"] = AvatarPrezone,
                ["dem"] = AvatarDem,
                ["theo"] = AvatarTheo,
            };


            foreach (var kv in ids)
            {
                var key = kv.Key;
                var uid = kv.Value;
                var brush = brushes[key];


                Task.Run(() =>
                {
                    try
                    {
                        string hash = GetAvatarHash(uid.ToString());
                        if (string.IsNullOrEmpty(hash)) return;


                        string ext = hash.StartsWith("a_") ? "gif" : "png";
                        string url = $"https://cdn.discordapp.com/avatars/{uid}/{hash}.{ext}?size=128";


                        using (var wc = new WebClient())
                        {
                            var data = wc.DownloadData(url);


                            Dispatcher.BeginInvoke((Action)(() =>
                            {
                                var bmp = new BitmapImage();
                                bmp.BeginInit();
                                bmp.CacheOption = BitmapCacheOption.OnLoad;
                                bmp.StreamSource = new MemoryStream(data);
                                bmp.EndInit();
                                bmp.Freeze();
                                brush.ImageSource = bmp;
                            }));
                        }
                    }
                    catch { }
                });
            }
        }


        private string GetAvatarHash(string userId)
        {
            try
            {
                using (var wc = new WebClient())
                {
                    var data = wc.DownloadString($"https://japi.rest/discord/v1/user/{userId}");
                    var hash = ExtractHash(data, "\"avatar\":");
                    if (!string.IsNullOrEmpty(hash) && hash != "null") return hash;
                }
            }
            catch { }


            try
            {
                using (var wc = new WebClient())
                {
                    var data = wc.DownloadString($"https://api.lanyard.rest/v1/users/{userId}");
                    var idx = data.LastIndexOf("\"avatar\":");
                    if (idx >= 0)
                    {
                        idx += 9;
                        var start = data.IndexOf('"', idx) + 1;
                        var end = data.IndexOf('"', start);
                        if (end > start)
                        {
                            var val = data.Substring(start, end - start);
                            if (!string.IsNullOrEmpty(val) && val != "null") return val;
                        }
                    }
                }
            }
            catch { }


            return null;
        }


        private string ExtractHash(string json, string marker)
        {
            var idx = json.IndexOf(marker);
            if (idx < 0) return null;
            idx += marker.Length;
            var start = json.IndexOf('"', idx) + 1;
            var end = json.IndexOf('"', start);
            if (end <= start) return null;
            var val = json.Substring(start, end - start);
            return string.IsNullOrEmpty(val) || val == "null" ? null : val;
        }


        private void Website_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => OpenUrl(WebsiteUrl);
        private void Discord_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => OpenUrl(DiscordUrl);
        private void GitHub_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => OpenUrl(GitHubUrl);


        private static void OpenUrl(string url)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch { }
        }
    }
}
