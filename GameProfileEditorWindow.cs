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
        Title="Configure game";Width=640;Height=510;WindowStartupLocation=WindowStartupLocation.CenterOwner;ResizeMode=ResizeMode.NoResize;
        var root=new Grid{Margin=new Thickness(24)};
        for(var i=0;i<7;i++)root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        Content=root;
        var row=0;
        nameBox=AddField(root,ref row,"Display name",profile.DisplayName,false);
        _=AddField(root,ref row,"Game root",profile.GameRoot,true);
        _=AddField(root,ref row,"Executable",profile.ExecutableRelativePath,true);
        modRootBox=AddField(root,ref row,"Mod target (relative to game root)",profile.ModRootRelativePath,profile.IsMonsterHunterWorld);
        nexusBox=AddField(root,ref row,"Nexus game domain (optional)",profile.NexusGameDomain??string.Empty,false);

        var savePanel=new Grid{Margin=new Thickness(0,8,0,0)};savePanel.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});savePanel.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        saveBox=new TextBox{Text=profile.SavePath??string.Empty,Padding=new Thickness(8,6)};savePanel.Children.Add(saveBox);
        var browse=new Button{Content="Browse save…",Margin=new Thickness(8,0,0,0),Padding=new Thickness(12,6)};Grid.SetColumn(browse,1);savePanel.Children.Add(browse);browse.Click+=(_,_)=>BrowseSave();
        AddLabeled(root,ref row,"Save file (optional)",savePanel);

        var hint=new TextBlock{Text=profile.IsMonsterHunterWorld
            ?"MHW uses the enhanced adapter. Its nativePC deployment target is fixed for safety."
            :"Generic mode deploys package files into the configured target. Leave it blank only for games whose mods belong directly in the game root.",TextWrapping=TextWrapping.Wrap,Opacity=.72,Margin=new Thickness(0,14,0,0)};
        Grid.SetRow(hint,row++);root.Children.Add(hint);

        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,24,0,0)};
        var cancel=new Button{Content="Cancel",Padding=new Thickness(18,7),Margin=new Thickness(0,0,8,0),IsCancel=true};
        var save=new Button{Content="Save",Padding=new Thickness(20,7),IsDefault=true};
        buttons.Children.Add(cancel);buttons.Children.Add(save);Grid.SetRow(buttons,8);root.Children.Add(buttons);
        save.Click+=(_,_)=>Save();
    }

    private static TextBox AddField(Grid root,ref int row,string label,string value,bool readOnly)
    {
        var box=new TextBox{Text=value,IsReadOnly=readOnly,Padding=new Thickness(8,6),Opacity=readOnly?.72:1};
        AddLabeled(root,ref row,label,box);return box;
    }

    private static void AddLabeled(Grid root,ref int row,string label,UIElement control)
    {
        var panel=new StackPanel{Margin=new Thickness(0,7,0,0)};panel.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,0,0,4),Opacity=.72});panel.Children.Add(control);Grid.SetRow(panel,row++);root.Children.Add(panel);
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
