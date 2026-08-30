@echo off
chcp 65001 >nul
title SCP-SL // SEED MONITOR
set "SEED_DIR=%~dp0"
set "SEED_SELF=%~f0"
set "SEED_ARG=%~1"
set "SEED_API=https://slmaps.com/api/detect"
set "SEED_TOKEN=545d04d6e3f2e813c548cabff6dc361ebda2c369353df5dc52bc12b8228e939513089cad37395c609562623b4480e46fdc415850e9778137b6e0f3f411ec1b74"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$m=':'+'PSCODE';$f=[IO.File]::ReadAllText('%~f0',[Text.Encoding]::UTF8);Invoke-Expression $f.Substring($f.IndexOf($m)+$m.Length)"
echo.
echo [ uplink closed ]
pause
exit /b
:PSCODE
$ErrorActionPreference = 'Stop'
$log     = Join-Path $env:USERPROFILE 'AppData\LocalLow\Northwood\SCPSL\Player.log'
$outJson = Join-Path $env:SEED_DIR 'scpsl_maps.jsonl'
$cfgPath = Join-Path $env:SEED_DIR 'seed-detector.cfg'

$KEYMAP = @{
    F1=@{vk=0x70;kc=282}; F2=@{vk=0x71;kc=283}; F3=@{vk=0x72;kc=284}; F4=@{vk=0x73;kc=285}
    F5=@{vk=0x74;kc=286}; F6=@{vk=0x75;kc=287}; F7=@{vk=0x76;kc=288}; F8=@{vk=0x77;kc=289}
    F9=@{vk=0x78;kc=290}; F10=@{vk=0x79;kc=291}; F11=@{vk=0x7A;kc=292}; F12=@{vk=0x7B;kc=293}
    Home=@{vk=0x24;kc=278}; End=@{vk=0x23;kc=279}; Insert=@{vk=0x2D;kc=277}
    Delete=@{vk=0x2E;kc=127}; PageUp=@{vk=0x21;kc=280}; PageDown=@{vk=0x22;kc=281}
}

