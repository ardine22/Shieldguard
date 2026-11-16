import json
import os
import sys
import threading
import time
import webbrowser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse

import tkinter as tk
from tkinter import filedialog, messagebox, ttk

from firewall_engine import BASE_DIR, FirewallEngine

PORT = 8765
engine = FirewallEngine()


class FirewallAPIHandler(BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        # Silence console spam from periodic polling
        pass

    def _send_json(self, payload, status=200):
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.end_headers()
        self.wfile.write(body)

    def _read_json(self):
        length = int(self.headers.get("Content-Length", "0"))
        if length <= 0:
            return {}
        raw = self.rfile.read(length).decode("utf-8", errors="ignore")
        try:
            return json.loads(raw)
        except Exception:
            return {}

    def do_OPTIONS(self):
        self._send_json({"ok": True})

    def do_GET(self):
        parsed = urlparse(self.path)
        path = parsed.path

        if path in ("/", "/dashboard"):
            dash_path = os.path.join(BASE_DIR, "dashboard.html")
            if os.path.exists(dash_path):
                with open(dash_path, "rb") as f:
                    content = f.read()
                self.send_response(200)
                self.send_header("Content-Type", "text/html; charset=utf-8")
                self.send_header("Content-Length", str(len(content)))
                self.end_headers()
                self.wfile.write(content)
                return
            self._send_json({"error": "Dashboard file not found"}, 404)
            return

        if path == "/api/status":
            with engine.lock:
                data = dict(engine.config)
            data["is_admin"] = engine.is_admin()
            data["server_port"] = PORT
            self._send_json(data)
            return

        if path == "/api/connections":
            conns = engine.get_active_connections()
            self._send_json({"connections": conns})
            return

        if path == "/api/scan-processes":
            findings = engine.scan_for_infostealers()
            self._send_json({"findings": findings})
            return

        self._send_json({"error": "Endpoint not found"}, 404)

    def do_POST(self):
        parsed = urlparse(self.path)
        path = parsed.path
        body = self._read_json()

        if path == "/api/check-url":
            url = body.get("url", "")
            result = engine.evaluate_url(url)
            self._send_json(result)
            return

        if path == "/api/scan-content":
            url = body.get("url", "")
            title = body.get("title", "")
            text = body.get("text", "")
            result = engine.evaluate_page_content(url, title, text)
            self._send_json(result)
            return

        if path == "/api/block-domain":
            domain = body.get("domain", "")
            reason = body.get("reason", "Diblokir oleh pengguna")
            ok, msg = engine.add_blocked_domain(domain, reason)
            self._send_json({"ok": ok, "message": msg, "blocked_domains": engine.config["blocked_domains"]})
            return

        if path == "/api/unblock-domain":
            domain = body.get("domain", "")
            ok, msg = engine.remove_blocked_domain(domain)
            self._send_json({"ok": ok, "message": msg, "blocked_domains": engine.config["blocked_domains"]})
            return

        if path == "/api/block-ip":
            ip = body.get("ip", "")
            reason = body.get("reason", "Blokir manual IP")
            ok, msg = engine.block_ip(ip, reason)
            self._send_json({"ok": ok, "message": msg, "blocked_ips": engine.config["blocked_ips"]})
            return

        if path == "/api/unblock-ip":
            ip = body.get("ip", "")
            ok, msg = engine.unblock_ip(ip)
            self._send_json({"ok": ok, "message": msg, "blocked_ips": engine.config["blocked_ips"]})
            return

        if path == "/api/block-app":
            exe_path = body.get("path", "")
            ok, msg = engine.block_application(exe_path)
            self._send_json({"ok": ok, "message": msg, "blocked_apps": engine.config["blocked_apps"]})
            return

        if path == "/api/unblock-app":
            exe_path = body.get("path", "")
            ok, msg = engine.unblock_application(exe_path)
            self._send_json({"ok": ok, "message": msg, "blocked_apps": engine.config["blocked_apps"]})
            return

        if path == "/api/toggle":
            key = body.get("key", "")
            val = bool(body.get("value", True))
            if key in ("firewall_enabled", "scam_protection_enabled", "cookie_guard_enabled", "hosts_sync_enabled"):
                with engine.lock:
                    engine.config[key] = val
                    engine._save_config_raw(engine.config)
                engine.add_log("INFO", "CONFIG_CHANGE", key, f"Pengaturan {key} diubah menjadi {val}", "CONFIG")
                self._send_json({"ok": True, "key": key, "value": val})
                return
            self._send_json({"ok": False, "error": "Invalid setting key"}, 400)
            return

        if path == "/api/log-event":
            level = body.get("level", "WARNING")
            category = body.get("category", "BROWSER_GUARD")
            target = body.get("target", "Browser")
            details = body.get("details", "")
            action = body.get("action", "BLOCKED")
            entry = engine.add_log(level, category, target, details, action)
            self._send_json({"ok": True, "entry": entry})
            return

        self._send_json({"error": "Unknown POST endpoint"}, 404)


def start_api_server():
    try:
        server = ThreadingHTTPServer(("127.0.0.1", PORT), FirewallAPIHandler)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        return server
    except OSError:
        # Port 8765 might already be running from another instance
        return None


class ShieldGuardApp:
    def __init__(self, root):
        self.root = root
        self.root.title("ShieldGuard PC Firewall & Anti-Scam Center (v1.0)")
        self.root.geometry("1080x700")
        self.root.configure(bg="#0b0f19")

        self._setup_styles()
        self._build_header()
        self._build_tabs()
        self._refresh_ui()
        self._refresh_connections()

    def _setup_styles(self):
        style = ttk.Style()
        style.theme_use("clam")
        style.configure("TNotebook", background="#0b0f19", borderwidth=0)
        style.configure(
            "TNotebook.Tab",
            background="#1e293b",
            foreground="#e2e8f0",
            padding=[14, 8],
            font=("Segoe UI", 10, "bold")
        )
        style.map(
            "TNotebook.Tab",
            background=[("selected", "#2563eb")],
            foreground=[("selected", "#ffffff")]
        )
        style.configure(
            "Treeview",
            background="#111827",
            foreground="#f3f4f6",
            fieldbackground="#111827",
            rowheight=26,
            font=("Segoe UI", 9)
        )
        style.configure(
            "Treeview.Heading",
            background="#1f2937",
            foreground="#9ca3af",
            font=("Segoe UI", 9, "bold")
        )
        style.map("Treeview", background=[("selected", "#2563eb")])

    def _build_header(self):
        header = tk.Frame(self.root, bg="#111827", pady=12, padx=18, highlightthickness=1, highlightbackground="#1f2937")
        header.pack(fill="x", padx=12, pady=(12, 6))

        left = tk.Frame(header, bg="#111827")
        left.pack(side="left")

        title = tk.Label(
            left,
            text="🛡️ ShieldGuard PC Firewall & Browser Protection",
            font=("Segoe UI", 15, "bold"),
            bg="#111827",
            fg="#f8fafc"
        )
        title.pack(anchor="w")

        admin_txt = "Mode Administrator Aktif (Windows Firewall & Hosts Siap)" if engine.is_admin() else "Mode Standar (Sinkronisasi Ekstensi Chrome/Edge Aktif — Jalankan Admin untuk Blokir Level Windows)"
        admin_color = "#34d399" if engine.is_admin() else "#fbbf24"
        self.sub_label = tk.Label(
            left,
            text=f"API Server Lokal: http://127.0.0.1:{PORT}  |  {admin_txt}",
            font=("Segoe UI", 9),
            bg="#111827",
            fg=admin_color
        )
        self.sub_label.pack(anchor="w", pady=(2, 0))

        right = tk.Frame(header, bg="#111827")
        right.pack(side="right")

        self.fw_btn = tk.Button(
            right,
            text="FIREWALL: AKTIF",
            font=("Segoe UI", 10, "bold"),
            bg="#10b981",
            fg="#ffffff",
            relief="flat",
            padx=14,
            pady=6,
            command=self.toggle_firewall
        )
        self.fw_btn.pack(side="left", padx=6)

        dash_btn = tk.Button(
            right,
            text="🌐 Buka Web Dashboard",
            font=("Segoe UI", 10, "bold"),
            bg="#2563eb",
            fg="#ffffff",
            relief="flat",
            padx=14,
            pady=6,
            command=lambda: webbrowser.open(f"http://127.0.0.1:{PORT}")
        )
        dash_btn.pack(side="left", padx=6)

    def _build_tabs(self):
        self.notebook = ttk.Notebook(self.root)
        self.notebook.pack(fill="both", expand=True, padx=12, pady=8)

        # Tab 1: Live Network Connections
        self.tab_net = tk.Frame(self.notebook, bg="#0b0f19")
        self.notebook.add(self.tab_net, text="📡 Monitor Jaringan Live")
        self._build_tab_network()

        # Tab 2: Domain & Anti-Scam Blocker
        self.tab_dom = tk.Frame(self.notebook, bg="#0b0f19")
        self.notebook.add(self.tab_dom, text="🚫 Blokir Domain & Anti-Scam")
        self._build_tab_domains()

        # Tab 3: Windows Firewall IP & App Blocker
        self.tab_app = tk.Frame(self.notebook, bg="#0b0f19")
        self.notebook.add(self.tab_app, text="🔒 Blokir IP & Aplikasi (.exe)")
        self._build_tab_apps()

        # Tab 4: Malware / Cookie Stealer Scanner & Logs
        self.tab_logs = tk.Frame(self.notebook, bg="#0b0f19")
        self.notebook.add(self.tab_logs, text="🔍 Anti-Stealer & Log Keamanan")
        self._build_tab_logs()

    def _build_tab_network(self):
        top_bar = tk.Frame(self.tab_net, bg="#0b0f19", pady=8)
        top_bar.pack(fill="x", padx=10)

        self.stats_lbl = tk.Label(
            top_bar,
            text="Memuat statistik...",
            font=("Segoe UI", 10, "bold"),
            bg="#0b0f19",
            fg="#93c5fd"
        )
        self.stats_lbl.pack(side="left")

        btn_block_sel = tk.Button(
            top_bar,
            text="⛔ Blokir IP Koneksi Terpilih",
            bg="#ef4444",
            fg="#ffffff",
            font=("Segoe UI", 9, "bold"),
            relief="flat",
            padx=12,
            pady=4,
            command=self.block_selected_connection_ip
        )
        btn_block_sel.pack(side="right", padx=4)

        btn_refresh = tk.Button(
            top_bar,
            text="🔄 Segarkan Koneksi",
            bg="#374151",
            fg="#ffffff",
            font=("Segoe UI", 9, "bold"),
            relief="flat",
            padx=12,
            pady=4,
            command=self._refresh_connections
        )
        btn_refresh.pack(side="right", padx=4)

        cols = ("process", "pid", "local", "remote_ip", "remote_port", "state", "risk", "reason")
        self.conn_tree = ttk.Treeview(self.tab_net, columns=cols, show="headings")
        headers = {
            "process": ("Proses Aplikasi", 150),
            "pid": ("PID", 65),
            "local": ("Alamat Lokal", 145),
            "remote_ip": ("IP Tujuan", 135),
            "remote_port": ("Port", 65),
            "state": ("Status", 100),
            "risk": ("Risiko", 90),
            "reason": ("Keterangan Analisis", 270)
        }
        for col, (text, width) in headers.items():
            self.conn_tree.heading(col, text=text)
            self.conn_tree.column(col, width=width, anchor="w")

        self.conn_tree.pack(fill="both", expand=True, padx=10, pady=(0, 10))

    def _build_tab_domains(self):
        frame = tk.Frame(self.tab_dom, bg="#0b0f19", padx=12, pady=12)
        frame.pack(fill="both", expand=True)

        lbl = tk.Label(
            frame,
            text="Tambah Situs/Domain Berbahaya (Otomatis Diblokir di Chrome, Edge, & File Hosts):",
            font=("Segoe UI", 10, "bold"),
            bg="#0b0f19",
            fg="#f3f4f6"
        )
        lbl.pack(anchor="w")

        row = tk.Frame(frame, bg="#0b0f19", pady=6)
        row.pack(fill="x")

        self.domain_entry = tk.Entry(row, font=("Segoe UI", 10), bg="#111827", fg="#ffffff", insertbackground="#ffffff")
        self.domain_entry.pack(side="left", fill="x", expand=True, ipady=5, padx=(0, 8))

        btn_add = tk.Button(
            row, text="+ Blokir Domain", bg="#ef4444", fg="#ffffff",
            font=("Segoe UI", 9, "bold"), relief="flat", padx=14, pady=5,
            command=self.add_domain_ui
        )
        btn_add.pack(side="left", padx=4)

        btn_del = tk.Button(
            row, text="Hapus Domain Terpilih", bg="#374151", fg="#ffffff",
            font=("Segoe UI", 9, "bold"), relief="flat", padx=14, pady=5,
            command=self.remove_domain_ui
        )
        btn_del.pack(side="left", padx=4)

        self.domain_listbox = tk.Listbox(
            frame, bg="#111827", fg="#f87171", font=("Consolas", 10),
            selectbackground="#2563eb", height=14
        )
        self.domain_listbox.pack(fill="both", expand=True, pady=8)

        # Scam Simulator / Tester Box
        test_box = tk.Frame(frame, bg="#111827", padx=12, pady=10, highlightthickness=1, highlightbackground="#1f2937")
        test_box.pack(fill="x", pady=6)

        tk.Label(
            test_box,
            text="🧪 Uji Coba Pemindai Heuristik Scam / Teks Postingan (Contoh: Kasus MrBeast / woukox.com):",
            font=("Segoe UI", 10, "bold"), bg="#111827", fg="#60a5fa"
        ).pack(anchor="w")

        test_row = tk.Frame(test_box, bg="#111827", pady=6)
        test_row.pack(fill="x")

        self.test_entry = tk.Entry(test_row, font=("Segoe UI", 10), bg="#0b0f19", fg="#ffffff", insertbackground="#ffffff")
        self.test_entry.insert(0, "MrBeast: Go to woukox.com and enter promo code: BOOM to receive your $2,500 bonus")
        self.test_entry.pack(side="left", fill="x", expand=True, ipady=4, padx=(0, 8))

        tk.Button(
            test_row, text="🔍 Periksa Teks / URL", bg="#10b981", fg="#ffffff",
            font=("Segoe UI", 9, "bold"), relief="flat", padx=12, pady=4,
            command=self.test_scam_scanner
        ).pack(side="left")

    def _build_tab_apps(self):
        frame = tk.Frame(self.tab_app, bg="#0b0f19", padx=12, pady=12)
        frame.pack(fill="both", expand=True)

        # IP Section
        tk.Label(
            frame, text="Blokir Alamat IP Mencurigakan (Integrasi Windows Defender Firewall):",
            font=("Segoe UI", 10, "bold"), bg="#0b0f19", fg="#f3f4f6"
        ).pack(anchor="w")

        ip_row = tk.Frame(frame, bg="#0b0f19", pady=6)
        ip_row.pack(fill="x")

        self.ip_entry = tk.Entry(ip_row, font=("Segoe UI", 10), bg="#111827", fg="#ffffff", insertbackground="#ffffff")
        self.ip_entry.pack(side="left", fill="x", expand=True, ipady=4, padx=(0, 8))

        tk.Button(
            ip_row, text="+ Blokir IP", bg="#ef4444", fg="#ffffff",
            font=("Segoe UI", 9, "bold"), relief="flat", padx=14, pady=4,
            command=self.add_ip_ui
        ).pack(side="left", padx=4)

        tk.Button(
            ip_row, text="Buka Blokir IP Terpilih", bg="#374151", fg="#ffffff",
            font=("Segoe UI", 9, "bold"), relief="flat", padx=14, pady=4,
            command=self.remove_ip_ui
        ).pack(side="left", padx=4)

        self.ip_listbox = tk.Listbox(frame, bg="#111827", fg="#fbbf24", font=("Consolas", 10), height=7)
        self.ip_listbox.pack(fill="x", pady=(4, 14))

        # Application (.exe) Blocker Section
        tk.Label(
            frame, text="Blokir Akses Internet Aplikasi (.exe) di PC Anda:",
            font=("Segoe UI", 10, "bold"), bg="#0b0f19", fg="#f3f4f6"
        ).pack(anchor="w")

        app_row = tk.Frame(frame, bg="#0b0f19", pady=6)
        app_row.pack(fill="x")

        tk.Button(
            app_row, text="📂 Pilih File Aplikasi (.exe) untuk Diblokir...", bg="#2563eb", fg="#ffffff",
            font=("Segoe UI", 9, "bold"), relief="flat", padx=14, pady=5,
            command=self.browse_and_block_app
        ).pack(side="left", padx=(0, 8))

        tk.Button(
            app_row, text="Buka Blokir Aplikasi Terpilih", bg="#374151", fg="#ffffff",
            font=("Segoe UI", 9, "bold"), relief="flat", padx=14, pady=5,
            command=self.unblock_selected_app
        ).pack(side="left")

        self.app_listbox = tk.Listbox(frame, bg="#111827", fg="#93c5fd", font=("Consolas", 10), height=8)
        self.app_listbox.pack(fill="both", expand=True, pady=4)

    def _build_tab_logs(self):
        frame = tk.Frame(self.tab_logs, bg="#0b0f19", padx=12, pady=12)
        frame.pack(fill="both", expand=True)

        top = tk.Frame(frame, bg="#0b0f19", pady=4)
        top.pack(fill="x")

        tk.Label(
            top, text="Log Peringatan Ancaman & Pemindai Malware Pencuri Sesi (Infostealer):",
            font=("Segoe UI", 10, "bold"), bg="#0b0f19", fg="#f3f4f6"
        ).pack(side="left")

        tk.Button(
            top, text="🔍 Pindai Proses Pencuri Cookie / Malware Sekarang", bg="#10b981", fg="#ffffff",
            font=("Segoe UI", 9, "bold"), relief="flat", padx=14, pady=5,
            command=self.run_infostealer_scan_ui
        ).pack(side="right")

        cols = ("timestamp", "level", "category", "target", "details", "action")
        self.log_tree = ttk.Treeview(frame, columns=cols, show="headings")
        headers = {
            "timestamp": ("Waktu", 140),
            "level": ("Level", 80),
            "category": ("Kategori", 130),
            "target": ("Target", 180),
            "details": ("Detail Kejadian", 390),
            "action": ("Tindakan", 90)
        }
        for col, (text, width) in headers.items():
            self.log_tree.heading(col, text=text)
            self.log_tree.column(col, width=width, anchor="w")

        self.log_tree.pack(fill="both", expand=True, pady=8)

    # ======================== UI ACTIONS ========================

    def toggle_firewall(self):
        with engine.lock:
            engine.config["firewall_enabled"] = not engine.config.get("firewall_enabled", True)
            enabled = engine.config["firewall_enabled"]
            engine._save_config_raw(engine.config)
        self._refresh_ui()

    def add_domain_ui(self):
        dom = self.domain_entry.get().strip()
        if not dom:
            return
        ok, msg = engine.add_blocked_domain(dom, "Ditambahkan lewat Aplikasi Desktop")
        self.domain_entry.delete(0, "end")
        self._refresh_ui()
        messagebox.showinfo("ShieldGuard Firewall", msg)

    def remove_domain_ui(self):
        sel = self.domain_listbox.curselection()
        if not sel:
            return
        dom = self.domain_listbox.get(sel[0])
        ok, msg = engine.remove_blocked_domain(dom)
        self._refresh_ui()

    def test_scam_scanner(self):
        txt = self.test_entry.get().strip()
        if not txt:
            return
        url_res = engine.evaluate_url(txt)
        content_res = engine.evaluate_page_content("http://test-check.local", "Uji Coba Pemeriksaan", txt)
        self._refresh_ui()

        if not url_res["allowed"] or not content_res["allowed"]:
            reasons = []
            if not url_res["allowed"]:
                reasons.append(url_res["reason"])
            if not content_res["allowed"]:
                reasons.append(content_res["reason"])
                reasons.extend(content_res.get("matched", []))
            messagebox.showwarning(
                "🚨 ANCAMAN SCAM TERDETEKSI!",
                f"Skor Risiko: {max(url_res['risk_score'], content_res['risk_score'])}/100\n\n"
                f"Status: DIBLOKIR OTOMATIS\n\nDetail:\n- " + "\n- ".join(reasons)
            )
        else:
            messagebox.showinfo("✅ Aman", "Tidak ditemukan indikasi scam atau domain terlarang pada teks tersebut.")

    def add_ip_ui(self):
        ip = self.ip_entry.get().strip()
        if not ip:
            return
        ok, msg = engine.block_ip(ip, "Blokir manual via Desktop GUI")
        self.ip_entry.delete(0, "end")
        self._refresh_ui()
        messagebox.showinfo("ShieldGuard IP Firewall", msg)

    def remove_ip_ui(self):
        sel = self.ip_listbox.curselection()
        if not sel:
            return
        ip = self.ip_listbox.get(sel[0])
        engine.unblock_ip(ip)
        self._refresh_ui()

    def block_selected_connection_ip(self):
        sel = self.conn_tree.selection()
        if not sel:
            messagebox.showwarning("Pilih Koneksi", "Klik salah satu baris koneksi pada tabel terlebih dahulu.")
            return
        vals = self.conn_tree.item(sel[0], "values")
        if len(vals) >= 4:
            proc_name, remote_ip = vals[0], vals[3]
            ok, msg = engine.block_ip(remote_ip, f"Diblokir dari koneksi proses {proc_name}")
            self._refresh_ui()
            self._refresh_connections()
            messagebox.showinfo("Blokir IP Berhasil", msg)

    def browse_and_block_app(self):
        path = filedialog.askopenfilename(
            title="Pilih Aplikasi (.exe) untuk Diblokir Akses Internetnya",
            filetypes=[("Aplikasi Windows", "*.exe"), ("Semua File", "*.*")]
        )
        if not path:
            return
        ok, msg = engine.block_application(path)
        self._refresh_ui()
        messagebox.showinfo("Blokir Aplikasi", msg)

    def unblock_selected_app(self):
        sel = self.app_listbox.curselection()
        if not sel:
            return
        path = self.app_listbox.get(sel[0])
        ok, msg = engine.unblock_application(path)
        self._refresh_ui()
        messagebox.showinfo("Buka Blokir Aplikasi", msg)

    def run_infostealer_scan_ui(self):
        findings = engine.scan_for_infostealers()
        self._refresh_ui()
        if findings:
            detail = "\n".join([f"• {f['name']} (PID {f['pid']}): {f['description']}" for f in findings])
            messagebox.showwarning("🚨 Ditemukan Proses Mencurigakan!", detail)
        else:
            messagebox.showinfo(
                "✅ Sistem Bersih",
                "Tidak ditemukan proses pencuri sesi/cookie (Infostealer) atau aplikasi mencurigakan dari folder Temp yang berjalan saat ini."
            )

    def _refresh_connections(self):
        for item in self.conn_tree.get_children():
            self.conn_tree.delete(item)
        conns = engine.get_active_connections()
        for c in conns:
            self.conn_tree.insert(
                "", "end",
                values=(c["process"], c["pid"], c["local"], c["remote_ip"], c["remote_port"], c["state"], c["risk"], c["reason"])
            )

    def _refresh_ui(self):
        with engine.lock:
            cfg = json.loads(json.dumps(engine.config))

        if cfg.get("firewall_enabled", True):
            self.fw_btn.configure(text="FIREWALL: AKTIF", bg="#10b981")
        else:
            self.fw_btn.configure(text="FIREWALL: NONAKTIF", bg="#ef4444")

        st = cfg.get("stats", {})
        self.stats_lbl.configure(
            text=f"URL Diperiksa: {st.get('total_requests_checked', 0)}   |   "
                 f"Ancaman Diblokir: {st.get('threats_blocked', 0)}   |   "
                 f"Halaman Scam Terdeteksi: {st.get('scam_pages_detected', 0)}   |   "
                 f"Domain Diblokir: {len(cfg.get('blocked_domains', []))}"
        )

        # Update Domain Listbox
        self.domain_listbox.delete(0, "end")
        for d in cfg.get("blocked_domains", []):
            self.domain_listbox.insert("end", d)

        # Update IP Listbox
        self.ip_listbox.delete(0, "end")
        for ip in cfg.get("blocked_ips", []):
            self.ip_listbox.insert("end", ip)

        # Update App Listbox
        self.app_listbox.delete(0, "end")
        for app in cfg.get("blocked_apps", []):
            self.app_listbox.insert("end", app)

        # Update Logs Treeview
        for item in self.log_tree.get_children():
            self.log_tree.delete(item)
        for l in cfg.get("logs", [])[:60]:
            self.log_tree.insert(
                "", "end",
                values=(l["timestamp"], l["level"], l["category"], l["target"], l["details"], l["action"])
            )

        self.root.after(4000, self._refresh_ui)


if __name__ == "__main__":
    server = start_api_server()
    if "--headless" in sys.argv:
        print(f"ShieldGuard Firewall API running on http://127.0.0.1:{PORT}")
        try:
            while True:
                time.sleep(1)
        except KeyboardInterrupt:
            sys.exit(0)
    else:
        root = tk.Tk()
        app = ShieldGuardApp(root)
        root.mainloop()

