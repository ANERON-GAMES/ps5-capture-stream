# ==============================================================================
# SETUP
# ==============================================================================
$ErrorActionPreference = 'SilentlyContinue'
$ProgressPreference = 'SilentlyContinue'
$Host.UI.RawUI.WindowTitle = 'ANERON DEVICES'

$ffmpeg = (Get-Command ffmpeg -ErrorAction SilentlyContinue).Source
if (-not $ffmpeg) {
    $ffmpeg = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Filter ffmpeg.exe -Recurse | Select-Object -First 1 -ExpandProperty FullName
}

$VirtualVideo = 'virtual|OBS'
$BuiltinVideo = 'webcam|IR camera|integrated'
$VirtualAudio = 'voicemeeter|cable|virtual'
$CaptureHint = 'capture|digital audio interface|hdmi|cam ?link|elgato|avermedia|live gamer|hd60|ugreen|ms21\d\d|macrosilicon|ripsaw|genki'
$GenericUsb = '^(USB |Generic |HID)'
$Ports = @{ 0 = 'VGA'; 4 = 'DVI'; 5 = 'HDMI'; 10 = 'DisplayPort'; 11 = 'built-in'; 13 = 'built-in'; 15 = 'wireless'; 2147483648 = 'built-in' }

$usbCache = @{}
$known = $null
$lastChange = $null
$prevHeight = 0
$prevWidth = 0

# ==============================================================================
# DATA
# ==============================================================================
function Get-UsbInfo($dev) {
    if (-not $usbCache.ContainsKey($dev.InstanceId)) {
        $props = Get-PnpDeviceProperty -InstanceId $dev.InstanceId -KeyName 'DEVPKEY_Device_BusReportedDeviceDesc', 'DEVPKEY_Device_InLocalMachineContainer'
        $bus = ($props | Where-Object KeyName -eq 'DEVPKEY_Device_BusReportedDeviceDesc').Data
        $local = ($props | Where-Object KeyName -eq 'DEVPKEY_Device_InLocalMachineContainer').Data
        $name = $dev.FriendlyName
        if ($bus -and (-not $name -or $name -match $GenericUsb)) { $name = $bus }
        $id = $dev.InstanceId -replace '^USB\\VID_(\w{4})&PID_(\w{4}).*', '$1:$2'
        $usbCache[$dev.InstanceId] = @{ Name = $name; Id = $id; Builtin = ($local -eq $true) }
    }
    $usbCache[$dev.InstanceId]
}

