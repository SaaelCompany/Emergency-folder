param(
  [Parameter(Mandatory = $true)]
  [string]$ExePath
)

$ErrorActionPreference = 'Continue'
$users = @()

try {
  $users = @(Get-Process -Name explorer -IncludeUserName -ErrorAction SilentlyContinue |
    ForEach-Object { $_.UserName } |
    Where-Object { $_ -and ($_ -notmatch '(?i)NT AUTHORITY\\SYSTEM$') } |
    Select-Object -Unique)
} catch {}

if (-not $users -or $users.Count -eq 0) {
  try {
    $owners = @{}
    Get-CimInstance Win32_Process -Filter "Name = 'explorer.exe'" -ErrorAction SilentlyContinue | ForEach-Object {
      $owner = Invoke-CimMethod -InputObject $_ -MethodName GetOwner
      if ($owner.ReturnValue -eq 0 -and $owner.User) {
        $name = if ($owner.Domain) { "$($owner.Domain)\$($owner.User)" } else { $owner.User }
        if ($name -notmatch '(?i)^NT AUTHORITY\\SYSTEM$') { $owners[$name] = $true }
      }
    }
    $users = @($owners.Keys)
  } catch {}
}

if (-not $users -or $users.Count -eq 0) {
  Write-Output 'NO_USER'
  exit 0
}

function Escape-Xml([string]$Value) {
  return (($Value -replace '&', '&amp;') -replace '<', '&lt;' -replace '>', '&gt;' -replace '"', '&quot;')
}

foreach ($user in $users) {
  $task = 'EmergencyFolderLaunch'
  $xmlPath = Join-Path $env:TEMP ("emergency-folder-" + [guid]::NewGuid().ToString() + '.xml')
  $userXml = Escape-Xml $user
  $exeXml = Escape-Xml $ExePath
  $xml = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <Triggers>
    <TimeTrigger>
      <StartBoundary>2020-01-01T00:00:00</StartBoundary>
      <Enabled>true</Enabled>
    </TimeTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <UserId>$userXml</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>false</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>true</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT1H</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>$exeXml</Command>
      <Arguments>--background</Arguments>
    </Exec>
  </Actions>
</Task>
"@
  [System.IO.File]::WriteAllText($xmlPath, $xml, [System.Text.Encoding]::Unicode)
  & schtasks.exe /Create /TN $task /XML $xmlPath /F
  if ($LASTEXITCODE -eq 0) {
    & schtasks.exe /Run /TN $task
    Start-Sleep -Seconds 3
    & schtasks.exe /Delete /TN $task /F | Out-Null
    Write-Output "STARTED $user"
  } else {
    Write-Output "FAILED $user"
  }
  Remove-Item $xmlPath -Force -ErrorAction SilentlyContinue
}
