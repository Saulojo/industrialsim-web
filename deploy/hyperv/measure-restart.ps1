#Requires -Version 5.1
<#
.SYNOPSIS
    Mede quanto o site leva para voltar depois de um reinício (Etapa 9A, critério "volta sozinho em menos de
    2 minutos").

.DESCRIPTION
    Consulta /api/health a cada segundo pelo curl.exe do Windows e registra, com horário, quando o site parou de
    responder e quando voltou. Rode antes de reiniciar:
      - a VM: neste PC (sudo reboot na VM, ou Restart-VM);
      - o Windows hospedeiro: em OUTRO computador da rede (este também reinicia).
    O tempo medido vai do comando de reinício (marque com Enter, ou use o instante da queda) até a volta.

.EXAMPLE
    .\deploy\hyperv\measure-restart.ps1 -Url https://10.0.67.80/api/health -CaPath .\root.crt
#>
param(
    [Parameter(Mandatory = $true)][string]$Url,
    [string]$CaPath,
    [int]$TimeoutMinutes = 15,
    [string]$LogPath = "reinicio-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"
)

$ErrorActionPreference = 'Stop'

function Test-Health {
    $curlArgs = @('-s', '-o', 'NUL', '-w', '%{http_code}', '--max-time', '2')
    if ($CaPath) { $curlArgs += @('--cacert', $CaPath) }
    $code = & curl.exe @curlArgs $Url 2>$null
    return $code -eq '200'
}

function Write-Mark([string]$text) {
    $line = '{0:yyyy-MM-dd HH:mm:ss.fff}  {1}' -f (Get-Date), $text
    Write-Host $line
    Add-Content -LiteralPath $LogPath -Value $line -Encoding UTF8
}

if (-not (Test-Health)) {
    throw "O site não responde agora em $Url; confira o endereço e o certificado (-CaPath) antes de reiniciar."
}

Write-Mark "Site respondendo em $Url. Reinicie a VM ou o hospedeiro agora."
$start = Get-Date
$down = $null
while (((Get-Date) - $start).TotalMinutes -lt $TimeoutMinutes) {
    $ok = Test-Health
    if (-not $ok -and -not $down) {
        $down = Get-Date
        Write-Mark 'Parou de responder.'
    }
    elseif ($ok -and $down) {
        $up = Get-Date
        Write-Mark 'Voltou a responder.'
        Write-Mark ('Fora do ar por {0:N1} s (da queda até a volta).' -f ($up - $down).TotalSeconds)
        exit 0
    }

    Start-Sleep -Seconds 1
}

Write-Mark "Sem volta em $TimeoutMinutes min."
exit 1
