# 🛡️ ShieldGuard PC Firewall & Browser Extension (Chrome & Edge)

Sistem Firewall mandiri dan pelindung Anti-Scam/Anti-Stealer untuk PC Windows Anda yang terintegrasi secara *real-time* dengan ekstensi **Google Chrome** dan **Microsoft Edge**.

---

## 📂 Struktur Folder (`c:\Firewall`)

* `Start-Firewall.bat` — Menjalankan aplikasi Desktop GUI + Server Firewall Lokal (`http://127.0.0.1:8765`).
* `Start-Firewall-Admin.bat` — Menjalankan aplikasi dalam **Mode Administrator** (agar bisa memblokir IP & aplikasi `.exe` langsung di level kernel *Windows Defender Firewall* `netsh` dan file `hosts` Windows).
* `pc-firewall/` — Kode sumber aplikasi Firewall PC (`firewall_app.py`, `firewall_engine.py`, `dashboard.html`).
* `browser-extension/` — Ekstensi Browser Manifest V3 untuk **Google Chrome** dan **Microsoft Edge**.

---

## 🚀 Cara Menjalankan Aplikasi PC Firewall

1. Klik 2x pada file **`Start-Firewall.bat`** (atau **`Start-Firewall-Admin.bat`** untuk proteksi penuh level Windows).
2. Jendela aplikasi **ShieldGuard PC Firewall** akan terbuka di layar Anda.
3. Anda juga dapat membuka tampilan Web Control Center kapan saja melalui browser di alamat:
   👉 `http://127.0.0.1:8765`

---

## 🧩 Cara Memasang Ekstensi di Google Chrome & Microsoft Edge

### A. Untuk Google Chrome:
1. Buka Google Chrome, ketik **`chrome://extensions`** di bilah alamat lalu tekan **Enter**.
2. Aktifkan sakelar **Mode pengembang (*Developer mode*)** di pojok kanan atas.
3. Klik tombol **Muat yang belum dikemas (*Load unpacked*)** di pojok kiri atas.
4. Pilih folder: **`c:\Firewall\browser-extension`** lalu klik **Select Folder**.
5. Sematkan (*Pin*) ikon perisai **ShieldGuard Firewall** di toolbar Chrome Anda.

### B. Untuk Microsoft Edge:
1. Buka Microsoft Edge, ketik **`edge://extensions`** di bilah alamat lalu tekan **Enter**.
2. Aktifkan sakelar **Mode pengembang (*Developer mode*)** di panel kiri/atas.
3. Klik tombol **Muat yang belum dikemas (*Load unpacked*)**.
4. Pilih folder: **`c:\Firewall\browser-extension`** lalu klik **Select Folder**.

---

## ✨ Fitur Utama yang Sudah Aktif

1. **Blokir Otomatis Situs Scam Kripto & Phishing (`woukox.com` dll.):**
   * Situs penipuan dari kasus Anda (`woukox.com` dan domain kasino kripto palsu lainnya) sudah masuk daftar hitam otomatis.
2. **Deteksi Heuristik Konten Scam (MrBeast / \$2,500 / Promo BOOM):**
   * Ekstensi memindai isi halaman web dan postingan sosial media (Instagram, X/Twitter, Discord, Facebook). Jika ada postingan/DM atau situs yang memuat kalimat jebakan seperti *"launch of my very own crypto casino"*, *"promo code: BOOM"*, atau *"giving away \$2,500"*, ShieldGuard langsung memunculkan peringatan merah.
3. **Pelindung Cookie & Anti-Self-XSS (Pencurian Sesi Akun):**
   * Mencegah skrip pencuri *cookie/session token* ditempelkan (*paste*) di halaman sosial media Anda.
4. **Monitor Koneksi Jaringan PC & Pemindai Malware Infostealer:**
   * Menampilkan seluruh aplikasi yang sedang terhubung ke internet di PC Anda lengkap dengan PID, IP tujuan, port, serta deteksi proses mencurigakan yang berjalan dari folder `Temp`.