function Get-Snapshot {
    $video = @(); $audio = @()
    if ($ffmpeg) {
        foreach ($l in (cmd /c "`"$ffmpeg`" -hide_banner -list_devices true -f dshow -i dummy 2>&1")) {
            if ($l -match '"([^"]+)" \((video|none)\)') { $video += $Matches[1] }
            elseif ($l -match '"([^"]+)" \(audio\)') { $audio += $Matches[1] }
        }
    }

    $ids = @{}
    foreach ($m in (Get-CimInstance -Namespace root\wmi -ClassName WmiMonitorID)) {
        $ids[$m.InstanceName] = (($m.UserFriendlyName | Where-Object { $_ } | ForEach-Object { [char]$_ }) -join '')
    }
    $displays = @(foreach ($c in (Get-CimInstance -Namespace root\wmi -ClassName WmiMonitorConnectionParams)) {
        $port = $Ports[[int64]$c.VideoOutputTechnology]
        if (-not $port) { $port = 'external' }
        $name = $ids[$c.InstanceName]
        if (-not $name) { $name = 'Unknown display' }
        @{ Name = $name; Port = $port }
    })

    $pnp = @(Get-PnpDevice -PresentOnly)
    $usb = @($pnp | Where-Object { $_.InstanceId -match '^USB\\VID_' -and $_.InstanceId -notmatch '&MI_' } | ForEach-Object { Get-UsbInfo $_ } | Sort-Object { $_.Builtin }, { $_.Name })
    $errors = @($pnp | Where-Object { $_.Status -eq 'Error' } | ForEach-Object { $_.FriendlyName })

    $capture = @($video | Where-Object { $_ -notmatch $VirtualVideo -and $_ -notmatch $BuiltinVideo })
    $captureAudio = @($audio | Where-Object { $_ -match $CaptureHint -and $_ -notmatch $VirtualAudio })

    @{ Video = $video; Audio = $audio; Displays = $displays; Usb = $usb; Errors = $errors; Capture = $capture; CaptureAudio = $captureAudio }
}

function Update-Changes($s) {
    $now = @($s.Video | ForEach-Object { "video|$_" }) + @($s.Audio | ForEach-Object { "audio|$_" }) +
           @($s.Displays | ForEach-Object { "display|$($_.Name)" }) + @($s.Usb | ForEach-Object { "usb|$($_.Name)" }) +
           @($s.Errors | ForEach-Object { "error|$_" })
    if ($null -ne $script:known) {
        $diff = @(Compare-Object $script:known $now)
        if ($diff.Count -gt 0) {
            $d = $diff[0]
            $p = $d.InputObject -split '\|', 2
            $added = $d.SideIndicator -eq '=>'
            $text = '{0}  {1} {2,-8} {3}' -f (Get-Date -Format 'HH:mm:ss'), $(if ($added) { '+' } else { '-' }), $p[0], $p[1]
            if ($diff.Count -gt 1) { $text += "  (+$($diff.Count - 1) more)" }
            $script:lastChange = @{ Text = $text }
        }
    }
    $script:known = $now
}

# ==============================================================================
# RENDER
# ==============================================================================
function Build-Frame($s) {
    $f = New-Object System.Collections.Generic.List[object]
    $w = [Math]::Max([Console]::WindowWidth - 1, 40)
    $add = { param($t, $inverted = $false) $f.Add(@($t, $inverted)) }
    $row = { param($name, $tag) $n = "$name"; if ($n.Length -gt 50) { $n = $n.Substring(0, 49) + '~' }; & $add (' {0,-52}{1}' -f $n, $tag) }
    $head = { param($t) & $add (' ' + $t) $true }

    & $add (' ANERON DEVICES' + (Get-Date -Format 'HH:mm:ss').PadLeft($w - 16)) $true

    if ($s.Capture.Count -gt 0) {
        & $add (' CAPTURE CARD   FOUND      ' + $s.Capture[0])
        if ($s.CaptureAudio.Count -gt 0) { & $add (' CAPTURE AUDIO  FOUND      ' + $s.CaptureAudio[0]) }
        else { & $add ' CAPTURE AUDIO  NOT FOUND  SET audio= IN ps5stream.ini' }
        & $add ' READY          RUN PS5.bat'
    } else {
        & $add ' CAPTURE CARD   NOT FOUND'
        & $add '                card USB cable -> laptop, PS5 HDMI -> card HDMI IN'
        & $add '                laptop HDMI port is output only'
    }

    & $head 'VIDEO'
    foreach ($v in $s.Video) {
        if ($s.Capture -contains $v) { & $row $v 'CAPTURE CARD' }
        elseif ($v -match $VirtualVideo) { & $row $v 'virtual' }
        else { & $row $v 'built-in' }
    }
    if ($s.Video.Count -eq 0) { & $add ' none' }

    & $head 'AUDIO IN'
    $virt = @($s.Audio | Where-Object { $_ -match $VirtualAudio })
    foreach ($a in ($s.Audio | Where-Object { $_ -notmatch $VirtualAudio })) {
        if ($s.CaptureAudio -contains $a) { & $row $a 'CAPTURE CARD' } else { & $row $a '' }
    }
    if ($virt.Count -gt 0) { & $row 'Voicemeeter / virtual inputs' "$($virt.Count) hidden" }

    & $head 'DISPLAYS'
    foreach ($d in $s.Displays) { & $row $d.Name $d.Port }

    & $head 'USB'
    foreach ($u in $s.Usb) {
        if ($u.Builtin) { & $row $u.Name "$($u.Id)  built-in" } else { & $row $u.Name $u.Id }
    }

    if ($s.Errors.Count -gt 0) {
        & $head 'ERRORS'
        foreach ($e in $s.Errors) { & $row $e 'DRIVER ERROR' }
    }

    $change = 'none'
    if ($script:lastChange) { $change = $script:lastChange.Text }
    & $add (' LAST CHANGE    ' + $change + '    Q / ESC EXIT') $true
    $f
}

function Set-ConsoleHeight($rows) {
    try {
        $w = [Console]::WindowWidth
        if ($rows -lt [Console]::WindowHeight) { [Console]::SetWindowSize($w, $rows); [Console]::SetBufferSize($w, $rows) }
        elseif ($rows -gt [Console]::WindowHeight) { [Console]::SetBufferSize($w, $rows); [Console]::SetWindowSize($w, $rows) }
    } catch { }
}

function Show-Frame($f) {
    if ($f.Count + 1 -ne [Console]::WindowHeight) { Set-ConsoleHeight ($f.Count + 1) }
    $w = [Console]::WindowWidth - 1
    $h = [Console]::WindowHeight - 1
    if ($w -ne $script:prevWidth) { Clear-Host; $script:prevWidth = $w; $script:prevHeight = 0 }
    [Console]::SetCursorPosition(0, 0)
    $n = [Math]::Min($f.Count, $h)
    for ($i = 0; $i -lt $n; $i++) {
        $t = [string]$f[$i][0]
        if ($t.Length -gt $w) { $t = $t.Substring(0, $w) }
        if ($f[$i][1]) { Write-Host $t.PadRight($w) -ForegroundColor Black -BackgroundColor White }
        else { Write-Host $t.PadRight($w) -ForegroundColor White -BackgroundColor Black }
    }
    for ($i = $n; $i -lt [Math]::Min($script:prevHeight, $h); $i++) { Write-Host (' ' * $w) -BackgroundColor Black }
    $script:prevHeight = $n
}

# ==============================================================================
# MAIN LOOP
# ==============================================================================
[Console]::TreatControlCAsInput = $true
[Console]::CursorVisible = $false
Clear-Host
try {
    $quit = $false
    while (-not $quit) {
        $s = Get-Snapshot
        Update-Changes $s
        Show-Frame (Build-Frame $s)
        for ($t = 0; $t -lt 15 -and -not $quit; $t++) {
            while ([Console]::KeyAvailable) {
                $k = [Console]::ReadKey($true)
                if ($k.Key -eq 'Q' -or $k.Key -eq 'Escape' -or ($k.Key -eq 'C' -and ($k.Modifiers -band [ConsoleModifiers]::Control))) { $quit = $true }
            }
            Start-Sleep -Milliseconds 100
        }
    }
} finally {
    [Console]::CursorVisible = $true
    Clear-Host
}