$MSG = @{
    ko = @{
        title='FACILITY SEED MONITOR'
        mode_auto='자동수집 ON — 새 맵마다 게임이 활성창이면 [{0}] 자동 입력'
        mode_manual='수동 모드 — 게임에서 [{0}] 직접 누름 (cmdbinding)'
        push_on='백엔드 전송 ON: {0}'
        push_off='백엔드 전송 OFF (로컬 기록만)'
        file='기록 파일: {0}'
        quit='중단: Ctrl+C'
        cmdbind='cmdbinding 동기화: {0}:seed'
        cmdbind_restart='게임 실행 중 — 단축키 변경은 게임 재시작 후 적용'
        unknownkey="알 수 없는 키 '{0}' — F8 사용"
        detected='접속 감지 (PID {0}) — 모니터링 개시'
        exited='접속 종료 — 대기'
        round='새 맵 감지 — seed 요청 대기'
        sent='[{0}] 자동 입력 — seed 요청'
        recorded='seed {0}  ·  {1}  — 기록됨'
        dup='seed {0} (이미 기록됨)'
        blocked='이 서버는 추출 거부됨 ({0}) — 건너뜀'
        pushok='전송: seed {0} -> {1}'
        pushfail='전송 실패: {0}'
        pushfail_session='전송 실패: 서버 세션을 받지 못했습니다 (네트워크 지연) — 이 seed는 전송되지 않음'
        update_avail='새 버전 있음 — 설치본 v{0}, 서버 최신 v{1}. 사이트에서 다시 받으세요: {2}'
        update_req='업데이트 필요 — 설치본 v{0}, 최소 요구 v{1}. 이 버전은 전송이 거부됩니다. 다시 받으세요: {2}'
        update_ok='버전 v{0} — 최신'
        ioerr='로그 읽기 오류: {0} — 재시도'
        writeerr='jsonl 기록 실패: {0}'
        ask_key='단축키 (F1~F12, Home, End, Insert, Delete, PageUp, PageDown) [기본 F8]'
        ask_auto='자동수집 — 새 맵마다 단축키 자동 입력? [Y/n]'
        ask_tray='시스템 트레이 아이콘 사용? [Y/n]'
        ask_push='백엔드로 seed 전송? [Y/n]'
        setup_saved='설정 저장됨: {0}'
        setup_badkey='알 수 없는 키 — F8 사용'
        setup_hint='설정 변경: scpsl-seed-detector.bat config  (또는 seed-detector.cfg 삭제)'
        monitoring='모니터링 중'
        boot1='시설 터미널 부팅'
        boot2='클리어런스 인증'
        boot3='seed 모니터 온라인'
        tray_tip='SCP:SL Seed Monitor'
        tray_show='콘솔 보이기 / 숨기기'
        tray_log='기록 폴더 열기'
        tray_config='재설정'
        tray_quit='종료'
        tray_min='트레이 활성 — 더블클릭/우클릭으로 제어 (콘솔 숨김 가능)'
        tray_fail='트레이 초기화 실패 — 콘솔 모드로 계속: {0}'
        balloon_title='SEED 기록됨'
        cmds_hint='명령 입력 — rconfig · pair · notify · hide · clear · quit  (입력 후 Enter)'
        unknown_cmd='알 수 없는 명령: {0}  (help)'
        reconfig_done='설정 갱신 완료'
        tray_restart_note='트레이 on/off 변경은 재시작 후 적용'
        hide_needs_tray='콘솔 숨김은 트레이가 켜져 있을 때만 가능'
        pair_label='페어링 링크: {0}'
        pair_hint='브라우저에서 위 링크를 한 번 열면 이 PC 감지가 자동 표시됨 (명령: pair)'
        pair_none='페어링 URL 없음 (백엔드 미설정)'
        ask_notify='시드 감지 시 Windows 토스트 알림? [Y/n]'
        notify_on='알림 ON — 시드 감지 시 토스트 표시 (클릭하면 맵 열림)'
        notify_off='알림 OFF — 토스트 표시 안 함'
    }
    en = @{
        title='FACILITY SEED MONITOR'
        mode_auto='auto-collect ON - presses [{0}] each new map while game is focused'
        mode_manual='manual mode - press [{0}] yourself in-game (cmdbinding)'
        push_on='backend push ON: {0}'
        push_off='backend push OFF (local record only)'
        file='log file: {0}'
        quit='stop: Ctrl+C'
        cmdbind='cmdbinding synced: {0}:seed'
        cmdbind_restart='game running - hotkey change applies after game restart'
        unknownkey="unknown key '{0}' - using F8"
        detected='client detected (PID {0}) - monitoring'
        exited='client closed - standby'
        round='new map detected - waiting for seed'
        sent='[{0}] auto-pressed - requesting seed'
        recorded='seed {0}  ·  {1}  - recorded'
        dup='seed {0} (already recorded)'
        blocked='extraction opted-out for this server ({0}) - skipped'
        pushok='push: seed {0} -> {1}'
        pushfail='push failed: {0}'
        pushfail_session='push failed: no session from server (network latency) - this seed was not pushed'
        update_avail='update available - installed v{0}, latest v{1}. re-download from the site: {2}'
        update_req='update required - installed v{0}, minimum v{1}. pushes are rejected on this version. re-download: {2}'
        update_ok='version v{0} - up to date'
        ioerr='log read error: {0} - retrying'
        writeerr='jsonl write failed: {0}'
        ask_key='hotkey (F1~F12, Home, End, Insert, Delete, PageUp, PageDown) [default F8]'
        ask_auto='auto-collect - press hotkey each new map? [Y/n]'
        ask_tray='use system tray icon? [Y/n]'
        ask_push='push seed to backend? [Y/n]'
        setup_saved='settings saved: {0}'
        setup_badkey='unknown key - using F8'
        setup_hint='reconfigure: scpsl-seed-detector.bat config  (or delete seed-detector.cfg)'
        monitoring='monitoring'
        boot1='facility terminal boot'
        boot2='clearance authenticated'
        boot3='seed monitor online'
        tray_tip='SCP:SL Seed Monitor'
        tray_show='show / hide console'
        tray_log='open records folder'
        tray_config='reconfigure'
        tray_quit='quit'
        tray_min='tray active - double/right-click to control (console can be hidden)'
        tray_fail='tray init failed - continuing in console mode: {0}'
        balloon_title='SEED RECORDED'
        cmds_hint='commands - rconfig · pair · notify · hide · clear · quit  (type + Enter)'
        unknown_cmd='unknown command: {0}  (help)'
        reconfig_done='settings reloaded'
        tray_restart_note='tray on/off change applies after restart'
        hide_needs_tray='hide requires tray enabled'
        pair_label='pairing link: {0}'
        pair_hint='open the link above once in your browser to auto-show detections (command: pair)'
        pair_none='no pairing url (backend not set)'
        ask_notify='show Windows toast when a seed is detected? [Y/n]'
        notify_on='notifications ON - toast on detect (click opens the map)'
        notify_off='notifications OFF - no toast'
    }
}

$BAR = ([char]0x2500).ToString() * 60

function Rule($c) { Write-Host ('  ' + $BAR) -ForegroundColor $c }

