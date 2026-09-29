$ErrorActionPreference = 'Stop'

function Emit-Result([object]$data) {
    [pscustomobject]@{ ok = $true; data = $data } | ConvertTo-Json -Depth 9 -Compress
    exit 0
}

function Emit-Error([string]$code, [string]$message, [object]$details = $null) {
    $body = [ordered]@{ ok = $false; code = $code; message = $message }
    if ($null -ne $details) { $body.details = $details }
    [pscustomobject]$body | ConvertTo-Json -Depth 7 -Compress
    exit 0
}

try {
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
} catch {
    Emit-Error 'UIA_BACKEND_UNAVAILABLE' 'Windows UI Automation assemblies are unavailable'
}

function Safe-Text([object]$value, [int]$limit = 512) {
    if ($null -eq $value) { return '' }
    $text = [string]$value
    if ($text.Length -gt $limit) { return $text.Substring(0, $limit) }
    return $text
}

function Control-Type-Name([object]$controlType) {
    if ($null -eq $controlType) { return '' }
    $name = [string]$controlType.ProgrammaticName
    if ($name.StartsWith('ControlType.')) { return $name.Substring(12) }
    return $name
}

function Describe-Element([System.Windows.Automation.AutomationElement]$element) {
    try { $rect = $element.Current.BoundingRectangle } catch { $rect = $null }
    try { $isPassword = [bool]$element.Current.IsPassword } catch { $isPassword = $false }
    return [pscustomobject]@{
        name = (Safe-Text $element.Current.Name)
        automation_id = (Safe-Text $element.Current.AutomationId)
        class_name = (Safe-Text $element.Current.ClassName)
        control_type = (Control-Type-Name $element.Current.ControlType)
        process_id = [int]$element.Current.ProcessId
        native_window_handle = [int]$element.Current.NativeWindowHandle
        enabled = [bool]$element.Current.IsEnabled
        offscreen = [bool]$element.Current.IsOffscreen
        focusable = [bool]$element.Current.IsKeyboardFocusable
        has_keyboard_focus = [bool]$element.Current.HasKeyboardFocus
        is_password = $isPassword
        rect = $(if ($null -eq $rect) { $null } else {
            [pscustomobject]@{
                x = [double]$rect.X; y = [double]$rect.Y
                width = [double]$rect.Width; height = [double]$rect.Height
            }
        })
    }
}

function Resolve-Root([object]$request) {
    if ($null -ne $request.hwnd -and [int64]$request.hwnd -gt 0) {
        try {
            return [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr][int64]$request.hwnd)
        } catch {
            Emit-Error 'UIA_WINDOW_NOT_FOUND' 'Unable to resolve requested hwnd' @{ hwnd = [int64]$request.hwnd }
        }
    }

    $pidValue = 0
    if ($null -ne $request.pid) { $pidValue = [int]$request.pid }
    $title = ''
    if ($null -ne $request.title) { $title = [string]$request.title }
    if ($pidValue -le 0 -and [string]::IsNullOrWhiteSpace($title)) {
        return [System.Windows.Automation.AutomationElement]::RootElement
    }

    $roots = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.Condition]::TrueCondition
    )
    $matches = New-Object System.Collections.ArrayList
    foreach ($element in $roots) {
        try {
            if ($pidValue -gt 0 -and [int]$element.Current.ProcessId -ne $pidValue) { continue }
            if (-not [string]::IsNullOrWhiteSpace($title) -and
                ([string]$element.Current.Name).IndexOf($title, [StringComparison]::OrdinalIgnoreCase) -lt 0) { continue }
            [void]$matches.Add($element)
        } catch {}
    }
    if ($matches.Count -eq 0) {
        Emit-Error 'UIA_WINDOW_NOT_FOUND' 'No matching top-level UI Automation window found'
    }
    if ($matches.Count -gt 1 -and -not [bool]$request.first_match) {
        $preview = @($matches | Select-Object -First 8 | ForEach-Object { Describe-Element $_ })
        Emit-Error 'UIA_WINDOW_AMBIGUOUS' 'Multiple top-level windows matched; provide hwnd/pid or a narrower title' @{
            matches = $preview; total = $matches.Count
        }
    }
    return $matches[0]
}

