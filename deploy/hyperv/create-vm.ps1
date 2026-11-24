#Requires -Version 5.1
<#
.SYNOPSIS
    Cria a VM Linux do servidor do IndustrialSim Web no Hyper-V (D10, Etapa 9A).

.DESCRIPTION
    VM de geração 2 com Secure Boot para Linux, memória fixa (sem memória dinâmica), núcleos com reserva de 100 %,
    disco dinâmico, sem pontos de verificação automáticos, e início automático junto com o Windows (desligamento
    limpo quando o Windows desliga). Liga a VM a um switch externo, para ela ter IP próprio na rede do laboratório;
    se o switch não existir, ele é criado no adaptador -NetAdapterName (a rede do Windows cai por alguns segundos).

    Precisa de PowerShell como administrador ou de um usuário do grupo "Administradores do Hyper-V".

.EXAMPLE
    .\deploy\hyperv\create-vm.ps1 -IsoPath "$env:USERPROFILE\Downloads\ubuntu-26.04.1-live-server-amd64.iso" -NetAdapterName "Ethernet 2"
#>
param(
    [Parameter(Mandatory = $true)][string]$IsoPath,
    [string]$Name = 'industrialsim',
    [string]$SwitchName = 'Externo',
    [string]$NetAdapterName,
    [int]$MemoryGB = 4,
    [int]$Cpus = 4,
    [int]$DiskGB = 40
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $IsoPath)) {
    throw "ISO não encontrada: $IsoPath"
}

if (Get-VM -Name $Name -ErrorAction SilentlyContinue) {
    throw "A VM '$Name' já existe."
}

$hostCpus = (Get-CimInstance Win32_ComputerSystem).NumberOfLogicalProcessors
if ($Cpus -gt $hostCpus / 2) {
    Write-Warning "A VM teria $Cpus de $hostCpus processadores lógicos; sobra pouco para o Windows."
}

if (-not (Get-VMSwitch -Name $SwitchName -ErrorAction SilentlyContinue)) {
    if (-not $NetAdapterName) {
        Get-NetAdapter -Physical | Where-Object Status -eq 'Up' | Format-Table Name, InterfaceDescription, LinkSpeed -AutoSize
        throw "O switch '$SwitchName' não existe. Informe -NetAdapterName com um dos adaptadores acima para criá-lo."
    }

    Write-Host "Criando o switch externo '$SwitchName' em '$NetAdapterName' (a rede do Windows cai por alguns segundos)..."
    New-VMSwitch -Name $SwitchName -NetAdapterName $NetAdapterName -AllowManagementOS $true | Out-Null
}

$vhd = Join-Path (Get-VMHost).VirtualHardDiskPath "$Name.vhdx"
if (Test-Path -LiteralPath $vhd) {
    throw "O disco $vhd já existe."
}

New-VM -Name $Name -Generation 2 -MemoryStartupBytes ($MemoryGB * 1GB) -NewVHDPath $vhd -NewVHDSizeBytes ($DiskGB * 1GB) -SwitchName $SwitchName | Out-Null

# Memória fixa e núcleos reservados: o laço de tempo real não disputa recursos com o Windows (D10).
Set-VMMemory -VMName $Name -DynamicMemoryEnabled $false -StartupBytes ($MemoryGB * 1GB)
Set-VMProcessor -VMName $Name -Count $Cpus -Reserve 100

# Secure Boot com o modelo da CA de terceiros da Microsoft, que assina o carregador do Ubuntu.
Set-VMFirmware -VMName $Name -EnableSecureBoot On -SecureBootTemplate MicrosoftUEFICertificateAuthority
Add-VMDvdDrive -VMName $Name -Path $IsoPath
Set-VMFirmware -VMName $Name -FirstBootDevice (Get-VMDvdDrive -VMName $Name)

# Liga sozinha com o Windows e desliga de forma limpa com ele; sem pontos de verificação.
Set-VM -Name $Name -AutomaticStartAction Start -AutomaticStartDelay 0 -AutomaticStopAction ShutDown -CheckpointType Disabled -AutomaticCheckpointsEnabled $false

Get-VM -Name $Name | Format-List Name, State, Generation, ProcessorCount, MemoryStartup, DynamicMemoryEnabled, AutomaticStartAction, AutomaticStopAction, CheckpointType
Write-Host "VM criada. Inicie com: Start-VM -Name $Name; e abra o console com: vmconnect localhost $Name"
Write-Host "Depois de instalar o Ubuntu, tire a ISO: .\deploy\hyperv\finish-install.ps1 -Name $Name"