function Banner {
    Write-Host ''
    Write-Host '    ███████╗ ██████╗██████╗ ' -ForegroundColor Red
    Write-Host '    ██╔════╝██╔════╝██╔══██╗' -ForegroundColor Red
    Write-Host '    ███████╗██║     ██████╔╝' -ForegroundColor White
    Write-Host '    ╚════██║██║     ██╔═══╝ ' -ForegroundColor White
    Write-Host '    ███████║╚██████╗██║     ' -ForegroundColor DarkRed
    Write-Host '    ╚══════╝ ╚═════╝╚═╝     ' -ForegroundColor DarkRed
    Write-Host '    S E C R E T   L A B O R A T O R Y' -ForegroundColor DarkGray
    Write-Host ''
}

function Tag($sym, $text, $color) { Write-Host ("   $sym " + $text) -ForegroundColor $color }

function Read-Default($prompt, $default) {
    $v = Read-Host $prompt
    if ([string]::IsNullOrWhiteSpace($v)) { return $default } else { return "$v".Trim() }
}

function Run-Setup($path) {
    Banner
    Rule 'DarkRed'
    Write-Host '   ▌ 초기 설정 / INITIAL CONFIGURATION' -ForegroundColor White
    Rule 'DarkRed'
    Write-Host ''
    $lng = Read-Default '   [1] 언어 / Language [ko/en]' 'ko'
    if ($lng -ne 'en') { $lng = 'ko' }
    $LL = $MSG[$lng]
    $k = Read-Default ('   [2] ' + $LL.ask_key) 'F8'
    if (-not $KEYMAP[$k]) { Tag '!' $LL.setup_badkey 'DarkYellow'; $k = 'F8' }
    $a = Read-Default ('   [3] ' + $LL.ask_auto) 'Y'
    $autoV = if ($a -match '^[nN]') { '0' } else { '1' }
    $tr = Read-Default ('   [4] ' + $LL.ask_tray) 'Y'
    $trayV = if ($tr -match '^[nN]') { '0' } else { '1' }
    $pu = Read-Default ('   [5] ' + $LL.ask_push) 'Y'
    $pushV = if ($pu -match '^[nN]') { '0' } else { '1' }
    $nt = Read-Default ('   [6] ' + $LL.ask_notify) 'Y'
    $notifyV = if ($nt -match '^[nN]') { '0' } else { '1' }
    $bakedApi = "$env:SEED_API".Trim(); $bakedTok = "$env:SEED_TOKEN".Trim()
    $exApi = ''; $exTok = ''
    if (Test-Path $path) { $ex = Load-Cfg $path; $exApi = "$($ex.API)".Trim(); $exTok = "$($ex.TOKEN)".Trim() }
    $apiV = if ($bakedApi) { $bakedApi } else { $exApi }
    $tokV = if ($bakedTok) { $bakedTok } elseif ($exTok) { $exTok } else { [guid]::NewGuid().ToString() }
    Save-Cfg $path @{ LANG = $lng; KEY = $k; AUTO = $autoV; TRAY = $trayV; PUSH = $pushV; NOTIFY = $notifyV; API = $apiV; TOKEN = $tokV }
    Write-Host ''
    Tag ([char]0x2713) ($LL.setup_saved -f $path) 'Green'
    Write-Host ''
    Start-Sleep -Milliseconds 300
}

function Load-Cfg($path) {
    $h = @{}
    foreach ($ln in [IO.File]::ReadAllLines($path)) {
        if ($ln -match '^\s*([A-Z]+)\s*=\s*(.*)$') { $h[$matches[1]] = "$($matches[2])".Trim() }
    }
    return $h
}

function Save-Cfg($path, $h) {
    $order = @('LANG', 'KEY', 'AUTO', 'TRAY', 'PUSH', 'NOTIFY', 'API', 'TOKEN')
    $lines = foreach ($key in $order) { "$key=$($h[$key])" }
    [IO.File]::WriteAllText($path, ($lines -join "`r`n") + "`r`n", (New-Object System.Text.UTF8Encoding($false)))
}

if (($env:SEED_ARG -eq 'config') -or ($env:SEED_ARG -eq 'setup') -or -not (Test-Path $cfgPath)) {
    Run-Setup $cfgPath
}
$cfg = Load-Cfg $cfgPath
$bakedApi = "$env:SEED_API".Trim(); $bakedTok = "$env:SEED_TOKEN".Trim()
$cfgDirty = $false
if ($bakedApi -and $cfg.API -ne $bakedApi) { $cfg.API = $bakedApi; $cfgDirty = $true }
if ($bakedTok -and $cfg.TOKEN -ne $bakedTok) { $cfg.TOKEN = $bakedTok; $cfgDirty = $true }
if (-not $cfg.TOKEN) { $cfg.TOKEN = [guid]::NewGuid().ToString(); $cfgDirty = $true }
if ($cfgDirty) { Save-Cfg $cfgPath $cfg }

