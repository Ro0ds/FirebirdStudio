[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][int]$ParentProcessId,
    [Parameter(Mandatory = $true)][string]$ArchivePath,
    [Parameter(Mandatory = $true)][long]$ExpectedSizeBytes,
    [Parameter(Mandatory = $true)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedSha256,
    [Parameter(Mandatory = $true)][string]$InstallDirectory,
    [Parameter(Mandatory = $true)][string]$ExecutablePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Windows.Forms

function Write-UpdateLog {
    param([string]$Message)
    $line = '[{0:yyyy-MM-dd HH:mm:ss}] {1}' -f (Get-Date), $Message
    Add-Content -LiteralPath $script:LogPath -Value $line -Encoding UTF8
}

function Get-SafeArchivePath {
    param([string]$EntryName, [string]$Root)
    if ([string]::IsNullOrWhiteSpace($EntryName) -or $EntryName.Contains('\') -or
        $EntryName.StartsWith('/') -or $EntryName -match '^[A-Za-z]:' -or
        $EntryName.Contains([char]0)) {
        throw "Unsafe ZIP entry path: '$EntryName'."
    }

    $segments = $EntryName.TrimEnd('/').Split('/')
    if ($segments.Count -eq 0 -or ($segments | Where-Object { $_ -eq '..' -or $_ -eq '.' -or $_ -eq '' })) {
        throw "Unsafe ZIP entry path: '$EntryName'."
    }

    $destination = [IO.Path]::GetFullPath((Join-Path $Root ($EntryName.Replace('/', [IO.Path]::DirectorySeparatorChar))))
    $rootPrefix = $Root.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $destination.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "ZIP entry escapes its destination: '$EntryName'."
    }

    $extension = [IO.Path]::GetExtension($destination)
    $protectedExtensions = @('.fdb', '.gdb', '.ib', '.sqlite', '.sqlite3', '.db', '.db3', '.backup', '.fbk')
    if ($protectedExtensions -contains $extension.ToLowerInvariant()) {
        throw "The release archive contains a database or backup file, which the updater will never install: '$EntryName'."
    }

    return $destination
}

$installDirectory = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
$archivePath = [IO.Path]::GetFullPath($ArchivePath)
$executablePath = [IO.Path]::GetFullPath($ExecutablePath)
$versionDirectory = Split-Path -Parent $archivePath
New-Item -ItemType Directory -Path $versionDirectory -Force | Out-Null
$script:LogPath = Join-Path $versionDirectory 'updater.log'
$workRoot = Join-Path $versionDirectory ('install-' + [Guid]::NewGuid().ToString('N'))
$stagingRoot = Join-Path $workRoot 'staging'
$backupRoot = Join-Path $workRoot 'backup'
$changedFiles = [Collections.Generic.List[string]]::new()
$newFiles = [Collections.Generic.List[string]]::new()
$completed = $false
$rollbackFailed = $false

try {
    Write-UpdateLog 'Updater started.'
    if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) { throw 'The downloaded update archive does not exist.' }
    if ((Get-Item -LiteralPath $archivePath).Length -ne $ExpectedSizeBytes) { throw 'The update archive size no longer matches the verified download.' }
    $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    if (-not [string]::Equals($actualHash, $ExpectedSha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The update archive SHA-256 no longer matches the verified download.'
    }

    if (-not (Test-Path -LiteralPath $installDirectory -PathType Container)) { throw 'The application installation directory does not exist.' }
    if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) { throw 'The application executable was not found.' }
    $installPrefix = $installDirectory + [IO.Path]::DirectorySeparatorChar
    if (-not $executablePath.StartsWith($installPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The executable is not inside the installation directory.'
    }

    $deadline = [DateTime]::UtcNow.AddMinutes(2)
    while ($null -ne (Get-Process -Id $ParentProcessId -ErrorAction SilentlyContinue)) {
        if ([DateTime]::UtcNow -ge $deadline) { throw 'Firebird Studio did not exit within two minutes.' }
        Start-Sleep -Milliseconds 250
    }
    Write-UpdateLog 'Firebird Studio process exited.'

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null

    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        if ($archive.Entries.Count -eq 0) { throw 'The update archive is empty.' }
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $archive.Entries) {
            $destination = Get-SafeArchivePath -EntryName $entry.FullName -Root $stagingRoot
            if (-not $seen.Add($entry.FullName)) { throw "The update archive contains a duplicate path: '$($entry.FullName)'." }

            $unixType = ($entry.ExternalAttributes -shr 16) -band 0xF000
            if ($unixType -eq 0xA000) { throw "The update archive contains a symbolic link: '$($entry.FullName)'." }

            if ($entry.FullName.EndsWith('/')) {
                New-Item -ItemType Directory -Path $destination -Force | Out-Null
                continue
            }

            $parentDirectory = Split-Path -Parent $destination
            New-Item -ItemType Directory -Path $parentDirectory -Force | Out-Null
            $input = $entry.Open()
            try {
                $output = [IO.File]::Open($destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
                try { $input.CopyTo($output) } finally { $output.Dispose() }
            } finally { $input.Dispose() }
        }
    }
    finally { $archive.Dispose() }

    $stagedFiles = @(Get-ChildItem -LiteralPath $stagingRoot -File -Recurse)
    if ($stagedFiles.Count -eq 0) { throw 'The update archive contains no application files.' }

    # Install as an overlay: files absent from the package are never deleted.
    foreach ($stagedFile in $stagedFiles) {
        $relativePath = $stagedFile.FullName.Substring($stagingRoot.Length).TrimStart([IO.Path]::DirectorySeparatorChar)
        $targetPath = Get-SafeArchivePath -EntryName ($relativePath.Replace([IO.Path]::DirectorySeparatorChar, '/')) -Root $installDirectory
        $targetParent = Split-Path -Parent $targetPath
        New-Item -ItemType Directory -Path $targetParent -Force | Out-Null

        $cursor = $targetParent
        while ($cursor.StartsWith($installPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw "The installation path contains a reparse point: '$cursor'."
            }
            $next = Split-Path -Parent $cursor
            if ($next -eq $cursor) { break }
            $cursor = $next
        }

        if (Test-Path -LiteralPath $targetPath -PathType Container) {
            throw "A directory already exists where the update expects a file: '$targetPath'."
        }

        if (Test-Path -LiteralPath $targetPath -PathType Leaf) {
            $backupPath = Join-Path $backupRoot $relativePath
            $backupParent = Split-Path -Parent $backupPath
            New-Item -ItemType Directory -Path $backupParent -Force | Out-Null
            [IO.File]::Copy($targetPath, $backupPath, $true)
            $changedFiles.Add($relativePath)
        } else {
            $newFiles.Add($relativePath)
        }

        $temporaryTarget = $targetPath + '.firebirdstudio-update'
        [IO.File]::Copy($stagedFile.FullName, $temporaryTarget, $true)
        if (Test-Path -LiteralPath $targetPath) { [IO.File]::Delete($targetPath) }
        [IO.File]::Move($temporaryTarget, $targetPath)
    }

    $completed = $true
    Write-UpdateLog ("Update installation completed. Replaced {0} file(s), added {1} file(s)." -f $changedFiles.Count, $newFiles.Count)
}
catch {
    Write-UpdateLog ("Update failed: " + $_.Exception.Message)
    Write-UpdateLog 'Attempting rollback.'

    foreach ($relativePath in $newFiles) {
        try {
            $target = Join-Path $installDirectory $relativePath
            if (Test-Path -LiteralPath $target -PathType Leaf) { [IO.File]::Delete($target) }
        } catch {
            $rollbackFailed = $true
            Write-UpdateLog ("Could not remove newly installed file '$relativePath': " + $_.Exception.Message)
        }
    }

    foreach ($relativePath in $changedFiles) {
        try {
            $backup = Join-Path $backupRoot $relativePath
            $target = Join-Path $installDirectory $relativePath
            if (-not (Test-Path -LiteralPath $backup -PathType Leaf)) { throw "Backup file is missing: $backup" }
            $temporaryTarget = $target + '.firebirdstudio-rollback'
            [IO.File]::Copy($backup, $temporaryTarget, $true)
            if (Test-Path -LiteralPath $target) { [IO.File]::Delete($target) }
            [IO.File]::Move($temporaryTarget, $target)
        } catch {
            $rollbackFailed = $true
            Write-UpdateLog ("Could not restore '$relativePath': " + $_.Exception.Message)
        }
    }

    if ($rollbackFailed) {
        Write-UpdateLog 'ROLLBACK INCOMPLETE. The previous installation may require manual recovery.'
    } else {
        Write-UpdateLog 'Rollback completed; the previous application files were restored.'
    }
}
finally {
    if (Test-Path -LiteralPath $workRoot) {
        try { Remove-Item -LiteralPath $workRoot -Recurse -Force } catch { Write-UpdateLog ("Could not remove staging files: " + $_.Exception.Message) }
    }
}

if ($completed -or -not $rollbackFailed) {
    try {
        Start-Process -FilePath $executablePath -WorkingDirectory $installDirectory
        Write-UpdateLog 'Firebird Studio restart requested.'
    } catch {
        Write-UpdateLog ("Could not restart Firebird Studio: " + $_.Exception.Message)
    }
}

if (-not $completed) {
    if ($rollbackFailed) {
        [void][System.Windows.Forms.MessageBox]::Show(
            "The update failed and automatic rollback was incomplete." + [Environment]::NewLine + [Environment]::NewLine + "Do not continue using this installation until it has been checked." + [Environment]::NewLine + [Environment]::NewLine + "Log: $script:LogPath",
            'Firebird Studio update failed', 'OK', 'Error')
    } else {
        [void][System.Windows.Forms.MessageBox]::Show(
            "The update could not be installed. The previous application files were restored." + [Environment]::NewLine + [Environment]::NewLine + "Log: $script:LogPath",
            'Firebird Studio update failed', 'OK', 'Warning')
    }
    exit 1
}

[void][System.Windows.Forms.MessageBox]::Show(
    'Firebird Studio was updated successfully and is restarting.',
    'Firebird Studio updated', 'OK', 'Information')
exit 0
