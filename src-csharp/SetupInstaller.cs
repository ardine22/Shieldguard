using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;

namespace ShieldGuardSetup
{
    public class SetupForm : Form
    {
        private TextBox txtInstallDir;
        private CheckBox chkDesktopShortcut;
        private CheckBox chkLaunchNow;
        private ProgressBar progressBar;
        private Label lblStatus;
        private Button btnInstall;

        public SetupForm()
        {
            this.Text = "ShieldGuard Pro Firewall v1.0 — Setup Installer";
            this.Size = new Size(580, 420);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(11, 15, 25);
            this.ForeColor = Color.FromArgb(243, 244, 246);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;

            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch {}

            BuildUI();
        }

        private void BuildUI()
        {
            Panel header = new Panel
            {
                Dock = DockStyle.Top, Height = 82,
                BackColor = Color.FromArgb(17, 24, 39), Padding = new Padding(20)
            };
            Label lblTitle = new Label
            {
                Text = "🛡️ Instalasi ShieldGuard Pro Firewall & Anti-Scam",
                Font = new Font("Segoe UI", 13, FontStyle.Bold),
                ForeColor = Color.White, AutoSize = true, Location = new Point(20, 14)
            };
            Label lblSub = new Label
            {
                Text = "Paket Aplikasi Desktop Firewall Windows (.exe) + Ekstensi Google Chrome & Edge",
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(52, 211, 153), AutoSize = true, Location = new Point(20, 44)
            };
            header.Controls.Add(lblTitle);
            header.Controls.Add(lblSub);
            this.Controls.Add(header);

            string defaultDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ShieldGuard Firewall"
            );

            Label lDir = new Label
            {
                Text = "Lokasi Folder Instalasi:",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(24, 102), AutoSize = true
            };
            this.Controls.Add(lDir);

            txtInstallDir = new TextBox
            {
                Text = defaultDir, Location = new Point(24, 128), Size = new Size(410, 28),
                BackColor = Color.FromArgb(17, 24, 39), ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f), BorderStyle = BorderStyle.FixedSingle
            };
            this.Controls.Add(txtInstallDir);

            Button btnBrowse = new Button
            {
                Text = "Pilih Folder...", Location = new Point(442, 126), Size = new Size(100, 28),
                BackColor = Color.FromArgb(55, 65, 81), ForeColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            btnBrowse.FlatAppearance.BorderSize = 0;
            btnBrowse.Click += delegate {
                FolderBrowserDialog fbd = new FolderBrowserDialog { SelectedPath = txtInstallDir.Text };
                if (fbd.ShowDialog() == DialogResult.OK) txtInstallDir.Text = fbd.SelectedPath;
            };
            this.Controls.Add(btnBrowse);

            chkDesktopShortcut = new CheckBox
            {
                Text = "Buat Shortcut 'ShieldGuard Firewall' di Layar Desktop Windows",
                Checked = true, Location = new Point(24, 174), AutoSize = true,
                Font = new Font("Segoe UI", 9.5f)
            };
            this.Controls.Add(chkDesktopShortcut);

            chkLaunchNow = new CheckBox
            {
                Text = "Langsung Jalankan ShieldGuard Firewall Setelah Instalasi Selesai",
                Checked = true, Location = new Point(24, 204), AutoSize = true,
                Font = new Font("Segoe UI", 9.5f)
            };
            this.Controls.Add(chkLaunchNow);

            progressBar = new ProgressBar
            {
                Location = new Point(24, 248), Size = new Size(518, 22), Minimum = 0, Maximum = 100, Value = 0
            };
            this.Controls.Add(progressBar);

            lblStatus = new Label
            {
                Text = "Siap menginstal ShieldGuard Pro Firewall ke PC Anda.",
                Location = new Point(24, 276), AutoSize = true,
                Font = new Font("Segoe UI", 9), ForeColor = Color.FromArgb(156, 163, 175)
            };
            this.Controls.Add(lblStatus);

            btnInstall = new Button
            {
                Text = "🚀 INSTAL SEKARANG",
                Location = new Point(24, 314), Size = new Size(518, 44),
                BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnInstall.FlatAppearance.BorderSize = 0;
            btnInstall.Click += OnInstallClick;
            this.Controls.Add(btnInstall);
        }

        private void OnInstallClick(object sender, EventArgs e)
        {
            string targetDir = txtInstallDir.Text.Trim();
            if (string.IsNullOrEmpty(targetDir)) return;

            try
            {
                btnInstall.Enabled = false;
                lblStatus.Text = "Menyiapkan direktori instalasi...";
                progressBar.Value = 20;
                Application.DoEvents();

                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

                string tempZip = Path.Combine(Path.GetTempPath(), "sg_payload_" + Guid.NewGuid().ToString("N") + ".zip");
                Stream resStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ShieldGuardPayload.zip");
                if (resStream == null)
                    resStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip");
                if (resStream == null)
                {
                    foreach (string resName in Assembly.GetExecutingAssembly().GetManifestResourceNames())
                    {
                        if (resName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            resStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resName);
                            break;
                        }
                    }
                }

                if (resStream == null)
                    throw new Exception("Paket instalasi internal tidak ditemukan.");

                using (resStream)
                using (FileStream fs = new FileStream(tempZip, FileMode.Create, FileAccess.Write))
                {
                    resStream.CopyTo(fs);
                }

                lblStatus.Text = "Mengekstrak aplikasi Firewall & Ekstensi Chrome/Edge...";
                progressBar.Value = 60;
                Application.DoEvents();

                using (ZipArchive archive = ZipFile.OpenRead(tempZip))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string fullPath = Path.Combine(targetDir, entry.FullName.Replace('/', '\\'));
                        string dirName = Path.GetDirectoryName(fullPath);
                        if (!string.IsNullOrEmpty(dirName) && !Directory.Exists(dirName))
                            Directory.CreateDirectory(dirName);
                        if (!string.IsNullOrEmpty(entry.Name))
                            entry.ExtractToFile(fullPath, true);
                    }
                }
                try { File.Delete(tempZip); } catch {}