$lang  = if ($cfg.LANG -eq 'en') { 'en' } else { 'ko' }
$L     = $MSG[$lang]
$keyName = if ($cfg.KEY) { $cfg.KEY } else { 'F8' }
$km = $KEYMAP[$keyName]
if (-not $km) { Tag '!' ($L.unknownkey -f $keyName) 'DarkYellow'; $keyName = 'F8'; $km = $KEYMAP['F8'] }
$autoVk = $km.vk; $keyCode = $km.kc
$auto  = ($cfg.AUTO -ne '0')
$push  = ($cfg.PUSH -ne '0')
$notify = ($cfg.NOTIFY -ne '0')
$tray  = ($cfg.TRAY -eq '1')
$api   = $cfg.API
$token = $cfg.TOKEN
$pairUrl = ''
if ($api -match '^https?://') { $pairUrl = ($api -replace '/api/detect/?$', '') + '/?pair=' + $token }
$siteUrl = if ($api -match '^https?://') { ($api -replace '/api/detect/?$', '') } else { '' }
# 백엔드 /api/client-version 의 latest/min 과 대조된다. 배포한 .bat을 고칠 때마다 올릴 것.
$CLIENT_VERSION = 2
$toastAppId = 'SCPSL.SeedMonitor'
$apiBase = if ($api) { $api -replace '/detect/?$', '' } else { '' }

# 1회용 세션 토큰을 받아온다. 실패하면 빈 문자열.
# 타임아웃이 2초였을 때는 지연이 큰 회선(중국 등 p95 1.6초)에서 자주 넘겨, 아래 Post-Seed가
# 빈 세션으로 전송 -> 백엔드가 401(invalid session)로 거부 -> 그 seed를 통째로 잃었다.
function Get-Session {
    if (-not $script:apiBase) { return '' }
    try {
        $sr = Invoke-RestMethod -Uri ($script:apiBase + '/session') -Method Post -TimeoutSec 8
        return "$($sr.session)"
    } catch { return '' }
}

function Post-Seed($seed, $server) {
    if (-not $push -or -not $api) { return 'off' }
    # 세션은 1회용이라 시도마다 새로 받는다. 세션이 없으면 아예 보내지 않는다(보내봐야 401 확정).
    $lastErr = ''
    $noSession = $false
    foreach ($attempt in 1..2) {
        $session = Get-Session
        if (-not $session) {
            $noSession = $true
            Start-Sleep -Milliseconds 500
            continue
        }
        $noSession = $false
        try {
            $body = @{ seed = [long]$seed; server = "$server"; token = "$token"; version = $script:CLIENT_VERSION; session = $session } | ConvertTo-Json -Compress
            $resp = Invoke-RestMethod -Uri $api -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 8
            if ($resp.blocked) { return 'blocked' }
            Tag ([char]0x2191) ($L.pushok -f $seed, $api) 'DarkCyan'
            return 'ok'
        } catch {
            $lastErr = $_.Exception.Message
            Start-Sleep -Milliseconds 500
        }
    }
    if ($noSession) { Tag '!' $L.pushfail_session 'DarkYellow' } else { Tag '!' ($L.pushfail -f $lastErr) 'DarkYellow' }
    return 'fail'
}

# 설치본 버전 vs 서버가 알려주는 최신/최소 버전 대조 — 시작 시 1회.
function Check-ClientVersion {
    if (-not $script:apiBase) { return }
    $vi = $null
    try { $vi = Invoke-RestMethod -Uri ($script:apiBase + '/client-version') -Method Get -TimeoutSec 8 } catch { return }
    if (-not $vi) { return }
    $minV = 0; $latestV = 0
    try { $minV = [int]$vi.min } catch { }
    try { $latestV = [int]$vi.latest } catch { }
    if ($minV -gt 0 -and $script:CLIENT_VERSION -lt $minV) {
        Tag '!' ($L.update_req -f $script:CLIENT_VERSION, $minV, $script:siteUrl) 'Red'
    } elseif ($latestV -gt 0 -and $script:CLIENT_VERSION -lt $latestV) {
        Tag '!' ($L.update_avail -f $script:CLIENT_VERSION, $latestV, $script:siteUrl) 'DarkYellow'
    }
}

function Register-ToastAppId {
    try {
        $base = 'HKCU:\Software\Classes\AppUserModelId\' + $script:toastAppId
        if (-not (Test-Path $base)) {
            New-Item -Path $base -Force | Out-Null
            New-ItemProperty -Path $base -Name 'DisplayName' -Value 'SCP:SL Seed Monitor' -PropertyType String -Force | Out-Null
        }
    } catch {}
}

