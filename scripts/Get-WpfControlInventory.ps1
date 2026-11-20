# PowerShell 7 on Windows supplies Roslyn; the installed desktop runtime supplies WPF.
# Emit one stable, reviewable line per source site. Never start Manager or run its code.
[CmdletBinding()]
param([string]$AppRoot = (Join-Path $PSScriptRoot '../src/MhwModManager.App'))
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
Add-Type -AssemblyName Microsoft.CodeAnalysis.CSharp
Add-Type -AssemblyName PresentationFramework
$app = (Resolve-Path $AppRoot).Path
$types = @{}
foreach ($type in [System.Windows.Controls.Control].Assembly.GetExportedTypes()) {
    if ([System.Windows.Controls.Control].IsAssignableFrom($type) -or
        [System.Windows.Controls.DataGridColumn].IsAssignableFrom($type) -or
        [System.Windows.Documents.Hyperlink].IsAssignableFrom($type)) { $types[$type.Name] = $true }
}
foreach ($extra in 'KeyBinding','MouseBinding','CommandBinding','OpenFileDialog','OpenFolderDialog','SaveFileDialog') { $types[$extra] = $true }

$files = @(Get-ChildItem $app -Recurse -File | Where-Object {
    $_.Extension -in '.xaml','.cs' -and $_.FullName -notmatch '[\\/](obj|bin)[\\/]'
})
$syntax = @{}
$declarations = foreach ($file in $files | Where-Object Extension -eq '.cs') {
    $tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($file.FullName))
    $syntax[$file.FullName] = @($tree.GetRoot().DescendantNodes())
    $syntax[$file.FullName] | Where-Object { $_.Kind() -eq 'ClassDeclaration' -and $_.BaseList }
}
do {
    $before = $types.Count
    foreach ($declaration in $declarations) {
        foreach ($base in $declaration.BaseList.Types) {
            if ($types.ContainsKey($base.Type.ToString().Split('.')[-1])) { $types[$declaration.Identifier.ValueText] = $true }
        }
    }
} while ($before -ne $types.Count)

$rows = foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath((Resolve-Path $app).Path,$file.FullName).Replace('\','/')
    if ($file.Extension -eq '.xaml') {
        $doc = [xml][IO.File]::ReadAllText($file.FullName)
        foreach ($element in $doc.SelectNodes('//*')) {
            $type = $element.LocalName
            $events = @($element.Attributes | Where-Object {
                $_.LocalName -match '(Click|Changed|DoubleClick|DragOver|Drop|KeyDown|KeyUp|MouseDown|MouseUp|MouseMove|Executed|CanExecute)$'
            })
            if (!$types.ContainsKey($type) -and !$events -and !$element.NamespaceURI.StartsWith('clr-namespace:')) { continue }
            $context = @($element.SelectNodes('ancestor::*') | Where-Object {
                $_.LocalName -in 'TabItem','Style','DataTemplate','ControlTemplate','DataGridTemplateColumn'
            } | ForEach-Object { $_.LocalName + ':' + ($_.GetAttribute('Header') + $_.GetAttribute('TargetType') + $_.GetAttribute('x:Key')) }) -join '/'
            $guards = @($element.SelectNodes('ancestor::*[@IsEnabled or @Visibility]') | ForEach-Object {
                $_.GetAttribute('IsEnabled') + $_.GetAttribute('Visibility')
            })
            $attrs = @($element.Attributes | Where-Object {
                $_.LocalName -in 'Name','AutomationProperties.Name','AutomationProperties.AutomationId','Content','Header','Text','Binding','Command','CommandParameter','Key','Modifiers','IsChecked','IsExpanded','IsEnabled','IsReadOnly','Visibility','SelectedItem','SelectedIndex','ItemsSource' -or $_ -in $events
            } | ForEach-Object { $_.LocalName + '=' + $_.Value })
            $scope = if ($guards) { ' ; parent=' + ($guards -join ', ') }
            "$relative | $context | $type | $($attrs -join ' ; ')$scope".TrimEnd()
        }
        continue
    }
    $nodes = $syntax[$file.FullName]
    $factories = @($nodes | Where-Object { $_.Kind() -eq 'MethodDeclaration' -and $types.ContainsKey($_.ReturnType.ToString()) } | ForEach-Object { $_.Identifier.ValueText })
    $variables = @{}
    foreach ($declaration in $nodes | Where-Object { $_.Kind() -eq 'VariableDeclaration' -and $types.ContainsKey($_.Type.ToString()) }) {
        foreach ($variable in $declaration.Variables) { $variables[$variable.Identifier.ValueText] = $declaration.Type.ToString() }
    }
    foreach ($node in $nodes) {
        $kind = $node.Kind().ToString()
        if ($kind -in 'ObjectCreationExpression','ImplicitObjectCreationExpression') {
            $ancestors = @($node.Ancestors())
            $variable = $ancestors | Where-Object { $_.Kind() -eq 'VariableDeclarator' } | Select-Object -First 1
            $assignment = $ancestors | Where-Object { $_.Kind() -eq 'SimpleAssignmentExpression' } | Select-Object -First 1
            $type = if ($node.Type) { $node.Type.ToString().Split('.')[-1] } elseif ($variable.Parent.Type) { $variable.Parent.Type.ToString() } elseif ($assignment) { $variables[$assignment.Left.ToString()] }
            if (!$type -or !$types.ContainsKey($type)) { continue }
            $owner = if ($variable) { $variable.Identifier.ValueText } elseif ($assignment) { $assignment.Left.ToString() }
            $props = @($node.Initializer.Expressions | Where-Object {
                $_.Left -and $_.Left.ToString() -in 'Header','Content','IsChecked','IsReadOnly','IsEnabled','IsDefault','IsCancel','SelectionMode','SelectedItem'
            } | ForEach-Object { $_.ToString() -replace '\s+', ' ' })
            $method = $ancestors | Where-Object { $_.Kind() -in 'MethodDeclaration','ConstructorDeclaration' } | Select-Object -First 1
            "$relative | $($method.Identifier) | $type $owner | $($props -join ' ; ')".TrimEnd()
        } elseif ($kind -eq 'AddAssignmentExpression' -and $relative -notlike 'ViewModels/*' -and $node.Left.ToString() -match '(Click|Checked|Changed|Mouse\w+|KeyDown|KeyUp|Drop|DragOver|Closing|Executed)$') {
            $handler = if ($node.Right.Kind().ToString().Contains('Lambda')) { 'inline' } else { $node.Right.ToString() }
            "$relative | event | $($node.Left) | $handler"
        } elseif ($kind -eq 'InvocationExpression' -and ($node.Expression.ToString() -eq 'MessageBox.Show' -or $node.Expression.ToString() -in $factories)) {
            # Each factory call or native dialog is a separate user decision.
            $method = $node.Ancestors() | Where-Object { $_.Kind() -in 'MethodDeclaration','ConstructorDeclaration' } | Select-Object -First 1
            $arguments = $node.ArgumentList.Arguments
            $label = if ($node.Expression.ToString() -in $factories) { $arguments.ToString() } else {
                [string]($arguments | Where-Object { $_.Expression.Kind().ToString() -eq 'StringLiteralExpression' } | Select-Object -Last 1)
            }
            "$relative | $($method.Identifier) | $($node.Expression) | $label".TrimEnd()
        } elseif ($kind -eq 'SimpleMemberAccessExpression' -and $node.Expression.ToString() -eq 'Key') {
            "$relative | keyboard | $node"
        }
    }
}
$rows | Sort-Object -CaseSensitive -Culture en-US
