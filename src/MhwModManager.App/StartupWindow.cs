using MhwModManager.Core;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace MhwModManager.App;

public sealed class StartupWindow:Window
{
    private readonly TextBlock detail;
    private readonly Button maintenanceSkipButton;
    private int skipRequested;
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private readonly DispatcherTimer heartbeat = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private string currentDetail = "Getting everything ready…";

    public StartupWindow()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        Title="Universal Mod Manager";
        Width=540;
        Height=280;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        ResizeMode=ResizeMode.NoResize;
        WindowStyle=WindowStyle.None;
        AllowsTransparency=true;
        Background=Brushes.Transparent;
        ShowInTaskbar=false;

        var accent=new SolidColorBrush(Color.FromRgb(216,168,78));
        var accentBright=new SolidColorBrush(Color.FromRgb(242,198,109));
        var text=new SolidColorBrush(Color.FromRgb(244,246,248));
        var muted=new SolidColorBrush(Color.FromRgb(146,156,170));
        var border=new SolidColorBrush(Color.FromRgb(41,49,61));
        var surface=new SolidColorBrush(Color.FromRgb(13,17,23));

        detail=new TextBlock
        {
            Text="Getting everything ready…",
            Foreground=muted,
            Margin=new Thickness(0,8,0,20),
            TextWrapping=TextWrapping.Wrap,
            FontSize=12.5
        };

        var brand=new Grid{Margin=new Thickness(0,0,0,2)};
        brand.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(44)});
        brand.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        var mark=new Border
        {
            Width=40,Height=40,CornerRadius=new CornerRadius(9),
            Background=new SolidColorBrush(Color.FromRgb(58,46,26)),
            BorderBrush=new SolidColorBrush(Color.FromRgb(102,80,32)),
            BorderThickness=new Thickness(1),
            Child=new TextBlock{Text="MH",Foreground=accentBright,FontWeight=FontWeights.Bold,FontSize=14,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}
        };
        brand.Children.Add(mark);
        var titleStack=new StackPanel{Margin=new Thickness(11,0,0,0),VerticalAlignment=VerticalAlignment.Center};
        titleStack.Children.Add(new TextBlock{Text="Universal Mod Manager",Foreground=text,FontSize=20,FontWeight=FontWeights.SemiBold});
        titleStack.Children.Add(new TextBlock{Text="Loading your games and mods",Foreground=muted,FontSize=11,Margin=new Thickness(0,2,0,0)});
        Grid.SetColumn(titleStack,1);
        brand.Children.Add(titleStack);

        var progress=new ProgressBar
        {
            IsIndeterminate=true,
            Height=5,
            Foreground=accent,
            Background=new SolidColorBrush(Color.FromRgb(32,39,51)),
            BorderThickness=new Thickness(0)
        };

        // Only shown after migration and interrupted-operation recovery have
        // completed. A click defers *new* optional work; it never tears down a
        // package while publication, catalog capture or rollback is in progress.
        maintenanceSkipButton=new Button
        {
            Content="Finish current stage, then open app",
            Visibility=Visibility.Collapsed,
            Margin=new Thickness(0,14,0,0),
            Padding=new Thickness(14,8,14,8),
            HorizontalAlignment=HorizontalAlignment.Right,
            Foreground=accentBright,
            Background=new SolidColorBrush(Color.FromRgb(32,39,51)),
            BorderBrush=border,
            BorderThickness=new Thickness(1),
            FontSize=12
        };
        maintenanceSkipButton.Click+=OnSkipButtonClicked;

        heartbeat.Tick += (_, _) => RenderDetail();
        heartbeat.Start();
        Closed += (_, _) => heartbeat.Stop();
        RenderDetail();

        Content=new Border
        {
            Margin=new Thickness(18),
            Padding=new Thickness(26),
            Background=surface,
            BorderBrush=border,
            BorderThickness=new Thickness(1),
            CornerRadius=new CornerRadius(12),
            Effect=new DropShadowEffect{BlurRadius=26,ShadowDepth=0,Opacity=0.42,Color=Colors.Black},
            Child=new StackPanel{Children={brand,detail,progress,maintenanceSkipButton}}
        };
    }

    public void SetDetail(string text)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        currentDetail = text;
        RenderDetail();
    }

    public bool SkipRequested=>Volatile.Read(ref skipRequested)!=0;

    public void SetMaintenanceSkipVisible(bool visible)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(visible)
        {
            Interlocked.Exchange(ref skipRequested,0);
            maintenanceSkipButton.IsEnabled=true;
        }
        maintenanceSkipButton.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
        RenderDetail();
    }

    private void OnSkipButtonClicked(object sender,RoutedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(maintenanceSkipButton.Visibility!=Visibility.Visible)return;
        Interlocked.Exchange(ref skipRequested,1);
        maintenanceSkipButton.IsEnabled=false;
        RenderDetail();
    }

    private void RenderDetail()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var waiting=SkipRequested
            ? Environment.NewLine+"Finishing the current package or stage safely before opening the app…"
            : string.Empty;
        detail.Text=currentDetail+Environment.NewLine+"Elapsed: "+
            elapsed.Elapsed.ToString(@"hh\:mm\:ss",System.Globalization.CultureInfo.InvariantCulture)+waiting;
    }
}