function Show-Toast($title, $line1, $line2, $launchUrl) {
    try {
        [void][Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]
        [void][Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime]
        $esc = { param($s) ("$s" -replace '&', '&amp;' -replace '<', '&lt;' -replace '>', '&gt;') }
        $attr = ''
        if ($launchUrl) { $attr = ' launch="' + (& $esc $launchUrl) + '" activationType="protocol"' }
        $xmlText = '<toast' + $attr + '><visual><binding template="ToastGeneric"><text>' + (& $esc $title) + '</text><text>' + (& $esc $line1) + '</text><text>' + (& $esc $line2) + '</text></binding></visual><audio src="ms-winsoundevent:Notification.Default"/></toast>'
        $xml = New-Object Windows.Data.Xml.Dom.XmlDocument
        $xml.LoadXml($xmlText)
        $toast = New-Object Windows.UI.Notifications.ToastNotification $xml
        [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($script:toastAppId).Show($toast)
        return $true
    } catch { return $false }
}

function Notify-Seed($seed, $server) {
    if (-not $script:notify) { return }
    $mapUrl = ''
    if ($script:api -match '^https?://') { $mapUrl = ($script:api -replace '/api/detect/?$', '') + '/' + $seed }
    $ok = Show-Toast $script:L.balloon_title ('seed ' + $seed) "$server" $mapUrl
    if (-not $ok -and $script:ni) {
        try { $script:ni.ShowBalloonTip(2500, $script:L.balloon_title, ('seed ' + $seed), [System.Windows.Forms.ToolTipIcon]::Info) } catch {}
    }
}

function Set-Notify($on) {
    $script:notify = [bool]$on
    try {
        $c = Load-Cfg $script:cfgPath
        $c.NOTIFY = if ($script:notify) { '1' } else { '0' }
        Save-Cfg $script:cfgPath $c
    } catch {}
    if ($script:notify) { Tag ([char]0x2713) $script:L.notify_on 'Green' }
    else { Tag ([char]0x2298) $script:L.notify_off 'DarkYellow' }
}

function Sync-Cmdbinding($keyCode) {
    $cbPath = Join-Path $env:APPDATA 'SCP Secret Laboratory\cmdbinding.txt'
    $aliases = @("$keyCode:seed", '281:seed')
    $lines = @()
    if (Test-Path $cbPath) { $lines = @(Get-Content $cbPath | Where-Object { $_.Trim() -ne '' }) }
    $kept = @($lines | Where-Object {
        $p = $_.Split(':', 2)
        $keep = $true
        if ($p.Count -eq 2) {
            $cmd = $p[1].Trim()
            $vk = $p[0].Trim()
            if ($cmd -eq 'seed') {
                $keep = $false
            }
            if ($vk -eq "$keyCode" -or $vk -eq '281') {
                $keep = $false
            }
        }
        return $keep
    })
    $content = ((@($kept) + $aliases) -join "`r`n") + "`r`n"
    $cur = if (Test-Path $cbPath) { [IO.File]::ReadAllText($cbPath) } else { '' }
    if ($cur -ne $content) { [IO.File]::WriteAllText($cbPath, $content, (New-Object System.Text.UTF8Encoding($false))) }
}

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class SLKey {
    [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow();
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    const uint UP=0x0002;
    public static void SendVK(byte vk){ keybd_event(vk,0,0,IntPtr.Zero); System.Threading.Thread.Sleep(40); keybd_event(vk,0,UP,IntPtr.Zero); }
    public static int ForegroundPid(){ int pid; GetWindowThreadProcessId(GetForegroundWindow(), out pid); return pid; }
}
"@

$ni = $null; $consoleVisible = $true; $cmdBuf = ''; $inputOk = $true

function Sleep-Pump($ms) {
    $end = (Get-Date).AddMilliseconds($ms)
    while ((Get-Date) -lt $end) {
        Poll-Input
        if ($script:tray) { [System.Windows.Forms.Application]::DoEvents() }
        Start-Sleep -Milliseconds 50
    }
}
function Quit-App { if ($script:ni) { $script:ni.Visible = $false; $script:ni.Dispose() }; [Environment]::Exit(0) }
function Toggle-Console {
    $hwnd = [SLKey]::GetConsoleWindow()
    if ($script:consoleVisible) { [void][SLKey]::ShowWindow($hwnd, 0); $script:consoleVisible = $false }
    else { [void][SLKey]::ShowWindow($hwnd, 5); $script:consoleVisible = $true }
}

function Init-Tray {
    if ($script:ni) { return }
    try {
        Add-Type -AssemblyName System.Windows.Forms
        Add-Type -AssemblyName System.Drawing
        $icon = $null
        try {
            $bmp = New-Object System.Drawing.Bitmap 32, 32
            $g = [System.Drawing.Graphics]::FromImage($bmp); $g.SmoothingMode = 'AntiAlias'
            $g.Clear([System.Drawing.Color]::FromArgb(18, 18, 20))
            $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(210, 45, 45)), 3
            $g.DrawEllipse($pen, 4, 4, 23, 23)
            $fnt = New-Object System.Drawing.Font 'Consolas', 13, ([System.Drawing.FontStyle]::Bold)
            $g.DrawString('S', $fnt, [System.Drawing.Brushes]::White, 7, 5)
            $g.Dispose()
            $icon = [System.Drawing.Icon]::FromHandle($bmp.GetHicon())
        } catch { $icon = [System.Drawing.SystemIcons]::Application }
        $n = New-Object System.Windows.Forms.NotifyIcon
        $n.Icon = $icon; $n.Text = $script:L.tray_tip; $n.Visible = $true
        $menu = New-Object System.Windows.Forms.ContextMenuStrip
        ($menu.Items.Add($script:L.tray_show)).add_Click({ Toggle-Console })
        ($menu.Items.Add($script:L.tray_log)).add_Click({ Start-Process explorer.exe $env:SEED_DIR })
        ($menu.Items.Add($script:L.tray_config)).add_Click({ Do-Reconfig })
        $menu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator)) | Out-Null
        ($menu.Items.Add($script:L.tray_quit)).add_Click({ Quit-App })
        $n.ContextMenuStrip = $menu
        $n.add_DoubleClick({ Toggle-Console })
        $script:ni = $n
        $script:tray = $true
    } catch {
        Tag '!' ($script:L.tray_fail -f $_.Exception.Message) 'DarkYellow'
        $script:tray = $false; $script:ni = $null
    }
}
function Kill-Tray {
    if ($script:ni) { $script:ni.Visible = $false; $script:ni.Dispose(); $script:ni = $null }
    $script:tray = $false
    if (-not $script:consoleVisible) { [void][SLKey]::ShowWindow([SLKey]::GetConsoleWindow(), 5); $script:consoleVisible = $true }
}