                string exePath = Path.Combine(targetDir, "ShieldGuardFirewall.exe");

                if (chkDesktopShortcut.Checked && File.Exists(exePath))
                {
                    lblStatus.Text = "Membuat shortcut di Desktop...";
                    progressBar.Value = 85;
                    Application.DoEvents();
                    CreateDesktopShortcut(exePath, targetDir);
                }

                progressBar.Value = 100;
                lblStatus.Text = "✅ Instalasi selesai dengan sukses!";
                btnInstall.Text = "✅ TERINSTAL";
                btnInstall.BackColor = Color.FromArgb(16, 185, 129);

                MessageBox.Show(
                    "🎉 ShieldGuard Pro Firewall & Ekstensi Browser berhasil diinstal di:\n" + targetDir + "\n\n" +
                    "Folder Ekstensi Chrome & Edge tersedia di:\n" + Path.Combine(targetDir, "browser-extension"),
                    "Instalasi Berhasil",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );

                if (chkLaunchNow.Checked && File.Exists(exePath))
                {
                    Process.Start(new ProcessStartInfo(exePath) { WorkingDirectory = targetDir });
                }
                this.Close();
            }
            catch (Exception ex)
            {
                btnInstall.Enabled = true;
                MessageBox.Show("Gagal menginstal: " + ex.Message, "Kesalahan Instalasi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void CreateDesktopShortcut(string exePath, string workDir)
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string lnkPath = Path.Combine(desktop, "ShieldGuard Firewall.lnk");
                string ps = string.Format(
                    "$s=(New-Object -COM WScript.Shell).CreateShortcut('{0}');$s.TargetPath='{1}';$s.WorkingDirectory='{2}';$s.Save()",
                    lnkPath.Replace("'", "''"), exePath.Replace("'", "''"), workDir.Replace("'", "''")
                );
                ProcessStartInfo psi = new ProcessStartInfo("powershell", "-NoProfile -Command \"" + ps + "\"")
                {
                    CreateNoWindow = true, UseShellExecute = false
                };
                using (Process p = Process.Start(psi)) { p.WaitForExit(3000); }
            }
            catch {}
        }

        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }
    }
}

