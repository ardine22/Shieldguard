import datetime
import hashlib
import json
import os
import ssl
import sys
import threading
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, unquote, urlparse

HTTP_PORT = int(os.environ.get("HTTP_PORT", 80))
HTTPS_PORT = int(os.environ.get("HTTPS_PORT", 443))
SSL_CERT = os.environ.get("SSL_CERT", "/etc/ssl/shieldguard.crt")
SSL_KEY = os.environ.get("SSL_KEY", "/etc/ssl/shieldguard.key")
ADMIN_SECRET = os.environ.get("ADMIN_SECRET", "SG_ADMIN_2026")

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
PUBLIC_DIR = os.path.join(BASE_DIR, "public")
DOWNLOADS_DIR = os.path.join(PUBLIC_DIR, "downloads")
DATA_DIR = os.path.join(BASE_DIR, "data")
LICENSES_FILE = os.path.join(DATA_DIR, "cloud_licenses.json")

os.makedirs(DATA_DIR, exist_ok=True)
os.makedirs(DOWNLOADS_DIR, exist_ok=True)

MIME_TYPES = {
    ".html": "text/html; charset=utf-8",
    ".css": "text/css; charset=utf-8",
    ".js": "application/javascript; charset=utf-8",
    ".json": "application/json; charset=utf-8",
    ".png": "image/png",
    ".jpg": "image/jpeg",
    ".jpeg": "image/jpeg",
    ".ico": "image/x-icon",
    ".svg": "image/svg+xml",
    ".exe": "application/octet-stream",
    ".zip": "application/zip",
    ".txt": "text/plain; charset=utf-8"
}

license_lock = threading.Lock()