function Show-Help {
    Tag 'i' 'rconfig  설정 재구성 / reconfigure' 'Gray'
    Tag 'i' 'pair     브라우저 페어링 링크 열기 / open pairing link' 'Gray'
    Tag 'i' 'notify   알림 켜기/끄기 토글 (mute/unmute) / toggle notifications' 'Gray'
    Tag 'i' 'hide     콘솔 숨김(트레이) / hide console' 'Gray'
    Tag 'i' 'clear    화면 정리 / clear' 'Gray'
    Tag 'i' 'quit     종료 / quit' 'Gray'
}
function Do-Reconfig {
    if (-not $script:consoleVisible) { [void][SLKey]::ShowWindow([SLKey]::GetConsoleWindow(), 5); $script:consoleVisible = $true }
    Run-Setup $script:cfgPath
    $c = Load-Cfg $script:cfgPath
    $newTray = ($c.TRAY -eq '1')
    $script:lang = if ($c.LANG -eq 'en') { 'en' } else { 'ko' }; $script:L = $MSG[$script:lang]
    $script:keyName = if ($c.KEY) { $c.KEY } else { 'F8' }
    $kk = $KEYMAP[$script:keyName]; if (-not $kk) { $script:keyName = 'F8'; $kk = $KEYMAP['F8'] }
    $script:autoVk = $kk.vk; $script:keyCode = $kk.kc
    $script:auto = ($c.AUTO -ne '0'); $script:push = ($c.PUSH -ne '0'); $script:notify = ($c.NOTIFY -ne '0'); $script:api = $c.API; $script:token = $c.TOKEN
    Sync-Cmdbinding $script:keyCode
    if ($newTray -and -not $script:tray) { Init-Tray }
    elseif (-not $newTray -and $script:tray) { Kill-Tray }
    Tag ([char]0x2713) $script:L.reconfig_done 'Green'
    Write-Host ('     KEY {0}  AUTO {1}  PUSH {2}  NOTIFY {3}  TRAY {4}  LANG {5}' -f $script:keyName, $script:auto, $script:push, $script:notify, $script:tray, $script:lang) -ForegroundColor DarkCyan
}
function Dispatch-Command($cmd) {
    $c = $cmd.ToLower()
    if ($c -eq 'rconfig' -or $c -eq 'config' -or $c -eq 'reconfig') { Do-Reconfig }
    elseif ($c -eq 'quit' -or $c -eq 'exit' -or $c -eq 'q') { Quit-App }
    elseif ($c -eq 'hide') { if (-not $script:tray) { Init-Tray }; if ($script:tray) { Toggle-Console } else { Tag '!' $script:L.hide_needs_tray 'DarkYellow' } }
    elseif ($c -eq 'pair') { if ($script:pairUrl) { Start-Process $script:pairUrl; Tag ([char]0x2191) ($script:L.pair_label -f $script:pairUrl) 'DarkCyan' } else { Tag '!' $script:L.pair_none 'DarkYellow' } }
    elseif ($c -eq 'notify') { Set-Notify (-not $script:notify) }
    elseif ($c -eq 'mute') { Set-Notify $false }
    elseif ($c -eq 'unmute') { Set-Notify $true }
    elseif ($c -eq 'clear' -or $c -eq 'cls') { Clear-Host }
    elseif ($c -eq 'help' -or $c -eq '?') { Show-Help }
    else { Tag '?' ($script:L.unknown_cmd -f $cmd) 'DarkYellow' }
}
function Poll-Input {
    if (-not $script:inputOk) { return }
    try {
        while ([Console]::KeyAvailable) {
            $k = [Console]::ReadKey($true)
            if ($k.Key -eq 'Enter') {
                $cmd = $script:cmdBuf.Trim(); $script:cmdBuf = ''
                if ($cmd) {
                    Write-Host ("`r[$(Get-Date -Format HH:mm:ss)] > $cmd") -ForegroundColor DarkGray
                    Dispatch-Command $cmd
                } else { Write-Host '' }
            } elseif ($k.Key -eq 'Backspace') {
                if ($script:cmdBuf.Length -gt 0) { $script:cmdBuf = $script:cmdBuf.Substring(0, $script:cmdBuf.Length - 1); Write-Host "`b `b" -NoNewline }
            } elseif ($k.KeyChar -and ([int][char]$k.KeyChar) -ge 32) {
                $script:cmdBuf += $k.KeyChar; Write-Host $k.KeyChar -NoNewline -ForegroundColor Cyan
            }
        }
    } catch { $script:inputOk = $false }
}

