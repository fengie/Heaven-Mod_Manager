# MHW Manual Mod Manager v7 desktop UI
# Windows PowerShell 5.1 + WPF; no third-party GUI/runtime dependencies.
$ErrorActionPreference='Stop'
$ToolRoot=Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $ToolRoot 'Engine.ps1')

Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml
Add-Type -AssemblyName System.Windows.Forms

function Short-Name([string]$name){
  $n=$name -replace '-\d{2,7}-\d+(?:-\d+){0,5}(?:\s*\(\d+\))?$',''
  if($n.Length -gt 82){return $n.Substring(0,79)+'...'}
  return $n
}
function Get-DuplicateLabel([string]$full,[string]$short){
  if($full -match '-(\d{2,7})-([0-9][0-9-]*)(?:\s*\(\d+\))?$'){return "$short  [Nexus $($Matches[1]) / $($Matches[2].TrimEnd('-'))]"}
  return $full
}
function Escape-Xml([string]$s){return [Security.SecurityElement]::Escape($s)}

# ----- Minimal modal dialogs -----
function Show-TextPrompt([string]$title,[string]$label,[string]$default=''){
  $x=@"
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Title="$(Escape-Xml $title)" Width="500" Height="190" WindowStartupLocation="CenterOwner" ResizeMode="NoResize" Background="#171A20" Foreground="#F3F5F7" FontFamily="Segoe UI">
  <Grid Margin="20"><Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="*"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
    <TextBlock Text="$(Escape-Xml $label)" FontSize="14" Margin="0,0,0,10"/>
    <TextBox x:Name="ValueBox" Grid.Row="1" Height="34" Padding="8,5" Background="#222833" Foreground="White" BorderBrush="#465163" Text="$(Escape-Xml $default)"/>
    <StackPanel Grid.Row="3" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,16,0,0">
      <Button x:Name="CancelBtn" Content="Cancel" Width="90" Height="32" Margin="0,0,8,0"/>
      <Button x:Name="OkBtn" Content="OK" Width="90" Height="32" IsDefault="True"/>
    </StackPanel>
  </Grid>
</Window>
"@
  $r=New-Object Xml.XmlNodeReader ([xml]$x);$w=[Windows.Markup.XamlReader]::Load($r);if($script:Window){$w.Owner=$script:Window}
  $box=$w.FindName('ValueBox');$result=$null
  $w.FindName('CancelBtn').Add_Click({$w.DialogResult=$false;$w.Close()})
  $w.FindName('OkBtn').Add_Click({$script:PromptResult=$box.Text;$w.DialogResult=$true;$w.Close()})
  $script:PromptResult=$null;$box.SelectAll();$box.Focus()|Out-Null
  if($w.ShowDialog() -eq $true){$result=$script:PromptResult}
  $script:PromptResult=$null;return $result
}
function Show-Confirm([string]$title,[string]$message){
  return ([System.Windows.MessageBox]::Show($script:Window,$message,$title,[System.Windows.MessageBoxButton]::YesNo,[System.Windows.MessageBoxImage]::Question) -eq [System.Windows.MessageBoxResult]::Yes)
}
function Show-ErrorDialog([string]$title,$err){
  $message=if($err -is [System.Management.Automation.ErrorRecord]){$err.Exception.Message}else{[string]$err}
  Write-ManagerLog 'error' $title $message
  [System.Windows.MessageBox]::Show($script:Window,"$message`n`nThe error was written to State\V2\Logs. Use Diagnostics > Export Support Bundle if you want me to debug it.",$title,[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Error)|Out-Null
}

# ----- Main XAML -----
$xaml=@'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Name="MainWindow" Title="MHW Manual Mod Manager" Width="1380" Height="860" MinWidth="1100" MinHeight="700"
        WindowStartupLocation="CenterScreen" Background="#12151A" Foreground="#F5F7FA" FontFamily="Segoe UI">
  <Window.Resources>
    <SolidColorBrush x:Key="Panel" Color="#1A1F27"/>
    <SolidColorBrush x:Key="Panel2" Color="#222833"/>
    <SolidColorBrush x:Key="Border" Color="#343C49"/>
    <SolidColorBrush x:Key="Muted" Color="#AAB3C2"/>
    <SolidColorBrush x:Key="Accent" Color="#4EA1FF"/>
    <Style TargetType="Button">
      <Setter Property="Foreground" Value="#F5F7FA"/><Setter Property="Background" Value="#2A313D"/><Setter Property="BorderBrush" Value="#3D4858"/>
      <Setter Property="Padding" Value="12,7"/><Setter Property="Margin" Value="0,0,8,0"/><Setter Property="Cursor" Value="Hand"/><Setter Property="FontSize" Value="13"/>
      <Style.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter Property="Background" Value="#354052"/></Trigger></Style.Triggers>
    </Style>
    <Style x:Key="PrimaryButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}"><Setter Property="Background" Value="#2878CC"/><Setter Property="BorderBrush" Value="#4EA1FF"/><Setter Property="FontWeight" Value="SemiBold"/></Style>
    <Style x:Key="DangerButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}"><Setter Property="Background" Value="#71383D"/><Setter Property="BorderBrush" Value="#A9535B"/></Style>
    <Style x:Key="NavButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
      <Setter Property="HorizontalContentAlignment" Value="Left"/><Setter Property="Background" Value="Transparent"/><Setter Property="BorderThickness" Value="0"/><Setter Property="Margin" Value="8,2"/><Setter Property="Padding" Value="14,10"/><Setter Property="FontSize" Value="14"/>
    </Style>
    <Style TargetType="TextBox"><Setter Property="Background" Value="#222833"/><Setter Property="Foreground" Value="White"/><Setter Property="BorderBrush" Value="#3D4858"/><Setter Property="Padding" Value="8,5"/><Setter Property="CaretBrush" Value="White"/></Style>
    <Style TargetType="ComboBox"><Setter Property="Background" Value="#222833"/><Setter Property="Foreground" Value="White"/><Setter Property="BorderBrush" Value="#3D4858"/><Setter Property="Padding" Value="5"/></Style>
    <Style TargetType="ComboBoxItem"><Setter Property="Background" Value="#222833"/><Setter Property="Foreground" Value="White"/><Setter Property="Padding" Value="6"/></Style>
    <Style TargetType="DataGrid">
      <Setter Property="Background" Value="#171B22"/><Setter Property="Foreground" Value="#EDF1F5"/><Setter Property="BorderBrush" Value="#343C49"/><Setter Property="GridLinesVisibility" Value="Horizontal"/><Setter Property="HorizontalGridLinesBrush" Value="#2D3440"/>
      <Setter Property="RowBackground" Value="#171B22"/><Setter Property="AlternatingRowBackground" Value="#1B2028"/><Setter Property="HeadersVisibility" Value="Column"/><Setter Property="CanUserAddRows" Value="False"/><Setter Property="CanUserDeleteRows" Value="False"/><Setter Property="IsReadOnly" Value="True"/><Setter Property="AutoGenerateColumns" Value="False"/><Setter Property="SelectionMode" Value="Extended"/><Setter Property="SelectionUnit" Value="FullRow"/>
      <Setter Property="EnableRowVirtualization" Value="True"/><Setter Property="EnableColumnVirtualization" Value="True"/><Setter Property="VirtualizingPanel.IsVirtualizing" Value="True"/><Setter Property="VirtualizingPanel.VirtualizationMode" Value="Recycling"/><Setter Property="ScrollViewer.CanContentScroll" Value="True"/><Setter Property="ScrollViewer.IsDeferredScrollingEnabled" Value="True"/>
    </Style>
    <Style TargetType="DataGridColumnHeader"><Setter Property="Background" Value="#242B36"/><Setter Property="Foreground" Value="#EAF0F7"/><Setter Property="BorderBrush" Value="#343C49"/><Setter Property="Padding" Value="8"/><Setter Property="FontWeight" Value="SemiBold"/></Style>
    <Style TargetType="DataGridRow"><Setter Property="MinHeight" Value="34"/><Setter Property="BorderThickness" Value="0"/></Style>
    <Style TargetType="DataGridCell"><Setter Property="BorderThickness" Value="0"/><Setter Property="Padding" Value="6,4"/></Style>
    <Style x:Key="CardBorder" TargetType="Border"><Setter Property="Background" Value="#1A1F27"/><Setter Property="BorderBrush" Value="#343C49"/><Setter Property="BorderThickness" Value="1"/><Setter Property="CornerRadius" Value="8"/><Setter Property="Padding" Value="18"/><Setter Property="Margin" Value="0,0,14,14"/></Style>
  </Window.Resources>

  <Grid>
    <Grid.ColumnDefinitions><ColumnDefinition Width="220"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
    <Border Grid.Column="0" Background="#15191F" BorderBrush="#2C333D" BorderThickness="0,0,1,0">
      <DockPanel>
        <StackPanel DockPanel.Dock="Top" Margin="18,20,18,14">
          <TextBlock Text="MHW" FontSize="26" FontWeight="Bold"/>
          <TextBlock Text="MOD MANAGER  v7" Foreground="#8EC5FF" FontSize="12" FontWeight="SemiBold"/>
        </StackPanel>
        <StackPanel DockPanel.Dock="Top">
          <Button x:Name="NavDashboard" Style="{StaticResource NavButton}" Content="⌂   Dashboard"/>
          <Button x:Name="NavMods" Style="{StaticResource NavButton}" Content="☷   Mods"/>
          <Button x:Name="NavConflicts" Style="{StaticResource NavButton}" Content="⚡   Conflicts"/>
          <Button x:Name="NavOutfits" Style="{StaticResource NavButton}" Content="♙   Outfit coverage"/>
          <Button x:Name="NavProfiles" Style="{StaticResource NavButton}" Content="▣   Profiles"/>
          <Button x:Name="NavDiagnostics" Style="{StaticResource NavButton}" Content="✓   Diagnostics"/>
          <Button x:Name="NavActivity" Style="{StaticResource NavButton}" Content="↶   Activity &amp; undo"/>
        </StackPanel>
        <StackPanel DockPanel.Dock="Bottom" Margin="14,10,14,18">
          <TextBlock Text="GAME" FontSize="10" Foreground="#7F8B9D" FontWeight="Bold" Margin="4,0,0,4"/>
          <TextBlock x:Name="GameRootText" TextWrapping="Wrap" Foreground="#AAB3C2" FontSize="11" Margin="4,0,0,10"/>
          <Button x:Name="OpenGameFolderBtn" Content="Open game folder"/>
          <Button x:Name="OpenDebugBtn" Content="Debug console" Margin="0,6,8,0"/>
        </StackPanel>
      </DockPanel>
    </Border>

    <Grid Grid.Column="1">
      <Grid.RowDefinitions><RowDefinition Height="68"/><RowDefinition Height="*"/><RowDefinition Height="52"/></Grid.RowDefinitions>
      <Border Grid.Row="0" Background="#171B22" BorderBrush="#2C333D" BorderThickness="0,0,0,1">
        <Grid Margin="22,0"><Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/><ColumnDefinition Width="Auto"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
          <StackPanel VerticalAlignment="Center"><TextBlock x:Name="PageTitle" Text="Dashboard" FontSize="22" FontWeight="SemiBold"/><TextBlock x:Name="TopSubtitle" Text="Smart conflict resolution is on" Foreground="#AAB3C2" FontSize="12"/></StackPanel>
          <Border Grid.Column="1" x:Name="PendingBadge" Background="#4A3B20" BorderBrush="#8E7334" BorderThickness="1" CornerRadius="12" Padding="10,5" Margin="0,0,12,0" VerticalAlignment="Center"><TextBlock x:Name="PendingText" Text="No pending changes" Foreground="#F2D38B" FontSize="12"/></Border>
          <Button Grid.Column="2" x:Name="DiscardBtn" Content="Discard" Width="84" Height="36" VerticalAlignment="Center"/><Button Grid.Column="3" x:Name="ApplyBtn" Style="{StaticResource PrimaryButton}" Content="Apply safely" Width="126" Height="36" VerticalAlignment="Center"/>
        </Grid>
      </Border>

      <Grid Grid.Row="1" Margin="22,18,22,16">
        <!-- DASHBOARD -->
        <Grid x:Name="PageDashboard">
          <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="*"/></Grid.RowDefinitions>
          <WrapPanel Grid.Row="0">
            <Border Style="{StaticResource CardBorder}" Width="190"><StackPanel><TextBlock Text="ENABLED" Foreground="#8D98A8" FontSize="11"/><TextBlock x:Name="DashEnabled" Text="0" FontSize="34" FontWeight="SemiBold"/><TextBlock Text="mods active" Foreground="#AAB3C2"/></StackPanel></Border>
            <Border Style="{StaticResource CardBorder}" Width="190"><StackPanel><TextBlock Text="INSTALLED" Foreground="#8D98A8" FontSize="11"/><TextBlock x:Name="DashInstalled" Text="0" FontSize="34" FontWeight="SemiBold"/><TextBlock Text="local mod folders" Foreground="#AAB3C2"/></StackPanel></Border>
            <Border Style="{StaticResource CardBorder}" Width="210"><StackPanel><TextBlock Text="NEEDS ATTENTION" Foreground="#8D98A8" FontSize="11"/><TextBlock x:Name="DashBlocking" Text="0" FontSize="34" FontWeight="SemiBold"/><TextBlock Text="blocking conflict groups" Foreground="#AAB3C2"/></StackPanel></Border>
            <Border Style="{StaticResource CardBorder}" Width="210"><StackPanel><TextBlock Text="AUTO-MANAGED" Foreground="#8D98A8" FontSize="11"/><TextBlock x:Name="DashAuto" Text="0" FontSize="34" FontWeight="SemiBold"/><TextBlock Text="overlap groups resolved" Foreground="#AAB3C2"/></StackPanel></Border>
          </WrapPanel>
          <Grid Grid.Row="1" Margin="0,4,0,16"><Grid.ColumnDefinitions><ColumnDefinition Width="2*"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
            <Border Style="{StaticResource CardBorder}" Margin="0,0,14,0"><StackPanel><TextBlock Text="Smart Apply" FontSize="17" FontWeight="SemiBold"/><TextBlock TextWrapping="Wrap" Margin="0,6,0,12" Foreground="#B7C0CE" Text="Toggle or bulk-select mods, then Apply safely. Harmless shared textures, identical files, recognized option layers, remembered rules, and high-confidence patch subsets are resolved automatically. Only ambiguous structural collisions stop deployment."/><StackPanel Orientation="Horizontal"><Button x:Name="GoModsBtn" Content="Manage mods" Style="{StaticResource PrimaryButton}"/><Button x:Name="GoConflictsBtn" Content="Review conflicts"/></StackPanel></StackPanel></Border>
            <Border Grid.Column="1" Style="{StaticResource CardBorder}" Margin="0"><StackPanel><TextBlock Text="Safety" FontSize="17" FontWeight="SemiBold"/><TextBlock x:Name="DashSafetyText" Text="Checking…" TextWrapping="Wrap" Margin="0,6,0,10" Foreground="#B7C0CE"/><Button x:Name="RunHealthQuickBtn" Content="Open diagnostics"/></StackPanel></Border>
          </Grid>
          <Border Grid.Row="2" Style="{StaticResource CardBorder}" Margin="0"><StackPanel><TextBlock Text="Recent activity" FontSize="17" FontWeight="SemiBold" Margin="0,0,0,8"/><ListBox x:Name="RecentList" Background="Transparent" BorderThickness="0" Foreground="#D6DCE5"/></StackPanel></Border>
        </Grid>

        <!-- MODS -->
        <Grid x:Name="PageMods" Visibility="Collapsed">
          <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="*"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
          <Grid Margin="0,0,0,10"><Grid.ColumnDefinitions><ColumnDefinition Width="320"/><ColumnDefinition Width="150"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
            <TextBox x:Name="ModSearch" Height="34" VerticalContentAlignment="Center" ToolTip="Search mod names"/>
            <ComboBox x:Name="ModFilter" Grid.Column="1" Height="34" Margin="10,0,0,0" SelectedIndex="0"><ComboBoxItem Content="All"/><ComboBoxItem Content="Enabled"/><ComboBoxItem Content="Disabled"/><ComboBoxItem Content="Needs attention"/></ComboBox>
            <StackPanel Grid.Column="3" Orientation="Horizontal"><Button x:Name="ImportBtn" Content="Import archive…"/><Button x:Name="RefreshModsBtn" Content="Refresh"/></StackPanel>
          </Grid>
          <DataGrid x:Name="ModsGrid" Grid.Row="1">
            <DataGrid.Columns>
              <DataGridTextColumn Header="State" Binding="{Binding State}" Width="72"/>
              <DataGridTextColumn Header="Mod" Binding="{Binding DisplayName}" Width="*" MinWidth="360"/>
              <DataGridTextColumn Header="Files" Binding="{Binding Files}" Width="70"/>
              <DataGridTextColumn Header="Overlaps" Binding="{Binding Overlaps}" Width="85"/>
              <DataGridTextColumn Header="Attention" Binding="{Binding Attention}" Width="90"/>
              <DataGridTextColumn Header="Folder" Binding="{Binding Folder}" Width="240"/>
            </DataGrid.Columns>
          </DataGrid>
          <Grid Grid.Row="2" Margin="0,10,0,0"><Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
            <TextBlock Text="Tip: double-click a mod to toggle it. Changes are staged until Apply safely." Foreground="#9EA9B8" VerticalAlignment="Center"/>
            <StackPanel Grid.Column="1" Orientation="Horizontal"><Button x:Name="EnableSelectedBtn" Content="Enable selected"/><Button x:Name="DisableSelectedBtn" Content="Disable selected"/><Button x:Name="MakeWinnerBtn" Content="Make selected mod win overlaps"/><Button x:Name="PinSharedProviderBtn" Content="Use as shared skin provider"/><Button x:Name="UpdateCapturedBtn" Content="Refresh captured version"/></StackPanel>
          </Grid>
        </Grid>

        <!-- CONFLICTS -->
        <Grid x:Name="PageConflicts" Visibility="Collapsed">
          <Grid.ColumnDefinitions><ColumnDefinition Width="1.25*"/><ColumnDefinition Width="0.85*"/></Grid.ColumnDefinitions>
          <Grid Grid.Column="0" Margin="0,0,14,0"><Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="*"/></Grid.RowDefinitions>
            <StackPanel Orientation="Horizontal" Margin="0,0,0,10"><TextBlock Text="Only ambiguous/high-risk overlaps need you. Everything else stays visible for transparency." Foreground="#AAB3C2" VerticalAlignment="Center"/><Button x:Name="RefreshConflictsBtn" Content="Refresh" Margin="14,0,0,0"/></StackPanel>
            <DataGrid x:Name="ConflictsGrid" Grid.Row="1" SelectionMode="Single">
              <DataGrid.Columns><DataGridTextColumn Header="Status" Binding="{Binding Status}" Width="115"/><DataGridTextColumn Header="Type" Binding="{Binding Kind}" Width="145"/><DataGridTextColumn Header="Mods" Binding="{Binding Mods}" Width="*"/><DataGridTextColumn Header="Paths" Binding="{Binding Paths}" Width="60"/><DataGridTextColumn Header="Winner" Binding="{Binding Winner}" Width="210"/></DataGrid.Columns>
            </DataGrid>
          </Grid>
          <Border Grid.Column="1" Style="{StaticResource CardBorder}" Margin="0">
            <Grid><Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="*"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
              <TextBlock x:Name="ConflictTitle" Text="Select a conflict" FontSize="18" FontWeight="SemiBold" TextWrapping="Wrap"/>
              <TextBlock x:Name="ConflictReason" Grid.Row="1" Text="" Foreground="#B7C0CE" TextWrapping="Wrap" Margin="0,8,0,12"/>
              <ComboBox x:Name="ConflictWinner" Grid.Row="2" Height="34" Margin="0,0,0,12"/>
              <ListBox x:Name="ConflictPaths" Grid.Row="3" Background="#15191F" BorderBrush="#343C49" Foreground="#CED5DF" FontFamily="Consolas" FontSize="11"/>
              <WrapPanel Grid.Row="4" Margin="0,12,0,0"><Button x:Name="UseWinnerBtn" Content="Use winner once"/><Button x:Name="RememberOverlayBtn" Content="Remember as overlay" Style="{StaticResource PrimaryButton}"/><Button x:Name="IncompatibleBtn" Content="Incompatible — keep selected"/><Button x:Name="ClearConflictRuleBtn" Content="Clear saved rule"/></WrapPanel>
            </Grid>
          </Border>
        </Grid>

        <!-- OUTFITS -->
        <Grid x:Name="PageOutfits" Visibility="Collapsed">
          <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="*"/></Grid.RowDefinitions>
          <Grid Margin="0,0,0,10"><Grid.ColumnDefinitions><ColumnDefinition Width="320"/><ColumnDefinition Width="160"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
            <TextBox x:Name="OutfitSearch" Height="34"/>
            <ComboBox x:Name="OutfitFilter" Grid.Column="1" Height="34" Margin="10,0,0,0" SelectedIndex="0"><ComboBoxItem Content="All"/><ComboBoxItem Content="Active"/><ComboBoxItem Content="Available"/><ComboBoxItem Content="Unmodded"/></ComboBox>
            <Button x:Name="RefreshOutfitsBtn" Grid.Column="3" Content="Scan outfit coverage"/>
          </Grid>
          <DataGrid x:Name="OutfitsGrid" Grid.Row="1">
            <DataGrid.Columns><DataGridTextColumn Header="Armor" Binding="{Binding Armor}" Width="210"/><DataGridTextColumn Header="Sex" Binding="{Binding Sex}" Width="42"/><DataGridTextColumn Header="Model" Binding="{Binding Model}" Width="95"/><DataGridTextColumn Header="Status" Binding="{Binding Status}" Width="85"/><DataGridTextColumn Header="Head" Binding="{Binding Head}" Width="160"/><DataGridTextColumn Header="Chest" Binding="{Binding Chest}" Width="160"/><DataGridTextColumn Header="Arms" Binding="{Binding Arms}" Width="160"/><DataGridTextColumn Header="Waist" Binding="{Binding Waist}" Width="160"/><DataGridTextColumn Header="Legs" Binding="{Binding Legs}" Width="160"/></DataGrid.Columns>
          </DataGrid>
        </Grid>

        <!-- PROFILES -->
        <Grid x:Name="PageProfiles" Visibility="Collapsed">
          <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="*"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
          <TextBlock Text="Profiles remember enabled mods and file winners. Loading a profile stages it first; nothing touches nativePC until Apply safely." Foreground="#AAB3C2" Margin="0,0,0,12"/>
          <DataGrid x:Name="ProfilesGrid" Grid.Row="1" SelectionMode="Single"><DataGrid.Columns><DataGridTextColumn Header="Profile" Binding="{Binding Name}" Width="*"/><DataGridTextColumn Header="Mods" Binding="{Binding Mods}" Width="80"/><DataGridTextColumn Header="Shared policy" Binding="{Binding SharedPolicy}" Width="140"/></DataGrid.Columns></DataGrid>
          <WrapPanel Grid.Row="2" Margin="0,10,0,0"><Button x:Name="ProfileSaveBtn" Content="Save current as…"/><Button x:Name="ProfileLoadBtn" Content="Load selected" Style="{StaticResource PrimaryButton}"/><Button x:Name="ProfileRenameBtn" Content="Rename"/><Button x:Name="ProfileDeleteBtn" Content="Delete"/><Button x:Name="ProfileExportBtn" Content="Export"/><Button x:Name="ProfileImportBtn" Content="Import…"/></WrapPanel>
        </Grid>

        <!-- DIAGNOSTICS -->
        <Grid x:Name="PageDiagnostics" Visibility="Collapsed">
          <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="*"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
          <Border Style="{StaticResource CardBorder}" Margin="0,0,0,12"><Grid><Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions><StackPanel><TextBlock Text="One-click debugging" FontSize="17" FontWeight="SemiBold"/><TextBlock Text="Run a full integrity scan, or export one small support ZIP with state metadata, conflict summaries, recent history and logs. Mod files and blob backups are not included." Foreground="#B7C0CE" TextWrapping="Wrap" Margin="0,5,20,0"/></StackPanel><StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center"><Button x:Name="RunHealthBtn" Content="Run full health check" Style="{StaticResource PrimaryButton}"/><Button x:Name="SupportBundleBtn" Content="Export support bundle"/></StackPanel></Grid></Border>
          <TextBlock x:Name="HealthSummary" Grid.Row="1" Text="Quick checks run automatically. Full source/snapshot hashing runs only when requested." Foreground="#AAB3C2" Margin="0,0,0,8"/>
          <DataGrid x:Name="HealthGrid" Grid.Row="2"><DataGrid.Columns><DataGridTextColumn Header="Severity" Binding="{Binding Severity}" Width="80"/><DataGridTextColumn Header="Category" Binding="{Binding Category}" Width="100"/><DataGridTextColumn Header="Mod" Binding="{Binding Mod}" Width="220"/><DataGridTextColumn Header="Message" Binding="{Binding Message}" Width="*"/><DataGridTextColumn Header="Path" Binding="{Binding Path}" Width="260"/></DataGrid.Columns></DataGrid>
          <WrapPanel Grid.Row="3" Margin="0,10,0,0"><Button x:Name="OpenStateBtn" Content="Open State folder"/><Button x:Name="OpenLogsBtn" Content="Open logs"/><Button x:Name="OpenModsBtn" Content="Open Mods folder"/><Button x:Name="CopyHealthBtn" Content="Copy selected issue"/></WrapPanel>
        </Grid>

        <!-- ACTIVITY -->
        <Grid x:Name="PageActivity" Visibility="Collapsed"><Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="*"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
          <TextBlock Text="Every deployment is transactional. The latest completed deployment can be rolled back using its saved before-state and file snapshots." Foreground="#AAB3C2" Margin="0,0,0,12"/>
          <DataGrid x:Name="HistoryGrid" Grid.Row="1" SelectionMode="Single"><DataGrid.Columns><DataGridTextColumn Header="Time" Binding="{Binding Time}" Width="190"/><DataGridTextColumn Header="Operation" Binding="{Binding Description}" Width="*"/><DataGridTextColumn Header="ID" Binding="{Binding Id}" Width="220"/></DataGrid.Columns></DataGrid>
          <WrapPanel Grid.Row="2" Margin="0,10,0,0"><Button x:Name="UndoBtn" Content="Undo latest deployment" Style="{StaticResource DangerButton}"/><Button x:Name="RefreshHistoryBtn" Content="Refresh"/></WrapPanel>
        </Grid>
      </Grid>

      <Border Grid.Row="2" Background="#171B22" BorderBrush="#2C333D" BorderThickness="0,1,0,0">
        <Grid Margin="22,0"><Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
          <TextBlock x:Name="StatusIcon" Text="●" Foreground="#5CCB8A" VerticalAlignment="Center" Margin="0,0,8,0"/>
          <TextBlock x:Name="StatusText" Grid.Column="1" Text="Ready" Foreground="#C1C9D5" VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/>
          <TextBlock x:Name="BusyText" Grid.Column="2" Text="" Foreground="#8EC5FF" VerticalAlignment="Center"/>
        </Grid>
      </Border>
    </Grid>

    <Border x:Name="BusyOverlay" Grid.ColumnSpan="2" Background="#CC101318" Visibility="Collapsed" Panel.ZIndex="9999">
      <Border HorizontalAlignment="Center" VerticalAlignment="Center" Background="#202630" BorderBrush="#3E4A5A" BorderThickness="1" CornerRadius="10" Padding="30" MinWidth="420">
        <StackPanel>
          <TextBlock x:Name="BusyOverlayText" Text="Working…" FontSize="18" FontWeight="SemiBold" HorizontalAlignment="Center"/>
          <ProgressBar IsIndeterminate="True" Height="5" Margin="0,18,0,14"/>
          <TextBlock Text="The manager is working in the background. The window should stay responsive." Foreground="#AAB3C2" TextWrapping="Wrap" TextAlignment="Center"/>
          <Button x:Name="CancelWorkerBtn" Content="Cancel safe operation" HorizontalAlignment="Center" Margin="0,18,0,0"/>
        </StackPanel>
      </Border>
    </Border>
  </Grid>
</Window>
'@

$reader=New-Object System.Xml.XmlNodeReader ([xml]$xaml)
$script:Window=[Windows.Markup.XamlReader]::Load($reader)

# Control lookup helper
function C([string]$name){return $script:Window.FindName($name)}
$script:PageNames=@('Dashboard','Mods','Conflicts','Outfits','Profiles','Diagnostics','Activity')
$script:CurrentPage='Dashboard'
$script:LiveState=$null;$script:PendingState=$null;$script:Dirty=$false
$script:ModRows=@();$script:ConflictGroups=@();$script:OutfitRows=@();$script:HealthRows=@()
$script:CandidateIndex=$null;$script:AnalysisDirty=$true;$script:OverlapCount=@{};$script:AttentionCount=@{}
$script:ModDirectories=@();$script:InstalledCount=0;$script:LiveStateHash=$null
$script:DraftFile=Join-Path $V2Root 'ui-draft.json'
$script:Worker=$null;$script:RecentHistory=@();$script:HistoryDirty=$true

$script:DraftTimer=New-Object Windows.Threading.DispatcherTimer
$script:DraftTimer.Interval=[TimeSpan]::FromMilliseconds(1200)
$script:ModFilterTimer=New-Object Windows.Threading.DispatcherTimer
$script:ModFilterTimer.Interval=[TimeSpan]::FromMilliseconds(180)
$script:OutfitFilterTimer=New-Object Windows.Threading.DispatcherTimer
$script:OutfitFilterTimer.Interval=[TimeSpan]::FromMilliseconds(180)
$script:WorkerPoll=New-Object Windows.Threading.DispatcherTimer
$script:WorkerPoll.Interval=[TimeSpan]::FromMilliseconds(90)

function Set-Status([string]$message,[string]$kind='ok'){
  (C 'StatusText').Text=$message
  if($kind -eq 'error'){(C 'StatusIcon').Foreground='#FF6B6B'}elseif($kind -eq 'warn'){(C 'StatusIcon').Foreground='#F0B45A'}else{(C 'StatusIcon').Foreground='#5CCB8A'}
}
function Pump-Ui { $frame=New-Object Windows.Threading.DispatcherFrame;[Windows.Threading.Dispatcher]::CurrentDispatcher.BeginInvoke([Windows.Threading.DispatcherPriority]::Background,[Windows.Threading.DispatcherOperationCallback]{param($f)$f.Continue=$false;return $null},$frame)|Out-Null;[Windows.Threading.Dispatcher]::PushFrame($frame) }
function Set-Busy([string]$message){(C 'BusyText').Text=$message;if($message){$script:Window.Cursor=[Windows.Input.Cursors]::Wait}else{$script:Window.Cursor=[Windows.Input.Cursors]::Arrow};Pump-Ui}
function Invoke-UiTask([string]$label,[scriptblock]$body){
  try{Set-Busy $label;Set-Status $label 'ok';$r=& $body;return $r}catch{Set-Status $_.Exception.Message 'error';Show-ErrorDialog $label $_;return $null}finally{Set-Busy ''}
}

function Set-BusyOverlay([bool]$busy,[string]$label='',[bool]$critical=$false){
  (C 'BusyOverlay').Visibility=if($busy){'Visible'}else{'Collapsed'}
  (C 'BusyOverlayText').Text=if($label){$label}else{'Working…'}
  (C 'CancelWorkerBtn').Visibility=if($busy -and !$critical){'Visible'}else{'Collapsed'}
  (C 'BusyText').Text=if($busy){$label}else{''}
}
function Stop-WorkerResources($w){
  if($null -eq $w){return}
  try{$w.PowerShell.Dispose()}catch{}
  try{$w.Runspace.Close()}catch{}
  try{$w.Runspace.Dispose()}catch{}
}
function Cancel-CurrentWorker {
  $w=$script:Worker
  if($null -eq $w){return}
  if($w.Critical){Set-Status 'This operation is transactional and cannot be cancelled safely.' 'warn';return}
  $w.Cancelled=$true
  Set-Status 'Cancelling background operation…' 'warn'
  try{$null=$w.PowerShell.BeginStop($null,$null)}catch{try{$w.PowerShell.Stop()}catch{}}
}
function Start-EngineWorker([string]$label,[string]$operation,$state,$payload,[scriptblock]$onComplete,[bool]$critical=$false){
  if($null -ne $script:Worker){Set-Status 'Another background operation is already running.' 'warn';return $false}
  $onComplete=$onComplete.GetNewClosure()
  $rs=[System.Management.Automation.Runspaces.RunspaceFactory]::CreateRunspace()
  $rs.ApartmentState=[Threading.ApartmentState]::MTA
  $rs.Open()
  $ps=[PowerShell]::Create();$ps.Runspace=$rs
  $workerCode=@'
param($toolRoot,$gameRoot,$operation,$state,$payload)
$ErrorActionPreference='Stop'
$workerTimer=[Diagnostics.Stopwatch]::StartNew()
try{
  . (Join-Path $toolRoot 'Engine.ps1')
  $GameRoot=$gameRoot;$NativeRoot=Join-Path $GameRoot 'nativePC'
  switch($operation){
    'stage' {$data=Stage-ModsEnabledBulk $state @($payload.Names) ([bool]$payload.Enabled)}
    'refreshCapture' {$next=Clone-State $state;Map-Set $next.mods ([string]$payload.Name) (Snapshot-Mod ([string]$payload.Name));$data=$next}
    'healthFast' {$data=@(Get-HealthIssues $state -Fast)}
    'healthFull' {$data=@(Get-HealthIssues $state)}
    'outfits' {$data=@(Get-CoverageRowsForUi $state)}
    'apply' {$data=Commit-PlanAutomatic $state $payload.NextState ([string]$payload.Description)}
    'undo' {$data=Undo-LastDeploymentAutomatic $state}
    'support' {$data=Export-SupportBundle $state}
    'inspectImport' {$data=New-ImportInspection ([string]$payload.Path)}
    'completeImport' {$data=Complete-ImportInspection $payload.Inspection ([string]$payload.Name) ([string]$payload.Layout) @($payload.RootFiles)}
    'cancelImport' {Cancel-ImportInspection $payload.Inspection;$data=$true}
    default {throw "Unknown worker operation: $operation"}
  }
  $workerTimer.Stop();[pscustomobject]@{ok=$true;data=$data;elapsedMs=$workerTimer.ElapsedMilliseconds}
}catch{
  $workerTimer.Stop();[pscustomobject]@{ok=$false;message=$_.Exception.Message;detail=$_.Exception.ToString();elapsedMs=$workerTimer.ElapsedMilliseconds}
}
'@
  $null=$ps.AddScript($workerCode).AddArgument($ToolRoot).AddArgument($GameRoot).AddArgument($operation).AddArgument($state).AddArgument($payload)
  try{$async=$ps.BeginInvoke()}catch{Stop-WorkerResources ([pscustomobject]@{PowerShell=$ps;Runspace=$rs});Show-ErrorDialog $label $_;return $false}
  $script:Worker=[pscustomobject]@{Label=$label;Operation=$operation;PowerShell=$ps;Runspace=$rs;Async=$async;OnComplete=$onComplete;Critical=$critical;Cancelled=$false}
  Set-BusyOverlay $true $label $critical;Set-Status $label
  $script:WorkerPoll.Start();return $true
}
$script:WorkerPoll.Add_Tick({
  $w=$script:Worker
  if($null -eq $w){$script:WorkerPoll.Stop();return}
  if(!$w.Async.IsCompleted){return}
  $script:WorkerPoll.Stop();$script:Worker=$null;Set-BusyOverlay $false
  try{
    $out=@($w.PowerShell.EndInvoke($w.Async))
    if($w.Cancelled){Set-Status 'Background operation cancelled.' 'warn';return}
    $packet=if($out.Count){$out[-1]}else{$null}
    if($null -eq $packet){throw 'Background worker returned no result.'}
    if(!$packet.ok){if($packet.detail){Write-ManagerLog 'error' $w.Label ([ordered]@{elapsedMs=$packet.elapsedMs;detail=[string]$packet.detail})};throw $packet.message}
    Write-ManagerLog 'info' ('Background operation finished: '+$w.Operation) ([ordered]@{elapsedMs=$packet.elapsedMs;label=$w.Label})
    & $w.OnComplete $packet.data
  }catch{
    if(!$w.Cancelled){Set-Status $_.Exception.Message 'error';Show-ErrorDialog $w.Label $_}
  }finally{Stop-WorkerResources $w}
})
(C 'CancelWorkerBtn').Add_Click({Cancel-CurrentWorker})

function Invalidate-Analysis {$script:AnalysisDirty=$true;$script:CandidateIndex=$null}
function Invalidate-History {$script:HistoryDirty=$true}
function Ensure-RecentHistory {
  if(!$script:HistoryDirty){return}
  $script:RecentHistory=@(Get-HistoryRecords 50)
  $script:HistoryDirty=$false
}
function Refresh-ModDirectoryCache {
  $script:ModDirectories=@(Get-ChildItem -LiteralPath $ModsRoot -Directory -ErrorAction SilentlyContinue | Sort-Object Name)
  $script:InstalledCount=$script:ModDirectories.Count
}
function Ensure-Analysis {
  if(!$script:AnalysisDirty -and $null -ne $script:CandidateIndex){return}
  $analysisTimer=[Diagnostics.Stopwatch]::StartNew()
  $script:CandidateIndex=Get-CandidateIndex $script:PendingState
  $script:ConflictGroups=@(Get-ConflictGroupsForUi $script:PendingState $script:CandidateIndex)
  $script:OverlapCount=@{};$script:AttentionCount=@{}
  foreach($g in $script:ConflictGroups){
    foreach($n in $g.names){
      if(!$script:OverlapCount.ContainsKey($n)){$script:OverlapCount[$n]=0};$script:OverlapCount[$n]++
      if($g.blocking){if(!$script:AttentionCount.ContainsKey($n)){$script:AttentionCount[$n]=0};$script:AttentionCount[$n]++}
    }
  }
  $script:AnalysisDirty=$false;$analysisTimer.Stop()
  Write-ManagerLog 'info' 'Conflict analysis rebuilt' ([ordered]@{elapsedMs=$analysisTimer.ElapsedMilliseconds;enabled=@($script:PendingState.order).Count;paths=$script:CandidateIndex.Count;groups=$script:ConflictGroups.Count})
}

function Show-Page([string]$name){
  $script:CurrentPage=$name
  foreach($p in $script:PageNames){(C ('Page'+$p)).Visibility=if($p -eq $name){'Visible'}else{'Collapsed'}}
  (C 'PageTitle').Text=if($name -eq 'Outfits'){'Outfit coverage'}elseif($name -eq 'Activity'){'Activity & undo'}else{$name}
  if($name -eq 'Outfits' -and !$script:OutfitRows.Count){Refresh-Outfits}
  if($name -eq 'Diagnostics' -and !$script:HealthRows.Count -and $null -eq $script:Worker){Refresh-Health -Fast}
  if($name -eq 'Activity'){Refresh-History}
  if($name -eq 'Profiles'){Refresh-Profiles}
}

function Save-DraftNow {
  $script:DraftTimer.Stop()
  if(!$script:Dirty){Remove-Item -LiteralPath $script:DraftFile -Force -ErrorAction SilentlyContinue;return}
  try{
    $changedMods=New-Map
    foreach($name in @($script:PendingState.order)){
      $pm=Map-Get $script:PendingState.mods $name;$lm=Map-Get $script:LiveState.mods $name
      if($null -eq $lm -or $pm.importedAt -ne $lm.importedAt){Map-Set $changedMods $name $pm}
    }
    $draft=[ordered]@{format=2;baseStateHash=$script:LiveStateHash;savedAt=(Get-Date).ToString('o');order=@($script:PendingState.order);changedMods=$changedMods;winners=$script:PendingState.winners;sharedPolicy=$script:PendingState.sharedPolicy;smartConflicts=$script:PendingState.smartConflicts;relations=$script:PendingState.relations;resourceProviders=$script:PendingState.resourceProviders}
    Atomic-Json $script:DraftFile $draft
  }catch{Write-ManagerLog 'warn' 'Could not save UI draft' $_.Exception.Message}
}
function Save-Draft {$script:DraftTimer.Stop();$script:DraftTimer.Start()}
$script:DraftTimer.Add_Tick({Save-DraftNow})
function Clear-Draft {$script:DraftTimer.Stop();Remove-Item -LiteralPath $script:DraftFile -Force -ErrorAction SilentlyContinue}
function Restore-DraftIfSafe {
  if(!(Test-Path -LiteralPath $script:DraftFile -PathType Leaf)){return}
  try{
    $d=Get-Content -LiteralPath $script:DraftFile -Raw|ConvertFrom-Json
    $actual=File-Hash $StateFile
    if(!$d.baseStateHash -or $d.baseStateHash -ne $actual){Clear-Draft;return}
    if($d.format -eq 2){
      $pending=Clone-State $script:LiveState;$mods=New-Map
      foreach($name in @($d.order)){
        if(Map-Has $d.changedMods $name){Map-Set $mods $name (Map-Get $d.changedMods $name)}
        elseif(Map-Has $script:LiveState.mods $name){Map-Set $mods $name (Map-Get $script:LiveState.mods $name)}
        else{throw "Draft references a mod snapshot that is no longer available: $name"}
      }
      $pending.mods=$mods;$pending.order=@($d.order);$pending.winners=Copy-MapShallow $d.winners
      if($d.sharedPolicy){$pending.sharedPolicy=$d.sharedPolicy};if($d.smartConflicts){$pending.smartConflicts=$d.smartConflicts}
      if($d.relations){$pending.relations=Copy-MapShallow $d.relations};if($d.resourceProviders){$pending.resourceProviders=Copy-MapShallow $d.resourceProviders}
      $script:PendingState=Ensure-StateShape $pending
    }elseif($d.pending){$script:PendingState=Ensure-StateShape $d.pending}else{Clear-Draft;return}
    $script:Dirty=$true;Invalidate-Analysis;Set-Status 'Restored staged changes from the previous UI session.' 'warn'
  }catch{Clear-Draft;Write-ManagerLog 'warn' 'Discarded unreadable UI draft' $_.Exception.Message}
}
function Mark-Dirty([string]$why){$script:Dirty=$true;Invalidate-Analysis;Save-Draft;Set-Status $why 'warn';Refresh-CoreVisuals}
function Discard-Pending {$script:PendingState=Clone-State $script:LiveState;$script:Dirty=$false;Clear-Draft;Invalidate-Analysis;Set-Status 'Discarded staged changes.';Refresh-CoreVisuals}

function Get-ModRows {
  Ensure-Analysis
  $shortCounts=@{};foreach($d in $script:ModDirectories){$sn=Short-Name $d.Name;if(!$shortCounts.ContainsKey($sn)){$shortCounts[$sn]=0};$shortCounts[$sn]++}
  $rows=[Collections.Generic.List[object]]::new()
  foreach($d in $script:ModDirectories){
    $on=Map-Has $script:PendingState.mods $d.Name;$sn=Short-Name $d.Name;$display=if($shortCounts[$sn]-gt 1){Get-DuplicateLabel $d.Name $sn}else{$sn}
    $files=if($on){@((Map-Get $script:PendingState.mods $d.Name).files.PSObject.Properties).Count}else{''}
    $rows.Add([pscustomobject]@{State=if($on){'● ON'}else{'○ OFF'};DisplayName=$display;Name=$d.Name;Files=$files;Overlaps=if($script:OverlapCount.ContainsKey($d.Name)){$script:OverlapCount[$d.Name]}else{0};Attention=if($script:AttentionCount.ContainsKey($d.Name)){$script:AttentionCount[$d.Name]}else{0};Folder=$d.Name})
  }
  return @($rows)
}
function Filter-ModGrid {
  $q=(C 'ModSearch').Text.Trim();$filter=((C 'ModFilter').SelectedItem.Content);$view=[Collections.Generic.List[object]]::new()
  foreach($r in $script:ModRows){
    if($q -and $r.DisplayName.IndexOf($q,[StringComparison]::OrdinalIgnoreCase) -lt 0 -and $r.Folder.IndexOf($q,[StringComparison]::OrdinalIgnoreCase) -lt 0){continue}
    if($filter -eq 'Enabled' -and $r.State -notlike '*ON'){continue};if($filter -eq 'Disabled' -and $r.State -notlike '*OFF'){continue};if($filter -eq 'Needs attention' -and $r.Attention -le 0){continue}
    $view.Add($r)
  }
  (C 'ModsGrid').ItemsSource=@($view)
}
function Refresh-ModGrid([switch]$Rebuild){if($Rebuild -or !$script:ModRows.Count){$script:ModRows=@(Get-ModRows)};Filter-ModGrid}
function Queue-ModFilter {$script:ModFilterTimer.Stop();$script:ModFilterTimer.Start()}
$script:ModFilterTimer.Add_Tick({$script:ModFilterTimer.Stop();Filter-ModGrid})
function Get-SelectedModNames {return @((C 'ModsGrid').SelectedItems | ForEach-Object {$_.Name} | Where-Object {$_})}
function Stage-SelectedMods([bool]$enabled){
  $names=Get-SelectedModNames;if(!$names.Count){Set-Status 'Select one or more mods first.' 'warn';return}
  if(!$enabled){
    $script:PendingState=Stage-ModsEnabledBulk $script:PendingState $names $false
    Mark-Dirty "Staged $($names.Count) mod(s) OFF."
    return
  }
  $payload=[pscustomobject]@{Names=@($names);Enabled=$true}
  Start-EngineWorker 'Capturing and enabling selected mods…' 'stage' $script:PendingState $payload {
    param($newState)
    $script:PendingState=Ensure-StateShape $newState
    Mark-Dirty "Staged $($names.Count) mod(s) ON."
  } | Out-Null
}
function Toggle-SelectedMod {
  $names=Get-SelectedModNames;if($names.Count -ne 1){return};$name=$names[0];$on=Map-Has $script:PendingState.mods $name
  Stage-SelectedMods (!$on)
}
function Make-SelectedWinner {
  $names=Get-SelectedModNames;if($names.Count -ne 1){Set-Status 'Select exactly one mod.' 'warn';return};$name=$names[0]
  if(!(Map-Has $script:PendingState.mods $name)){Set-Status 'Enable that mod first.' 'warn';return}
  Ensure-Analysis;$m=Map-Get $script:PendingState.mods $name;$count=0
  foreach($p in $m.files.PSObject.Properties){if(@(Get-Candidates $script:PendingState $p.Name $script:CandidateIndex).Count -gt 1){Map-Set $script:PendingState.winners $p.Name $name;$count++}}
  Mark-Dirty "$name will win $count overlapping file(s)."
}
function Pin-SelectedSharedProvider {
  $names=Get-SelectedModNames;if($names.Count -ne 1){Set-Status 'Select exactly one enabled mod.' 'warn';return};$name=$names[0]
  if(!(Map-Has $script:PendingState.mods $name)){Set-Status 'Enable that mod first.' 'warn';return}
  $ns='nativepc\pl\f_equip\mod_hepsy';Map-Set $script:PendingState.resourceProviders $ns $name
  $provider=Map-Get $script:PendingState.mods $name
  foreach($p in @($script:PendingState.winners.PSObject.Properties)){if($p.Name.ToLowerInvariant().StartsWith($ns+'\') -and (Map-Has $provider.files $p.Name)){Map-Remove $script:PendingState.winners $p.Name}}
  Mark-Dirty "Pinned $name as the shared mod_hepsy texture/resource provider."
}
function Refresh-CapturedSelected {
  $names=Get-SelectedModNames;if($names.Count -ne 1){Set-Status 'Select exactly one enabled mod.' 'warn';return};$name=$names[0]
  if(!(Map-Has $script:PendingState.mods $name)){Set-Status 'That mod is OFF.' 'warn';return}
  if(!(Show-Confirm 'Refresh captured version' "Re-snapshot the current files in '$name'?`n`nThis stages the changed source as the new enabled version; nothing deploys until Apply safely.")){return}
  Start-EngineWorker 'Hashing current mod source…' 'refreshCapture' $script:PendingState ([pscustomobject]@{Name=$name}) {
    param($newState)
    $script:PendingState=Ensure-StateShape $newState
    Mark-Dirty "Refreshed captured version for $name."
  } | Out-Null
}

function Refresh-Conflicts([switch]$Rebuild){
  if($Rebuild){Invalidate-Analysis};Ensure-Analysis
  $rows=[Collections.Generic.List[object]]::new()
  for($i=0;$i -lt $script:ConflictGroups.Count;$i++){
    $g=$script:ConflictGroups[$i]
    $rows.Add([pscustomobject]@{Index=$i;Status=if($g.blocking){'! NEEDS ATTENTION'}elseif($g.resolution -eq 'Automatic'){'✓ AUTO'}else{'✓ RESOLVED'};Kind=$g.kind;Mods=($g.names -join '  ↔  ');Paths=$g.items.Count;Winner=if($g.winner){Short-Name $g.winner}else{'—'}})
  }
  (C 'ConflictsGrid').ItemsSource=@($rows)
  if($rows.Count){(C 'ConflictsGrid').SelectedIndex=0}else{Show-ConflictDetails $null}
}
function Get-SelectedConflictGroup {$row=(C 'ConflictsGrid').SelectedItem;if($null -eq $row){return $null};return $script:ConflictGroups[[int]$row.Index]}
function Show-ConflictDetails($g){
  if(!$g){(C 'ConflictTitle').Text='No active overlaps';(C 'ConflictReason').Text='';(C 'ConflictPaths').ItemsSource=@();(C 'ConflictWinner').ItemsSource=@();return}
  (C 'ConflictTitle').Text="[$($g.kind)]  $($g.names -join '  ↔  ')";(C 'ConflictReason').Text=$g.reason;(C 'ConflictPaths').ItemsSource=@($g.items|ForEach-Object{$_.key});(C 'ConflictWinner').ItemsSource=@($g.names);if($g.winner){(C 'ConflictWinner').SelectedItem=$g.winner}else{(C 'ConflictWinner').SelectedIndex=0}
  (C 'RememberOverlayBtn').IsEnabled=($g.names.Count -eq 2);(C 'IncompatibleBtn').IsEnabled=($g.names.Count -eq 2)
}
function Resolve-Conflict([string]$mode){
  $g=Get-SelectedConflictGroup;if(!$g){return};$winner=[string](C 'ConflictWinner').SelectedItem;if(!$winner){Set-Status 'Choose a winner first.' 'warn';return}
  if($mode -eq 'once'){$script:PendingState=Set-GroupWinnerMemory $script:PendingState $g $winner}
  elseif($mode -eq 'overlay'){$script:PendingState=Set-OverlayMemory $script:PendingState $g.names[0] $g.names[1] $winner}
  elseif($mode -eq 'incompatible'){
    Set-ModRelationMemory $script:PendingState $g.names[0] $g.names[1] 'incompatible' $null
    $losers=@($g.names|Where-Object{$_ -ne $winner});$script:PendingState=Stage-ModsEnabledBulk $script:PendingState $losers $false
  }elseif($mode -eq 'clear'){
    if($g.names.Count -eq 2){Set-ModRelationMemory $script:PendingState $g.names[0] $g.names[1] 'clear' $null}
    foreach($item in $g.items){Map-Remove $script:PendingState.winners $item.key}
  }
  Mark-Dirty ($(if($mode -eq 'overlay'){"Remembered overlay: $winner wins this pair."}elseif($mode -eq 'incompatible'){"Marked pair incompatible; keeping $winner enabled."}elseif($mode -eq 'clear'){'Cleared saved conflict rule.'}else{"Saved file winner: $winner."}))
}

function Refresh-Dashboard {
  Ensure-Analysis
  $sum=Get-ManagerSummary $script:PendingState $script:ConflictGroups $script:InstalledCount
  (C 'DashEnabled').Text=[string]$sum.Enabled;(C 'DashInstalled').Text=[string]$sum.Installed;(C 'DashBlocking').Text=[string]$sum.Blocking;(C 'DashAuto').Text=[string]$sum.AutoManaged
  (C 'DashSafetyText').Text=if($sum.HealthIssues -eq 0 -and $sum.Blocking -eq 0){'Quick checks are clean. Deployment remains transactional and crash-recoverable.'}elseif($sum.Blocking -gt 0){"$($sum.Blocking) conflict group(s) need one-time decisions before deployment."}else{"$($sum.HealthIssues) quick health issue(s) detected. Open Diagnostics for details."}
  Ensure-RecentHistory;$hist=@($script:RecentHistory|Select-Object -First 5);(C 'RecentList').ItemsSource=@($hist|ForEach-Object{"$($_.startedAt)   $($_.description)"})
}
function Refresh-PendingUi {
  (C 'PendingText').Text=if($script:Dirty){'Staged changes — not deployed'}else{'No pending changes'};(C 'PendingBadge').Background=if($script:Dirty){'#4A3B20'}else{'#203B2B'};(C 'ApplyBtn').IsEnabled=$script:Dirty
}
function Refresh-CoreVisuals {
  Ensure-Analysis;Refresh-PendingUi;Refresh-Dashboard;Refresh-ModGrid -Rebuild;Refresh-Conflicts
  if($script:CurrentPage -eq 'Profiles'){Refresh-Profiles};if($script:CurrentPage -eq 'Activity'){Refresh-History}
}
function Refresh-AllVisuals {Refresh-CoreVisuals}

function Apply-Pending {
  if(!$script:Dirty){Set-Status 'Nothing is staged.';return}
  Save-DraftNow
  $payload=[pscustomobject]@{NextState=$script:PendingState;Description='GUI Apply'}
  Start-EngineWorker 'Building and applying transactional plan…' 'apply' $script:LiveState $payload {
    param($result)
    if($result.blocked){Set-Status "$($result.blockers.Count) conflict decision(s) need attention. Nothing was changed on disk." 'warn';Invalidate-Analysis;Show-Page 'Conflicts';Refresh-CoreVisuals;return}
    $script:LiveState=Load-State;$script:LiveStateHash=File-Hash $StateFile;$script:PendingState=Clone-State $script:LiveState;$script:Dirty=$false;Clear-Draft;$script:OutfitRows=@();Invalidate-Analysis;Invalidate-History;Refresh-CoreVisuals;Refresh-History;Set-Status $result.message
  } $true | Out-Null
}

function Refresh-Profiles {
  if(!$script:LiveState){return};$rows=[Collections.Generic.List[object]]::new()
  foreach($p in $script:LiveState.profiles.PSObject.Properties|Sort-Object Name){$policy=if($p.Value.PSObject.Properties['sharedPolicy']){$p.Value.sharedPolicy}else{'identical'};$rows.Add([pscustomobject]@{Name=$p.Name;Mods=@($p.Value.order).Count;SharedPolicy=$policy})}
  (C 'ProfilesGrid').ItemsSource=@($rows)
}
function Get-SelectedProfileName {$r=(C 'ProfilesGrid').SelectedItem;if($r){return $r.Name};return $null}
function Sync-LiveAfterMetadataChange {
  $script:LiveState=Load-State;$script:LiveStateHash=File-Hash $StateFile;$script:PendingState.profiles=$script:LiveState.profiles
  if($script:Dirty){Save-Draft};Refresh-Profiles
}
function Save-CurrentProfile {
  $name=Show-TextPrompt 'Save profile' 'Profile name:' '';if(!$name){return}
  try{$live=Load-State;$next=Clone-State $live;$mods=New-Map;foreach($m in @($script:PendingState.order)){Map-Set $mods $m (Map-Get $script:PendingState.mods $m)};Map-Set $next.profiles $name ([pscustomobject]@{order=@($script:PendingState.order);mods=$mods;winners=(Copy-MapShallow $script:PendingState.winners);sharedPolicy=$script:PendingState.sharedPolicy});Save-State $next;Sync-LiveAfterMetadataChange;Set-Status "Saved profile '$name'."}catch{Show-ErrorDialog 'Save profile' $_}
}
function Load-SelectedProfile {
  $name=Get-SelectedProfileName;if(!$name){Set-Status 'Select a profile.' 'warn';return};$profile=Map-Get $script:LiveState.profiles $name
  $script:PendingState.mods=Copy-MapShallow $profile.mods;$script:PendingState.order=@($profile.order);$script:PendingState.winners=Copy-MapShallow $profile.winners
  if($profile.PSObject.Properties['sharedPolicy']){$script:PendingState.sharedPolicy=$profile.sharedPolicy};Mark-Dirty "Loaded profile '$name' into the staging area."
}
function Delete-SelectedProfile {$name=Get-SelectedProfileName;if(!$name){return};if(!(Show-Confirm 'Delete profile' "Delete profile '$name'?`n`nThis does not change enabled mods.")){return};try{Remove-ProfileAutomatic (Load-State) $name;Sync-LiveAfterMetadataChange;Set-Status "Deleted profile '$name'."}catch{Show-ErrorDialog 'Delete profile' $_}}
function Rename-SelectedProfile {$name=Get-SelectedProfileName;if(!$name){return};$new=Show-TextPrompt 'Rename profile' 'New name:' $name;if(!$new -or $new -eq $name){return};try{Rename-Profile (Load-State) $name $new;Sync-LiveAfterMetadataChange;Set-Status "Renamed profile '$name'."}catch{Show-ErrorDialog 'Rename profile' $_}}
function Export-SelectedProfile {$name=Get-SelectedProfileName;if(!$name){return};try{$path=Export-Profile (Load-State) $name;Set-Status "Exported profile to $path";Start-Process explorer.exe "/select,`"$path`""}catch{Show-ErrorDialog 'Export profile' $_}}
function Import-ProfileUi {$dlg=New-Object Microsoft.Win32.OpenFileDialog;$dlg.Filter='MHW profile (*.mhwprofile.json)|*.mhwprofile.json|JSON (*.json)|*.json';if($dlg.ShowDialog() -ne $true){return};$name=Show-TextPrompt 'Import profile' 'Saved profile name (blank = exported name):' '';try{Import-Profile (Load-State) $dlg.FileName $name;Sync-LiveAfterMetadataChange;Set-Status 'Imported profile.'}catch{Show-ErrorDialog 'Import profile' $_}}

function Refresh-History {Ensure-RecentHistory;$rows=[Collections.Generic.List[object]]::new();foreach($h in @($script:RecentHistory)){$rows.Add([pscustomobject]@{Id=$h.id;Time=$h.startedAt;Description=$h.description})};(C 'HistoryGrid').ItemsSource=@($rows)}
function Undo-Latest {
  if($script:Dirty){if(!(Show-Confirm 'Undo latest deployment' 'You have staged but unapplied changes. Discard them and undo the latest completed deployment?')){return};Discard-Pending}
  elseif(!(Show-Confirm 'Undo latest deployment' 'Undo the latest completed deployment?')){return}
  Start-EngineWorker 'Undoing latest deployment…' 'undo' $script:LiveState $null {
    param($r)
    if($r -and $r.success){$script:LiveState=Load-State;$script:LiveStateHash=File-Hash $StateFile;$script:PendingState=Clone-State $script:LiveState;$script:Dirty=$false;Clear-Draft;$script:OutfitRows=@();Invalidate-Analysis;Invalidate-History;Refresh-CoreVisuals;Refresh-History;Set-Status $r.message}
  } $true | Out-Null
}

function Set-HealthRows($issues){
  $script:HealthRows=@($issues);(C 'HealthGrid').ItemsSource=$script:HealthRows
  (C 'HealthSummary').Text=if(!$script:HealthRows.Count){'✓ No issues found.'}else{"$($script:HealthRows.Count) issue(s) found. Errors and Action items should be handled before deployment."}
  if(!$script:HealthRows.Count){Set-Status 'Health check passed.'}else{Set-Status "$($script:HealthRows.Count) health issue(s) found." 'warn'}
}
function Refresh-Health([switch]$Fast){
  $label=if($Fast){'Running quick safety checks…'}else{'Hashing sources, snapshots and deployment…'};$op=if($Fast){'healthFast'}else{'healthFull'}
  Start-EngineWorker $label $op $script:PendingState $null {param($issues) Set-HealthRows @($issues)} | Out-Null
}
function Export-SupportUi {
  Start-EngineWorker 'Creating support bundle…' 'support' $script:PendingState $null {
    param($path)
    if($path){Set-Status "Support bundle created: $path";Start-Process explorer.exe "/select,`"$path`""}
  } | Out-Null
}

function Refresh-Outfits {
  Start-EngineWorker 'Scanning armor model coverage…' 'outfits' $script:PendingState $null {
    param($rows)
    $script:OutfitRows=@($rows);Filter-Outfits;Set-Status "Loaded $($script:OutfitRows.Count) armor/sex coverage rows."
  } | Out-Null
}
function Filter-Outfits {
  $q=(C 'OutfitSearch').Text.Trim();$f=(C 'OutfitFilter').SelectedItem.Content;$view=[Collections.Generic.List[object]]::new()
  foreach($r in $script:OutfitRows){
    if($q -and $r.Armor.IndexOf($q,[StringComparison]::OrdinalIgnoreCase) -lt 0 -and $r.Model.IndexOf($q,[StringComparison]::OrdinalIgnoreCase) -lt 0 -and $r.Mods.IndexOf($q,[StringComparison]::OrdinalIgnoreCase) -lt 0){continue}
    if($f -ne 'All' -and $r.Status -ne $f){continue};$view.Add($r)
  }
  (C 'OutfitsGrid').ItemsSource=@($view)
}
function Queue-OutfitFilter {$script:OutfitFilterTimer.Stop();$script:OutfitFilterTimer.Start()}
$script:OutfitFilterTimer.Add_Tick({$script:OutfitFilterTimer.Stop();Filter-Outfits})

function Show-ImportChoiceDialog($inspection,[string]$suggestedName){
  $x=@'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Title="Import mod" Width="700" Height="510" WindowStartupLocation="CenterOwner" Background="#171A20" Foreground="#F3F5F7" FontFamily="Segoe UI">
<Grid Margin="20"><Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="*"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
<TextBlock Text="Import layout" FontSize="20" FontWeight="SemiBold"/><TextBlock Grid.Row="1" Text="Choose the install layout. Root-level DLL/INI/JSON files are never selected automatically." Foreground="#AAB3C2" Margin="0,4,0,12"/>
<StackPanel Grid.Row="2"><TextBlock Text="Mod folder name"/><TextBox x:Name="NameBox" Height="32" Margin="0,4,0,10"/><TextBlock Text="Detected layout"/><ComboBox x:Name="LayoutBox" Height="32" Margin="0,4,0,10"/></StackPanel>
<Grid Grid.Row="3"><Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="*"/></Grid.RowDefinitions><TextBlock Text="Optional files beside the layout (Ctrl+click to select)"/><ListBox x:Name="RootFiles" Grid.Row="1" SelectionMode="Extended" Background="#11151B" Foreground="White" BorderBrush="#343C49" Margin="0,5,0,0"/></Grid>
<StackPanel Grid.Row="4" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,14,0,0"><Button x:Name="Cancel" Content="Cancel" Width="90" Margin="0,0,8,0"/><Button x:Name="Import" Content="Import" Width="100"/></StackPanel>
</Grid></Window>
'@
  $r=New-Object Xml.XmlNodeReader ([xml]$x);$w=[Windows.Markup.XamlReader]::Load($r);$w.Owner=$script:Window;$nameBox=$w.FindName('NameBox');$layout=$w.FindName('LayoutBox');$root=$w.FindName('RootFiles');$nameBox.Text=$suggestedName;$layout.ItemsSource=@($inspection.layouts|ForEach-Object{$_.relative});$layout.SelectedIndex=0
  $refreshRoots={if($layout.SelectedIndex -ge 0){$lp=$inspection.layouts[$layout.SelectedIndex].fullPath;$root.ItemsSource=@(Get-ImportRootFiles $inspection $lp|ForEach-Object{$_.Name})}};$layout.Add_SelectionChanged($refreshRoots);& $refreshRoots
  $script:ImportDialogResult=$null;$w.FindName('Cancel').Add_Click({$w.DialogResult=$false;$w.Close()});$w.FindName('Import').Add_Click({if(!$nameBox.Text.Trim()){[Windows.MessageBox]::Show($w,'Enter a mod folder name.')|Out-Null;return};$script:ImportDialogResult=[pscustomobject]@{name=$nameBox.Text.Trim();layout=$inspection.layouts[$layout.SelectedIndex].fullPath;rootFiles=@($root.SelectedItems)};$w.DialogResult=$true;$w.Close()})
  $ok=$w.ShowDialog();$res=$script:ImportDialogResult;$script:ImportDialogResult=$null;if($ok -eq $true){return $res};return $null
}
function Import-ModUi {
  $dlg=New-Object Microsoft.Win32.OpenFileDialog;$dlg.Filter='Mod archives (*.zip;*.7z;*.rar)|*.zip;*.7z;*.rar|All files (*.*)|*.*';if($dlg.ShowDialog() -ne $true){return}
  $archive=$dlg.FileName;$suggest=[IO.Path]::GetFileNameWithoutExtension($archive)
  Start-EngineWorker 'Inspecting archive safely…' 'inspectImport' $null ([pscustomobject]@{Path=$archive}) {
    param($inspection)
    $choice=Show-ImportChoiceDialog $inspection $suggest
    if(!$choice){Start-EngineWorker 'Cleaning temporary import files…' 'cancelImport' $null ([pscustomobject]@{Inspection=$inspection}) {param($x) Set-Status 'Import cancelled.' 'warn'} | Out-Null;return}
    $payload=[pscustomobject]@{Inspection=$inspection;Name=$choice.name;Layout=$choice.layout;RootFiles=@($choice.rootFiles)}
    Start-EngineWorker 'Importing selected layout…' 'completeImport' $null $payload {
      param($dest)
      if($dest){Refresh-ModDirectoryCache;Refresh-ModGrid -Rebuild;Refresh-Dashboard;Set-Status "Imported '$($choice.name)'. It remains OFF until you enable it."}
    } | Out-Null
  } | Out-Null
}

# ----- Events -----
(C 'NavDashboard').Add_Click({Show-Page 'Dashboard'});(C 'NavMods').Add_Click({Show-Page 'Mods'});(C 'NavConflicts').Add_Click({Show-Page 'Conflicts'});(C 'NavOutfits').Add_Click({Show-Page 'Outfits'});(C 'NavProfiles').Add_Click({Show-Page 'Profiles'});(C 'NavDiagnostics').Add_Click({Show-Page 'Diagnostics'});(C 'NavActivity').Add_Click({Show-Page 'Activity'})
(C 'GoModsBtn').Add_Click({Show-Page 'Mods'});(C 'GoConflictsBtn').Add_Click({Show-Page 'Conflicts'});(C 'DiscardBtn').Add_Click({if($script:Dirty -and (Show-Confirm 'Discard staged changes' 'Discard all staged changes that have not been deployed?')){Discard-Pending}});(C 'ApplyBtn').Add_Click({Apply-Pending});(C 'RunHealthQuickBtn').Add_Click({Show-Page 'Diagnostics'})
(C 'ModSearch').Add_TextChanged({Queue-ModFilter});(C 'ModFilter').Add_SelectionChanged({Queue-ModFilter});(C 'ModsGrid').Add_MouseDoubleClick({Toggle-SelectedMod});(C 'EnableSelectedBtn').Add_Click({Stage-SelectedMods $true});(C 'DisableSelectedBtn').Add_Click({Stage-SelectedMods $false});(C 'MakeWinnerBtn').Add_Click({Make-SelectedWinner});(C 'PinSharedProviderBtn').Add_Click({Pin-SelectedSharedProvider});(C 'UpdateCapturedBtn').Add_Click({Refresh-CapturedSelected});(C 'ImportBtn').Add_Click({Import-ModUi});(C 'RefreshModsBtn').Add_Click({Refresh-ModDirectoryCache;Invalidate-Analysis;Refresh-CoreVisuals;Set-Status 'Refreshed mod folders and analysis.'})
(C 'ConflictsGrid').Add_SelectionChanged({Show-ConflictDetails (Get-SelectedConflictGroup)});(C 'RefreshConflictsBtn').Add_Click({Invalidate-Analysis;Refresh-CoreVisuals});(C 'UseWinnerBtn').Add_Click({Resolve-Conflict 'once'});(C 'RememberOverlayBtn').Add_Click({Resolve-Conflict 'overlay'});(C 'IncompatibleBtn').Add_Click({Resolve-Conflict 'incompatible'});(C 'ClearConflictRuleBtn').Add_Click({Resolve-Conflict 'clear'})
(C 'OutfitSearch').Add_TextChanged({Queue-OutfitFilter});(C 'OutfitFilter').Add_SelectionChanged({Queue-OutfitFilter});(C 'RefreshOutfitsBtn').Add_Click({$script:OutfitRows=@();Refresh-Outfits})
(C 'ProfileSaveBtn').Add_Click({Save-CurrentProfile});(C 'ProfileLoadBtn').Add_Click({Load-SelectedProfile});(C 'ProfileRenameBtn').Add_Click({Rename-SelectedProfile});(C 'ProfileDeleteBtn').Add_Click({Delete-SelectedProfile});(C 'ProfileExportBtn').Add_Click({Export-SelectedProfile});(C 'ProfileImportBtn').Add_Click({Import-ProfileUi})
(C 'RunHealthBtn').Add_Click({Refresh-Health});(C 'SupportBundleBtn').Add_Click({Export-SupportUi});(C 'OpenStateBtn').Add_Click({Start-Process explorer.exe $V2Root});(C 'OpenLogsBtn').Add_Click({New-Item -ItemType Directory -Force -Path $LogRoot|Out-Null;Start-Process explorer.exe $LogRoot});(C 'OpenModsBtn').Add_Click({Start-Process explorer.exe $ModsRoot});(C 'CopyHealthBtn').Add_Click({$r=(C 'HealthGrid').SelectedItem;if($r){[Windows.Clipboard]::SetText("$($r.Severity) | $($r.Category) | $($r.Mod) | $($r.Message) | $($r.Path)");Set-Status 'Copied issue details.'}})
(C 'UndoBtn').Add_Click({Undo-Latest});(C 'RefreshHistoryBtn').Add_Click({Refresh-History});(C 'OpenGameFolderBtn').Add_Click({if(Test-Path -LiteralPath $GameRoot){Start-Process explorer.exe $GameRoot}else{Set-Status 'Game folder is missing.' 'error'}});(C 'OpenDebugBtn').Add_Click({$debugScript=Join-Path $ToolRoot 'ModManager.ps1';Start-Process powershell.exe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -NoExit -File `"$debugScript`""})

$script:Window.Add_Closing({param($sender,$e)
  if($null -ne $script:Worker -and $script:Worker.Critical){$e.Cancel=$true;Set-Status 'Finish the current transactional operation before closing.' 'warn';return}
  if($null -ne $script:Worker){try{$script:Worker.Cancelled=$true;$script:Worker.PowerShell.Stop()}catch{}}
  Save-DraftNow
})

# ----- Startup -----
try{
  if(!(Test-Path -LiteralPath $GameRoot -PathType Container) -and !(Test-Path -LiteralPath $StateFile -PathType Leaf)){
    $picker=New-Object System.Windows.Forms.FolderBrowserDialog;$picker.Description='Select your Monster Hunter World game folder';$picker.ShowNewFolderButton=$false
    if($picker.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK){$GameRoot=$picker.SelectedPath;$NativeRoot=Join-Path $GameRoot 'nativePC';Save-UiSettings @{gameRoot=$GameRoot}}
  }
  $script:LiveState=Initialize-State;$script:LiveStateHash=File-Hash $StateFile;$script:PendingState=Clone-State $script:LiveState
  Refresh-ModDirectoryCache;Restore-DraftIfSafe
  (C 'GameRootText').Text=$GameRoot;(C 'TopSubtitle').Text="Smart conflicts: $($script:PendingState.smartConflicts)  •  shared files: $($script:PendingState.sharedPolicy)"
  Refresh-CoreVisuals;Show-Page 'Dashboard';Write-ManagerLog 'info' 'GUI started' ([ordered]@{version='7.0';enabled=@($script:LiveState.order).Count;gameRoot=$GameRoot})
}catch{Show-ErrorDialog 'Startup failed' $_;throw}

[void]$script:Window.ShowDialog()
