using MhwModManager.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace MhwModManager.App;

public sealed class StartupWindow:Window
{
    private readonly TextBlock detail;

    public StartupWindow()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        Title="Universal Mod Manager";
        Width=540;
        Height=228;
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

        Content=new Border
        {
            Margin=new Thickness(18),
            Padding=new Thickness(26),
            Background=surface,
            BorderBrush=border,
            BorderThickness=new Thickness(1),
            CornerRadius=new CornerRadius(12),
            Effect=new DropShadowEffect{BlurRadius=26,ShadowDepth=0,Opacity=0.42,Color=Colors.Black},
            Child=new StackPanel{Children={brand,detail,progress}}
        };
    }

    public void SetDetail(string text)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        detail.Text=text;
    }
}
