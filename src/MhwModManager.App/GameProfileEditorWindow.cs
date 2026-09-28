using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MhwModManager.Core;

namespace MhwModManager.App;

public sealed class GameProfileEditorWindow : Window
{
    private readonly GameProfile original;
    private readonly TextBox nameBox;
    private readonly TextBox modRootBox;
    private readonly TextBox nexusBox;
    private readonly TextBox saveBox;
    public GameProfile? Result { get; private set; }

    public GameProfileEditorWindow(GameProfile profile)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"game={profile.Id}");
        original=profile;
        Title=$"Game settings — {profile.DisplayName}";
        Width=680;Height=600;MinWidth=580;MinHeight=540;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        ResizeMode=ResizeMode.CanResizeWithGrip;
        var root=new Grid{Margin=new Thickness(28)};
        for(var i=0;i<8;i++)root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        Content=root;
        var row=0;
        var heading=new StackPanel();
        heading.Children.Add(new TextBlock{Text="Game settings",FontSize=24,FontWeight=FontWeights.SemiBold});
        var subtitle=new TextBlock{Text="Change the user-facing profile details and integration paths. Detected installation fields stay read-only.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,8)};
        subtitle.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        heading.Children.Add(subtitle);
        Grid.SetRow(heading,row++);root.Children.Add(heading);
        nameBox=AddField(root,ref row,"Display name",profile.DisplayName,false);
        _=AddField(root,ref row,"Game root",profile.GameRoot,true);
        _=AddField(root,ref row,"Executable",profile.ExecutableRelativePath,true);
        modRootBox=AddField(root,ref row,"Mod target (relative to game root)",profile.ModRootRelativePath,profile.IsMonsterHunterWorld);
        nexusBox=AddField(root,ref row,"Nexus game domain (optional)",profile.NexusGameDomain??string.Empty,false);

        var savePanel=new Grid{Margin=new Thickness(0,8,0,0)};savePanel.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});savePanel.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        saveBox=new TextBox{Text=profile.SavePath??string.Empty,Padding=new Thickness(8,6,8,6)};savePanel.Children.Add(saveBox);
        var browse=new Button{Content="Browse save…",Margin=new Thickness(8,0,0,0),Padding=new Thickness(12,6,12,6)};Grid.SetColumn(browse,1);savePanel.Children.Add(browse);browse.Click+=(_,_)=>BrowseSave();
        AddLabeled(root,ref row,"Save file (optional)",savePanel);

        var hint=new TextBlock{Text=profile.IsMonsterHunterWorld
            ?"Monster Hunter: World uses the enhanced adapter. Its nativePC deployment target is locked for safety."
            :"Generic profiles deploy package files into the configured target. Leave the target blank only when mods belong directly in the game root.",TextWrapping=TextWrapping.Wrap};
        hint.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        var hintCard=new Border{Margin=new Thickness(0,14,0,0),Padding=new Thickness(12),CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(1)};
        hintCard.SetResourceReference(Border.BackgroundProperty,"Panel2");
        hintCard.SetResourceReference(Border.BorderBrushProperty,"Border");
        hintCard.Child=hint;
        Grid.SetRow(hintCard,row++);root.Children.Add(hintCard);

        var footer=new Grid{Margin=new Thickness(0,22,0,0)};
        footer.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        footer.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var keyHint=new TextBlock{Text="Enter saves  •  Esc cancels",VerticalAlignment=VerticalAlignment.Center,FontSize=11};
        keyHint.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        footer.Children.Add(keyHint);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var cancel=new Button{Content="Cancel",Padding=new Thickness(18,7,18,7),Margin=new Thickness(0,0,8,0),IsCancel=true};
        var save=new Button{Content="Save settings",Padding=new Thickness(20,7,20,7),IsDefault=true};
        save.SetResourceReference(Button.StyleProperty,"PrimaryButton");
        buttons.Children.Add(cancel);buttons.Children.Add(save);
        Grid.SetColumn(buttons,1);footer.Children.Add(buttons);
        Grid.SetRow(footer,9);root.Children.Add(footer);
        save.Click+=(_,_)=>Save();
    }

    private static TextBox AddField(Grid root,ref int row,string label,string value,bool readOnly)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var box=new TextBox{Text=value,IsReadOnly=readOnly,Padding=new Thickness(8,6,8,6),Opacity=readOnly?.72:1};
        AddLabeled(root,ref row,label,box);return box;
    }

    private static void AddLabeled(Grid root,ref int row,string label,UIElement control)
    {
        var panel=new StackPanel{Margin=new Thickness(0,8,0,0)};
        var labelText=new TextBlock{Text=label,Margin=new Thickness(2,0,0,5),FontSize=11,FontWeight=FontWeights.SemiBold};
        labelText.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        panel.Children.Add(labelText);
        panel.Children.Add(control);
        Grid.SetRow(panel,row++);root.Children.Add(panel);
    }

    private void BrowseSave()
    {
        var dialog=new OpenFileDialog{Title="Select this game's save file",CheckFileExists=true,Multiselect=false};
        if(dialog.ShowDialog(this)==true)saveBox.Text=dialog.FileName;
    }

    private void Save()
    {
        try
        {
            var modRoot=original.IsMonsterHunterWorld?original.ModRootRelativePath:GameProfile.NormalizeRelative(modRootBox.Text,true);
            var save=string.IsNullOrWhiteSpace(saveBox.Text)?null:Path.GetFullPath(saveBox.Text.Trim());
            Result=original with
            {
                DisplayName=string.IsNullOrWhiteSpace(nameBox.Text)?original.DisplayName:nameBox.Text.Trim(),
                ModRootRelativePath=modRoot,
                NexusGameDomain=string.IsNullOrWhiteSpace(nexusBox.Text)?null:nexusBox.Text.Trim().Trim('/'),
                SavePath=save
            };
            DialogResult=true;
        }
        catch(Exception ex) when(ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            MessageBox.Show(this,ex.Message,"Invalid game profile",MessageBoxButton.OK,MessageBoxImage.Warning);
        }
    }
}
