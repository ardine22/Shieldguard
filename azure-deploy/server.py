import os
import sys
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import unquote, urlparse

PORT = int(os.environ.get("PORT", 80))
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
                self.send_header("Cache-Control", "public, max-age=300")
                self.end_headers()
                self.wfile.write(content)
                return

        # 3. Static Files (assets, icons, etc.)
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


def main():
    print("=" * 60)
    print("  ShieldGuard Pro Web Server (Azure VM Cloud Deploy)")
    print(f"  Listening on: http://0.0.0.0:{PORT}")
    print("=" * 60)
    server = ThreadingHTTPServer(("0.0.0.0", PORT), ShieldGuardDeployHandler)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nServer dihentikan.")
        sys.exit(0)


if __name__ == "__main__":
    main()

