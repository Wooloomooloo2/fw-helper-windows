# Windows side of the firmware snapshot (read-only): power plan incl. hidden settings, power-related drivers, Intel DTT/IPF registry.
param([Parameter(Mandatory)][string]$Dir)
$ErrorActionPreference = 'Continue'
function Save($name, $block) { try { & $block 2>&1 | Out-File -Encoding utf8 -Width 400 (Join-Path $Dir $name) } catch { "$_" | Out-File (Join-Path $Dir $name) } }

Save 'bios.txt' { Get-CimInstance Win32_BIOS | Format-List SMBIOSBIOSVersion, BIOSVersion, ReleaseDate, EmbeddedControllerMajorVersion, EmbeddedControllerMinorVersion, SystemBiosMajorVersion, SystemBiosMinorVersion
                  Get-CimInstance Win32_ComputerSystem | Format-List Manufacturer, Model, SystemSKUNumber }
Save 'powercfg.txt' { powercfg /getactivescheme; powercfg /list; powercfg /qh }
Save 'power-overlay.txt' { Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes' -ErrorAction SilentlyContinue | Format-List
                          powercfg /availablesleepstates }
# Drivers that could take part in CPU/GPU power sharing (versions change with BIOS/driver bundles)
Save 'drivers.txt' { Get-CimInstance Win32_PnPSignedDriver |
    Where-Object { $_.DeviceName -match 'Dynamic Tuning|Innovation Platform|IPF|DTT|Graphics|Arc|Management Engine|Power|Thermal|Energy|Platform Monitoring|GNA|NPU|Gaussian|Serial IO|Chipset|CrosEC|Embedded Controller' -or $_.InfName -match 'ipf|dtt|dptf' } |
    Sort-Object DeviceName | Format-Table DeviceName, DriverVersion, DriverDate, InfName, DeviceID -AutoSize }
Save 'services.txt' { Get-CimInstance Win32_Service | Where-Object { $_.Name -match 'ipf|dtt|dptf|esif|Intel' -or $_.DisplayName -match 'Intel' } |
    Sort-Object Name | Format-Table Name, State, StartMode, PathName -AutoSize }
Save 'intel-registry.txt' {
    foreach ($k in 'HKLM:\SYSTEM\CurrentControlSet\Services\ipf_lf', 'HKLM:\SYSTEM\CurrentControlSet\Services\ipf_cpu', 'HKLM:\SYSTEM\CurrentControlSet\Services\ipfsvc',
                   'HKLM:\SYSTEM\CurrentControlSet\Services\esif_lf', 'HKLM:\SYSTEM\CurrentControlSet\Services\dptf_cpu', 'HKLM:\SOFTWARE\Intel\IPF', 'HKLM:\SOFTWARE\Intel\DTT',
                   'HKLM:\SOFTWARE\Intel\DPTF', 'HKLM:\SOFTWARE\Intel\GFX', 'HKLM:\SOFTWARE\Intel\Display') {
        if (Test-Path $k) { "== $k"; Get-ChildItem $k -Recurse -ErrorAction SilentlyContinue | ForEach-Object { "-- $($_.Name)"; $_ | Get-ItemProperty -ErrorAction SilentlyContinue | Format-List | Out-String -Width 400 }
                            Get-ItemProperty $k -ErrorAction SilentlyContinue | Format-List | Out-String -Width 400 } } }
