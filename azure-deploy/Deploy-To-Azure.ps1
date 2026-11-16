# ==============================================================================
# Script Otomatis Deploy Website ShieldGuard Pro ke Azure Student VM
# ==============================================================================

Write-Host "====================================================================" -ForegroundColor Cyan
Write-Host "  🚀 DEPLOYMENT WIZARD: SHIELDGUARD PRO KE AZURE STUDENT VM" -ForegroundColor Green
Write-Host "====================================================================" -ForegroundColor Cyan
Write-Host ""

$deployDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# 1. Minta IP Publik VM Azure
$vmIp = Read-Host "1. Masukkan Alamat IP Publik Azure VM Anda (contoh: 20.198.xxx.xxx)"
if ([string]::IsNullOrWhiteSpace($vmIp)) {
    Write-Host "❌ IP tidak boleh kosong!" -ForegroundColor Red
    exit
}

# 2. Minta Username VM
$vmUser = Read-Host "2. Masukkan Username VM Anda (default: azureuser)"
if ([string]::IsNullOrWhiteSpace($vmUser)) {
    $vmUser = "azureuser"
}

# 3. Metode Login: SSH Key (.pem) atau Password
Write-Host ""
Write-Host "Metode Login ke Azure VM:" -ForegroundColor Yellow
Write-Host "  [1] Menggunakan File SSH Key (.pem)"
Write-Host "  [2] Menggunakan Password"
$authChoice = Read-Host "Pilih metode login (1 atau 2)"

$sshArgs = ""
$scpArgs = ""

if ($authChoice -eq "1") {
    $keyPath = Read-Host "Masukkan path file private key .pem (contoh: C:\Users\Owner\Downloads\myKey.pem)"
    $keyPath = $keyPath.Trim().Trim('"')
    if (-not (Test-Path $keyPath)) {
        Write-Host "❌ File key tidak ditemukan di: $keyPath" -ForegroundColor Red
        exit
    }
    $sshArgs = "-i `"$keyPath`""
    $scpArgs = "-i `"$keyPath`""
}

Write-Host ""
Write-Host "📦 [Langkah 1/3] Mengunggah file website & installer ke Azure VM via SCP..." -ForegroundColor Cyan

$remoteTarget = "${vmUser}@${vmIp}:~/shieldguard-web"

# Buat folder remote terlebih dahulu
if ($authChoice -eq "1") {
    & ssh.exe -i $keyPath -o StrictHostKeyChecking=no "${vmUser}@${vmIp}" "mkdir -p ~/shieldguard-web"
    & scp.exe -i $keyPath -r -o StrictHostKeyChecking=no "$deployDir/*" "${remoteTarget}/"
} else {
    & ssh.exe -o StrictHostKeyChecking=no "${vmUser}@${vmIp}" "mkdir -p ~/shieldguard-web"
    & scp.exe -r -o StrictHostKeyChecking=no "$deployDir/*" "${remoteTarget}/"
}

Write-Host ""
Write-Host "⚙️ [Langkah 2/3] Menjalankan script setup & mengaktifkan service 24/7 di Azure..." -ForegroundColor Cyan

$remoteCmd = "cd ~/shieldguard-web && chmod +x setup-vm.sh && bash setup-vm.sh"
if ($authChoice -eq "1") {
    & ssh.exe -i $keyPath -o StrictHostKeyChecking=no "${vmUser}@${vmIp}" $remoteCmd
} else {
    & ssh.exe -o StrictHostKeyChecking=no "${vmUser}@${vmIp}" $remoteCmd
}

Write-Host ""
Write-Host "====================================================================" -ForegroundColor Green
Write-Host "🎉 DEPLOYMENT SELESAI!" -ForegroundColor Green
Write-Host "Website Anda sekarang sudah aktif dan dapat diakses publik di:" -ForegroundColor Yellow
Write-Host "👉 http://$vmIp" -ForegroundColor Cyan
Write-Host ""
Write-Host "Pastikan di Azure Portal -> VM -> Networking (Inbound Port Rules) Anda telah menambahkan:" -ForegroundColor White
Write-Host "  - Port 80 (HTTP) -> Allow" -ForegroundColor Green
Write-Host "  - Port 443 (HTTPS) -> Allow" -ForegroundColor Green
Write-Host "====================================================================" -ForegroundColor Green

