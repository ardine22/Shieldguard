#!/bin/bash
# ==============================================================================
# Script Otomatis Setup Web Server ShieldGuard Pro HTTPS di Azure Student VM
# ==============================================================================

set -e

echo "🚀 [1/5] Memperbarui paket sistem Ubuntu & menginstal openssl, python3, ufw..."
sudo apt update -y
sudo apt install -y python3 openssl ufw

echo "🔒 [2/5] Menyiapkan Sertifikat SSL/TLS untuk HTTPS (Port 443)..."
sudo mkdir -p /etc/ssl
if [ ! -f /etc/ssl/shieldguard.crt ] || [ ! -f /etc/ssl/shieldguard.key ]; then
    echo "Membuat sertifikat SSL RSA 2048-bit untuk ShieldGuard..."
    sudo openssl req -x509 -newkey rsa:2048 \
        -keyout /etc/ssl/shieldguard.key \
        -out /etc/ssl/shieldguard.crt \
        -days 365 -nodes \
        -subj "/C=ID/ST=Jakarta/L=Jakarta/O=ShieldGuard/CN=70.151.150.190"
fi
sudo chmod 600 /etc/ssl/shieldguard.key
sudo chmod 644 /etc/ssl/shieldguard.crt

echo "🔓 [3/5] Mengizinkan Port 443 (HTTPS), 80 (HTTP), dan 22 (SSH) di Firewall Linux UFW..."
sudo ufw allow 22/tcp || true
sudo ufw allow 80/tcp || true
sudo ufw allow 443/tcp || true
sudo ufw --force enable || true

APP_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
echo "📂 Direktori aplikasi: $APP_DIR"

echo "⚙️ [4/5] Mendaftarkan systemd service HTTPS agar website berjalan 24/7 di background..."
sudo bash -c "cat <<EOF > /etc/systemd/system/shieldguard-web.service
[Unit]
Description=ShieldGuard Pro Web & Download Server HTTPS (Azure VM)
After=network.target

[Service]
Type=simple
User=root
WorkingDirectory=$APP_DIR
ExecStart=/usr/bin/python3 $APP_DIR/server.py
Restart=always
RestartSec=3
Environment=HTTP_PORT=80
Environment=HTTPS_PORT=443
Environment=SSL_CERT=/etc/ssl/shieldguard.crt
Environment=SSL_KEY=/etc/ssl/shieldguard.key

[Install]
WantedBy=multi-user.target
EOF"

echo "🔄 [5/5] Menjalankan service ShieldGuard HTTPS..."
sudo systemctl daemon-reload
sudo systemctl enable shieldguard-web.service
sudo systemctl restart shieldguard-web.service

echo "=============================================================================="
echo "🎉 SETUP HTTPS BERHASIL! Website ShieldGuard Pro sekarang LIVE via HTTPS 24/7!"
echo "Status Service:"
sudo systemctl status shieldguard-web.service --no-pager
echo "=============================================================================="
