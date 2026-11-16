import ctypes
import datetime
import json
import os
import re
import socket
import subprocess
import threading
from urllib.parse import urlparse

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
DATA_DIR = os.path.join(BASE_DIR, "data")
RULES_FILE = os.path.join(DATA_DIR, "firewall_rules.json")
HOSTS_PATH = r"C:\Windows\System32\drivers\etc\hosts"
HOSTS_MARKER_START = "# >>> SHIELDGUARD FIREWALL START >>>"
HOSTS_MARKER_END = "# <<< SHIELDGUARD FIREWALL END <<<"

DEFAULT_CONFIG = {
    "firewall_enabled": True,
    "scam_protection_enabled": True,
    "cookie_guard_enabled": True,
    "hosts_sync_enabled": True,
    "blocked_domains": [
        "woukox.com",
        "www.woukox.com",
        "mrbeast-casino.com",
        "beast-crypto.com",
        "elon-giveaway.org",
        "stake-bonus-claim.com",
        "crypto-boom-bonus.com",
        "usdt-instant-claim.com",
        "free-2500-usdt.com",
        "discord-nitro-free.ru",
        "instagram-verify-badge.com"
    ],
    "whitelisted_domains": [
        "google.com",
        "www.google.com",
        "youtube.com",
        "www.youtube.com",
        "instagram.com",
        "www.instagram.com",
        "facebook.com",
        "www.facebook.com",
        "x.com",
        "twitter.com",
        "github.com",
        "microsoft.com",
        "localhost",
        "127.0.0.1"
    ],
    "blocked_ips": [
        "185.220.101.1",
        "45.142.214.99"
    ],
    "blocked_apps": [],
    "scam_keywords": [
        "launch of my very own crypto casino",
        "giving away $2,500 to everyone who registers",
        "enter the special promo code: boom",
        "enter the promo code: boom",
        "withdraw the bonus instantly",
        "your withdrawal of $2500.00 was successfully",
        "receive your $2,500 bonus",
        "woukox.com",
        "deposit to verify your wallet",
        "activate your wallet for withdrawal",
        "mrbeast crypto casino",
        "elon musk crypto giveaway",
        "send 0.05 btc to receive 0.1 btc"
    ],
    "suspicious_domain_patterns": [
        r"woukox",
        r"beast.*casino",
        r"crypto.*giveaway",
        r"claim.*usdt",
        r"bonus.*2500",
        r"free.*nitro.*discord",
        r"insta.*login.*verify"
    ],
    "stats": {
        "total_requests_checked": 0,
        "threats_blocked": 0,
        "scam_pages_detected": 0,
        "network_ips_blocked": 0
    },
    "logs": []
}


