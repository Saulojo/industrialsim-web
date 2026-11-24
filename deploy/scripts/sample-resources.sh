#!/bin/sh
# Amostra CPU e RAM da VM e do serviço do servidor, uma linha por intervalo (Etapa 9A, teste de carga).
# O "simrun carga-rede --ssh" manda este script pelo ssh (sh -s) e lê a saída; ele também roda à mão:
#   sh sample-resources.sh [intervalo_s] [servico]
#
# Formato de cada linha (números inteiros, exceto o uptime):
#   S <uptime_s> <nucleos> <clk_tck> <cpu_total> <cpu_ocioso> <mem_total_kB> <mem_disponivel_kB> <cpu_servico> <rss_servico_kB>
# cpu_* em "jiffies" acumulados (de /proc/stat); cpu_servico = utime + stime do processo principal do serviço;
# cpu_servico e rss_servico valem -1 se o serviço não estiver rodando.
set -u

interval="${1:-5}"
service="${2:-industrialsim}"
clk=$(getconf CLK_TCK)
cpus=$(nproc)

while :; do
    proc=-1
    rss=-1
    pid=$(systemctl show -p MainPID --value "$service" 2>/dev/null || echo 0)
    if [ -n "$pid" ] && [ "$pid" != 0 ] && [ -r "/proc/$pid/stat" ]; then
        # Tira "pid (nome) " do começo (o nome pode ter espaços); utime e stime viram os campos 12 e 13.
        proc=$(sed 's/^.*) //' "/proc/$pid/stat" | awk '{ print $12 + $13 }')
        rss=$(awk '/^VmRSS:/ { print $2 }' "/proc/$pid/status")
    fi

    # Primeira linha de /proc/stat: cpu user nice system idle iowait irq softirq steal guest guest_nice
    read -r _ user nice system idle iowait irq softirq steal _ < /proc/stat
    total=$((user + nice + system + idle + iowait + irq + softirq + steal))
    idle_all=$((idle + iowait))

    mem=$(awk '/^MemTotal:/ { t = $2 } /^MemAvailable:/ { a = $2 } END { print t, a }' /proc/meminfo)
    up=$(cut -d ' ' -f 1 /proc/uptime)

    echo "S $up $cpus $clk $total $idle_all $mem ${proc:--1} ${rss:--1}"
    sleep "$interval"
done
