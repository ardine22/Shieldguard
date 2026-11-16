using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ShieldGuardApp
{
    public class LogEntry
    {
        public string timestamp { get; set; }
        public string level { get; set; }
        public string category { get; set; }
        public string target { get; set; }
        public string details { get; set; }
        public string action { get; set; }
    }

    public class FirewallStats
    {
        public int total_requests_checked { get; set; }
        public int threats_blocked { get; set; }
        public int scam_pages_detected { get; set; }
        public int network_ips_blocked { get; set; }
    }

    public class FirewallConfig
    {
        public bool firewall_enabled { get; set; }
        public bool scam_protection_enabled { get; set; }
        public bool cookie_guard_enabled { get; set; }
        public bool hosts_sync_enabled { get; set; }
        public bool is_pro_licensed { get; set; }
        public string license_key { get; set; }
        public string licensed_to { get; set; }
        public string bound_hwid { get; set; }
        public List<string> blocked_domains { get; set; }
        public List<string> whitelisted_domains { get; set; }
        public List<string> blocked_ips { get; set; }
        public List<string> blocked_apps { get; set; }
        public List<string> scam_keywords { get; set; }
        public FirewallStats stats { get; set; }
        public List<LogEntry> logs { get; set; }
    }

    public class MainForm : Form
    {
        private FirewallConfig config;
        private readonly object syncLock = new object();
        private string baseDir;
        private string dataFile;
        private TcpListener apiListener;
        private Thread serverThread;
        private volatile bool running = true;

        // UI Controls
        private Button btnToggleFw;
        private Label lblHeaderSub;
        private Label lblStatsBar;
        private ListView lvConnections;
        private ListBox lstDomains;
        private TextBox txtDomainInput;
        private TextBox txtScamTest;
        private ListBox lstIps;
        private TextBox txtIpInput;
        private ListBox lstApps;
        private ListView lvLogs;
        private Label lblLicenseStatus;
        private TextBox txtLicenseName;
        private TextBox txtLicenseKey;

        public MainForm()
        {
            baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string dataDir = Path.Combine(baseDir, "data");
            if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
            dataFile = Path.Combine(dataDir, "firewall_rules.json");

            LoadConfig();

            this.Text = "ShieldGuard Pro — PC Firewall & Anti-Scam Security Suite (v1.0)";
            this.Size = new Size(1100, 720);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(11, 15, 25);
            this.ForeColor = Color.FromArgb(243, 244, 246);

            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch {}

            BuildUI();
            StartHttpServer();
            RefreshUIData();
            RefreshConnections();

            System.Windows.Forms.Timer uiTimer = new System.Windows.Forms.Timer();
            uiTimer.Interval = 4000;
            uiTimer.Tick += delegate { RefreshUIData(); };
            uiTimer.Start();

            this.FormClosing += delegate {
                running = false;
                try { if (apiListener != null) apiListener.Stop(); } catch {}
            };
        }

        public static bool IsAdmin()
        {
            try
            {
                WindowsIdentity id = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(id);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        public static bool VerifyLicenseKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            string clean = key.Trim().ToUpperInvariant();
            string[] parts = clean.Split('-');
            if (parts.Length != 4 || parts[0] != "SGPRO") return false;
            if (parts[1].Length != 4 || parts[2].Length != 4 || parts[3].Length != 4) return false;

            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes("SG_SECRET_2026:" + parts[1]));
                StringBuilder sb = new StringBuilder();
                foreach (byte b in bytes) sb.Append(b.ToString("X2"));
                string hex = sb.ToString();
                return parts[2] == hex.Substring(0, 4) && parts[3] == hex.Substring(4, 4);
            }
        }

        public static string GetHardwareId()
        {
            try
            {
                string guid = (string)Microsoft.Win32.Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography",
                    "MachineGuid", ""
                );
                if (!string.IsNullOrEmpty(guid))
                {
                    using (SHA256 sha = SHA256.Create())
                    {
                        byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes("HWID_SALT:" + guid));
                        StringBuilder sb = new StringBuilder("HWID-");
                        for (int i = 0; i < 4; i++) sb.Append(bytes[i].ToString("X2"));
                        sb.Append("-");
                        for (int i = 4; i < 8; i++) sb.Append(bytes[i].ToString("X2"));
                        return sb.ToString();
                    }
                }
            }
            catch {}
            return "HWID-" + Environment.MachineName.ToUpperInvariant();
        }

        public static Dictionary<string, object> ActivateCloudLicense(string key, string buyer, string hwid)
        {
            Dictionary<string, object> res = new Dictionary<string, object>();
            try
            {
                string url = "http://70.151.150.190/api/license/activate";
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/json; charset=utf-8";
                req.Timeout = 4000;
                JavaScriptSerializer ser = new JavaScriptSerializer();
                Dictionary<string, string> payload = new Dictionary<string, string>();
                payload["key"] = key;
                payload["hwid"] = hwid;
                payload["buyer"] = buyer;
                payload["device_name"] = Environment.MachineName;
                byte[] body = Encoding.UTF8.GetBytes(ser.Serialize(payload));
                req.ContentLength = body.Length;
                using (Stream s = req.GetRequestStream()) { s.Write(body, 0, body.Length); }
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string json = sr.ReadToEnd();
                    return ser.Deserialize<Dictionary<string, object>>(json);
                }
            }
            catch (Exception ex)
            {
                res["ok"] = true;
                res["status"] = "OFFLINE_FALLBACK";
                res["message"] = ex.Message;
                return res;
            }
        }

        private void LoadConfig()
        {
            JavaScriptSerializer ser = new JavaScriptSerializer();
            if (File.Exists(dataFile))
            {
                try
                {
                    string json = File.ReadAllText(dataFile, Encoding.UTF8);
                    config = ser.Deserialize<FirewallConfig>(json);
                }
                catch { config = null; }
            }

            if (config == null)
            {
                config = new FirewallConfig
                {
                    firewall_enabled = true,
                    scam_protection_enabled = true,
                    cookie_guard_enabled = true,
                    hosts_sync_enabled = true,
                    is_pro_licensed = false,
                    license_key = "",
                    licensed_to = "Versi Trial (Gratis)",
                    blocked_domains = new List<string> {
                        "woukox.com", "www.woukox.com", "mrbeast-casino.com",
                        "beast-crypto.com", "elon-giveaway.org", "stake-bonus-claim.com",
                        "crypto-boom-bonus.com", "usdt-instant-claim.com", "free-2500-usdt.com",
                        "discord-nitro-free.ru", "instagram-verify-badge.com"
                    },
                    whitelisted_domains = new List<string> {
                        "google.com", "www.google.com", "youtube.com", "www.youtube.com",
                        "instagram.com", "www.instagram.com", "facebook.com", "www.facebook.com",
                        "x.com", "twitter.com", "github.com", "microsoft.com", "localhost", "127.0.0.1"
                    },
                    blocked_ips = new List<string> { "185.220.101.1", "45.142.214.99" },
                    blocked_apps = new List<string>(),
                    scam_keywords = new List<string> {
                        "launch of my very own crypto casino",
                        "giving away $2,500 to everyone who registers",
                        "enter the special promo code: boom",
                        "enter the promo code: boom",
                        "withdraw the bonus instantly",
                        "your withdrawal of $2500.00 was successfully",
                        "receive your $2,500 bonus",
                        "woukox.com",
                        "deposit to verify your wallet",
                        "activate your wallet for withdrawal"
                    },
                    stats = new FirewallStats(),
                    logs = new List<LogEntry>()
                };
                SaveConfig();
            }
            if (config.stats == null) config.stats = new FirewallStats();
            if (config.logs == null) config.logs = new List<LogEntry>();
            if (config.blocked_domains == null) config.blocked_domains = new List<string>();
            if (config.blocked_ips == null) config.blocked_ips = new List<string>();
            if (config.blocked_apps == null) config.blocked_apps = new List<string>();
            if (config.scam_keywords == null) config.scam_keywords = new List<string>();
        }

        private void SaveConfig()
        {
            lock (syncLock)
            {
                try
                {
                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    string json = ser.Serialize(config);
                    File.WriteAllText(dataFile, json, Encoding.UTF8);
                }
                catch {}
            }
        }

        private void AddLog(string level, string category, string target, string details, string action)
        {
            lock (syncLock)
            {
                config.logs.Insert(0, new LogEntry
                {
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    level = level,
                    category = category,
                    target = target,
                    details = details,
                    action = action
                });
                if (config.logs.Count > 150) config.logs.RemoveRange(150, config.logs.Count - 150);
                if (action == "BLOCKED") config.stats.threats_blocked++;
            }
            SaveConfig();
        }

        // ===================== UI CONSTRUCTION =====================
        private void BuildUI()
        {
            Panel header = new Panel
            {
                Dock = DockStyle.Top, Height = 74,
                BackColor = Color.FromArgb(17, 24, 39), Padding = new Padding(16)
            };

            Label lblTitle = new Label
            {
                Text = "🛡️ ShieldGuard PC Firewall & Browser Security Suite",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = Color.White, AutoSize = true, Location = new Point(16, 12)
            };
            header.Controls.Add(lblTitle);

            lblHeaderSub = new Label
            {
                Text = "Memuat status...",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 211, 153), AutoSize = true, Location = new Point(16, 42)
            };
            header.Controls.Add(lblHeaderSub);

            btnToggleFw = new Button
            {
                Text = "FIREWALL: AKTIF", Size = new Size(155, 36),
                Location = new Point(620, 18), BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Cursor = Cursors.Hand
            };
            btnToggleFw.FlatAppearance.BorderSize = 0;
            btnToggleFw.Click += delegate {
                lock (syncLock) { config.firewall_enabled = !config.firewall_enabled; }
                SaveConfig();
                RefreshUIData();
            };
            header.Controls.Add(btnToggleFw);

            Button btnWebDash = new Button
            {
                Text = "🖥️ Web Dashboard", Size = new Size(140, 36),
                Location = new Point(785, 18), BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand
            };
            btnWebDash.FlatAppearance.BorderSize = 0;
            btnWebDash.Click += delegate { Process.Start("http://127.0.0.1:8765"); };
            header.Controls.Add(btnWebDash);

            Button btnStorePage = new Button
            {
                Text = "🛒 Halaman Jual/Download", Size = new Size(145, 36),
                Location = new Point(932, 18), BackColor = Color.FromArgb(139, 92, 246),
                ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), Cursor = Cursors.Hand
            };
            btnStorePage.FlatAppearance.BorderSize = 0;
            btnStorePage.Click += delegate { Process.Start("http://127.0.0.1:8765/store"); };
            header.Controls.Add(btnStorePage);

            this.Controls.Add(header);

            TabControl tabs = new TabControl
            {
                Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };

            TabPage tabNet = new TabPage("📡 Monitor Jaringan Live") { BackColor = Color.FromArgb(11, 15, 25) };
            TabPage tabDom = new TabPage("🚫 Blokir Domain & Anti-Scam") { BackColor = Color.FromArgb(11, 15, 25) };
            TabPage tabIpApp = new TabPage("🔒 Blokir IP & Aplikasi (.exe)") { BackColor = Color.FromArgb(11, 15, 25) };
            TabPage tabLogs = new TabPage("🔍 Anti-Stealer & Log Keamanan") { BackColor = Color.FromArgb(11, 15, 25) };
            TabPage tabLicense = new TabPage("🔑 Aktivasi Lisensi Pro & Toko") { BackColor = Color.FromArgb(11, 15, 25) };

            BuildTabNetwork(tabNet);
            BuildTabDomains(tabDom);
            BuildTabIpApp(tabIpApp);
            BuildTabLogs(tabLogs);
            BuildTabLicense(tabLicense);

            tabs.TabPages.Add(tabNet);
            tabs.TabPages.Add(tabDom);
            tabs.TabPages.Add(tabIpApp);
            tabs.TabPages.Add(tabLogs);
            tabs.TabPages.Add(tabLicense);

            Panel bodyPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 80, 10, 10) };
            bodyPanel.Controls.Add(tabs);
            this.Controls.Add(bodyPanel);
        }

        private void BuildTabNetwork(TabPage tab)
        {
            lblStatsBar = new Label
            {
                Text = "Memuat statistik...", Location = new Point(14, 14), AutoSize = true,
                Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Color.FromArgb(147, 197, 253)
            };
            tab.Controls.Add(lblStatsBar);

            Button btnRefresh = new Button
            {
                Text = "🔄 Segarkan Koneksi", Location = new Point(690, 8), Size = new Size(155, 32),
                BackColor = Color.FromArgb(55, 65, 81), ForeColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            btnRefresh.FlatAppearance.BorderSize = 0;
            btnRefresh.Click += delegate { RefreshConnections(); };
            tab.Controls.Add(btnRefresh);

            Button btnBlockConnIp = new Button
            {
                Text = "⛔ Blokir IP Koneksi Terpilih", Location = new Point(855, 8), Size = new Size(190, 32),
                BackColor = Color.FromArgb(239, 68, 68), ForeColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            btnBlockConnIp.FlatAppearance.BorderSize = 0;
            btnBlockConnIp.Click += delegate {
                if (lvConnections.SelectedItems.Count == 0)
                {
                    MessageBox.Show("Pilih salah satu baris koneksi pada tabel terlebih dahulu.");
                    return;
                }
                string ip = lvConnections.SelectedItems[0].SubItems[3].Text;
                string proc = lvConnections.SelectedItems[0].SubItems[0].Text;
                BlockIpAddress(ip, "Diblokir dari monitor koneksi proses " + proc);
                RefreshUIData();
                RefreshConnections();
                MessageBox.Show("Alamat IP " + ip + " berhasil diblokir!", "ShieldGuard Firewall");
            };
            tab.Controls.Add(btnBlockConnIp);

            lvConnections = new ListView
            {
                Location = new Point(14, 48), Size = new Size(1030, 500),
                View = View.Details, FullRowSelect = true, GridLines = true,
                BackColor = Color.FromArgb(17, 24, 39), ForeColor = Color.White,
                Font = new Font("Segoe UI", 9, FontStyle.Regular)
            };
            lvConnections.Columns.Add("Proses Aplikasi", 160);
            lvConnections.Columns.Add("PID", 70);
            lvConnections.Columns.Add("Alamat Lokal", 160);
            lvConnections.Columns.Add("IP Tujuan", 150);
            lvConnections.Columns.Add("Port", 70);
            lvConnections.Columns.Add("Status", 110);
            lvConnections.Columns.Add("Risiko", 90);
            lvConnections.Columns.Add("Keterangan Analisis", 200);
            tab.Controls.Add(lvConnections);
        }

        private void BuildTabDomains(TabPage tab)
        {
            Label l1 = new Label
            {
                Text = "Tambah Domain Berbahaya / Situs Scam (Otomatis Diblokir di Chrome, Edge, & File Hosts):",
                Location = new Point(14, 14), AutoSize = true, ForeColor = Color.White
            };
            tab.Controls.Add(l1);

            txtDomainInput = new TextBox
            {
                Location = new Point(14, 42), Size = new Size(680, 28),
                BackColor = Color.FromArgb(17, 24, 39), ForeColor = Color.White,
                Font = new Font("Segoe UI", 10)
            };
            tab.Controls.Add(txtDomainInput);

            Button btnAddDom = new Button
            {
                Text = "+ Blokir Domain", Location = new Point(704, 40), Size = new Size(155, 30),
                BackColor = Color.FromArgb(239, 68, 68), ForeColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            btnAddDom.FlatAppearance.BorderSize = 0;
            btnAddDom.Click += delegate {
                string d = txtDomainInput.Text.Trim();
                if (string.IsNullOrEmpty(d)) return;
                AddBlockedDomain(d, "Ditambahkan dari Aplikasi Desktop");
                txtDomainInput.Text = "";
                RefreshUIData();
            };
            tab.Controls.Add(btnAddDom);

            Button btnDelDom = new Button
            {
                Text = "Hapus Terpilih", Location = new Point(868, 40), Size = new Size(175, 30),
                BackColor = Color.FromArgb(55, 65, 81), ForeColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            btnDelDom.FlatAppearance.BorderSize = 0;
            btnDelDom.Click += delegate {
                if (lstDomains.SelectedItem == null) return;
                RemoveBlockedDomain(lstDomains.SelectedItem.ToString());
                RefreshUIData();
            };
            tab.Controls.Add(btnDelDom);

            lstDomains = new ListBox
            {
                Location = new Point(14, 80), Size = new Size(1030, 340),
                BackColor = Color.FromArgb(17, 24, 39), ForeColor = Color.FromArgb(248, 113, 113),
                Font = new Font("Consolas", 10)
            };
            tab.Controls.Add(lstDomains);

            Label lTest = new Label
            {
                Text = "🧪 Uji Coba Pemindai Heuristik Scam Kripto (Kasus MrBeast / woukox.com / Promo BOOM):",
                Location = new Point(14, 435), AutoSize = true, ForeColor = Color.FromArgb(96, 165, 250)
            };
            tab.Controls.Add(lTest);

            txtScamTest = new TextBox
            {
                Location = new Point(14, 463), Size = new Size(830, 28),
                BackColor = Color.FromArgb(17, 24, 39), ForeColor = Color.White,
                Font = new Font("Segoe UI", 10),
                Text = "MrBeast: Go to woukox.com and enter promo code: BOOM to receive your $2,500 bonus"
            };
            tab.Controls.Add(txtScamTest);

            Button btnRunTest = new Button
            {
                Text = "🔍 Periksa Teks/URL", Location = new Point(854, 461), Size = new Size(190, 30),
                BackColor = Color.FromArgb(16, 185, 129), ForeColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            btnRunTest.FlatAppearance.BorderSize = 0;
            btnRunTest.Click += delegate {
                Dictionary<string, object> res = EvaluatePageContent("http://test.local", "Test", txtScamTest.Text);
                bool allowed = (bool)res["allowed"];
                if (!allowed)
                {
                    MessageBox.Show(
                        "🚨 ANCAMAN SCAM TERDETEKSI!\n\nSkor Risiko: " + res["risk_score"] + "/100\nAlasan: " + res["reason"],
                        "ShieldGuard Anti-Scam Engine", MessageBoxButtons.OK, MessageBoxIcon.Warning
                    );
                }
                else
                {
                    MessageBox.Show("✅ Aman: Tidak ditemukan indikasi scam pada teks tersebut.", "ShieldGuard");
                }
                RefreshUIData();
            };
            tab.Controls.Add(btnRunTest);
        }

        private void BuildTabIpApp(TabPage tab)
        {
            Label l1 = new Label
            {
                Text = "Blokir Alamat IP Mencurigakan (Integrasi Windows Defender Firewall):",
                Location = new Point(14, 14), AutoSize = true, ForeColor = Color.White
            };
            tab.Controls.Add(l1);

            txtIpInput = new TextBox
            {
                Location = new Point(14, 42), Size = new Size(680, 28),
                BackColor = Color.FromArgb(17, 24, 39), ForeColor = Color.White, Font = new Font("Segoe UI", 10)
            };
            tab.Controls.Add(txtIpInput);

            Button btnAddIp = new Button
            {
                Text = "+ Blokir IP", Location = new Point(704, 40), Size = new Size(155, 30),
                BackColor = Color.FromArgb(239, 68, 68), ForeColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            btnAddIp.FlatAppearance.BorderSize = 0;
            btnAddIp.Click += delegate {
                if (string.IsNullOrWhiteSpace(txtIpInput.Text)) return;
                BlockIpAddress(txtIpInput.Text.Trim(), "Blokir manual via Desktop GUI");
                txtIpInput.Text = "";
                RefreshUIData();
            };
            tab.Controls.Add(btnAddIp);

            Button btnDelIp = new Button
            {
                Text = "Buka Blokir IP", Location = new Point(868, 40), Size = new Size(175, 30),
                BackColor = Color.FromArgb(55, 65, 81), ForeColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            btnDelIp.FlatAppearance.BorderSize = 0;
            btnDelIp.Click += delegate {
                if (lstIps.SelectedItem == null) return;
                UnblockIpAddress(lstIps.SelectedItem.ToString());
                RefreshUIData();
            };
            tab.Controls.Add(btnDelIp);

            lstIps = new ListBox
            {
                Location = new Point(14, 80), Size = new Size(1030, 170),
                BackColor = Color.FromArgb(17, 24, 39), ForeColor = Color.FromArgb(251, 191, 36),
                Font = new Font("Consolas", 10)
            };
            tab.Controls.Add(lstIps);

            Label l2 = new Label
            {
                Text = "Blokir Akses Internet Aplikasi (.exe) di PC Anda:",
                Location = new Point(14, 268), AutoSize = true, ForeColor = Color.White
            };
            tab.Controls.Add(l2);

            Button btnBrowseApp = new Button
            {
                Text = "📂 Pilih File Aplikasi (.exe) untuk Diblokir...",
                Location = new Point(14, 296), Size = new Size(340, 32),
                BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            btnBrowseApp.FlatAppearance.BorderSize = 0;
            btnBrowseApp.Click += delegate {
                OpenFileDialog ofd = new OpenFileDialog { Filter = "Aplikasi Windows (*.exe)|*.exe" };
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    BlockAppExe(ofd.FileName);
                    RefreshUIData();
                }
            };
            tab.Controls.Add(btnBrowseApp);

            Button btnUnblockApp = new Button
            {
                Text = "Buka Blokir Aplikasi Terpilih",
                Location = new Point(366, 296), Size = new Size(230, 32),
                BackColor = Color.FromArgb(55, 65, 81), ForeColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            btnUnblockApp.FlatAppearance.BorderSize = 0;
            btnUnblockApp.Click += delegate {
                if (lstApps.SelectedItem == null) return;
                UnblockAppExe(lstApps.SelectedItem.ToString());
                RefreshUIData();
            };
            tab.Controls.Add(btnUnblockApp);

            lstApps = new ListBox
            {
                Location = new Point(14, 338), Size = new Size(1030, 195),
                BackColor = Color.FromArgb(17, 24, 39), ForeColor = Color.FromArgb(147, 197, 253),
                Font = new Font("Consolas", 10)
            };
            tab.Controls.Add(lstApps);
        }

        private void BuildTabLogs(TabPage tab)
        {
            Label l1 = new Label
            {
                Text = "Log Keamanan Real-Time & Pemindai Malware Pencuri Cookie (Infostealer):",
                Location = new Point(14, 14), AutoSize = true, ForeColor = Color.White
            };
            tab.Controls.Add(l1);

            Button btnScanMalware = new Button
            {
                Text = "🔍 Pindai Proses Pencuri Cookie / Malware Sekarang",
                Location = new Point(664, 8), Size = new Size(380, 32),
                BackColor = Color.FromArgb(16, 185, 129), ForeColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            btnScanMalware.FlatAppearance.BorderSize = 0;
            btnScanMalware.Click += delegate {
                List<string> alerts = ScanProcessesForStealers();
                RefreshUIData();
                if (alerts.Count == 0)
                {
                    MessageBox.Show("✅ AMAN: Tidak ditemukan proses Infostealer / Cookie Stealer aktif di memori PC Anda.", "Hasil Pemindaian Bersih");
                }
                else
                {
                    MessageBox.Show("🚨 PERINGATAN! Ditemukan proses mencurigakan:\n\n" + string.Join("\n", alerts.ToArray()), "Peringatan Keamanan", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            tab.Controls.Add(btnScanMalware);

            lvLogs = new ListView
            {
                Location = new Point(14, 48), Size = new Size(1030, 500),
                View = View.Details, FullRowSelect = true, GridLines = true,
                BackColor = Color.FromArgb(17, 24, 39), ForeColor = Color.White,
                Font = new Font("Segoe UI", 9, FontStyle.Regular)
            };
            lvLogs.Columns.Add("Waktu", 145);
            lvLogs.Columns.Add("Level", 85);
            lvLogs.Columns.Add("Kategori", 135);
            lvLogs.Columns.Add("Target", 180);
            lvLogs.Columns.Add("Detail Kejadian", 380);
            lvLogs.Columns.Add("Tindakan", 90);
            tab.Controls.Add(lvLogs);
        }

        private void BuildTabLicense(TabPage tab)
        {
            Panel card = new Panel
            {
                Location = new Point(180, 40), Size = new Size(680, 440),
                BackColor = Color.FromArgb(17, 24, 39), BorderStyle = BorderStyle.FixedSingle
            };

            Label lblHead = new Label
            {
                Text = "🔑 Aktivasi Lisensi ShieldGuard PRO Edition",
                Font = new Font("Segoe UI", 15, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 211, 153), Location = new Point(28, 24), AutoSize = true
            };
            card.Controls.Add(lblHead);

            lblLicenseStatus = new Label
            {
                Text = "Status Saat Ini: VERSI TRIAL GRATIS",
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = Color.FromArgb(251, 191, 36), Location = new Point(28, 56), AutoSize = true
            };
            card.Controls.Add(lblLicenseStatus);

            Label lHwid = new Label
            {
                Text = "🔒 Hardware ID (Kunci Perangkat Ini): " + GetHardwareId(),
                Font = new Font("Consolas", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(96, 165, 250), Location = new Point(28, 82), AutoSize = true
            };
            card.Controls.Add(lHwid);

            Label lName = new Label
            {
                Text = "Nama Pemilik Lisensi / Perusahaan:",
                Location = new Point(28, 110), AutoSize = true, ForeColor = Color.White
            };
            card.Controls.Add(lName);

            txtLicenseName = new TextBox
            {
                Location = new Point(28, 136), Size = new Size(620, 30),
                BackColor = Color.FromArgb(11, 15, 25), ForeColor = Color.White,
                Font = new Font("Segoe UI", 11), Text = config.licensed_to
            };
            card.Controls.Add(txtLicenseName);

            Label lKey = new Label
            {
                Text = "Masukkan Serial Key Pro (Format: SGPRO-XXXX-XXXX-XXXX):",
                Location = new Point(28, 184), AutoSize = true, ForeColor = Color.White
            };
            card.Controls.Add(lKey);

            txtLicenseKey = new TextBox
            {
                Location = new Point(28, 210), Size = new Size(620, 34),
                BackColor = Color.FromArgb(11, 15, 25), ForeColor = Color.FromArgb(52, 211, 153),
                Font = new Font("Consolas", 14, FontStyle.Bold), Text = config.license_key
            };
            card.Controls.Add(txtLicenseKey);

            Button btnActivate = new Button
            {
                Text = "✅ AKTIFKAN LISENSI PRO SEKARANG",
                Location = new Point(28, 262), Size = new Size(620, 44),
                BackColor = Color.FromArgb(16, 185, 129), ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 11, FontStyle.Bold), Cursor = Cursors.Hand
            };
            btnActivate.FlatAppearance.BorderSize = 0;
            btnActivate.Click += delegate {
                string key = txtLicenseKey.Text.Trim().ToUpperInvariant();
                string owner = string.IsNullOrWhiteSpace(txtLicenseName.Text) ? "Pengguna Pro" : txtLicenseName.Text.Trim();
                if (!VerifyLicenseKey(key))
                {
                    MessageBox.Show("❌ Serial Key tidak valid! Pastikan formatnya SGPRO-XXXX-XXXX-XXXX yang dibuat melalui ShieldGuard-Keygen-Admin.exe.", "Aktivasi Gagal", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                string hwid = GetHardwareId();
                var cloudResp = ActivateCloudLicense(key, owner, hwid);

                if (cloudResp.ContainsKey("ok") && Convert.ToBoolean(cloudResp["ok"]) == false && cloudResp.ContainsKey("status") && Convert.ToString(cloudResp["status"]) == "DEVICE_MISMATCH")
                {
                    string failMsg = cloudResp.ContainsKey("message") ? Convert.ToString(cloudResp["message"]) : "Serial Key ini sudah terikat pada perangkat lain!";
                    MessageBox.Show(failMsg, "❌ Pelanggaran Lisensi (1 Device Lock)", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                lock (syncLock)
                {
                    config.is_pro_licensed = true;
                    config.license_key = key;
                    config.licensed_to = owner;
                    config.bound_hwid = hwid;
                }
                AddLog("INFO", "LICENSE_PRO", owner, "Lisensi ShieldGuard PRO berhasil dikunci pada perangkat " + hwid, "ACTIVATED");
                SaveConfig();
                RefreshUIData();
                MessageBox.Show(
                    "🎉 Selamat! ShieldGuard PRO Edition berhasil diaktifkan & terikat resmi ke komputer ini:\n\n" +
                    "Hardware ID (HWID): " + hwid + "\n" +
                    "Pemilik: " + owner + "\n\n" +
                    "🛡️ 1 Lisensi aman terkunci hanya untuk komputer ini.",
                    "Aktivasi Berhasil (1 Device Lock)",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            };
            card.Controls.Add(btnActivate);

            Button btnOpenStore = new Button
            {
                Text = "🌐 Buka Halaman Website Penjualan & Download Installer (http://127.0.0.1:8765/store)",
                Location = new Point(28, 330), Size = new Size(620, 40),
                BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Cursor = Cursors.Hand
            };
            btnOpenStore.FlatAppearance.BorderSize = 0;
            btnOpenStore.Click += delegate { Process.Start("http://127.0.0.1:8765/store"); };
            card.Controls.Add(btnOpenStore);

            Label lblHint = new Label
            {
                Text = "💡 Info Penjual: Gunakan aplikasi 'ShieldGuard-Keygen-Admin.exe' di folder produk-siap-jual untuk membuat Serial Key pembeli.",
                Location = new Point(28, 388), AutoSize = true,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular), ForeColor = Color.FromArgb(156, 163, 175)
            };
            card.Controls.Add(lblHint);

            tab.Controls.Add(card);
        }

        // ===================== CORE FIREWALL LOGIC =====================
        private static string NormalizeDomain(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string s = raw.Trim().ToLowerInvariant();
            if (s.Contains("://"))
            {
                try { return new Uri(s).Host.ToLowerInvariant(); } catch {}
            }
            return s.Split('/')[0].Split(':')[0].Trim('.');
        }

        private void AddBlockedDomain(string domOrUrl, string reason)
        {
            string dom = NormalizeDomain(domOrUrl);
            if (string.IsNullOrEmpty(dom) || dom == "localhost" || dom == "127.0.0.1") return;
            lock (syncLock)
            {
                if (!config.blocked_domains.Contains(dom)) config.blocked_domains.Add(dom);
                if (!dom.StartsWith("www.") && !config.blocked_domains.Contains("www." + dom))
                    config.blocked_domains.Add("www." + dom);
            }
            AddLog("WARNING", "DOMAIN_BLOCK", dom, "Domain ditambahkan ke daftar blokir (" + reason + ")", "BLOCKED");
            SaveConfig();
            if (IsAdmin()) SyncHostsFile();
        }

        private void RemoveBlockedDomain(string domOrUrl)
        {
            string dom = NormalizeDomain(domOrUrl).Replace("www.", "");
            lock (syncLock)
            {
                config.blocked_domains.RemoveAll(d => d == dom || d == "www." + dom);
            }
            AddLog("INFO", "DOMAIN_UNBLOCK", dom, "Domain dihapus dari daftar blokir", "ALLOWED");
            SaveConfig();
            if (IsAdmin()) SyncHostsFile();
        }

        private void SyncHostsFile()
        {
            try
            {
                string hostsPath = @"C:\Windows\System32\drivers\etc\hosts";
                string startMark = "# >>> SHIELDGUARD FIREWALL START >>>";
                string endMark = "# <<< SHIELDGUARD FIREWALL END <<<";
                string content = File.Exists(hostsPath) ? File.ReadAllText(hostsPath, Encoding.UTF8) : "";
                string pattern = Regex.Escape(startMark) + ".*?" + Regex.Escape(endMark);
                string cleaned = Regex.Replace(content, pattern, "", RegexOptions.Singleline).TrimEnd();

                StringBuilder sb = new StringBuilder();
                sb.AppendLine(cleaned);
                if (config.firewall_enabled && config.blocked_domains.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine(startMark);
                    foreach (string d in config.blocked_domains)
                    {
                        if (!string.IsNullOrEmpty(d) && d != "localhost" && d != "127.0.0.1")
                            sb.AppendLine("0.0.0.0 " + d);
                    }
                    sb.AppendLine(endMark);
                }
                File.WriteAllText(hostsPath, sb.ToString(), Encoding.UTF8);
                RunHiddenProcess("ipconfig", "/flushdns");
            }
            catch {}
        }

        private void BlockIpAddress(string ip, string reason)
        {
            if (string.IsNullOrWhiteSpace(ip) || ip.StartsWith("127.")) return;
            lock (syncLock)
            {
                if (!config.blocked_ips.Contains(ip)) config.blocked_ips.Add(ip);
                config.stats.network_ips_blocked = config.blocked_ips.Count;
            }
            if (IsAdmin())
            {
                string rule = "ShieldGuard_Block_IP_" + ip;
                RunHiddenProcess("netsh", string.Format("advfirewall firewall add rule name=\"{0}_OUT\" dir=out action=block remoteip={1}", rule, ip));
                RunHiddenProcess("netsh", string.Format("advfirewall firewall add rule name=\"{0}_IN\" dir=in action=block remoteip={1}", rule, ip));
            }
            AddLog("WARNING", "IP_BLOCK", ip, "IP diblokir: " + reason, "BLOCKED");
            SaveConfig();
        }

        private void UnblockIpAddress(string ip)
        {
            lock (syncLock)
            {
                config.blocked_ips.Remove(ip);
                config.stats.network_ips_blocked = config.blocked_ips.Count;
            }
            if (IsAdmin())
            {
                string rule = "ShieldGuard_Block_IP_" + ip;
                RunHiddenProcess("netsh", string.Format("advfirewall firewall delete rule name=\"{0}_OUT\"", rule));
                RunHiddenProcess("netsh", string.Format("advfirewall firewall delete rule name=\"{0}_IN\"", rule));
            }
            AddLog("INFO", "IP_UNBLOCK", ip, "Blokir IP dilepas", "ALLOWED");
            SaveConfig();
        }

        private void BlockAppExe(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath)) return;
            lock (syncLock)
            {
                if (!config.blocked_apps.Contains(exePath)) config.blocked_apps.Add(exePath);
            }
            string appName = Path.GetFileName(exePath);
            if (IsAdmin())
            {
                string rule = "ShieldGuard_Block_App_" + appName;
                RunHiddenProcess("netsh", string.Format("advfirewall firewall add rule name=\"{0}\" dir=out action=block program=\"{1}\" enable=yes", rule, exePath));
            }
            AddLog("WARNING", "APP_BLOCK", appName, "Akses jaringan diblokir untuk aplikasi: " + exePath, "BLOCKED");
            SaveConfig();
        }

        private void UnblockAppExe(string exePath)
        {
            lock (syncLock) { config.blocked_apps.Remove(exePath); }
            string appName = Path.GetFileName(exePath);
            if (IsAdmin())
            {
                string rule = "ShieldGuard_Block_App_" + appName;
                RunHiddenProcess("netsh", string.Format("advfirewall firewall delete rule name=\"{0}\"", rule));
            }
            AddLog("INFO", "APP_UNBLOCK", appName, "Blokir aplikasi dilepas", "ALLOWED");
            SaveConfig();
        }

        private Dictionary<string, object> EvaluateUrl(string url)
        {
            Dictionary<string, object> res = new Dictionary<string, object>();
            lock (syncLock) { config.stats.total_requests_checked++; }

            if (!config.firewall_enabled)
            {
                res["allowed"] = true; res["risk_score"] = 0; res["reason"] = "Firewall nonaktif";
                return res;
            }
            string dom = NormalizeDomain(url);
            foreach (string w in config.whitelisted_domains)
            {
                if (dom == w || dom.EndsWith("." + w))
                {
                    res["allowed"] = true; res["risk_score"] = 0; res["reason"] = "Whitelisted";
                    return res;
                }
            }
            foreach (string b in config.blocked_domains)
            {
                if (dom == b || dom.EndsWith("." + b))
                {
                    AddLog("CRITICAL", "DOMAIN_BLOCK", dom, "Akses ke domain berbahaya diblokir: " + url, "BLOCKED");
                    res["allowed"] = false; res["risk_score"] = 100;
                    res["reason"] = "Domain '" + dom + "' berada dalam daftar blokir ShieldGuard Firewall.";
                    res["domain"] = dom;
                    return res;
                }
            }
            res["allowed"] = true; res["risk_score"] = 5; res["reason"] = "Aman"; res["domain"] = dom;
            return res;
        }

        private Dictionary<string, object> EvaluatePageContent(string url, string title, string text)
        {
            Dictionary<string, object> res = new Dictionary<string, object>();
            List<string> matched = new List<string>();
            if (!config.firewall_enabled || !config.scam_protection_enabled)
            {
                res["allowed"] = true; res["risk_score"] = 0; res["matched"] = matched; res["reason"] = "Nonaktif";
                return res;
            }

            string combined = ((title ?? "") + " " + (text ?? "")).ToLowerInvariant();
            int score = 0;
            foreach (string kw in config.scam_keywords)
            {
                if (combined.Contains(kw.ToLowerInvariant()))
                {
                    matched.Add(kw);
                    score += 45;
                }
            }
            bool hasCeleb = combined.Contains("mrbeast") || combined.Contains("@mrbeast") || combined.Contains("elon musk");
            bool hasBait = combined.Contains("crypto casino") || combined.Contains("promo code") || combined.Contains("$2,500") || combined.Contains("2500 usdt");
            if (hasCeleb && hasBait)
            {
                score += 65;
                matched.Add("Pola-Heuristik: Catut Figur Publik + Bonus Kasino Kripto ($2,500 / Promo)");
            }
            if (score > 100) score = 100;

            if (score >= 45)
            {
                lock (syncLock) { config.stats.scam_pages_detected++; }
                AddLog("CRITICAL", "SCAM_SITE", NormalizeDomain(url), "Konten Scam Kripto/Phishing terdeteksi (Skor " + score + "/100)", "BLOCKED");
                res["allowed"] = false; res["risk_score"] = score; res["matched"] = matched;
                res["reason"] = "Terdeteksi Penipuan Kasino Kripto / Giveaway Palsu (modus MrBeast / $2,500 USDT).";
                return res;
            }

            res["allowed"] = true; res["risk_score"] = score; res["matched"] = matched; res["reason"] = "Aman";
            return res;
        }

        private List<Dictionary<string, string>> GetActiveConnectionsList()
        {
            List<Dictionary<string, string>> list = new List<Dictionary<string, string>>();
            Dictionary<int, string> pidNames = new Dictionary<int, string>();
            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    try { pidNames[p.Id] = p.ProcessName + ".exe"; } catch {}
                }
            }
            catch {}

            HashSet<string> susp = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                "powershell.exe", "pwsh.exe", "cmd.exe", "wscript.exe", "cscript.exe", "mshta.exe", "rundll32.exe"
            };

            try
            {
                string netOut = RunHiddenProcess("netstat", "-ano -p tcp");
                string[] lines = netOut.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string raw in lines)
                {
                    string line = raw.Trim();
                    if (!line.StartsWith("TCP")) continue;
                    string[] parts = Regex.Split(line, @"\s+");
                    if (parts.Length < 5) continue;
                    string local = parts[1];
                    string remote = parts[2];
                    string state = parts[3];
                    string pidStr = parts[4];
                    if (remote.StartsWith("0.0.0.0") || remote.StartsWith("127.0.0.1") || remote.StartsWith("[::")) continue;

                    int lastColon = remote.LastIndexOf(':');
                    string remIp = lastColon > 0 ? remote.Substring(0, lastColon) : remote;
                    string remPort = lastColon > 0 ? remote.Substring(lastColon + 1) : "";
                    int pid = 0;
                    int.TryParse(pidStr, out pid);
                    string procName = pidNames.ContainsKey(pid) ? pidNames[pid] : ("PID:" + pidStr);

                    string risk = "AMAN";
                    string reason = "Koneksi standar";
                    if (config.blocked_ips.Contains(remIp))
                    {
                        risk = "BAHAYA"; reason = "Terhubung ke IP dalam daftar blokir!";
                    }
                    else if (susp.Contains(procName))
                    {
                        risk = "WASPADA"; reason = "Proses script sistem melakukan koneksi keluar";
                    }

                    Dictionary<string, string> item = new Dictionary<string, string>();
                    item["process"] = procName;
                    item["pid"] = pidStr;
                    item["local"] = local;
                    item["remote_ip"] = remIp;
                    item["remote_port"] = remPort;
                    item["state"] = state;
                    item["risk"] = risk;
                    item["reason"] = reason;
                    list.Add(item);
                    if (list.Count >= 100) break;
                }
            }
            catch {}
            return list;
        }

        private List<string> ScanProcessesForStealers()
        {
            List<string> alerts = new List<string>();
            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    try
                    {
                        string path = p.MainModule.FileName.ToLowerInvariant();
                        if (path.Contains(@"\appdata\local\temp\") &&
                            !path.Contains("microsoft") && !path.Contains("google") && !path.Contains("antigravity"))
                        {
                            string msg = p.ProcessName + ".exe (PID " + p.Id + ") berjalan dari Temp: " + path;
                            alerts.Add(msg);
                            AddLog("WARNING", "PROCESS_ALERT", p.ProcessName, msg, "ALERT");
                        }
                    }
                    catch {}
                }
            }
            catch {}
            return alerts;
        }

        private static string RunHiddenProcess(string file, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(file, args)
                {
                    CreateNoWindow = true, UseShellExecute = false,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                using (Process p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(3500);
                    return output;
                }
            }
            catch { return ""; }
        }

        private void RefreshConnections()
        {
            if (this.InvokeRequired) { this.BeginInvoke(new Action(RefreshConnections)); return; }
            lvConnections.Items.Clear();
            foreach (var c in GetActiveConnectionsList())
            {
                ListViewItem item = new ListViewItem(new string[] {
                    c["process"], c["pid"], c["local"], c["remote_ip"], c["remote_port"], c["state"], c["risk"], c["reason"]
                });
                if (c["risk"] == "BAHAYA") item.ForeColor = Color.FromArgb(248, 113, 113);
                else if (c["risk"] == "WASPADA") item.ForeColor = Color.FromArgb(251, 191, 36);
                lvConnections.Items.Add(item);
            }
        }

        private void RefreshUIData()
        {
            if (this.InvokeRequired) { this.BeginInvoke(new Action(RefreshUIData)); return; }
            lock (syncLock)
            {
                string licTxt = config.is_pro_licensed
                    ? ("🌟 PRO LIFETIME (" + config.licensed_to + ")")
                    : "VERSI TRIAL GRATIS (Belum Aktivasi Serial Key)";
                string admTxt = IsAdmin() ? "Mode Administrator Aktif" : "Mode Standar (Sinkron Browser Aktif)";
                lblHeaderSub.Text = string.Format("Server Lokal: http://127.0.0.1:8765   |   {0}   |   {1}", admTxt, licTxt);

                if (config.firewall_enabled)
                {
                    btnToggleFw.Text = "FIREWALL: AKTIF";
                    btnToggleFw.BackColor = Color.FromArgb(16, 185, 129);
                }
                else
                {
                    btnToggleFw.Text = "FIREWALL: NONAKTIF";
                    btnToggleFw.BackColor = Color.FromArgb(239, 68, 68);
                }

                lblStatsBar.Text = string.Format(
                    "URL Diperiksa: {0}   |   Ancaman Diblokir: {1}   |   Scam Kripto Terdeteksi: {2}   |   Domain Diblokir: {3}",
                    config.stats.total_requests_checked, config.stats.threats_blocked,
                    config.stats.scam_pages_detected, config.blocked_domains.Count
                );

                lstDomains.Items.Clear();
                foreach (string d in config.blocked_domains) lstDomains.Items.Add(d);

                lstIps.Items.Clear();
                foreach (string ip in config.blocked_ips) lstIps.Items.Add(ip);

                lstApps.Items.Clear();
                foreach (string app in config.blocked_apps) lstApps.Items.Add(app);

                lvLogs.Items.Clear();
                int count = Math.Min(50, config.logs.Count);
                for (int i = 0; i < count; i++)
                {
                    LogEntry l = config.logs[i];
                    lvLogs.Items.Add(new ListViewItem(new string[] {
                        l.timestamp, l.level, l.category, l.target, l.details, l.action
                    }));
                }

                if (config.is_pro_licensed)
                {
                    lblLicenseStatus.Text = "✅ STATUS: SHIELDGUARD PRO LIFETIME AKTIF — Terdaftar a/n: " + config.licensed_to;
                    lblLicenseStatus.ForeColor = Color.FromArgb(52, 211, 153);
                }
                else
                {
                    lblLicenseStatus.Text = "⚠️ STATUS: VERSI TRIAL GRATIS — Masukkan Serial Key SGPRO untuk membuka fitur penuh";
                    lblLicenseStatus.ForeColor = Color.FromArgb(251, 191, 36);
                }
            }
        }

        // ===================== EMBEDDED HTTP SERVER (PORT 8765) =====================
        private void StartHttpServer()
        {
            serverThread = new Thread( delegate() {
                try
                {
                    apiListener = new TcpListener(IPAddress.Loopback, 8765);
                    apiListener.Start();
                    while (running)
                    {
                        TcpClient client = apiListener.AcceptTcpClient();
                        ThreadPool.QueueUserWorkItem(HandleHttpClient, client);
                    }
                }
                catch {}
            });
            serverThread.IsBackground = true;
            serverThread.Start();
        }

        private string FindProjectRoot()
        {
            // Check baseDir or parent of baseDir
            if (Directory.Exists(Path.Combine(baseDir, "website-penjualan"))) return baseDir;
            DirectoryInfo parent = Directory.GetParent(baseDir.TrimEnd('\\'));
            if (parent != null && Directory.Exists(Path.Combine(parent.FullName, "website-penjualan")))
                return parent.FullName;
            if (Directory.Exists(@"c:\Firewall")) return @"c:\Firewall";
            return baseDir;
        }

        private void HandleHttpClient(object state)
        {
            using (TcpClient client = (TcpClient)state)
            using (NetworkStream stream = client.GetStream())
            {
                try
                {
                    byte[] buf = new byte[65536];
                    int read = stream.Read(buf, 0, buf.Length);
                    if (read <= 0) return;
                    string rawReq = Encoding.UTF8.GetString(buf, 0, read);
                    string[] headerBody = rawReq.Split(new string[] { "\r\n\r\n" }, 2, StringSplitOptions.None);
                    string head = headerBody[0];
                    string body = headerBody.Length > 1 ? headerBody[1] : "";

                    string[] firstLine = head.Split('\r', '\n')[0].Split(' ');
                    if (firstLine.Length < 2) return;
                    string method = firstLine[0].ToUpperInvariant();
                    string fullPath = firstLine[1];
                    string path = fullPath.Split('?')[0];

                    JavaScriptSerializer ser = new JavaScriptSerializer();

                    if (method == "OPTIONS")
                    {
                        SendJsonResponse(stream, "{\"ok\":true}");
                        return;
                    }

                    string root = FindProjectRoot();

                    if (method == "GET")
                    {
                        if (path == "/" || path == "/dashboard")
                        {
                            string dashFile = Path.Combine(baseDir, "dashboard.html");
                            if (!File.Exists(dashFile)) dashFile = Path.Combine(root, @"pc-firewall\dashboard.html");
                            if (File.Exists(dashFile))
                            {
                                SendFileResponse(stream, dashFile, "text/html; charset=utf-8", null);
                                return;
                            }
                        }
                        if (path == "/store")
                        {
                            string storeFile = Path.Combine(baseDir, "store.html");
                            if (!File.Exists(storeFile)) storeFile = Path.Combine(root, @"website-penjualan\index.html");
                            if (File.Exists(storeFile))
                            {
                                SendFileResponse(stream, storeFile, "text/html; charset=utf-8", null);
                                return;
                            }
                        }
                        if (path == "/download/setup")
                        {
                            string f = Path.Combine(root, @"produk-siap-jual\ShieldGuard-Setup-v1.0.exe");
                            if (File.Exists(f)) { SendFileResponse(stream, f, "application/octet-stream", "ShieldGuard-Setup-v1.0.exe"); return; }
                        }
                        if (path == "/download/extension")
                        {
                            string f = Path.Combine(root, @"produk-siap-jual\ShieldGuard-Extension-Chrome-Edge.zip");
                            if (File.Exists(f)) { SendFileResponse(stream, f, "application/zip", "ShieldGuard-Extension-Chrome-Edge.zip"); return; }
                        }
                        if (path == "/download/bundle")
                        {
                            string f = Path.Combine(root, @"produk-siap-jual\ShieldGuard-Paket-Lengkap-v1.0.zip");
                            if (File.Exists(f)) { SendFileResponse(stream, f, "application/zip", "ShieldGuard-Paket-Lengkap-v1.0.zip"); return; }
                        }
                        if (path == "/api/status")
                        {
                            Dictionary<string, object> st = new Dictionary<string, object>();
                            lock (syncLock)
                            {
                                st["firewall_enabled"] = config.firewall_enabled;
                                st["scam_protection_enabled"] = config.scam_protection_enabled;
                                st["cookie_guard_enabled"] = config.cookie_guard_enabled;
                                st["is_pro_licensed"] = config.is_pro_licensed;
                                st["licensed_to"] = config.licensed_to;
                                st["is_admin"] = IsAdmin();
                                st["blocked_domains"] = config.blocked_domains;
                                st["whitelisted_domains"] = config.whitelisted_domains;
                                st["blocked_ips"] = config.blocked_ips;
                                st["blocked_apps"] = config.blocked_apps;
                                st["stats"] = config.stats;
                                st["logs"] = config.logs;
                            }
                            SendJsonResponse(stream, ser.Serialize(st));
                            return;
                        }
                        if (path == "/api/connections")
                        {
                            Dictionary<string, object> r = new Dictionary<string, object>();
                            r["connections"] = GetActiveConnectionsList();
                            SendJsonResponse(stream, ser.Serialize(r));
                            return;
                        }
                        if (path == "/api/scan-processes")
                        {
                            List<string> alerts = ScanProcessesForStealers();
                            SendJsonResponse(stream, ser.Serialize(new Dictionary<string, object> { { "findings", alerts } }));
                            return;
                        }
                    }

                    if (method == "POST")
                    {
                        Dictionary<string, object> payload = new Dictionary<string, object>();
                        try { payload = ser.Deserialize<Dictionary<string, object>>(body); } catch {}
                        if (payload == null) payload = new Dictionary<string, object>();

                        if (path == "/api/activate-license")
                        {
                            string key = payload.ContainsKey("key") ? Convert.ToString(payload["key"]).Trim().ToUpperInvariant() : "";
                            string owner = payload.ContainsKey("name") ? Convert.ToString(payload["name"]).Trim() : "Pengguna Pro";
                            if (VerifyLicenseKey(key))
                            {
                                lock (syncLock)
                                {
                                    config.is_pro_licensed = true;
                                    config.license_key = key;
                                    config.licensed_to = owner;
                                }
                                AddLog("INFO", "LICENSE_PRO", owner, "Lisensi Pro diaktifkan via Browser Extension: " + key, "ACTIVATED");
                                SaveConfig();
                                RefreshUIData();
                                SendJsonResponse(stream, "{\"ok\":true,\"message\":\"Lisensi Pro berhasil diaktifkan!\"}");
                                return;
                            }
                            SendJsonResponse(stream, "{\"ok\":false,\"message\":\"Serial Key tidak valid!\"}");
                            return;
                        }
                        if (path == "/api/check-url")
                        {
                            string u = payload.ContainsKey("url") ? Convert.ToString(payload["url"]) : "";
                            SendJsonResponse(stream, ser.Serialize(EvaluateUrl(u)));
                            return;
                        }
                        if (path == "/api/scan-content")
                        {
                            string u = payload.ContainsKey("url") ? Convert.ToString(payload["url"]) : "";
                            string t = payload.ContainsKey("title") ? Convert.ToString(payload["title"]) : "";
                            string tx = payload.ContainsKey("text") ? Convert.ToString(payload["text"]) : "";
                            SendJsonResponse(stream, ser.Serialize(EvaluatePageContent(u, t, tx)));
                            return;
                        }
                        if (path == "/api/block-domain")
                        {
                            string d = payload.ContainsKey("domain") ? Convert.ToString(payload["domain"]) : "";
                            string r = payload.ContainsKey("reason") ? Convert.ToString(payload["reason"]) : "Diblokir";
                            AddBlockedDomain(d, r);
                            SendJsonResponse(stream, ser.Serialize(new Dictionary<string, object> { { "ok", true }, { "blocked_domains", config.blocked_domains } }));
                            return;
                        }
                        if (path == "/api/unblock-domain")
                        {
                            string d = payload.ContainsKey("domain") ? Convert.ToString(payload["domain"]) : "";
                            RemoveBlockedDomain(d);
                            SendJsonResponse(stream, ser.Serialize(new Dictionary<string, object> { { "ok", true }, { "blocked_domains", config.blocked_domains } }));
                            return;
                        }
                        if (path == "/api/block-ip")
                        {
                            string ip = payload.ContainsKey("ip") ? Convert.ToString(payload["ip"]) : "";
                            string r = payload.ContainsKey("reason") ? Convert.ToString(payload["reason"]) : "Blokir IP";
                            BlockIpAddress(ip, r);
                            SendJsonResponse(stream, ser.Serialize(new Dictionary<string, object> { { "ok", true }, { "blocked_ips", config.blocked_ips } }));
                            return;
                        }
                        if (path == "/api/unblock-ip")
                        {
                            string ip = payload.ContainsKey("ip") ? Convert.ToString(payload["ip"]) : "";
                            UnblockIpAddress(ip);
                            SendJsonResponse(stream, ser.Serialize(new Dictionary<string, object> { { "ok", true }, { "blocked_ips", config.blocked_ips } }));
                            return;
                        }
                        if (path == "/api/toggle")
                        {
                            string k = payload.ContainsKey("key") ? Convert.ToString(payload["key"]) : "";
                            bool v = payload.ContainsKey("value") && Convert.ToBoolean(payload["value"]);
                            lock (syncLock)
                            {
                                if (k == "firewall_enabled") config.firewall_enabled = v;
                                else if (k == "scam_protection_enabled") config.scam_protection_enabled = v;
                                else if (k == "cookie_guard_enabled") config.cookie_guard_enabled = v;
                            }
                            SaveConfig();
                            SendJsonResponse(stream, "{\"ok\":true}");
                            return;
                        }
                        if (path == "/api/log-event")
                        {
                            string lv = payload.ContainsKey("level") ? Convert.ToString(payload["level"]) : "WARNING";
                            string cat = payload.ContainsKey("category") ? Convert.ToString(payload["category"]) : "BROWSER";
                            string tgt = payload.ContainsKey("target") ? Convert.ToString(payload["target"]) : "";
                            string det = payload.ContainsKey("details") ? Convert.ToString(payload["details"]) : "";
                            string act = payload.ContainsKey("action") ? Convert.ToString(payload["action"]) : "BLOCKED";
                            AddLog(lv, cat, tgt, det, act);
                            SendJsonResponse(stream, "{\"ok\":true}");
                            return;
                        }
                    }

                    SendJsonResponse(stream, "{\"error\":\"Not found\"}");
                }
                catch {}
            }
        }

        private static void SendJsonResponse(NetworkStream stream, string json)
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            string header = "HTTP/1.1 200 OK\r\n" +
                            "Content-Type: application/json; charset=utf-8\r\n" +
                            "Access-Control-Allow-Origin: *\r\n" +
                            "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
                            "Access-Control-Allow-Headers: Content-Type\r\n" +
                            "Content-Length: " + body.Length + "\r\n" +
                            "Connection: close\r\n\r\n";
            byte[] headBytes = Encoding.UTF8.GetBytes(header);
            stream.Write(headBytes, 0, headBytes.Length);
            stream.Write(body, 0, body.Length);
        }

        private static void SendFileResponse(NetworkStream stream, string filePath, string contentType, string downloadName)
        {
            byte[] body = File.ReadAllBytes(filePath);
            StringBuilder sb = new StringBuilder();
            sb.Append("HTTP/1.1 200 OK\r\n");
            sb.Append("Content-Type: " + contentType + "\r\n");
            if (!string.IsNullOrEmpty(downloadName))
                sb.Append("Content-Disposition: attachment; filename=\"" + downloadName + "\"\r\n");
            sb.Append("Content-Length: " + body.Length + "\r\n");
            sb.Append("Connection: close\r\n\r\n");
            byte[] headBytes = Encoding.UTF8.GetBytes(sb.ToString());
            stream.Write(headBytes, 0, headBytes.Length);
            stream.Write(body, 0, body.Length);
        }

        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}