function Element-Matches(
    [System.Windows.Automation.AutomationElement]$element,
    [object]$selector
) {
    if ($null -eq $selector) { return $true }
    try {
        if ($null -ne $selector.automation_id -and [string]$element.Current.AutomationId -ne [string]$selector.automation_id) { return $false }
        if ($null -ne $selector.name -and [string]$element.Current.Name -ne [string]$selector.name) { return $false }
        if ($null -ne $selector.name_contains -and
            ([string]$element.Current.Name).IndexOf([string]$selector.name_contains, [StringComparison]::OrdinalIgnoreCase) -lt 0) { return $false }
        if ($null -ne $selector.class_name -and [string]$element.Current.ClassName -ne [string]$selector.class_name) { return $false }
        if ($null -ne $selector.control_type -and
            (Control-Type-Name $element.Current.ControlType) -ine [string]$selector.control_type) { return $false }
        if ($null -ne $selector.process_id -and [int]$element.Current.ProcessId -ne [int]$selector.process_id) { return $false }
        if ($null -ne $selector.enabled -and [bool]$element.Current.IsEnabled -ne [bool]$selector.enabled) { return $false }
        if ($null -ne $selector.offscreen -and [bool]$element.Current.IsOffscreen -ne [bool]$selector.offscreen) { return $false }
        return $true
    } catch {
        return $false
    }
}

function Collect-Elements(
    [System.Windows.Automation.AutomationElement]$root,
    [object]$selector,
    [int]$maxNodes,
    [int]$maxDepth,
    [bool]$childrenOnly,
    [bool]$includeRoot = $false
) {
    $items = New-Object System.Collections.ArrayList
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $queue = New-Object System.Collections.Queue

    if ($includeRoot) {
        $queue.Enqueue([pscustomobject]@{ element = $root; depth = 0 })
    } else {
        try { $child = $walker.GetFirstChild($root) } catch { $child = $null }
        while ($null -ne $child) {
            $queue.Enqueue([pscustomobject]@{ element = $child; depth = 1 })
            try { $child = $walker.GetNextSibling($child) } catch { $child = $null }
        }
    }

    $visited = 0
    $truncated = $false
    while ($queue.Count -gt 0) {
        $node = $queue.Dequeue()
        $visited++
        if (Element-Matches $node.element $selector) { [void]$items.Add($node.element) }
        if ($visited -ge $maxNodes -or $items.Count -ge $maxNodes) {
            $truncated = ($queue.Count -gt 0)
            break
        }
        if (-not $childrenOnly -and [int]$node.depth -lt $maxDepth) {
            try { $child = $walker.GetFirstChild($node.element) } catch { $child = $null }
            while ($null -ne $child) {
                $queue.Enqueue([pscustomobject]@{ element = $child; depth = ([int]$node.depth + 1) })
                try { $child = $walker.GetNextSibling($child) } catch { $child = $null }
            }
        }
    }
    return [pscustomobject]@{ items = $items; visited = $visited; truncated = $truncated }
}

function Resolve-One(
    [System.Windows.Automation.AutomationElement]$root,
    [object]$request
) {
    $maxNodes = [Math]::Min([Math]::Max([int]$request.max_nodes, 10), 1000)
    $maxDepth = [Math]::Min([Math]::Max([int]$request.max_depth, 1), 12)
    $childrenOnly = ([string]$request.scope -eq 'children')
    $found = Collect-Elements $root $request.selector $maxNodes $maxDepth $childrenOnly $false
    if ($found.items.Count -eq 0) {
        Emit-Error 'UIA_NOT_FOUND' 'No UI Automation element matched the selector'
    }
    if ($found.items.Count -gt 1 -and -not [bool]$request.first_match) {
        $preview = @($found.items | Select-Object -First 8 | ForEach-Object { Describe-Element $_ })
        Emit-Error 'UIA_AMBIGUOUS' 'Multiple UI Automation elements matched; narrow the selector or explicitly set first_match=true' @{
            matches = $preview; total = $found.items.Count
        }
    }
    return $found.items[0]
}

