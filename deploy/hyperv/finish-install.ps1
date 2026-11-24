#Requires -Version 5.1
<#
.SYNOPSIS
    Depois de instalar o Ubuntu na VM (Etapa 9A): tira a ISO e faz a VM iniciar pelo disco.
#>
param(
    [string]$Name = 'industrialsim'
)

$ErrorActionPreference = 'Stop'

$dvd = Get-VMDvdDrive -VMName $Name
if ($dvd) {
    Set-VMDvdDrive -VMName $Name -ControllerNumber $dvd.ControllerNumber -ControllerLocation $dvd.ControllerLocation -Path $null
}

Set-VMFirmware -VMName $Name -FirstBootDevice (Get-VMHardDiskDrive -VMName $Name | Select-Object -First 1)
Get-VMFirmware -VMName $Name | Select-Object -ExpandProperty BootOrder | Format-Table BootType, Device -AutoSize
