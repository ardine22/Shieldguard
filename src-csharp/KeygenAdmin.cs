using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace ShieldGuardKeygen
{
    public class MemberItem
    {
        public string Key { get; set; }
        public string Buyer { get; set; }
        public string Phone { get; set; }
        public string BoundHwid { get; set; }
        public string DeviceName { get; set; }
        public string Status { get; set; } // ACTIVE, UNUSED, UNBOUND
        public string CreatedAt { get; set; }
        public string ActivatedAt { get; set; }

        public MemberItem()
        {
            Key = "";
            Buyer = "Pembeli Baru";
            Phone = "-";
            BoundHwid = "";
            DeviceName = "-";
            Status = "UNUSED";
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            ActivatedAt = "-";
        }
    }

    public class KeygenForm : Form
    {
        private const string CLOUD_BASE_URL = "http://70.151.150.190";
        private const string ADMIN_SECRET = "SG_ADMIN_2026";

        // Navigation
        private Button btnNavGen;
        private Button btnNavMembers;
        private Panel panelGen;
        private Panel panelMembers;

        // Header Status
        private Label lblCloudStatus;
        private Button btnSyncCloud;

        // Generator Controls
        private TextBox txtBuyer;
        private TextBox txtPhone;
        private TextBox txtPrefix;
        private TextBox txtOutputKey;
        private TextBox txtWaTemplate;
        private ListBox lstQuickHistory;

        // Member Monitor Controls
        private TextBox txtSearch;
        private Label lblStats;
        private ListView lvMembers;
        private List<MemberItem> allMembers = new List<MemberItem>();

        private string baseDir;
        private string historyTxtPath;
        private string memberJsonPath;

        public KeygenForm()
        {
            this.Text = "ShieldGuard Pro — Admin & Member Control Center";
            this.Size = new Size(1100, 750);
            this.MinimumSize = new Size(1000, 680);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(11, 15, 25);
            this.ForeColor = Color.FromArgb(243, 244, 246);
            this.Font = new Font("Segoe UI", 9.5f);
            this.MaximizeBox = true;

            baseDir = AppDomain.CurrentDomain.BaseDirectory;
            historyTxtPath = Path.Combine(baseDir, "riwayat-lisensi-terjual.txt");
            memberJsonPath = Path.Combine(baseDir, "riwayat-member.json");

            try {
                this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            } catch {}

            BuildUI();
            LoadLocalData();
            RefreshMemberList();
            UpdateStats();

            // Real-Time Auto-Refresh Timer (Every 3 seconds)
            System.Windows.Forms.Timer realTimeTimer = new System.Windows.Forms.Timer();
            realTimeTimer.Interval = 3000;
            realTimeTimer.Tick += (s, e) => {
                LoadLocalData();
                RefreshMemberList();
                UpdateStats();
                ThreadPool.QueueUserWorkItem(state => SyncCloudData(false));
            };
            realTimeTimer.Start();

            // Background check cloud connectivity and sync
            ThreadPool.QueueUserWorkItem(state => SyncCloudData(false));
        }

        private void BuildUI()
        {
            // 1. TOP MASTER CONTAINER (Holds Header & Nav Bar in fixed order)
            Panel topMaster = new Panel {
                Dock = DockStyle.Top,
                Height = 125,
                BackColor = Color.FromArgb(17, 24, 39)
            };

            // 1A. Header Panel (Height: 74)
            Panel header = new Panel {
                Dock = DockStyle.Top,
                Height = 74,
                BackColor = Color.FromArgb(17, 24, 39),
                Padding = new Padding(18, 10, 18, 10)
            };

            Label lblTitle = new Label {
                Text = "🛡️ ShieldGuard PRO — Admin License & Member Center",
                Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 211, 153),
                AutoSize = true, Location = new Point(18, 12)
            };
            Label lblSub = new Label {
                Text = "Pusat Pembuatan Lisensi, Manajemen Serial Key & Monitor Kunci Perangkat (1-Device HWID Lock)",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(156, 163, 175),
                AutoSize = true, Location = new Point(18, 42)
            };

            lblCloudStatus = new Label {
                Text = "🟡 Memeriksa Cloud...",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(251, 191, 36),
                AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(800, 16)
            };

            btnSyncCloud = new Button {
                Text = "🔄 Sinkron Cloud",
                Size = new Size(130, 30),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(920, 34),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnSyncCloud.FlatAppearance.BorderSize = 0;
            btnSyncCloud.Click += (s, e) => {
                btnSyncCloud.Enabled = false;
                btnSyncCloud.Text = "⏳ Sinkron...";
                LoadLocalData();
                RefreshMemberList();
                UpdateStats();
                ThreadPool.QueueUserWorkItem(state => SyncCloudData(true));
            };

            header.Controls.Add(lblTitle);
            header.Controls.Add(lblSub);
            header.Controls.Add(lblCloudStatus);
            header.Controls.Add(btnSyncCloud);

            // 1B. Nav Bar Panel (Height: 51)
            Panel navBar = new Panel {
                Dock = DockStyle.Bottom,
                Height = 51,
                BackColor = Color.FromArgb(15, 23, 42),
                Padding = new Padding(18, 8, 18, 8)
            };

            btnNavGen = new Button {
                Text = "⚡ Buat Lisensi Baru (Generator)",
                Size = new Size(240, 34), Location = new Point(18, 8),
                BackColor = Color.FromArgb(16, 185, 129), ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnNavGen.FlatAppearance.BorderSize = 0;
            btnNavGen.Click += (s, e) => SwitchTab(true);

            btnNavMembers = new Button {
                Text = "👥 Data Member & Monitor Perangkat (0)",
                Size = new Size(300, 34), Location = new Point(270, 8),
                BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.FromArgb(203, 213, 225),
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnNavMembers.FlatAppearance.BorderSize = 0;
            btnNavMembers.Click += (s, e) => SwitchTab(false);

            navBar.Controls.Add(btnNavGen);
            navBar.Controls.Add(btnNavMembers);

            // Assemble Top Master
            topMaster.Controls.Add(navBar);
            topMaster.Controls.Add(header);
            this.Controls.Add(topMaster);

            // 2. MAIN CONTENT HOST (Fills below Top Master)
            Panel contentHost = new Panel {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(11, 15, 25),
                Padding = new Padding(16, 12, 16, 12)
            };
            this.Controls.Add(contentHost);
            contentHost.BringToFront();

            // Panel A: Generator
            panelGen = BuildGeneratorPanel();
            panelGen.Dock = DockStyle.Fill;
            contentHost.Controls.Add(panelGen);

            // Panel B: Members Monitor
            panelMembers = BuildMembersPanel();
            panelMembers.Dock = DockStyle.Fill;
            panelMembers.Visible = false;
            contentHost.Controls.Add(panelMembers);
        }

        private void SwitchTab(bool showGenerator)
        {
            panelGen.Visible = showGenerator;
            panelMembers.Visible = !showGenerator;

            if (showGenerator)
            {
                btnNavGen.BackColor = Color.FromArgb(16, 185, 129);
                btnNavGen.ForeColor = Color.White;
                btnNavMembers.BackColor = Color.FromArgb(30, 41, 59);
                btnNavMembers.ForeColor = Color.FromArgb(203, 213, 225);
            }
            else
            {
                btnNavGen.BackColor = Color.FromArgb(30, 41, 59);
                btnNavGen.ForeColor = Color.FromArgb(203, 213, 225);
                btnNavMembers.BackColor = Color.FromArgb(37, 99, 235);
                btnNavMembers.ForeColor = Color.White;
                LoadLocalData();
                RefreshMemberList();
                UpdateStats();
            }
        }

        // =========================================================================
        // GENERATOR PANEL
        // =========================================================================
        private Panel BuildGeneratorPanel()
        {
            Panel p = new Panel {
                AutoScroll = true,
                BackColor = Color.FromArgb(11, 15, 25),
                Dock = DockStyle.Fill,
                Padding = new Padding(12)
            };

            // Row 1: Inputs
            Label l1 = new Label {
                Text = "Nama / Email Pembeli:", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(10, 10), AutoSize = true
            };
            p.Controls.Add(l1);

            txtBuyer = new TextBox {
                Location = new Point(10, 36), Width = 360,
                Font = new Font("Segoe UI", 10.5f),
                BackColor = Color.FromArgb(17, 24, 39), ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle, Text = "Pelanggan VIP"
            };
            p.Controls.Add(txtBuyer);

            Label lPhone = new Label {
                Text = "No. WhatsApp / HP Pembeli:", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(390, 10), AutoSize = true
            };
            p.Controls.Add(lPhone);

            txtPhone = new TextBox {
                Location = new Point(390, 36), Width = 240,
                Font = new Font("Segoe UI", 10.5f),
                BackColor = Color.FromArgb(17, 24, 39), ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle, Text = "08123456789"
            };
            p.Controls.Add(txtPhone);

            Label l2 = new Label {
                Text = "Kode Segmen (4 Char):", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(650, 10), AutoSize = true
            };
            p.Controls.Add(l2);

            txtPrefix = new TextBox {
                Location = new Point(650, 36), Width = 110, MaxLength = 4,
                Font = new Font("Consolas", 11.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(17, 24, 39), ForeColor = Color.FromArgb(96, 165, 250),
                BorderStyle = BorderStyle.FixedSingle, Text = "PRO1", TextAlign = HorizontalAlignment.Center
            };
            p.Controls.Add(txtPrefix);

            Button btnRandom = new Button {
                Text = "🎲 Acak", Location = new Point(770, 34), Size = new Size(80, 30),
                BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnRandom.FlatAppearance.BorderSize = 0;
            btnRandom.Click += (s, e) => {
                string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
                Random r = new Random();
                string rnd = "" + chars[r.Next(chars.Length)] + chars[r.Next(chars.Length)] + chars[r.Next(chars.Length)] + chars[r.Next(chars.Length)];
                txtPrefix.Text = rnd;
            };
            p.Controls.Add(btnRandom);

            // Row 2: Generate Button
            Button btnGen = new Button {
                Text = "⚡ GENERATE SERIAL KEY BARU & SALIN KE CLIPBOARD",
                Location = new Point(10, 80), Size = new Size(840, 46),
                BackColor = Color.FromArgb(16, 185, 129), ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnGen.FlatAppearance.BorderSize = 0;
            btnGen.Click += OnGenerateClick;
            p.Controls.Add(btnGen);

            // Row 3: Output Key
            Label l3 = new Label {
                Text = "Serial Key Hasil Generate (Siap Dikirim ke Pembeli):",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(10, 140), AutoSize = true
            };
            p.Controls.Add(l3);

            txtOutputKey = new TextBox {
                Location = new Point(10, 166), Width = 840, ReadOnly = true,
                Font = new Font("Consolas", 18f, FontStyle.Bold),
                BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.FromArgb(52, 211, 153),
                BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Center,
                Text = GenerateKey("PRO1")
            };
            p.Controls.Add(txtOutputKey);

            // Row 4: WhatsApp Message Template
            Label lWa = new Label {
                Text = "💬 Template Pesan WhatsApp Siap Kirim ke Pembeli:",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(10, 226), AutoSize = true
            };
            p.Controls.Add(lWa);

            Button btnCopyWa = new Button {
                Text = "📋 Salin Pesan WA Lengkap", Location = new Point(660, 222), Size = new Size(190, 28),
                BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnCopyWa.FlatAppearance.BorderSize = 0;
            btnCopyWa.Click += (s, e) => {
                try {
                    Clipboard.SetText(txtWaTemplate.Text);
                    MessageBox.Show("Pesan WhatsApp berhasil disalin ke Clipboard! Tinggal Paste (Ctrl+V) ke chat pembeli.", "Berhasil Disalin", MessageBoxButtons.OK, MessageBoxIcon.Information);
                } catch {}
            };
            p.Controls.Add(btnCopyWa);

            txtWaTemplate = new TextBox {
                Location = new Point(10, 254), Width = 840, Height = 90,
                Multiline = true, ReadOnly = true,
                Font = new Font("Segoe UI", 9.5f),
                BackColor = Color.FromArgb(17, 24, 39), ForeColor = Color.FromArgb(209, 213, 219),
                BorderStyle = BorderStyle.FixedSingle,
                Text = BuildWaTemplate("Pelanggan VIP", txtOutputKey.Text)
            };
            p.Controls.Add(txtWaTemplate);

            // Row 5: Quick History
            Label l4 = new Label {
                Text = "📜 Riwayat Serial Key yang Baru Dibuat:",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(10, 360), AutoSize = true
            };
            p.Controls.Add(l4);

            lstQuickHistory = new ListBox {
                Location = new Point(10, 386), Width = 840, Height = 140,
                BackColor = Color.FromArgb(17, 24, 39), ForeColor = Color.FromArgb(209, 213, 219),
                Font = new Font("Consolas", 9.5f), BorderStyle = BorderStyle.FixedSingle
            };
            p.Controls.Add(lstQuickHistory);

            return p;
        }

        // =========================================================================
        // MEMBERS MONITOR PANEL
        // =========================================================================
        private Panel BuildMembersPanel()
        {
            Panel p = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(11, 15, 25) };

            // Row 1: Search & Stats Bar
            Panel topBar = new Panel {
                Dock = DockStyle.Top, Height = 50,
                BackColor = Color.FromArgb(17, 24, 39), Padding = new Padding(12, 8, 12, 8)
            };

            Label lSearch = new Label {
                Text = "🔍 Cari Member:",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(12, 14), AutoSize = true, ForeColor = Color.FromArgb(156, 163, 175)
            };
            topBar.Controls.Add(lSearch);

            txtSearch = new TextBox {
                Location = new Point(135, 11), Width = 360,
                Font = new Font("Segoe UI", 10f),
                BackColor = Color.FromArgb(11, 15, 25), ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            txtSearch.TextChanged += (s, e) => FilterMemberList(txtSearch.Text);
            topBar.Controls.Add(txtSearch);

            Button btnRefresh = new Button {
                Text = "🔄 Refresh",
                Size = new Size(95, 30),
                Location = new Point(505, 10),
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnRefresh.FlatAppearance.BorderSize = 0;
            btnRefresh.Click += (s, e) => {
                LoadLocalData();
                RefreshMemberList();
                UpdateStats();
            };
            topBar.Controls.Add(btnRefresh);

            lblStats = new Label {
                Text = "👥 Total: 0 Member  |  🔒 Terkunci: 0 PC  |  ⏳ Belum Dipakai: 0",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 211, 153),
                Location = new Point(615, 14), AutoSize = true
            };
            topBar.Controls.Add(lblStats);

            // Row 2: Bottom Action Bar
            Panel botBar = new Panel {
                Dock = DockStyle.Bottom, Height = 55,
                BackColor = Color.FromArgb(17, 24, 39), Padding = new Padding(10, 10, 10, 10)
            };

            Button btnCopyKey = new Button {
                Text = "📋 Salin Serial Key",
                Size = new Size(150, 34), Location = new Point(10, 10),
                BackColor = Color.FromArgb(16, 185, 129), ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnCopyKey.FlatAppearance.BorderSize = 0;
            btnCopyKey.Click += OnCopySelectedKey;
            botBar.Controls.Add(btnCopyKey);

            Button btnResetDevice = new Button {
                Text = "🔄 Reset Kunci Device",
                Size = new Size(170, 34), Location = new Point(170, 10),
                BackColor = Color.FromArgb(245, 158, 11), ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnResetDevice.FlatAppearance.BorderSize = 0;
            btnResetDevice.Click += OnResetSelectedDevice;
            botBar.Controls.Add(btnResetDevice);

            Button btnChatWa = new Button {
                Text = "💬 Hubungi WA Pembeli",
                Size = new Size(170, 34), Location = new Point(350, 10),
                BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnChatWa.FlatAppearance.BorderSize = 0;
            btnChatWa.Click += OnChatWaSelected;
            botBar.Controls.Add(btnChatWa);

            Button btnDelete = new Button {
                Text = "🗑️ Hapus Lisensi",
                Size = new Size(140, 34), Location = new Point(530, 10),
                BackColor = Color.FromArgb(239, 68, 68), ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnDelete.FlatAppearance.BorderSize = 0;
            btnDelete.Click += OnDeleteSelectedMember;
            botBar.Controls.Add(btnDelete);

            Button btnOpenFolder = new Button {
                Text = "📁 Buka File Riwayat",
                Size = new Size(150, 34), Location = new Point(680, 10),
                BackColor = Color.FromArgb(51, 65, 85), ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnOpenFolder.FlatAppearance.BorderSize = 0;
            btnOpenFolder.Click += (s, e) => {
                try { Process.Start("explorer.exe", "/select,\"" + historyTxtPath + "\""); } catch {}
            };
            botBar.Controls.Add(btnOpenFolder);

            // Row 3: ListView Table (Docks to Fill between topBar and botBar)
            lvMembers = new ListView {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                BackColor = Color.FromArgb(17, 24, 39),
                ForeColor = Color.FromArgb(243, 244, 246),
                Font = new Font("Segoe UI", 9.5f),
                BorderStyle = BorderStyle.None
            };

            lvMembers.Columns.Add("Member / Pembeli", 160);
            lvMembers.Columns.Add("No. WhatsApp", 130);
            lvMembers.Columns.Add("Serial Key Resmi", 210);
            lvMembers.Columns.Add("Kunci HWID Perangkat", 170);
            lvMembers.Columns.Add("Nama Komputer (Device)", 150);
            lvMembers.Columns.Add("Status Kunci", 130);
            lvMembers.Columns.Add("Terdaftar / Aktif", 140);

            lvMembers.DoubleClick += OnCopySelectedKey;

            // Add in proper docking order
            p.Controls.Add(lvMembers);
            p.Controls.Add(topBar);
            p.Controls.Add(botBar);
            lvMembers.BringToFront();

            return p;
        }

        // =========================================================================
        // GENERATOR LOGIC
        // =========================================================================
        public static string GenerateKey(string prefix)
        {
            string clean = "";
            foreach (char c in (prefix ?? "").ToUpperInvariant())
            {
                if (char.IsLetterOrDigit(c)) clean += c;
            }
            if (clean.Length < 4)
            {
                string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
                Random rng = new Random(Guid.NewGuid().GetHashCode());
                while (clean.Length < 4) clean += chars[rng.Next(chars.Length)];
            }
            string seg1 = clean.Substring(0, 4);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes("SG_SECRET_2026:" + seg1));
                StringBuilder sb = new StringBuilder();
                foreach (byte b in bytes) sb.Append(b.ToString("X2"));
                string hex = sb.ToString();
                return string.Format("SGPRO-{0}-{1}-{2}", seg1, hex.Substring(0, 4), hex.Substring(4, 4));
            }
        }

        private string BuildWaTemplate(string buyer, string key)
        {
            return string.Format(
                "Halo {0},\n" +
                "Terima kasih telah membeli ShieldGuard Pro!\n\n" +
                "Berikut adalah Serial Key resmi Anda:\n" +
                "🔑 {1}\n\n" +
                "Catatan: 1 Lisensi berlaku permanen dan otomatis terkunci untuk 1 komputer Anda.\n" +
                "Download installer di: {2}\n\n" +
                "Jika butuh bantuan instalasi, kami siap membantu!",
                buyer, key, CLOUD_BASE_URL
            );
        }

        private void OnGenerateClick(object sender, EventArgs e)
        {
            string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            Random rng = new Random(Guid.NewGuid().GetHashCode());
            string seg1 = txtPrefix.Text.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(seg1) || seg1 == "ACAK")
            {
                seg1 = "";
                for (int i = 0; i < 4; i++) seg1 += chars[rng.Next(chars.Length)];
            }
            else
            {
                seg1 = seg1.Substring(0, Math.Min(2, seg1.Length)) + chars[rng.Next(chars.Length)] + chars[rng.Next(chars.Length)];
                txtPrefix.Text = seg1;
            }

            string key = GenerateKey(seg1);
            txtOutputKey.Text = key;
            try { Clipboard.SetText(key); } catch {}

            string buyer = string.IsNullOrWhiteSpace(txtBuyer.Text) ? "Pelanggan Baru" : txtBuyer.Text.Trim();
            string phone = string.IsNullOrWhiteSpace(txtPhone.Text) ? "-" : txtPhone.Text.Trim();

            txtWaTemplate.Text = BuildWaTemplate(buyer, key);

            // Create Member Record
            MemberItem m = new MemberItem {
                Key = key,
                Buyer = buyer,
                Phone = phone,
                BoundHwid = "",
                DeviceName = "-",
                Status = "UNUSED",
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
            };

            allMembers.Insert(0, m);
            SaveLocalData();

            string record = string.Format("[{0}]  {1}  -->  Pembeli: {2}", DateTime.Now.ToString("yyyy-MM-dd HH:mm"), key, buyer);
            try { File.AppendAllText(historyTxtPath, record + Environment.NewLine, Encoding.UTF8); } catch {}
            lstQuickHistory.Items.Insert(0, record);

            UpdateStats();

            // Background register to Azure Cloud
            ThreadPool.QueueUserWorkItem(state => {
                RegisterKeyToCloud(key, buyer, phone, seg1);
            });

            MessageBox.Show(
                "🎉 Serial Key Pro Berhasil Dibuat & Disalin ke Clipboard:\n\n" + key + "\n\n" +
                "Pembeli: " + buyer + " (" + phone + ")\n\n" +
                "Data otomatis tersimpan di riwayat Member dan didaftarkan ke Cloud Server Azure!",
                "Lisensi Sukses Dibuat",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        // =========================================================================
        // MEMBER ACTIONS
        // =========================================================================
        private void OnCopySelectedKey(object sender, EventArgs e)
        {
            if (lvMembers.SelectedItems.Count == 0)
            {
                MessageBox.Show("Pilih salah satu member di tabel terlebih dahulu.", "Pilih Member", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            MemberItem m = lvMembers.SelectedItems[0].Tag as MemberItem;
            if (m != null)
            {
                try {
                    Clipboard.SetText(m.Key);
                    MessageBox.Show(
                        "✅ Serial Key berhasil disalin ke Clipboard:\n\n" + m.Key + "\n\n" +
                        "Member: " + m.Buyer + "\nStatus: " + m.Status + "\n\n" +
                        "Silakan langsung kirimkan kode ini ke WhatsApp pembeli yang kehilangan kuncinya!",
                        "Serial Key Disalin",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                } catch {}
            }
        }

        private void OnResetSelectedDevice(object sender, EventArgs e)
        {
            if (lvMembers.SelectedItems.Count == 0)
            {
                MessageBox.Show("Pilih salah satu member yang ingin di-reset kuncinya.", "Pilih Member", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            MemberItem m = lvMembers.SelectedItems[0].Tag as MemberItem;
            if (m == null) return;

            DialogResult dr = MessageBox.Show(
                "Apakah Anda yakin ingin me-reset kunci perangkat untuk Serial Key:\n" + m.Key + "\n\n" +
                "Member: " + m.Buyer + "\n" +
                "Perangkat Lama: " + (string.IsNullOrEmpty(m.BoundHwid) ? "Belum Terikat" : m.BoundHwid) + "\n\n" +
                "Tindakan ini akan mengizinkan member untuk mengaktifkan kembali kuncinya di komputer/laptop barunya.",
                "Konfirmasi Reset Kunci Device",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );

            if (dr == DialogResult.Yes)
            {
                m.BoundHwid = "";
                m.DeviceName = "-";
                m.Status = "UNBOUND";
                SaveLocalData();
                RefreshMemberList();
                UpdateStats();

                ThreadPool.QueueUserWorkItem(state => {
                    ResetKeyOnCloud(m.Key);
                });

                MessageBox.Show("✅ Kunci perangkat berhasil di-reset! Member sekarang bisa mengaktifkannya di laptop baru.", "Reset Sukses", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void OnChatWaSelected(object sender, EventArgs e)
        {
            if (lvMembers.SelectedItems.Count == 0)
            {
                MessageBox.Show("Pilih member di tabel terlebih dahulu.", "Pilih Member", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            MemberItem m = lvMembers.SelectedItems[0].Tag as MemberItem;
            if (m == null) return;

            string phone = (m.Phone ?? "").Replace("-", "").Replace(" ", "").Replace("+", "").Trim();
            if (phone.StartsWith("08")) phone = "628" + phone.Substring(2);

            string text = Uri.EscapeDataString(BuildWaTemplate(m.Buyer, m.Key));
            string url = string.IsNullOrEmpty(phone) || phone == "-" 
                ? "https://wa.me/" 
                : "https://wa.me/" + phone + "?text=" + text;

            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch {}
        }

        private void OnDeleteSelectedMember(object sender, EventArgs e)
        {
            if (lvMembers.SelectedItems.Count == 0) return;
            MemberItem m = lvMembers.SelectedItems[0].Tag as MemberItem;
            if (m == null) return;

            DialogResult dr = MessageBox.Show(
                "Hapus permanen lisensi berikut dari database?\n\nKey: " + m.Key + "\nPembeli: " + m.Buyer,
                "Konfirmasi Hapus",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            if (dr == DialogResult.Yes)
            {
                allMembers.Remove(m);
                SaveLocalData();
                RefreshMemberList();
                UpdateStats();

                ThreadPool.QueueUserWorkItem(state => DeleteKeyOnCloud(m.Key));
            }
        }

        // =========================================================================
        // DATA REFRESH & STATS
        // =========================================================================
        private void RefreshMemberList()
        {
            FilterMemberList(txtSearch != null ? txtSearch.Text : "");
        }

        private void FilterMemberList(string query)
        {
            if (lvMembers == null) return;
            lvMembers.Items.Clear();
            string q = (query ?? "").ToLowerInvariant().Trim();

            foreach (var m in allMembers)
            {
                if (!string.IsNullOrEmpty(q))
                {
                    bool match = (m.Buyer ?? "").ToLowerInvariant().Contains(q) ||
                                 (m.Key ?? "").ToLowerInvariant().Contains(q) ||
                                 (m.Phone ?? "").ToLowerInvariant().Contains(q) ||
                                 (m.BoundHwid ?? "").ToLowerInvariant().Contains(q) ||
                                 (m.DeviceName ?? "").ToLowerInvariant().Contains(q);
                    if (!match) continue;
                }

                ListViewItem item = new ListViewItem(m.Buyer);
                item.SubItems.Add(string.IsNullOrEmpty(m.Phone) ? "-" : m.Phone);
                item.SubItems.Add(m.Key);
                item.SubItems.Add(string.IsNullOrEmpty(m.BoundHwid) ? "Belum Terikat" : m.BoundHwid);
                item.SubItems.Add(string.IsNullOrEmpty(m.DeviceName) ? "-" : m.DeviceName);

                string statusText = !string.IsNullOrEmpty(m.BoundHwid) ? "Terkunci (1 PC)" : "Belum Dipakai";
                item.SubItems.Add(statusText);
                item.SubItems.Add(string.IsNullOrEmpty(m.ActivatedAt) || m.ActivatedAt == "-" ? m.CreatedAt : m.ActivatedAt);

                if (!string.IsNullOrEmpty(m.BoundHwid))
                {
                    item.ForeColor = Color.FromArgb(52, 211, 153); // Emerald
                }
                else
                {
                    item.ForeColor = Color.FromArgb(251, 191, 36); // Amber
                }

                item.Tag = m;
                lvMembers.Items.Add(item);
            }
        }

        private void UpdateStats()
        {
            int total = allMembers.Count;
            int locked = 0;
            foreach (var m in allMembers)
            {
                if (!string.IsNullOrEmpty(m.BoundHwid)) locked++;
            }
            int unused = total - locked;

            if (lblStats != null)
            {
                lblStats.Text = string.Format("👥 Total: {0} Member  |  🔒 Terkunci: {1} PC  |  ⏳ Belum Dipakai: {2}", total, locked, unused);
            }
            if (btnNavMembers != null)
            {
                btnNavMembers.Text = string.Format("👥 Data Member & Monitor Perangkat ({0})", total);
            }
        }

        // =========================================================================
        // LOCAL STORAGE (JSON & TXT)
        // =========================================================================
        private void LoadLocalData()
        {
            allMembers.Clear();

            // 1. Try reading structured JSON
            if (File.Exists(memberJsonPath))
            {
                try
                {
                    string json = File.ReadAllText(memberJsonPath, Encoding.UTF8);
                    ParseJsonMemberList(json);
                }
                catch {}
            }

            // 2. Also import from riwayat-lisensi-terjual.txt if any records missing
            if (File.Exists(historyTxtPath))
            {
                try
                {
                    string[] lines = File.ReadAllLines(historyTxtPath, Encoding.UTF8);
                    foreach (string line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        lstQuickHistory.Items.Insert(0, line.Trim());

                        // Pattern: [2026-11-16 20:36]  SGPRO-PRUH-60A0-4768  -->  Pembeli: Pelanggan VIP
                        Match m = Regex.Match(line, @"\[(.*?)\]\s*(SGPRO-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4})\s*-->\s*Pembeli:\s*(.*)");
                        if (m.Success)
                        {
                            string date = m.Groups[1].Value.Trim();
                            string key = m.Groups[2].Value.Trim();
                            string buyer = m.Groups[3].Value.Trim();

                            if (!allMembers.Exists(x => x.Key == key))
                            {
                                allMembers.Add(new MemberItem {
                                    Key = key,
                                    Buyer = buyer,
                                    Phone = "-",
                                    BoundHwid = "",
                                    DeviceName = "-",
                                    Status = "UNUSED",
                                    CreatedAt = date
                                });
                            }
                        }
                    }
                }
                catch {}
            }

            // 3. Auto-detect local firewall activation from firewall_rules.json
            string currentHwid = GetCurrentHwid();
            string currentDevice = Environment.MachineName + " (PC Windows)";

            string[] searchPaths = new string[] {
                Path.Combine(baseDir, "data", "firewall_rules.json"),
                Path.Combine(baseDir, "..", "produk-siap-jual", "data", "firewall_rules.json"),
                Path.Combine(baseDir, "..", "pc-firewall", "data", "firewall_rules.json"),
                Path.Combine(@"c:\Firewall\produk-siap-jual\data", "firewall_rules.json"),
                Path.Combine(@"c:\Firewall\pc-firewall\data", "firewall_rules.json")
            };

            foreach (string p in searchPaths)
            {
                if (File.Exists(p))
                {
                    try
                    {
                        string content = File.ReadAllText(p, Encoding.UTF8);
                        // Check active license_key
                        Match mKey = Regex.Match(content, "\"license_key\"\\s*:\\s*\"(SGPRO-[A-Z0-9-]{14})\"");
                        Match mOwner = Regex.Match(content, "\"licensed_to\"\\s*:\\s*\"([^\"]*)\"");
                        Match mPro = Regex.Match(content, "\"is_pro_licensed\"\\s*:\\s*true");

                        if (mKey.Success && mPro.Success)
                        {
                            string activeKey = mKey.Groups[1].Value;
                            string activeOwner = mOwner.Success ? mOwner.Groups[1].Value : "Pengguna Pro";

                            var target = allMembers.Find(x => x.Key == activeKey);
                            if (target != null && (string.IsNullOrEmpty(target.BoundHwid) || target.BoundHwid == currentHwid))
                            {
                                target.BoundHwid = currentHwid;
                                target.DeviceName = currentDevice;
                                target.Status = "ACTIVE";
                                if (string.IsNullOrEmpty(target.ActivatedAt) || target.ActivatedAt == "-")
                                {
                                    target.ActivatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                                }
                            }
                        }

                        // Also scan logs for any other activated keys
                        MatchCollection mcLogs = Regex.Matches(content, "\"timestamp\"\\s*:\\s*\"([^\"]*)\"[^}]*\"details\"\\s*:\\s*\"[^\"]*(SGPRO-[A-Z0-9-]{14})[^\"]*\"[^}]*\"action\"\\s*:\\s*\"ACTIVATED\"");
                        foreach (Match ml in mcLogs)
                        {
                            string tstamp = ml.Groups[1].Value;
                            string lkey = ml.Groups[2].Value;
                            var target = allMembers.Find(x => x.Key == lkey);
                            if (target != null && string.IsNullOrEmpty(target.BoundHwid))
                            {
                                target.BoundHwid = currentHwid;
                                target.DeviceName = currentDevice;
                                target.Status = "ACTIVE";
                                target.ActivatedAt = tstamp;
                            }
                        }
                    }
                    catch {}
                }
            }

            SaveLocalData();
        }

        private static string GetCurrentHwid()
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

        private void SaveLocalData()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("[");
                for (int i = 0; i < allMembers.Count; i++)
                {
                    var m = allMembers[i];
                    sb.AppendLine("  {");
                    sb.AppendLine(string.Format("    \"key\": \"{0}\",", EscapeJson(m.Key)));
                    sb.AppendLine(string.Format("    \"buyer\": \"{0}\",", EscapeJson(m.Buyer)));
                    sb.AppendLine(string.Format("    \"phone\": \"{0}\",", EscapeJson(m.Phone)));
                    sb.AppendLine(string.Format("    \"bound_hwid\": \"{0}\",", EscapeJson(m.BoundHwid)));
                    sb.AppendLine(string.Format("    \"device_name\": \"{0}\",", EscapeJson(m.DeviceName)));
                    sb.AppendLine(string.Format("    \"status\": \"{0}\",", EscapeJson(m.Status)));
                    sb.AppendLine(string.Format("    \"created_at\": \"{0}\",", EscapeJson(m.CreatedAt)));
                    sb.AppendLine(string.Format("    \"activated_at\": \"{0}\"", EscapeJson(m.ActivatedAt)));
                    sb.AppendLine(i < allMembers.Count - 1 ? "  }," : "  }");
                }
                sb.AppendLine("]");
                File.WriteAllText(memberJsonPath, sb.ToString(), Encoding.UTF8);
            }
            catch {}
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " ");
        }

        private void ParseJsonMemberList(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            // Match each JSON object in array
            MatchCollection mc = Regex.Matches(json, @"\{([^{}]+)\}");
            foreach (Match m in mc)
            {
                string block = m.Groups[1].Value;
                MemberItem item = new MemberItem();
                item.Key = ExtractJsonValue(block, "key");
                item.Buyer = ExtractJsonValue(block, "buyer");
                item.Phone = ExtractJsonValue(block, "phone");
                item.BoundHwid = ExtractJsonValue(block, "bound_hwid");
                item.DeviceName = ExtractJsonValue(block, "device_name");
                item.Status = ExtractJsonValue(block, "status");
                item.CreatedAt = ExtractJsonValue(block, "created_at");
                item.ActivatedAt = ExtractJsonValue(block, "activated_at");

                if (!string.IsNullOrEmpty(item.Key) && !allMembers.Exists(x => x.Key == item.Key))
                {
                    allMembers.Add(item);
                }
            }
        }

        private static string ExtractJsonValue(string block, string key)
        {
            Match m = Regex.Match(block, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
            if (m.Success) return m.Groups[1].Value;
            // Or null / boolean
            Match m2 = Regex.Match(block, "\"" + key + "\"\\s*:\\s*([a-zA-Z0-9_-]+)");
            if (m2.Success)
            {
                string v = m2.Groups[1].Value;
                return v == "null" ? "" : v;
            }
            return "";
        }

        // =========================================================================
        // CLOUD AZURE SYNC
        // =========================================================================
        private void SyncCloudData(bool showPopup)
        {
            try
            {
                string url = string.Format("{0}/api/license/admin-view?secret={1}", CLOUD_BASE_URL, ADMIN_SECRET);
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 4000;
                req.Method = "GET";

                using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                {
                    string resp = sr.ReadToEnd();
                    if (resp.Contains("\"ok\": true") || resp.Contains("\"ok\":true"))
                    {
                        // Parse cloud licenses and merge into local
                        ParseCloudLicensesResponse(resp);

                        this.Invoke((Action)(() => {
                            lblCloudStatus.Text = "🟢 Cloud: Terhubung (70.151.150.190)";
                            lblCloudStatus.ForeColor = Color.FromArgb(52, 211, 153);
                            btnSyncCloud.Enabled = true;
                            btnSyncCloud.Text = "🔄 Sinkron Cloud";
                            SaveLocalData();
                            RefreshMemberList();
                            UpdateStats();

                            if (showPopup)
                            {
                                MessageBox.Show(
                                    "🎉 Sinkronisasi Cloud Berhasil!\n\nSemua data aktivasi member dan status HWID terkunci terbaru dari server Azure telah diperbarui di aplikasi ini.",
                                    "Sinkron Selesai",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Information
                                );
                            }
                        }));
                        return;
                    }
                }
            }
            catch
            {
                // Cloud connection timeout or offline
            }

            this.Invoke((Action)(() => {
                lblCloudStatus.Text = "⚪ Cloud: Offline (Mode Database Lokal)";
                lblCloudStatus.ForeColor = Color.FromArgb(148, 163, 184);
                btnSyncCloud.Enabled = true;
                btnSyncCloud.Text = "🔄 Sinkron Cloud";

                if (showPopup)
                {
                    MessageBox.Show(
                        "Cloud server Azure saat ini sedang tidak merespons (atau Anda sedang offline).\n\nAplikasi tetap berfungsi normal 100% menggunakan database lokal di PC Anda.",
                        "Mode Offline",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                }
            }));
        }

        private void ParseCloudLicensesResponse(string json)
        {
            MatchCollection mc = Regex.Matches(json, "\"SGPRO-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}\"\\s*:\\s*\\{([^{}]+)\\}");
            foreach (Match m in mc)
            {
                string block = m.Groups[1].Value;
                string key = ExtractJsonValue(block, "key");
                string buyer = ExtractJsonValue(block, "buyer");
                string phone = ExtractJsonValue(block, "phone");
                string hwid = ExtractJsonValue(block, "bound_hwid");
                string dev = ExtractJsonValue(block, "device_name");
                string act = ExtractJsonValue(block, "activated_at");

                if (string.IsNullOrEmpty(key)) continue;

                var existing = allMembers.Find(x => x.Key == key);
                if (existing != null)
                {
                    if (!string.IsNullOrEmpty(hwid)) existing.BoundHwid = hwid;
                    if (!string.IsNullOrEmpty(dev) && dev != "-") existing.DeviceName = dev;
                    if (!string.IsNullOrEmpty(act) && act != "-") existing.ActivatedAt = act;
                    if (!string.IsNullOrEmpty(buyer) && existing.Buyer == "Pelanggan VIP") existing.Buyer = buyer;
                }
                else
                {
                    allMembers.Add(new MemberItem {
                        Key = key,
                        Buyer = string.IsNullOrEmpty(buyer) ? "Member Pro" : buyer,
                        Phone = string.IsNullOrEmpty(phone) ? "-" : phone,
                        BoundHwid = hwid,
                        DeviceName = string.IsNullOrEmpty(dev) ? "-" : dev,
                        Status = !string.IsNullOrEmpty(hwid) ? "ACTIVE" : "UNUSED",
                        CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                        ActivatedAt = string.IsNullOrEmpty(act) ? "-" : act
                    });
                }
            }
        }

        private void RegisterKeyToCloud(string key, string buyer, string phone, string prefix)
        {
            try
            {
                string url = CLOUD_BASE_URL + "/api/license/admin-create";
                string json = string.Format("{{\"secret\":\"{0}\",\"buyer\":\"{1}\",\"phone\":\"{2}\",\"prefix\":\"{3}\"}}", ADMIN_SECRET, EscapeJson(buyer), EscapeJson(phone), prefix);
                SendPostJson(url, json);
            }
            catch {}
        }

        private void ResetKeyOnCloud(string key)
        {
            try
            {
                string url = CLOUD_BASE_URL + "/api/license/admin-reset";
                string json = string.Format("{{\"secret\":\"{0}\",\"key\":\"{1}\"}}", ADMIN_SECRET, key);
                SendPostJson(url, json);
            }
            catch {}
        }

        private void DeleteKeyOnCloud(string key)
        {
            try
            {
                string url = CLOUD_BASE_URL + "/api/license/admin-delete";
                string json = string.Format("{{\"secret\":\"{0}\",\"key\":\"{1}\"}}", ADMIN_SECRET, key);
                SendPostJson(url, json);
            }
            catch {}
        }

        private static void SendPostJson(string url, string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 4000;
            req.Method = "POST";
            req.ContentType = "application/json; charset=utf-8";
            req.ContentLength = bytes.Length;
            using (Stream s = req.GetRequestStream())
            {
                s.Write(bytes, 0, bytes.Length);
            }
            using (HttpWebResponse res = (HttpWebResponse)req.GetResponse()) {}
        }

        [STAThread]
        public static void Main()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new KeygenForm());
            }
            catch (Exception ex)
            {
                try {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "keygen-error.log"), ex.ToString());
                    MessageBox.Show(ex.ToString(), "Keygen Error");
                } catch {}
            }
        }
    }
}