Sync-Cmdbinding $keyCode
Register-ToastAppId
if ($tray) { Init-Tray }

$autoTxt = if ($auto) { 'ON ' } else { 'OFF' }
$pushTxt = if ($push -and $api) { 'ON ' } else { 'OFF' }
$notifyTxt = if ($notify) { 'ON ' } else { 'OFF' }
$trayTxt = if ($tray) { 'ON ' } else { 'OFF' }

Banner
Write-Host ('   ' + $L.boot1 + ' .........') -NoNewline -ForegroundColor DarkGray; Start-Sleep -Milliseconds 130; Write-Host ' OK' -ForegroundColor DarkGreen
Write-Host ('   ' + $L.boot2 + ' .........') -NoNewline -ForegroundColor DarkGray; Start-Sleep -Milliseconds 130; Write-Host ' OK' -ForegroundColor DarkGreen
Write-Host ('   ' + $L.boot3 + ' .........') -NoNewline -ForegroundColor DarkGray; Start-Sleep -Milliseconds 130; Write-Host ' OK' -ForegroundColor DarkGreen
Write-Host ''
Rule 'DarkRed'
Write-Host ('   ▌ ' + $L.title) -ForegroundColor White
Rule 'DarkRed'
if ($auto) { Tag '»' ($L.mode_auto -f $keyName) 'Gray' } else { Tag '»' ($L.mode_manual -f $keyName) 'Gray' }
Write-Host ('     KEY  {0,-7}   AUTO {1}   PUSH {2}   NOTIFY {3}   TRAY {4}   LANG {5}' -f $keyName, $autoTxt, $pushTxt, $notifyTxt, $trayTxt, $lang) -ForegroundColor DarkCyan
Write-Host ('     ' + ($L.cmdbind -f $keyCode)) -ForegroundColor DarkGray
if (Get-Process SCPSL -ErrorAction SilentlyContinue) { Tag '!' $L.cmdbind_restart 'DarkYellow' }
if ($push -and $api) { Write-Host ('     ' + ($L.push_on -f $api)) -ForegroundColor DarkGray } else { Write-Host ('     ' + $L.push_off) -ForegroundColor DarkGray }
if ($push -and $api) { Check-ClientVersion }
if ($push -and $pairUrl) {
    Write-Host ('     ' + ($L.pair_label -f $pairUrl)) -ForegroundColor DarkCyan
    Write-Host ('     ' + $L.pair_hint) -ForegroundColor DarkGray
}
Write-Host ('     ' + ($L.file -f $outJson)) -ForegroundColor DarkGray
if ($tray) { Write-Host ('     ' + $L.tray_min) -ForegroundColor DarkGray }
Write-Host ('     ' + $L.setup_hint) -ForegroundColor DarkGray
Write-Host ('     ' + $L.cmds_hint) -ForegroundColor DarkGray
Rule 'DarkRed'
Write-Host ("   ▓▒░ " + $L.monitoring + " ░▒▓   ") -NoNewline -ForegroundColor DarkGreen
Write-Host $L.quit -ForegroundColor DarkGray
Write-Host ''