class FirewallEngine:
    def __init__(self):
        os.makedirs(DATA_DIR, exist_ok=True)
        self.lock = threading.Lock()
        self.config = self._load_config()
        self._startup_flags = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0

    def _load_config(self):
        if not os.path.exists(RULES_FILE):
            self._save_config_raw(DEFAULT_CONFIG)
            return json.loads(json.dumps(DEFAULT_CONFIG))
        try:
            with open(RULES_FILE, "r", encoding="utf-8") as f:
                data = json.load(f)
            for k, v in DEFAULT_CONFIG.items():
                if k not in data:
                    data[k] = v
            return data
        except Exception:
            return json.loads(json.dumps(DEFAULT_CONFIG))

    def _save_config_raw(self, data):
        os.makedirs(DATA_DIR, exist_ok=True)
        with open(RULES_FILE, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=2, ensure_ascii=False)

    def save_config(self):
        with self.lock:
            self._save_config_raw(self.config)

    @staticmethod
    def is_admin():
        try:
            return bool(ctypes.windll.shell32.IsUserAnAdmin())
        except Exception:
            return False

    def add_log(self, level, category, target, details, action="BLOCKED"):
        entry = {
            "timestamp": datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
            "level": level,         # INFO, WARNING, CRITICAL
            "category": category,   # SCAM_SITE, DOMAIN_BLOCK, IP_BLOCK, APP_BLOCK, COOKIE_GUARD, PROCESS_ALERT
            "target": target,
            "details": details,
            "action": action
        }
        with self.lock:
            self.config["logs"].insert(0, entry)
            self.config["logs"] = self.config["logs"][:200]
            if action == "BLOCKED":
                self.config["stats"]["threats_blocked"] = self.config["stats"].get("threats_blocked", 0) + 1
            self._save_config_raw(self.config)
        return entry

    # ======================== DOMAIN & URL FIREWALL ========================

    @staticmethod
    def normalize_domain(domain_or_url):
        raw = (domain_or_url or "").strip().lower()
        if not raw:
            return ""
        if "://" in raw:
            parsed = urlparse(raw)
            host = parsed.hostname or ""
        else:
            host = raw.split("/")[0].split(":")[0]
        return host.strip(".")

    def add_blocked_domain(self, domain_or_url, reason="Manual user block"):
        domain = self.normalize_domain(domain_or_url)
        if not domain:
            return False, "Domain tidak valid."
        if domain in ("localhost", "127.0.0.1"):
            return False, "Tidak dapat memblokir localhost."

        with self.lock:
            if domain not in self.config["blocked_domains"]:
                self.config["blocked_domains"].append(domain)
            if not domain.startswith("www.") and f"www.{domain}" not in self.config["blocked_domains"]:
                self.config["blocked_domains"].append(f"www.{domain}")
            self._save_config_raw(self.config)

        self.add_log("WARNING", "DOMAIN_BLOCK", domain, f"Domain ditambahkan ke daftar blokir ({reason})", "BLOCKED")
        if self.config.get("hosts_sync_enabled", True) and self.is_admin():
            self.sync_hosts_file()
        return True, f"Domain {domain} berhasil diblokir."

    def remove_blocked_domain(self, domain_or_url):
        domain = self.normalize_domain(domain_or_url)
        with self.lock:
            self.config["blocked_domains"] = [
                d for d in self.config["blocked_domains"]
                if d != domain and d != f"www.{domain}"
            ]
            self._save_config_raw(self.config)
        if self.config.get("hosts_sync_enabled", True) and self.is_admin():
            self.sync_hosts_file()
        self.add_log("INFO", "DOMAIN_UNBLOCK", domain, "Domain dihapus dari daftar blokir", "ALLOWED")
        return True, f"Domain {domain} dibuka kembali."

    def sync_hosts_file(self):
        """Syncs blocked domains to Windows C:\\Windows\\System32\\drivers\\etc\\hosts if Admin."""
        if not self.is_admin():
            return False, "Membutuhkan hak Administrator untuk menulis ke file Windows Hosts."
        try:
            content = ""
            if os.path.exists(HOSTS_PATH):
                with open(HOSTS_PATH, "r", encoding="utf-8", errors="ignore") as f:
                    content = f.read()

            # Remove previous section
            pattern = re.compile(
                re.escape(HOSTS_MARKER_START) + r".*?" + re.escape(HOSTS_MARKER_END) + r"\r?\n?",
                re.DOTALL
            )
            cleaned = pattern.sub("", content).rstrip()

            if self.config.get("firewall_enabled", True) and self.config.get("blocked_domains"):
                lines = [HOSTS_MARKER_START]
                for dom in sorted(set(self.config["blocked_domains"])):
                    if dom and dom not in ("localhost", "127.0.0.1"):
                        lines.append(f"0.0.0.0 {dom}")
                lines.append(HOSTS_MARKER_END)
                new_content = cleaned + "\n\n" + "\n".join(lines) + "\n"
            else:
                new_content = cleaned + "\n"

            with open(HOSTS_PATH, "w", encoding="utf-8") as f:
                f.write(new_content)

            # Flush DNS cache
            subprocess.run(
                ["ipconfig", "/flushdns"],
                capture_output=True,
                text=True,
                creationflags=self._startup_flags
            )
            return True, "File Windows Hosts & DNS Cache berhasil diperbarui."
        except Exception as e:
            return False, f"Gagal memperbarui Hosts: {e}"

    def evaluate_url(self, url):
        """Evaluates whether a URL is safe or should be blocked."""
        with self.lock:
            self.config["stats"]["total_requests_checked"] = self.config["stats"].get("total_requests_checked", 0) + 1
            fw_enabled = self.config.get("firewall_enabled", True)
            blocked_domains = list(self.config.get("blocked_domains", []))
            whitelisted = list(self.config.get("whitelisted_domains", []))
            patterns = list(self.config.get("suspicious_domain_patterns", []))
            self._save_config_raw(self.config)

        if not fw_enabled:
            return {"allowed": True, "risk_score": 0, "reason": "Firewall nonaktif", "category": "SAFE"}

        domain = self.normalize_domain(url)
        if not domain:
            return {"allowed": True, "risk_score": 0, "reason": "URL internal/kosong", "category": "SAFE"}

        # Check exact or subdomain match in whitelist
        for w in whitelisted:
            if domain == w or domain.endswith("." + w):
                return {"allowed": True, "risk_score": 0, "reason": "Domain tepercaya (Whitelist)", "category": "SAFE"}

        # Check blocked domains
        for b in blocked_domains:
            if domain == b or domain.endswith("." + b):
                self.add_log("CRITICAL", "DOMAIN_BLOCK", domain, f"Akses ke situs terlarang diblokir: {url}", "BLOCKED")
                return {
                    "allowed": False,
                    "risk_score": 100,
                    "reason": f"Domain '{domain}' ada di daftar hitam ShieldGuard Firewall (Scam/Berbahaya).",
                    "category": "BLOCKED_DOMAIN",
                    "domain": domain
                }

        # Check suspicious regex patterns
        url_lower = (url or "").lower()
        for pat in patterns:
            if re.search(pat, url_lower):
                self.add_log("CRITICAL", "SCAM_SITE", domain, f"Pola URL scam terdeteksi ({pat}): {url}", "BLOCKED")
                return {
                    "allowed": False,
                    "risk_score": 95,
                    "reason": f"URL cocok dengan pola situs scam/phishing berbahaya ({pat}).",
                    "category": "HEURISTIC_URL_SCAM",
                    "domain": domain
                }

        return {"allowed": True, "risk_score": 5, "reason": "Tidak ditemukan ancaman pada URL", "category": "SAFE", "domain": domain}

    def evaluate_page_content(self, url, title="", text=""):
        """Heuristic analysis of web page content for Crypto Casino Scams (like MrBeast/woukox), Phishing, and Token Stealers."""
        if not self.config.get("firewall_enabled", True) or not self.config.get("scam_protection_enabled", True):
            return {"allowed": True, "risk_score": 0, "matched": [], "reason": "Proteksi scam nonaktif"}

        domain = self.normalize_domain(url)
        combined = f"{title} {text}".lower()
        matched_keywords = []
        risk_score = 0

        for kw in self.config.get("scam_keywords", []):
            if kw.lower() in combined:
                matched_keywords.append(kw)
                risk_score += 45

        # Heuristic combinations (e.g. MrBeast/Elon + Casino/Promo Code + $2,500/USDT)
        has_celeb = any(c in combined for c in ["mrbeast", "@mrbeast", "elon musk", "kai cenat", "beast games"])
        has_crypto_bait = any(c in combined for c in ["crypto casino", "promo code", "$2,500", "2,500 usdt", "2500 usdt", "$2500.00", "withdraw the bonus instantly"])
        has_deposit_trap = any(c in combined for c in ["verification deposit", "minimum deposit to withdraw", "activate your wallet", "withdrawal success"])

        if has_celeb and has_crypto_bait:
            risk_score += 65
            matched_keywords.append("Pola-heuristik: Catut Nama Figur Publik + Bonus Kasino Kripto ($2,500/Promo)")

        if has_crypto_bait and has_deposit_trap:
            risk_score += 55
            matched_keywords.append("Pola-heuristik: Umpan Saldo Kripto Palsu + Jebakan Deposit Penarikan")

        risk_score = min(100, risk_score)

        if risk_score >= 45:
            with self.lock:
                self.config["stats"]["scam_pages_detected"] = self.config["stats"].get("scam_pages_detected", 0) + 1
                self._save_config_raw(self.config)
            self.add_log(
                "CRITICAL",
                "SCAM_SITE",
                domain or url[:60],
                f"Konten Scam Kripto/Phishing terdeteksi (Skor: {risk_score}/100): {', '.join(matched_keywords[:3])}",
                "BLOCKED"
            )
            return {
                "allowed": False,
                "risk_score": risk_score,
                "matched": matched_keywords,
                "reason": "Terdeteksi indikasi kuat Penipuan Kasino Kripto / Giveaway Palsu (modus MrBeast / $2,500 USDT).",
                "category": "CRYPTO_SCAM_CONTENT"
            }

        return {
            "allowed": True,
            "risk_score": risk_score,
            "matched": matched_keywords,
            "reason": "Konten halaman aman",
            "category": "SAFE"
        }

    # ======================== WINDOWS DEFENDER FIREWALL (IP & APP BLOCKING) ========================

    def block_ip(self, ip_address, reason="Manual IP block"):
        ip_address = (ip_address or "").strip()
        if not re.match(r"^\d{1,3}(\.\d{1,3}){3}$", ip_address):
            return False, "Format alamat IPv4 tidak valid."
        if ip_address.startswith("127.") or ip_address == "0.0.0.0":
            return False, "Tidak dapat memblokir alamat loopback lokal."

        with self.lock:
            if ip_address not in self.config["blocked_ips"]:
                self.config["blocked_ips"].append(ip_address)
                self.config["stats"]["network_ips_blocked"] = len(self.config["blocked_ips"])
                self._save_config_raw(self.config)

        rule_name = f"ShieldGuard_Block_IP_{ip_address}"
        win_fw_msg = "(Tersimpan di aturan ShieldGuard. Jalankan sebagai Admin untuk blokir level kernel Windows Firewall)"
        if self.is_admin():
            cmd_out = [
                "netsh", "advfirewall", "firewall", "add", "rule",
                f"name={rule_name}_OUT", "dir=out", "action=block", f"remoteip={ip_address}"
            ]
            cmd_in = [
                "netsh", "advfirewall", "firewall", "add", "rule",
                f"name={rule_name}_IN", "dir=in", "action=block", f"remoteip={ip_address}"
            ]
            subprocess.run(cmd_out, capture_output=True, text=True, creationflags=self._startup_flags)
            subprocess.run(cmd_in, capture_output=True, text=True, creationflags=self._startup_flags)
            win_fw_msg = "(Aturan Windows Defender Firewall IN/OUT berhasil diaktifkan)"

        self.add_log("WARNING", "IP_BLOCK", ip_address, f"IP diblokir: {reason} {win_fw_msg}", "BLOCKED")
        return True, f"IP {ip_address} berhasil diblokir {win_fw_msg}."

    def unblock_ip(self, ip_address):
        ip_address = (ip_address or "").strip()
        with self.lock:
            self.config["blocked_ips"] = [ip for ip in self.config["blocked_ips"] if ip != ip_address]
            self.config["stats"]["network_ips_blocked"] = len(self.config["blocked_ips"])
            self._save_config_raw(self.config)

        if self.is_admin():
            rule_name = f"ShieldGuard_Block_IP_{ip_address}"
            subprocess.run(
                ["netsh", "advfirewall", "firewall", "delete", "rule", f"name={rule_name}_OUT"],
                capture_output=True, text=True, creationflags=self._startup_flags
            )
            subprocess.run(
                ["netsh", "advfirewall", "firewall", "delete", "rule", f"name={rule_name}_IN"],
                capture_output=True, text=True, creationflags=self._startup_flags
            )
        self.add_log("INFO", "IP_UNBLOCK", ip_address, "Blokir IP dilepas", "ALLOWED")
        return True, f"IP {ip_address} berhasil dibuka kembali."

    def block_application(self, exe_path, reason="Blokir akses internet aplikasi"):
        exe_path = (exe_path or "").strip().strip('"')
        if not exe_path or not os.path.exists(exe_path):
            return False, f"File aplikasi tidak ditemukan: {exe_path}"

        app_name = os.path.basename(exe_path)
        rule_name = f"ShieldGuard_Block_App_{app_name}"

        with self.lock:
            if exe_path not in self.config["blocked_apps"]:
                self.config["blocked_apps"].append(exe_path)
                self._save_config_raw(self.config)

        if self.is_admin():
            cmd = [
                "netsh", "advfirewall", "firewall", "add", "rule",
                f"name={rule_name}", "dir=out", "action=block", f"program={exe_path}", "enable=yes"
            ]
            res = subprocess.run(cmd, capture_output=True, text=True, creationflags=self._startup_flags)
            if res.returncode == 0:
                self.add_log("WARNING", "APP_BLOCK", app_name, f"Akses jaringan diblokir untuk {exe_path}", "BLOCKED")
                return True, f"Akses internet untuk '{app_name}' berhasil diblokir di Windows Firewall."
            return False, f"Gagal membuat aturan Windows Firewall: {res.stderr or res.stdout}"
        else:
            self.add_log("WARNING", "APP_BLOCK", app_name, f"Ditambahkan ke daftar blokir aplikasi (Butuh Admin untuk netsh): {exe_path}", "BLOCKED")
            return True, f"'{app_name}' masuk daftar blokir. Jalankan Start-Firewall-Admin.bat untuk menerapkan ke Windows Firewall."

    def unblock_application(self, exe_path):
        exe_path = (exe_path or "").strip().strip('"')
        app_name = os.path.basename(exe_path)
        rule_name = f"ShieldGuard_Block_App_{app_name}"

        with self.lock:
            self.config["blocked_apps"] = [p for p in self.config["blocked_apps"] if p.lower() != exe_path.lower()]
            self._save_config_raw(self.config)

        if self.is_admin():
            subprocess.run(
                ["netsh", "advfirewall", "firewall", "delete", "rule", f"name={rule_name}"],
                capture_output=True, text=True, creationflags=self._startup_flags
            )
        self.add_log("INFO", "APP_UNBLOCK", app_name, f"Blokir aplikasi dilepas: {exe_path}", "ALLOWED")
        return True, f"Akses internet untuk '{app_name}' dibuka kembali."

    # ======================== REAL-TIME NETWORK & PROCESS MONITOR ========================

    def get_active_connections(self, limit=120):
        """Returns active TCP connections with PID, Process Name, Remote IP, Port, and Risk Status."""
        pid_map = {}
        try:
            proc_out = subprocess.run(
                ["tasklist", "/FO", "CSV", "/NH"],
                capture_output=True, text=True, creationflags=self._startup_flags, timeout=4
            ).stdout
            for line in proc_out.splitlines():
                parts = [p.strip('"') for p in line.split('","')]
                if len(parts) >= 2 and parts[1].isdigit():
                    pid_map[parts[1]] = parts[0]
        except Exception:
            pass

        connections = []
        suspicious_procs = {
            "powershell.exe", "pwsh.exe", "cmd.exe", "wscript.exe",
            "cscript.exe", "mshta.exe", "rundll32.exe", "regsvr32.exe", "certutil.exe"
        }
        blocked_ips = set(self.config.get("blocked_ips", []))

        try:
            net_out = subprocess.run(
                ["netstat", "-ano", "-p", "tcp"],
                capture_output=True, text=True, creationflags=self._startup_flags, timeout=4
            ).stdout

            for line in net_out.splitlines():
                line = line.strip()
                if not line.startswith("TCP"):
                    continue
                parts = line.split()
                if len(parts) < 5:
                    continue
                _, local_addr, remote_addr, state, pid = parts[0], parts[1], parts[2], parts[3], parts[4]
                if remote_addr.startswith("0.0.0.0") or remote_addr.startswith("127.0.0.1") or remote_addr.startswith("[::"):
                    continue

                remote_ip = remote_addr.rsplit(":", 1)[0]
                remote_port = remote_addr.rsplit(":", 1)[1] if ":" in remote_addr else ""
                proc_name = pid_map.get(pid, f"PID:{pid}")
                proc_lower = proc_name.lower()

                risk = "AMAN"
                reason = "Koneksi standar"
                if remote_ip in blocked_ips:
                    risk = "BAHAYA"
                    reason = "Terhubung ke IP dalam daftar blokir!"
                elif proc_lower in suspicious_procs:
                    risk = "WASPADA"
                    reason = f"Proses sistem/script ({proc_name}) melakukan koneksi keluar (indikasi backdoor/stealer)"
                elif remote_port in ("4444", "1337", "6667", "3389", "5900"):
                    risk = "WASPADA"
                    reason = f"Port tujuan tidak lazim ({remote_port})"

                connections.append({
                    "pid": pid,
                    "process": proc_name,
                    "local": local_addr,
                    "remote_ip": remote_ip,
                    "remote_port": remote_port,
                    "state": state,
                    "risk": risk,
                    "reason": reason
                })
                if len(connections) >= limit:
                    break
        except Exception:
            pass

        # Sort so BAHAYA and WASPADA appear at the top
        priority = {"BAHAYA": 0, "WASPADA": 1, "AMAN": 2}
        connections.sort(key=lambda c: (priority.get(c["risk"], 3), c["process"].lower()))
        return connections

    def scan_for_infostealers(self):
        """Scans running processes for suspicious executables running from Temp/AppData or script engines."""
        findings = []
        try:
            ps_cmd = (
                "Get-CimInstance Win32_Process | "
                "Select-Object ProcessId, Name, ExecutablePath, CommandLine | "
                "ConvertTo-Json -Compress"
            )
            res = subprocess.run(
                ["powershell", "-NoProfile", "-Command", ps_cmd],
                capture_output=True, text=True, creationflags=self._startup_flags, timeout=8
            )
            if not res.stdout.strip():
                return findings

            procs = json.loads(res.stdout)
            if isinstance(procs, dict):
                procs = [procs]

            trusted_temp_keywords = ["antigravity", "microsoft", "google", "windows", "vscode", "defender"]
            for p in procs:
                pid = p.get("ProcessId")
                name = (p.get("Name") or "")
                exe_path = (p.get("ExecutablePath") or "")
                cmdline = (p.get("CommandLine") or "")
                path_lower = exe_path.lower()
                cmd_lower = cmdline.lower()

                # 1. Check if process is attempting to access Chrome/Edge Login Data or Cookies
                if any(k in cmd_lower for k in ["login data", "network\\cookies", "local state", "webhook", "discord.com/api/webhooks"]):
                    findings.append({
                        "pid": pid,
                        "name": name,
                        "path": exe_path or cmdline[:80],
                        "severity": "CRITICAL",
                        "description": "Proses mencoba mengakses Cookie/Password Browser atau mengirim ke Webhook!"
                    })
                    continue

                # 2. Check unsigned/unknown executables running directly from Temp folder
                if ("\\appdata\\local\\temp\\" in path_lower) and not any(t in path_lower for t in trusted_temp_keywords):
                    findings.append({
                        "pid": pid,
                        "name": name,
                        "path": exe_path,
                        "severity": "WARNING",
                        "description": "Aplikasi berjalan dari folder Temp (sering digunakan oleh malware Infostealer)."
                    })

        except Exception:
            pass

        if findings:
            for f in findings:
                self.add_log(
                    f["severity"],
                    "PROCESS_ALERT",
                    f"{f['name']} (PID {f['pid']})",
                    f["description"],
                    "ALERT"
                )
        return findings