try {
    if ([string]::IsNullOrWhiteSpace($env:HEAVEN_UIA_REQUEST)) {
        Emit-Error 'UIA_REQUEST_MISSING' 'HEAVEN_UIA_REQUEST is missing'
    }
    $json = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($env:HEAVEN_UIA_REQUEST))
    $request = $json | ConvertFrom-Json
    $root = Resolve-Root $request
    $operation = [string]$request.operation
    $maxNodes = [Math]::Min([Math]::Max([int]$request.max_nodes, 10), 1000)
    $maxDepth = [Math]::Min([Math]::Max([int]$request.max_depth, 1), 12)

    if ($operation -eq 'uia_tree') {
        $found = Collect-Elements $root $null $maxNodes $maxDepth $false [bool]$request.include_root
        $described = @($found.items | ForEach-Object { Describe-Element $_ })
        Emit-Result ([pscustomobject]@{
            items = $described; count = $described.Count; visited = $found.visited
            truncated = $found.truncated; max_nodes = $maxNodes; max_depth = $maxDepth
        })
    }

    if ($operation -eq 'uia_find') {
        $found = Collect-Elements $root $request.selector $maxNodes $maxDepth ([string]$request.scope -eq 'children') $false
        $described = @($found.items | ForEach-Object { Describe-Element $_ })
        Emit-Result ([pscustomobject]@{
            items = $described; count = $described.Count; visited = $found.visited; truncated = $found.truncated
        })
    }

    $element = Resolve-One $root $request
    if ($operation -eq 'uia_focus') {
        $element.SetFocus()
        Start-Sleep -Milliseconds 30
        Emit-Result (Describe-Element $element)
    }

    if ($operation -eq 'uia_invoke') {
        $pattern = $null
        if (-not $element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
            Emit-Error 'UIA_PATTERN_UNSUPPORTED' 'Target does not support InvokePattern'
        }
        $pattern.Invoke()
        Emit-Result ([pscustomobject]@{ invoked = $true; element = (Describe-Element $element) })
    }

    if ($operation -eq 'uia_set_value') {
        if ([bool]$element.Current.IsPassword) {
            Emit-Error 'UIA_PASSWORD_VALUE_BLOCKED' 'Password controls cannot receive relay-carried values; use the credential-safe secret channel'
        }
        $pattern = $null
        if (-not $element.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$pattern)) {
            Emit-Error 'UIA_PATTERN_UNSUPPORTED' 'Target does not support ValuePattern'
        }
        if ([bool]$pattern.Current.IsReadOnly) {
            Emit-Error 'UIA_READ_ONLY' 'Target ValuePattern is read-only'
        }
        $pattern.SetValue([string]$request.value)
        Emit-Result ([pscustomobject]@{
            set_value = $true; characters = ([string]$request.value).Length; element = (Describe-Element $element)
        })
    }

    if ($operation -eq 'uia_toggle') {
        $pattern = $null
        if (-not $element.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$pattern)) {
            Emit-Error 'UIA_PATTERN_UNSUPPORTED' 'Target does not support TogglePattern'
        }
        $pattern.Toggle()
        Emit-Result ([pscustomobject]@{ toggled = $true; state = [string]$pattern.Current.ToggleState; element = (Describe-Element $element) })
    }

    if ($operation -eq 'uia_select') {
        $pattern = $null
        if (-not $element.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) {
            Emit-Error 'UIA_PATTERN_UNSUPPORTED' 'Target does not support SelectionItemPattern'
        }
        $pattern.Select()
        Emit-Result ([pscustomobject]@{ selected = $true; element = (Describe-Element $element) })
    }

    if ($operation -eq 'uia_expand' -or $operation -eq 'uia_collapse') {
        $pattern = $null
        if (-not $element.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$pattern)) {
            Emit-Error 'UIA_PATTERN_UNSUPPORTED' 'Target does not support ExpandCollapsePattern'
        }
        if ($operation -eq 'uia_expand') { $pattern.Expand() } else { $pattern.Collapse() }
        Emit-Result ([pscustomobject]@{
            expanded = ($operation -eq 'uia_expand'); state = [string]$pattern.Current.ExpandCollapseState
            element = (Describe-Element $element)
        })
    }

    Emit-Error 'UIA_INVALID_OPERATION' 'Unsupported UI Automation operation'
} catch {
    Emit-Error 'UIA_BACKEND_ERROR' 'Windows UI Automation operation failed' @{ type = $_.Exception.GetType().FullName }
}
