using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Animation;
using System.Numerics;

namespace S4;

public sealed partial class MainWindow : Window
{
    public int Clicks;
    public bool Reduce;

    public MainWindow()
    {
        InitializeComponent();
        for (var i = 1; i <= 40; i++) MailList.Items.Add($"Message {i}");
        Root.Loaded += async (_, _) =>
        {
            await Web.EnsureCoreWebView2Async();
            Web.NavigateToString(Html);
        };
    }

    // A page with a CSS animation (a moving bar) and a button, standing in for the mail body.
    const string Html = """
      <html><body style="margin:0;background:#202020;color:#eee;font:16px Segoe UI">
      <div id=t style="padding:12px">WebView body: <span id=n>0</span></div>
      <button id=b style="margin:12px;padding:8px 16px" onclick="n.textContent=+n.textContent+1;document.title='clicked '+n.textContent">web button</button>
      <div id=bar style="height:24px;width:80px;background:#5af;animation:m 1.2s linear infinite alternate;animation-play-state:paused"></div>
      <style>@keyframes m{from{margin-left:0}to{margin-left:300px}}</style></body></html>
      """;

    void Compose_Click(object sender, RoutedEventArgs e) { Clicks++; Status.Text = $"compose {Clicks}"; AnimateComposition(); }
    public void Dispatch_Compose() => Compose_Click(this, null);
    void Dark_Click(object sender, RoutedEventArgs e)
    {
        Root.RequestedTheme = ChkDark.IsChecked == true ? ElementTheme.Dark : ElementTheme.Light;
        Root.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(ChkDark.IsChecked == true ? Windows.UI.Color.FromArgb(255, 10, 10, 10) : Windows.UI.Color.FromArgb(255, 245, 245, 245));
        AnimateStoryboard();
    }
    void MailList_SelectionChanged(object sender, SelectionChangedEventArgs e) => Status.Text = $"selected {MailList.SelectedIndex}";
    void Ctx_Click(object sender, RoutedEventArgs e) => Status.Text = "ctx " + ((MenuFlyoutItem)sender).Text;

    /// <summary>150 ms slide on the composition thread: invisible to CompositionTarget.Rendering.</summary>
    public void AnimateComposition()
    {
        var v = ElementCompositionPreview.GetElementVisual(Slider);
        var a = v.Compositor.CreateVector3KeyFrameAnimation();
        var d = Reduce ? TimeSpan.FromMilliseconds(1) : TimeSpan.FromMilliseconds(150);
        a.InsertKeyFrame(0, new Vector3(0, 0, 0));
        a.InsertKeyFrame(1, new Vector3(200, 0, 0), v.Compositor.CreateCubicBezierEasingFunction(new Vector2(.2f, 0), new Vector2(0, 1)));
        a.Duration = d;
        v.StartAnimation("Offset", a);
    }

    /// <summary>150 ms slide on the UI thread (dependent animation): ticks CompositionTarget.Rendering.</summary>
    public void AnimateStoryboard()
    {
        var da = new DoubleAnimation { From = 0, To = 200, Duration = TimeSpan.FromMilliseconds(Reduce ? 1 : 150), EnableDependentAnimation = true };
        Storyboard.SetTarget(da, SlideT);
        Storyboard.SetTargetProperty(da, "X");
        var sb = new Storyboard();
        sb.Children.Add(da);
        sb.Begin();
    }
}
