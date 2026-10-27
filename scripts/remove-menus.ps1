# Recovery removal: only keys with Context Menu Studio's ownership marker are removed.
$ErrorActionPreference = 'Stop'
$classes = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Classes', $true)
if ($null -eq $classes) { exit 0 }
try {
    $parents = [System.Collections.Generic.List[string]]::new()
    foreach ($path in @('*\shell', 'Directory\shell', 'Directory\Background\shell', 'DesktopBackground\Shell', 'Drive\shell')) { $parents.Add($path) }
    $associations = $classes.OpenSubKey('SystemFileAssociations')
    if ($null -ne $associations) {
        try { foreach ($extension in $associations.GetSubKeyNames()) { if ($extension.StartsWith('.')) { $parents.Add('SystemFileAssociations\' + $extension + '\shell') } } }
        finally { $associations.Dispose() }
    }
    foreach ($parent in $parents) {
        $path = $parent + '\ContextMenuStudio'
        $key = $classes.OpenSubKey($path)
        if ($null -eq $key) { continue }
        try { $owned = $key.GetValue('Owner') -eq 'a00a0f57-554c-449e-9876-31023c39ecb3' }
        finally { $key.Dispose() }
        if ($owned) { $classes.DeleteSubKeyTree($path, $false) }
    }
    $installed = Join-Path $env:LOCALAPPDATA 'ContextMenuStudio\installed.json'
    if (Test-Path $installed) { Remove-Item -LiteralPath $installed }
    Write-Host 'Context Menu Studio menus removed. Drafts and logs were kept. Reopen the context menu to refresh.'
} finally { $classes.Dispose() }
