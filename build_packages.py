import os
import subprocess
import zipfile

root = r"c:\Firewall"
out_dir = os.path.join(root, "produk-siap-jual")
src_cs = os.path.join(root, "src-csharp")
ext_dir = os.path.join(root, "browser-extension")
csc = r"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
ico = os.path.join(root, "assets", "shieldguard.ico")

# 1. Compile ShieldGuardFirewall.exe
exe_path = os.path.join(out_dir, "ShieldGuardFirewall.exe")
cmd1 = [
    csc, "/target:winexe", f"/out:{exe_path}",
    f"/win32icon:{ico}", "/r:System.Web.Extensions.dll",
    os.path.join(src_cs, "ShieldGuardFirewall.cs")
]
res1 = subprocess.run(cmd1, capture_output=True, text=True)
print("1. Compile ShieldGuardFirewall:", res1.returncode)
if res1.returncode != 0:
    print(res1.stderr)

# 2. Compile KeygenAdmin.exe
keygen_path = os.path.join(out_dir, "ShieldGuard-Keygen-Admin.exe")
cmd2 = [
    csc, "/target:winexe", f"/out:{keygen_path}",
    f"/win32icon:{ico}", os.path.join(src_cs, "KeygenAdmin.cs")
]
res2 = subprocess.run(cmd2, capture_output=True, text=True)
print("2. Compile KeygenAdmin:", res2.returncode)

# 3. Create updated Extension ZIP
ext_zip_path = os.path.join(out_dir, "ShieldGuard-Extension-Chrome-Edge.zip")
with zipfile.ZipFile(ext_zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
    for dirpath, _, filenames in os.walk(ext_dir):
        for f in filenames:
            full = os.path.join(dirpath, f)
            rel = os.path.relpath(full, ext_dir)
            zf.write(full, rel)
print("3. Created extension zip:", os.path.getsize(ext_zip_path), "bytes")

# 4. Create payload.zip for installer
payload_zip = os.path.join(src_cs, "payload.zip")
with zipfile.ZipFile(payload_zip, "w", zipfile.ZIP_DEFLATED) as zf:
    zf.write(exe_path, "ShieldGuardFirewall.exe")
    zf.write(os.path.join(root, "pc-firewall", "dashboard.html"), "dashboard.html")
    zf.write(os.path.join(root, "website-penjualan", "index.html"), "store.html")
    zf.write(os.path.join(out_dir, "Panduan-Pembeli.txt"), "Panduan-Pembeli.txt")
    for dirpath, _, filenames in os.walk(ext_dir):
        for f in filenames:
            full = os.path.join(dirpath, f)
            rel = os.path.join("browser-extension", os.path.relpath(full, ext_dir))
            zf.write(full, rel)
print("4. Created payload zip:", os.path.getsize(payload_zip), "bytes")

# 5. Compile ShieldGuard-Setup-v1.0.exe
setup_exe = os.path.join(out_dir, "ShieldGuard-Setup-v1.0.exe")
cmd3 = [
    csc, "/target:winexe", f"/out:{setup_exe}", f"/win32icon:{ico}",
    "/r:System.IO.Compression.dll", "/r:System.IO.Compression.FileSystem.dll",
    f"/resource:{payload_zip},ShieldGuardPayload.zip",
    os.path.join(src_cs, "SetupInstaller.cs")
]
res3 = subprocess.run(cmd3, capture_output=True, text=True)
print("5. Compile SetupInstaller:", res3.returncode)

# 6. Create complete commercial bundle ZIP
bundle_zip = os.path.join(out_dir, "ShieldGuard-Paket-Lengkap-v1.0.zip")
with zipfile.ZipFile(bundle_zip, "w", zipfile.ZIP_DEFLATED) as zf:
    zf.write(setup_exe, "ShieldGuard-Setup-v1.0.exe")
    zf.write(exe_path, "ShieldGuardFirewall.exe")
    zf.write(ext_zip_path, "ShieldGuard-Extension-Chrome-Edge.zip")
    zf.write(os.path.join(out_dir, "Panduan-Pembeli.txt"), "Panduan-Pembeli.txt")
    for dirpath, _, filenames in os.walk(ext_dir):
        for f in filenames:
            full = os.path.join(dirpath, f)
            rel = os.path.join("browser-extension", os.path.relpath(full, ext_dir))
            zf.write(full, rel)
print("6. Created full bundle zip:", os.path.getsize(bundle_zip), "bytes")

