import os
import ssl
import sys
import threading
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import unquote, urlparse

HTTP_PORT = int(os.environ.get("HTTP_PORT", 80))
HTTPS_PORT = int(os.environ.get("HTTPS_PORT", 443))
SSL_CERT = os.environ.get("SSL_CERT", "/etc/ssl/shieldguard.crt")
SSL_KEY = os.environ.get("SSL_KEY", "/etc/ssl/shieldguard.key")

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
PUBLIC_DIR = os.path.join(BASE_DIR, "public")
DOWNLOADS_DIR = os.path.join(PUBLIC_DIR, "downloads")

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

        # 2. Main Web Landing Page
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

        # 3. Static Files
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
    print("  ShieldGuard Pro Web Server (Azure VM Cloud Deploy)")
    if has_ssl:
        print(f"  🔒 HTTPS AKTIF pada port: https://0.0.0.0:{HTTPS_PORT}")
        print(f"  Sertifikat SSL: {SSL_CERT}")
    else:
        print(f"  HTTP AKTIF pada port: http://0.0.0.0:{HTTP_PORT}")
    print("=" * 65)

    if has_ssl:
        # Start HTTP -> HTTPS redirect in background thread
        t = threading.Thread(target=run_http_redirect, daemon=True)
        t.start()

        # Start HTTPS server
        https_server = ThreadingHTTPServer(("0.0.0.0", HTTPS_PORT), ShieldGuardDeployHandler)
        ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        ctx.load_cert_chain(certfile=SSL_CERT, keyfile=SSL_KEY)
        https_server.socket = ctx.wrap_socket(https_server.socket, server_side=True)
        try:
            https_server.serve_forever()
        except KeyboardInterrupt:
            sys.exit(0)
    else:
        # Fallback HTTP
        http_server = ThreadingHTTPServer(("0.0.0.0", HTTP_PORT), ShieldGuardDeployHandler)
        try:
            http_server.serve_forever()
        except KeyboardInterrupt:
            sys.exit(0)


if __name__ == "__main__":
    main()
