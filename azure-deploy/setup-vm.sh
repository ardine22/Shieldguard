#!/bin/bash
# ==============================================================================
# Script Otomatis Setup Web Server ShieldGuard Pro di Azure Student VM (Ubuntu/Debian)
# ==============================================================================

set -e

echo "🚀 [1/4] Memperbarui paket sistem Ubuntu & memeriksa Python 3..."
sudo apt update -y
sudo apt install -y python3 ufw

echo "🔓 [2/4] Mengizinkan Port 80 (HTTP), 443 (HTTPS), dan 22 (SSH) di Firewall Linux..."
sudo ufw allow 22/tcp || true
sudo ufw allow 80/tcp || true
sudo ufw allow 443/tcp || true
sudo ufw --force enable || true

APP_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
echo "📂 Direktori aplikasi: $APP_DIR"

echo "⚙️ [3/4] Mendaftarkan systemd service agar website berjalan 24/7 di background..."
sudo bash -c "cat <<EOF > /etc/systemd/system/shieldguard-web.service
[Unit]
Description=ShieldGuard Pro Web & Download Server (Azure VM)
After=network.target

[Service]
Type=simple
User=root
WorkingDirectory=$APP_DIR
ExecStart=/usr/bin/python3 $APP_DIR/server.py
Restart=always
RestartSec=3
Environment=PORT=80

[Install]
WantedBy=multi-user.target
EOF"

echo "🔄 [4/4] Menjalankan service ShieldGuard..."
sudo systemctl daemon-reload
sudo systemctl enable shieldguard-web.service
sudo systemctl restart shieldguard-web.service

echo "=============================================================================="
echo "🎉 SETUP BERHASIL! Website penjualan & download Anda sekarang LIVE 24/7!"
echo "Status Service:"
sudo systemctl status shieldguard-web.service --no-pager
echo "=============================================================================="