def load_cloud_licenses():
    if not os.path.exists(LICENSES_FILE):
        return {}
    try:
        with open(LICENSES_FILE, "r", encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return {}


def save_cloud_licenses(data):
    os.makedirs(DATA_DIR, exist_ok=True)
    with open(LICENSES_FILE, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)


def verify_key_format(key):
    if not key or not isinstance(key, str):
        return False
    clean = key.strip().upper()
    parts = clean.split("-")
    if len(parts) != 4 or parts[0] != "SGPRO":
        return False
    if len(parts[1]) != 4 or len(parts[2]) != 4 or len(parts[3]) != 4:
        return False
    seg1 = parts[1]
    h = hashlib.sha256(("SG_SECRET_2026:" + seg1).encode("utf-8")).hexdigest().upper()
    return parts[2] == h[:4] and parts[3] == h[4:8]


class HTTPSRedirectHandler(SimpleHTTPRequestHandler):
    def log_message(self, format, *args):
        pass

    def do_GET(self):
        host = self.headers.get("Host", "").split(":")[0] or "70.151.150.190"
        target_url = f"https://{host}{self.path}"
        self.send_response(301)
        self.send_header("Location", target_url)
        self.end_headers()


class ShieldGuardDeployHandler(SimpleHTTPRequestHandler):
    def log_message(self, format, *args):
        client_ip = self.client_address[0]
        print(f"[{client_ip}] {format % args}")

    def send_json(self, payload, status=200):
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.end_headers()
        self.wfile.write(body)

    def read_json_body(self):
        length = int(self.headers.get("Content-Length", "0"))
        if length <= 0:
            return {}
        raw = self.rfile.read(length).decode("utf-8", errors="ignore")
        try:
            return json.loads(raw)
        except Exception:
            return {}

    def do_OPTIONS(self):
        self.send_json({"ok": True})

    def send_file_with_download(self, file_path, download_filename, mime_type):
        if not os.path.exists(file_path):
            self.send_error(404, "File tidak ditemukan di server.")
            return

        file_size = os.path.getsize(file_path)
        self.send_response(200)
        self.send_header("Content-Type", mime_type)
        self.send_header("Content-Length", str(file_size))
        self.send_header("Content-Disposition", f'attachment; filename="{download_filename}"')
        self.send_header("Cache-Control", "public, max-age=3600")
        self.send_header("Access-Control-Allow-Origin", "*")
        self.end_headers()

        with open(file_path, "rb") as f:
            while chunk := f.read(65536):
                self.wfile.write(chunk)

    def do_GET(self):
        parsed = urlparse(self.path)
        path = unquote(parsed.path)

        # 1. Download Routes
        if path in ("/download/setup", "/download/setup.exe"):
            f = os.path.join(DOWNLOADS_DIR, "ShieldGuard-Setup-v1.0.exe")
            self.send_file_with_download(f, "ShieldGuard-Setup-v1.0.exe", "application/octet-stream")
            return

        if path in ("/download/extension", "/download/extension.zip"):
            f = os.path.join(DOWNLOADS_DIR, "ShieldGuard-Extension-Chrome-Edge.zip")
            self.send_file_with_download(f, "ShieldGuard-Extension-Chrome-Edge.zip", "application/zip")
            return

        if path in ("/download/bundle", "/download/bundle.zip"):
            f = os.path.join(DOWNLOADS_DIR, "ShieldGuard-Paket-Lengkap-v1.0.zip")
            self.send_file_with_download(f, "ShieldGuard-Paket-Lengkap-v1.0.zip", "application/zip")
            return

        # 2. Admin View of Registered Licenses & Bound Devices
        if path == "/api/license/admin-view":
            query = parse_qs(parsed.query)
            secret = query.get("secret", [""])[0]
            if secret != ADMIN_SECRET:
                self.send_json({"ok": False, "message": "Akses ditolak: Secret salah."}, 403)
                return
            with license_lock:
                db = load_cloud_licenses()
            self.send_json({"ok": True, "total_licenses": len(db), "licenses": db})
            return

        # 3. Admin Dashboard Route
        if path in ("/admin", "/admin/", "/admin.html"):
            admin_path = os.path.join(PUBLIC_DIR, "admin.html")
            if os.path.exists(admin_path):
                with open(admin_path, "rb") as f:
                    content = f.read()
                self.send_response(200)
                self.send_header("Content-Type", "text/html; charset=utf-8")
                self.send_header("Content-Length", str(len(content)))
                self.send_header("Cache-Control", "no-cache, no-store, must-revalidate")
                self.send_header("Pragma", "no-cache")
                self.send_header("Expires", "0")
                self.end_headers()
                self.wfile.write(content)
                return

        # 4. Main Web Landing Page
        if path in ("/", "/index.html", "/store"):
            index_path = os.path.join(PUBLIC_DIR, "index.html")
            if os.path.exists(index_path):
                with open(index_path, "rb") as f:
                    content = f.read()
                self.send_response(200)
                self.send_header("Content-Type", "text/html; charset=utf-8")
                self.send_header("Content-Length", str(len(content)))
                self.send_header("Cache-Control", "no-cache, no-store, must-revalidate")
                self.send_header("Pragma", "no-cache")
                self.send_header("Expires", "0")
                self.end_headers()
                self.wfile.write(content)
                return

        # 4. Static Files
        local_path = os.path.join(PUBLIC_DIR, path.lstrip("/"))
        if os.path.isfile(local_path):
            ext = os.path.splitext(local_path)[1].lower()
            mime = MIME_TYPES.get(ext, "application/octet-stream")
            with open(local_path, "rb") as f:
                content = f.read()
            self.send_response(200)
            self.send_header("Content-Type", mime)
            self.send_header("Content-Length", str(len(content)))
            self.end_headers()
            self.wfile.write(content)
            return

        self.send_error(404, "Halaman atau file tidak ditemukan.")

    def do_POST(self):
        parsed = urlparse(self.path)
        path = unquote(parsed.path)
        body = self.read_json_body()

        # CLOUD 1-DEVICE HWID LICENSE ACTIVATION
        if path == "/api/license/activate":
            key = (body.get("key") or "").strip().upper()
            hwid = (body.get("hwid") or "").strip()
            buyer = (body.get("buyer") or body.get("name") or "Pengguna Pro").strip()
            device_name = (body.get("device_name") or "Windows Device").strip()
            client_ip = self.client_address[0]

            if not verify_key_format(key):
                self.send_json({
                    "ok": False,
                    "status": "INVALID_FORMAT",
                    "message": "❌ Format Serial Key tidak valid! Pastikan format SGPRO-XXXX-XXXX-XXXX."
                })
                return

            if not hwid:
                self.send_json({
                    "ok": False,
                    "status": "MISSING_HWID",
                    "message": "❌ Hardware ID perangkat tidak terdeteksi."
                })
                return

            with license_lock:
                db = load_cloud_licenses()
                existing = db.get(key)

                if existing:
                    bound_hwid = existing.get("bound_hwid")
                    if not bound_hwid:
                        # KEY WAS PRE-GENERATED BY ADMIN OR WAS RESET
                        now_str = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
                        existing["bound_hwid"] = hwid
                        existing["device_name"] = device_name
                        existing["activated_at"] = now_str
                        existing["client_ip"] = client_ip
                        existing["status"] = "ACTIVE"
                        if buyer and buyer != "Pengguna Pro" and not existing.get("buyer"):
                            existing["buyer"] = buyer
                        save_cloud_licenses(db)
                        print(f"[LICENSE SERVER] 🔒 Key {key} BOUND to HWID: {hwid} ({device_name}) a/n {existing.get('buyer', buyer)}")
                        self.send_json({
                            "ok": True,
                            "status": "ACTIVATED",
                            "bound_hwid": hwid,
                            "buyer": existing.get("buyer", buyer),
                            "message": f"🎉 Aktivasi Berhasil! Lisensi ShieldGuard Pro terikat resmi ke perangkat ini ({device_name})."
                        })
                        return
                    elif bound_hwid == hwid:
                        # Re-activation on the SAME device is allowed
                        self.send_json({
                            "ok": True,
                            "status": "ALREADY_ACTIVE",
                            "bound_hwid": hwid,
                            "buyer": existing.get("buyer", buyer),
                            "message": f"✅ Lisensi Pro aktif pada perangkat ini ({device_name})."
                        })
                        return
                    else:
                        # DIFFERENT DEVICE ATTEMPT! REJECT!
                        prev_device = existing.get("device_name", "Komputer Lain")
                        prev_date = existing.get("activated_at", "Sebelumnya")
                        self.send_json({
                            "ok": False,
                            "status": "DEVICE_MISMATCH",
                            "message": f"❌ GAGAL: Serial Key ini sudah terikat pada perangkat lain ({prev_device} terdaftar {prev_date})! 1 Lisensi ShieldGuard Pro hanya berlaku untuk 1 komputer."
                        })
                        return
                else:
                    # FIRST ACTIVATION: BIND KEY TO THIS DEVICE PERMANENTLY!
                    now_str = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
                    db[key] = {
                        "key": key,
                        "bound_hwid": hwid,
                        "buyer": buyer,
                        "device_name": device_name,
                        "activated_at": now_str,
                        "client_ip": client_ip,
                        "status": "ACTIVE"
                    }
                    save_cloud_licenses(db)

                    print(f"[LICENSE SERVER] 🔒 Key {key} successfully BOUND to HWID: {hwid} ({device_name}) a/n {buyer}")
                    self.send_json({
                        "ok": True,
                        "status": "ACTIVATED",
                        "bound_hwid": hwid,
                        "buyer": buyer,
                        "message": f"🎉 Aktivasi Berhasil! Lisensi ShieldGuard Pro terikat resmi ke perangkat ini ({device_name})."
                    })
                    return

        # VERIFY LICENSE STATUS (Periodic Check)
        if path == "/api/license/verify":
            key = (body.get("key") or "").strip().upper()
            hwid = (body.get("hwid") or "").strip()
            with license_lock:
                db = load_cloud_licenses()
                rec = db.get(key)
                if rec and rec.get("bound_hwid") == hwid:
                    self.send_json({"ok": True, "valid": True, "buyer": rec.get("buyer")})
                    return
            self.send_json({"ok": False, "valid": False, "message": "Lisensi tidak valid untuk perangkat ini."})
            return

        # ADMIN RESET (Allows seller to unbind key if customer legitimately replaced their laptop)
        if path == "/api/license/admin-reset":
            secret = (body.get("secret") or "").strip()
            key = (body.get("key") or "").strip().upper()
            if secret != ADMIN_SECRET:
                self.send_json({"ok": False, "message": "Secret salah."}, 403)
                return
            with license_lock:
                db = load_cloud_licenses()
                if key in db:
                    db[key]["bound_hwid"] = None
                    db[key]["device_name"] = None
                    db[key]["status"] = "UNBOUND"
                    save_cloud_licenses(db)
                    self.send_json({"ok": True, "message": f"Kunci {key} berhasil di-reset. Sekarang bisa diaktifkan di device baru."})
                    return
            self.send_json({"ok": False, "message": "Serial Key tidak ditemukan di database."})
            return

        # ADMIN DELETE (Deletes license permanently)
        if path == "/api/license/admin-delete":
            secret = (body.get("secret") or "").strip()
            key = (body.get("key") or "").strip().upper()
            if secret != ADMIN_SECRET:
                self.send_json({"ok": False, "message": "Secret salah."}, 403)
                return
            with license_lock:
                db = load_cloud_licenses()
                if key in db:
                    del db[key]
                    save_cloud_licenses(db)
                    self.send_json({"ok": True, "message": f"Lisensi {key} berhasil dihapus dari cloud."})
                    return
            self.send_json({"ok": False, "message": "Serial Key tidak ditemukan di database."})
            return

        # ADMIN CREATE (Generate new valid key and save to database)
        if path == "/api/license/admin-create":
            secret = (body.get("secret") or "").strip()
            if secret != ADMIN_SECRET:
                self.send_json({"ok": False, "message": "Secret salah."}, 403)
                return
            buyer = (body.get("buyer") or "Member Baru").strip()
            phone = (body.get("phone") or "-").strip()
            raw_prefix = (body.get("prefix") or "VIP1").strip().upper()
            prefix = "".join(c for c in raw_prefix if c.isalnum())[:4]
            if len(prefix) < 4:
                prefix = (prefix + "SG01")[:4]

            # SGPRO-[seg1]-[hash1]-[hash2]
            seg1 = prefix
            h = hashlib.sha256(("SG_SECRET_2026:" + seg1).encode("utf-8")).hexdigest().upper()
            key = f"SGPRO-{seg1}-{h[:4]}-{h[4:8]}"

            now_str = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
            with license_lock:
                db = load_cloud_licenses()
                db[key] = {
                    "key": key,
                    "bound_hwid": None,
                    "buyer": buyer,
                    "phone": phone,
                    "device_name": None,
                    "created_at": now_str,
                    "status": "UNUSED"
                }
                save_cloud_licenses(db)

            self.send_json({
                "ok": True,
                "key": key,
                "buyer": buyer,
                "message": f"Serial Key {key} berhasil dibuat!"
            })
            return

        self.send_json({"error": "Endpoint tidak ditemukan."}, 404)


def run_http_redirect():
    try:
        http_server = ThreadingHTTPServer(("0.0.0.0", HTTP_PORT), HTTPSRedirectHandler)
        print(f"  HTTP Redirect running on port {HTTP_PORT} -> HTTPS")
        http_server.serve_forever()
    except Exception as e:
        print(f"HTTP Redirect Server info: {e}")


def main():
    has_ssl = os.path.exists(SSL_CERT) and os.path.exists(SSL_KEY)

    print("=" * 65)
    print("  ShieldGuard Pro Web & Cloud License Server (1-Device Lock)")
    print(f"  Cloud License API: /api/license/activate")
    if has_ssl:
        print(f"  🔒 HTTPS AKTIF pada port: https://0.0.0.0:{HTTPS_PORT}")
    else:
        print(f"  HTTP AKTIF pada port: http://0.0.0.0:{HTTP_PORT}")
    print("=" * 65)

    if has_ssl:
        t = threading.Thread(target=run_http_redirect, daemon=True)
        t.start()

        https_server = ThreadingHTTPServer(("0.0.0.0", HTTPS_PORT), ShieldGuardDeployHandler)
        ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        ctx.load_cert_chain(certfile=SSL_CERT, keyfile=SSL_KEY)
        https_server.socket = ctx.wrap_socket(https_server.socket, server_side=True)
        try:
            https_server.serve_forever()
        except KeyboardInterrupt:
            sys.exit(0)
    else:
        http_server = ThreadingHTTPServer(("0.0.0.0", HTTP_PORT), ShieldGuardDeployHandler)
        try:
            http_server.serve_forever()
        except KeyboardInterrupt:
            sys.exit(0)


if __name__ == "__main__":
    main()
