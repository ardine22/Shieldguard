using System;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace ShieldGuardKeygen
{
    public class KeygenForm : Form
    {
        private TextBox txtBuyer;
        private TextBox txtPrefix;
        private TextBox txtOutputKey;
        private ListBox lstHistory;
        private string historyPath;

        public KeygenForm()
        {
            this.Text = "ShieldGuard Pro — Generator Lisensi Resmi (Khusus Penjual/Admin)";
            this.Size = new Size(640, 520);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(11, 15, 25);
            this.ForeColor = Color.FromArgb(243, 244, 246);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;

            try {
                this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            } catch {}

            historyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "riwayat-lisensi-terjual.txt");
            BuildUI();
            LoadHistory();
        }

        private void BuildUI()
        {
            Panel header = new Panel {
                Dock = DockStyle.Top, Height = 72,
                BackColor = Color.FromArgb(17, 24, 39), Padding = new Padding(18, 12, 18, 12)
            };
            Label lblTitle = new Label {
                Text = "🔑 ShieldGuard PRO — License Key Generator (Admin)",
                Font = new Font("Segoe UI", 13, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 211, 153),
                AutoSize = true, Location = new Point(18, 12)
            };
            Label lblSub = new Label {
                Text = "Gunakan aplikasi ini untuk membuat Serial Key resmi bagi pelanggan yang membeli Aplikasi PC Windows maupun Ekstensi Chrome & Edge.",
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(156, 163, 175),
                AutoSize = true, Location = new Point(18, 40)
            };
            header.Controls.Add(lblTitle);
            header.Controls.Add(lblSub);
            this.Controls.Add(header);

            int y = 90;
            Label l1 = new Label {
                Text = "Nama / Email Pembeli:", Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Location = new Point(22, y), AutoSize = true
            };
            this.Controls.Add(l1);

            txtBuyer = new TextBox {
                Location = new Point(22, y + 24), Width = 420,
                Font = new Font("Segoe UI", 10),
                BackColor = Color.FromArgb(17, 24, 39), ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle, Text = "Pelanggan VIP"
            };
            this.Controls.Add(txtBuyer);

            Label l2 = new Label {
                Text = "Kode ID (4 Huruf/Angka):", Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Location = new Point(458, y), AutoSize = true
            };
            this.Controls.Add(l2);

            txtPrefix = new TextBox {
                Location = new Point(458, y + 24), Width = 140, MaxLength = 4,
                Font = new Font("Consolas", 11, FontStyle.Bold),
                BackColor = Color.FromArgb(17, 24, 39), ForeColor = Color.FromArgb(96, 165, 250),
                BorderStyle = BorderStyle.FixedSingle, Text = "PRO1"
            };
            this.Controls.Add(txtPrefix);

            y += 66;
            Button btnGen = new Button {
                Text = "⚡ GENERATE SERIAL KEY BARU & SALIN KE CLIPBOARD",
                Location = new Point(22, y), Size = new Size(576, 42),
                BackColor = Color.FromArgb(16, 185, 129), ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnGen.FlatAppearance.BorderSize = 0;
            btnGen.Click += OnGenerateClick;
            this.Controls.Add(btnGen);

            y += 56;
            Label l3 = new Label {
                Text = "Serial Key Hasil Generate (Siap Dikirim ke Pembeli):",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Location = new Point(22, y), AutoSize = true
            };
            this.Controls.Add(l3);

            txtOutputKey = new TextBox {
                Location = new Point(22, y + 24), Width = 576, ReadOnly = true,
                Font = new Font("Consolas", 16, FontStyle.Bold),
                BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.FromArgb(52, 211, 153),
                BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Center,
                Text = GenerateKey("PRO1")
            };
            this.Controls.Add(txtOutputKey);

            y += 74;
            Label l4 = new Label {
                Text = "📜 Riwayat Lisensi Pelanggan yang Pernah Dibuat:",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Location = new Point(22, y), AutoSize = true
            };
            this.Controls.Add(l4);

            lstHistory = new ListBox {
                Location = new Point(22, y + 24), Size = new Size(576, 145),
                BackColor = Color.FromArgb(17, 24, 39), ForeColor = Color.FromArgb(209, 213, 219),
                Font = new Font("Consolas", 9.5f), BorderStyle = BorderStyle.FixedSingle
            };
            this.Controls.Add(lstHistory);
        }

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
                // Generate fresh random suffix if user didn't change default
                seg1 = seg1.Substring(0, Math.Min(2, seg1.Length)) + chars[rng.Next(chars.Length)] + chars[rng.Next(chars.Length)];
                txtPrefix.Text = seg1;
            }

            string key = GenerateKey(seg1);
            txtOutputKey.Text = key;
            try { Clipboard.SetText(key); } catch {}

            string buyer = string.IsNullOrWhiteSpace(txtBuyer.Text) ? "Umum" : txtBuyer.Text.Trim();
            string record = string.Format("[{0}]  {1}  -->  Pembeli: {2}", DateTime.Now.ToString("yyyy-MM-dd HH:mm"), key, buyer);
            try { File.AppendAllText(historyPath, record + Environment.NewLine, Encoding.UTF8); } catch {}
            lstHistory.Items.Insert(0, record);

            MessageBox.Show(
                "Serial Key Pro berhasil dibuat dan disalin ke Clipboard:\n\n" + key + "\n\nKirimkan kode ini kepada pembeli untuk diaktifkan di aplikasi ShieldGuard Firewall.",
                "Lisensi Berhasil Dibuat",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        private void LoadHistory()
        {
            if (!File.Exists(historyPath)) return;
            try
            {
                string[] lines = File.ReadAllLines(historyPath, Encoding.UTF8);
                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    if (!string.IsNullOrWhiteSpace(lines[i])) lstHistory.Items.Add(lines[i]);
                }
            }
            catch {}
        }

        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new KeygenForm());
        }
    }
}

