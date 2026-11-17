[CmdletBinding()]
param(
    [string] $AdbPath = "adb",
    [ValidatePattern('^emulator-[0-9]+$')]
    [string] $EmulatorSerial
)

$ErrorActionPreference = "Stop"
$adb = (Get-Command $AdbPath -CommandType Application).Source
$devices = & $adb devices
if ($LASTEXITCODE -ne 0) { throw "ADB could not list devices." }
$online = @($devices | ForEach-Object {
    if ($_ -match '^(emulator-[0-9]+)\s+device\s*$') { $Matches[1] }
})
if (!$EmulatorSerial -and $online.Count -eq 1) { $EmulatorSerial = $online[0] }
if (!$EmulatorSerial -or $online -notcontains $EmulatorSerial) {
    throw "Start one Android emulator or specify an online emulator with -EmulatorSerial."
}

function Test-LocalSocks {
    $client = [System.Net.Sockets.TcpClient]::new()
    try {
        $client.Connect("127.0.0.1", 9050)
        $stream = $client.GetStream()
        $stream.ReadTimeout = 1000
        $stream.WriteTimeout = 1000
        # SOCKS5 greeting with no authentication. No destination is contacted.
        $stream.Write([byte[]](5, 1, 0), 0, 3)
        return ($stream.ReadByte() -eq 5 -and $stream.ReadByte() -eq 0)
    } catch [System.IO.IOException], [System.Net.Sockets.SocketException] {
        return $false
    } finally {
        $client.Dispose()
    }
}

if (-not (Test-LocalSocks)) {
    if (Get-NetTCPConnection -LocalPort 9050 -State Listen -ErrorAction SilentlyContinue) {
        throw "Port 9050 is in use by a service that did not answer as SOCKS5."
    }
    $tor = Join-Path $PSScriptRoot "../WalletWasabi/BundledApps/Binaries/win-x64/Tor/tor.exe"
    $data = Join-Path $env:LOCALAPPDATA "WasabiMobile/EmulatorTor"
    New-Item -ItemType Directory -Path $data -Force | Out-Null
    $log = Join-Path $data "tor.log"
    $options = @{
        FilePath = $tor
        ArgumentList = @("--SocksPort", "127.0.0.1:9050", "--DataDirectory", ('"' + $data + '"'))
        RedirectStandardOutput = $log
        RedirectStandardError = Join-Path $data "tor-error.log"
        WindowStyle = "Hidden"
        PassThru = $true
    }
    $process = Start-Process @options
    $ready = $false
    for ($attempt = 0; $attempt -lt 45; $attempt++) {
        if ($process.HasExited) { break }
        if ((Select-String -Path $log -SimpleMatch "Bootstrapped 100%" -Quiet) -and (Test-LocalSocks)) {
            $ready = $true
            break
        }
        Start-Sleep -Seconds 1
    }
    if (!$ready) { throw "Tor did not finish bootstrapping. Check the logs in $data." }
    Write-Output "Tor bootstrapped on Windows."
} else {
    Write-Output "Reusing the local SOCKS5 listener on port 9050."
}

& $adb -s $EmulatorSerial reverse tcp:9050 tcp:9050 | Out-Null
if ($LASTEXITCODE -ne 0) { throw "ADB could not forward the emulator's Tor port." }
$forwarding = & $adb -s $EmulatorSerial reverse --list
if ($LASTEXITCODE -ne 0 -or -not ($forwarding -match '\btcp:9050\s+tcp:9050\s*$')) {
    throw "ADB did not report the expected Tor forwarding."
}
Write-Output "Tor forwarding is ready for $EmulatorSerial. Select Start sync in the wallet."
