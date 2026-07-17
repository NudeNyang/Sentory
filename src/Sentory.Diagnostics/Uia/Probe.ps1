param(
    [Parameter(Mandatory = $true)]
    [string]$ProcessNames,

    [ValidateSet('raw', 'control', 'content')]
    [string]$View = 'raw',

    [ValidateRange(1, 100000)]
    [int]$MaxElements = 5000
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
[Console]::OutputEncoding = $utf8
$OutputEncoding = $utf8

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function Get-LengthBucket {
    param([int]$Length)

    if ($Length -eq 0) { return 'empty' }
    if ($Length -le 4) { return '1-4' }
    if ($Length -le 16) { return '5-16' }
    if ($Length -le 64) { return '17-64' }
    if ($Length -le 256) { return '65-256' }
    return '257+'
}

function Get-SafeIdentifier {
    param([AllowNull()][string]$Value)

    if ([string]::IsNullOrEmpty($Value)) {
        return ''
    }

    if ($Value -cmatch '^[A-Za-z0-9_.:#-]{1,128}$') {
        return $Value
    }

    return '<redacted>'
}

function Get-RuntimeIdHash {
    param([System.Windows.Automation.AutomationElement]$Element)

    try {
        $runtimeId = $Element.GetRuntimeId()
        if ($null -eq $runtimeId -or $runtimeId.Count -eq 0) {
            return 'unavailable'
        }

        $bytes = [Text.Encoding]::UTF8.GetBytes(($runtimeId -join '.'))
        $sha256 = [Security.Cryptography.SHA256]::Create()
        try {
            $hash = $sha256.ComputeHash($bytes)
            return ([BitConverter]::ToString($hash, 0, 12) -replace '-', '').ToLowerInvariant()
        }
        finally {
            $sha256.Dispose()
        }
    }
    catch {
        return 'unavailable'
    }
}

function Get-ElementRecord {
    param(
        [System.Windows.Automation.AutomationElement]$Element,
        [int]$Index
    )

    try { $controlType = $Element.Current.ControlType.ProgrammaticName }
    catch { $controlType = 'ControlType.Unavailable' }

    try { $automationId = Get-SafeIdentifier $Element.Current.AutomationId }
    catch { $automationId = '' }

    try { $className = Get-SafeIdentifier $Element.Current.ClassName }
    catch { $className = '' }

    try { $frameworkId = Get-SafeIdentifier $Element.Current.FrameworkId }
    catch { $frameworkId = '' }

    try { $isEnabled = $Element.Current.IsEnabled }
    catch { $isEnabled = $null }

    try { $isOffscreen = $Element.Current.IsOffscreen }
    catch { $isOffscreen = $null }

    try { $isKeyboardFocusable = $Element.Current.IsKeyboardFocusable }
    catch { $isKeyboardFocusable = $null }

    try { $hasKeyboardFocus = $Element.Current.HasKeyboardFocus }
    catch { $hasKeyboardFocus = $null }

    try { $nameLength = $Element.Current.Name.Length }
    catch { $nameLength = 0 }

    try { $nativeWindowHandle = $Element.Current.NativeWindowHandle }
    catch { $nativeWindowHandle = 0 }

    try {
        $supportedPatterns = @(
            $Element.GetSupportedPatterns() |
                ForEach-Object { $_.ProgrammaticName } |
                Sort-Object
        )
    }
    catch {
        $supportedPatterns = @()
    }

    [pscustomobject]@{
        index = $Index
        runtimeIdHash = Get-RuntimeIdHash $Element
        controlType = $controlType
        automationId = $automationId
        className = $className
        frameworkId = $frameworkId
        isEnabled = $isEnabled
        isOffscreen = $isOffscreen
        isKeyboardFocusable = $isKeyboardFocusable
        hasKeyboardFocus = $hasKeyboardFocus
        nameLengthBucket = Get-LengthBucket $nameLength
        nativeWindowHandle = if ($nativeWindowHandle -eq 0) {
            ''
        }
        else {
            '0x{0:X}' -f $nativeWindowHandle
        }
        supportedPatterns = @($supportedPatterns)
    }
}

$requestedNames = @(
    $ProcessNames.Split(
        ',',
        [StringSplitOptions]::RemoveEmptyEntries
    ) |
        ForEach-Object {
            [IO.Path]::GetFileNameWithoutExtension($_.Trim())
        } |
        Sort-Object -Unique
)

$targetProcesses = @(
    foreach ($name in $requestedNames) {
        Get-Process -Name $name -ErrorAction SilentlyContinue
    }
) | Sort-Object Id -Unique

$targetById = @{}
foreach ($process in $targetProcesses) {
    $targetById[$process.Id] = $process
}

$viewCondition = switch ($View) {
    'raw' {
        [System.Windows.Automation.Condition]::TrueCondition
    }
    'control' {
        New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::IsControlElementProperty,
            $true
        )
    }
    'content' {
        New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::IsContentElementProperty,
            $true
        )
    }
}