$server = ''; $curSeed = ''; $running = $false; $pending = $false; $nextPress = Get-Date

while ($true) {
    $proc = Get-Process SCPSL -ErrorAction SilentlyContinue | Where-Object { -not $_.HasExited } | Select-Object -First 1

    if (-not $proc) {
        if ($running) {
            Tag ([char]0x25CB) ("[$(Get-Date -Format HH:mm:ss)] " + $L.exited) 'DarkGray'
            $running = $false; $server = ''; $curSeed = ''; $pending = $false
        }
        Sleep-Pump 3000
        continue
    }

    if (-not $running) {
        Tag ([char]0x25CF) ("[$(Get-Date -Format HH:mm:ss)] " + ($L.detected -f $proc.Id)) 'Green'
        $running = $true
        if ($auto) { $pending = $true; $curSeed = ''; $nextPress = Get-Date }
    }
    $w = 0; while (-not (Test-Path $log) -and $w -lt 10) { Sleep-Pump 1000; $w++ }
    if (-not (Test-Path $log)) { Sleep-Pump 2000; continue }

    $fs = $null; $sr = $null
    try {
    $fs = [System.IO.File]::Open($log, 'Open', 'Read', 'ReadWrite')
    $sr = New-Object System.IO.StreamReader($fs)

    while (-not $proc.HasExited) {
        $line = $sr.ReadLine()
        if ($null -eq $line) {
            if ($auto -and $pending -and ((Get-Date) -ge $nextPress)) {
                if ([SLKey]::ForegroundPid() -eq $proc.Id) {
                    [SLKey]::SendVK([byte]$autoVk)
                    $nextPress = (Get-Date).AddSeconds(4)
                    Tag ([char]0x00BB) ($L.sent -f $keyName) 'DarkCyan'
                }
            }
            Sleep-Pump 300; $proc.Refresh(); continue
        }
        $clean = ($line -replace '<[^>]*>', '').Trim()

        if ($clean -match 'Connection IP set to (.+?), port:\s*(\d+)') {
            $server = "$($matches[1]):$($matches[2])"
        }
        elseif ($clean -match 'Connecting to (.+?)!') {
            if (-not $server) { $server = $matches[1] }
        }
        elseif ($clean -match 'Server starting at .*port (\d+)') {
            $server = "localhost:$($matches[1])"
        }
        elseif ($clean -match 'Map seed is:\s*(-?\d+)') {
            $s = $matches[1]
            $pending = $false
            if ($s -ne $curSeed) {
                $curSeed = $s
                # 차단 판단은 백엔드가 한다: 일단 보내고 응답의 blocked를 본다(로컬 blocklist 판단 없음).
                $res = Post-Seed $curSeed $server
                if ($res -eq 'blocked') {
                    Tag ([char]0x2298) ($L.blocked -f $server) 'DarkYellow'
                } else {
                    $ts = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
                    $obj = [pscustomobject]@{ time = $ts; server = $server; seed = $curSeed }
                    try { [IO.File]::AppendAllText($outJson, ($obj | ConvertTo-Json -Compress) + "`r`n", (New-Object System.Text.UTF8Encoding($false))) } catch { Tag '!' ($L.writeerr -f $_.Exception.Message) 'DarkYellow' }
                    Tag ([char]0x2713) ($L.recorded -f $curSeed, $server) 'Green'
                    Notify-Seed $curSeed $server
                }
            } else {
                Tag ([char]0x00B7) ($L.dup -f $s) 'DarkGray'
            }
        }
        elseif ($clean -match 'Sequence of procedural level generation completed|New round has been started|\[FROM SERVER\] round id:') {
            if (-not $pending) {
                $pending = $true; $curSeed = ''; $nextPress = Get-Date
                Tag ([char]0x25B8) $L.round 'DarkYellow'
            }
        }
    }
    } catch {
        Tag '!' ($L.ioerr -f $_.Exception.Message) 'DarkYellow'
        Sleep-Pump 2000
    } finally {
        if ($sr) { $sr.Close() }
        if ($fs) { $fs.Close() }
    }
}