$desktopWindows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
    [System.Windows.Automation.TreeScope]::Children,
    [System.Windows.Automation.Condition]::TrueCondition
)

$snapshots = @()
for ($windowIndex = 0; $windowIndex -lt $desktopWindows.Count; $windowIndex++) {
    $window = $desktopWindows.Item($windowIndex)
    try { $processId = $window.Current.ProcessId }
    catch { continue }

    if (-not $targetById.ContainsKey($processId)) {
        continue
    }

    try { $nativeWindowHandle = $window.Current.NativeWindowHandle }
    catch { $nativeWindowHandle = 0 }

    if ($nativeWindowHandle -eq 0) {
        continue
    }

    $process = $targetById[$processId]
    try { $processVersion = $process.MainModule.FileVersionInfo.FileVersion }
    catch { $processVersion = 'unknown' }

    try { $nativeClassName = Get-SafeIdentifier $window.Current.ClassName }
    catch { $nativeClassName = '' }

    try { $titleLength = $window.Current.Name.Length }
    catch { $titleLength = 0 }

    $records = New-Object System.Collections.Generic.List[object]
    $counts = @{}

    $rootRecord = Get-ElementRecord $window 0
    $records.Add($rootRecord)
    $counts[$rootRecord.controlType] = 1

    $descendants = $window.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        $viewCondition
    )
    $limit = [Math]::Min($descendants.Count, $MaxElements - 1)

    for ($elementIndex = 0; $elementIndex -lt $limit; $elementIndex++) {
        $record = Get-ElementRecord $descendants.Item($elementIndex) $records.Count
        $records.Add($record)

        if ($counts.ContainsKey($record.controlType)) {
            $counts[$record.controlType]++
        }
        else {
            $counts[$record.controlType] = 1
        }
    }

    $warnings = @(
        'flat FindAll traversal used; parent/depth relationships were not reconstructed'
    )
    if ($descendants.Count -gt $limit) {
        $warnings += 'max-elements limit reached'
    }

    $snapshots += [pscustomobject]@{
        window = [pscustomobject]@{
            processName = $process.ProcessName
            processId = $process.Id
            processVersion = $processVersion
            windowHandle = ('0x{0:X}' -f $nativeWindowHandle)
            nativeClassName = $nativeClassName
            titleLengthBucket = Get-LengthBucket $titleLength
        }
        view = $View
        maxElements = $MaxElements
        capturedElements = $records.Count
        truncated = ($descendants.Count -gt $limit)
        controlTypeCounts = $counts
        elements = $records.ToArray()
        warnings = $warnings
    }
}

[pscustomobject]@{
    capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    runtime = 'Windows PowerShell 5.1 / .NET Framework UI Automation'
    privacyMode = 'No names, values, text, titles, or full executable paths are collected.'
    requestedProcesses = $requestedNames
    snapshots = @($snapshots)
} | ConvertTo-Json -Depth 8 -Compress
