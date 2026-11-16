(function () {
  if (window.location.href.startsWith("http://127.0.0.1:8765")) return;

  function safeSendMessage(msg, callback) {
    try {
      if (typeof chrome !== "undefined" && chrome.runtime && chrome.runtime.id) {
        chrome.runtime.sendMessage(msg, (res) => {
          if (chrome.runtime.lastError) return;
          if (callback) callback(res);
        });
      }
    } catch (_) {}
  }

  let alreadyAlerted = false;
  const hostname = window.location.hostname.toLowerCase();
  const isSocialMedia = [
    "instagram.com",
    "facebook.com",
    "x.com",
    "twitter.com",
    "discord.com",
    "threads.net",
    "reddit.com",
    "tiktok.com",
    "youtube.com"
  ].some((d) => hostname === d || hostname.endsWith("." + d));

  // 1. Cookie & Session Hijack Guard (Anti-Self-XSS & Token Stealer Paste Guard)
  document.addEventListener(
    "paste",
    (e) => {
      try {
        const pasted = (e.clipboardData || window.clipboardData).getData("text") || "";
        const lower = pasted.toLowerCase();
        const dangerousPatterns = [
          "document.cookie",
          "discord.com/api/webhooks",
          "localstorage.getItem",
          "sessionid",
          "xmlhttprequest",
          "eval(atob("
        ];
        if (dangerousPatterns.some((p) => lower.includes(p))) {
          e.preventDefault();
          e.stopPropagation();
          showToastWarning(
            "🛡️ ShieldGuard Cookie & Session Guard",
            "PERINGATAN KERAS: Teks yang Anda tempel (paste) mengandung skrip pencuri Cookie / Token Akun! Aksi paste dibatalkan demi keamanan akun Anda.",
            [],
            true
          );
          safeSendMessage({
            type: "LOG_EVENT",
            payload: {
              level: "CRITICAL",
              category: "COOKIE_GUARD",
              target: hostname,
              details: "Upaya paste skrip pencuri Cookie/Token (Self-XSS) berhasil dicegah!",
              action: "BLOCKED"
            }
          });
        }
      } catch (_) {}
    },
    true
  );

  // 2. Scan page content for Crypto Casino Scams / Giveaway Phishing
  function runContentScan() {
    if (alreadyAlerted || !document.body) return;
    const textSample = document.body.innerText || "";
    if (textSample.length < 30) return;

    safeSendMessage(
      {
        type: "SCAN_CONTENT",
        url: window.location.href,
        title: document.title || "",
        text: textSample.slice(0, 12000)
      },
      (res) => {
        if (!res) return;

        // If in Trial mode and scam detected -> Show Trial alert with Upgrade prompt
        if (res.is_trial_warning) {
          alreadyAlerted = true;
          showToastWarning(
            "⚠️ ShieldGuard Trial: Scam Kripto Terdeteksi!",
            `${res.reason} Aktifkan Serial Key Pro pada ekstensi untuk pemblokiran otomatis & fitur Anti-Stealer penuh.`,
            res.matched || [],
            false,
            true
          );
          return;
        }

        // If Pro and threat confirmed -> Full Block
        if (res.allowed === false && res.risk_score >= 45) {
          alreadyAlerted = true;
          if (isSocialMedia) {
            showToastWarning(
              "🚨 Peringatan Scam Terdeteksi di Halaman Ini!",
              `${res.reason} Jangan pernah mengunjungi domain atau memasukkan kode promo yang tertera pada postingan/DM tersebut.`,
              res.matched || []
            );
          } else {
            showFullBlockOverlay(res.reason, res.risk_score, res.matched || []);
          }
        }
      }
    );
  }

  function showToastWarning(title, message, matched, isCritical = false, isTrial = false) {
    if (document.getElementById("shieldguard-toast-alert")) return;
    const box = document.createElement("div");
    box.id = "shieldguard-toast-alert";
    const borderColor = isTrial ? "#f59e0b" : "#ef4444";
    box.style.cssText = `
      position: fixed; top: 20px; right: 20px; width: 400px; z-index: 2147483647;
      background: linear-gradient(135deg, #111827 0%, #1f2937 100%);
      color: #f9fafb; border: 2px solid ${borderColor}; border-radius: 12px;
      padding: 16px 18px; box-shadow: 0 20px 35px rgba(0,0,0,0.65);
      font-family: 'Segoe UI', system-ui, sans-serif; font-size: 13px; line-height: 1.45;
    `;
    const matchHtml =
      matched && matched.length
        ? `<div style="margin-top:8px;padding:6px 8px;background:rgba(239,68,68,0.15);border-radius:6px;color:#fca5a5;font-size:11px;">
             <b>Indikator Terdeteksi:</b> ${matched.slice(0, 3).join(", ")}
           </div>`
        : "";

    const trialAction = isTrial
      ? `<div style="margin-top:10px;text-align:right;">
           <span style="background:#f59e0b;color:#000;font-weight:700;font-size:11px;padding:4px 10px;border-radius:6px;cursor:pointer;">
             🔑 Buka Ekstensi untuk Aktivasi PRO
           </span>
         </div>`
      : "";

    box.innerHTML = `
      <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:6px;">
        <strong style="color:${isTrial ? "#fbbf24" : "#f87171"};font-size:14px;">${title}</strong>
        <span id="sg-close-toast" style="cursor:pointer;color:#9ca3af;font-weight:bold;font-size:16px;">✕</span>
      </div>
      <div style="color:#e5e7eb;">${message}</div>
      ${matchHtml}
      ${trialAction}
    `;
    document.body.appendChild(box);
    document.getElementById("sg-close-toast").onclick = () => box.remove();
  }

  function showFullBlockOverlay(reason, score, matched) {
    if (document.getElementById("shieldguard-full-overlay")) return;
    const overlay = document.createElement("div");
    overlay.id = "shieldguard-full-overlay";
    overlay.style.cssText = `
      position: fixed; inset: 0; z-index: 2147483647;
      background: rgba(11, 15, 25, 0.97); color: #f9fafb;
      display: flex; align-items: center; justify-content: center;
      font-family: 'Segoe UI', system-ui, sans-serif; padding: 24px;
    `;
    overlay.innerHTML = `
      <div style="max-width:580px;width:100%;background:#111827;border:2px solid #ef4444;border-radius:16px;padding:32px;text-align:center;box-shadow:0 25px 50px rgba(0,0,0,0.8);">
        <div style="font-size:54px;margin-bottom:12px;">🛡️🚨</div>
        <h1 style="font-size:24px;color:#f87171;margin-bottom:10px;">Situs Scam / Berbahaya Dihentikan!</h1>
        <p style="color:#d1d5db;font-size:14px;line-height:1.6;margin-bottom:18px;">
          <b>ShieldGuard PRO Browser Extension</b> mendeteksi pola penipuan aktif pada halaman <b>${hostname}</b> (Skor Risiko: <b>${score}/100</b>).
        </p>
        <div style="text-align:left;background:#1f2937;padding:14px;border-radius:10px;font-size:13px;color:#fca5a5;margin-bottom:22px;">
          <div><b>Alasan Pemblokiran:</b> ${reason}</div>
          ${matched.length ? `<div style="margin-top:6px;color:#9ca3af;"><b>Pola Terdeteksi:</b> ${matched.join(" | ")}</div>` : ""}
        </div>
        <div style="display:flex;gap:10px;justify-content:center;flex-wrap:wrap;">
          <button id="sg-btn-block-domain" style="background:#ef4444;color:#fff;border:none;padding:11px 18px;border-radius:8px;font-weight:700;cursor:pointer;">
            ⛔ Blokir Permanen Domain (${hostname})
          </button>
          <button id="sg-btn-back" style="background:#2563eb;color:#fff;border:none;padding:11px 18px;border-radius:8px;font-weight:700;cursor:pointer;">
            ⬅️ Kembali ke Halaman Aman
          </button>
          <button id="sg-btn-ignore" style="background:transparent;color:#9ca3af;border:1px solid #374151;padding:11px 14px;border-radius:8px;cursor:pointer;font-size:12px;">
            Abaikan (Risiko Tinggi)
          </button>
        </div>
      </div>
    `;
    document.body.appendChild(overlay);

    document.getElementById("sg-btn-back").onclick = () => {
      if (window.history.length > 1) window.history.back();
      else window.location.href = "https://www.google.com";
    };
    document.getElementById("sg-btn-block-domain").onclick = () => {
      safeSendMessage(
        {
          type: "BLOCK_DOMAIN",
          domain: hostname,
          reason: "Diblokir dari perisai deteksi konten Scam"
        },
        () => {
          try {
            window.location.href = chrome.runtime.getURL(
              `blocked.html?domain=${encodeURIComponent(hostname)}&reason=${encodeURIComponent(reason)}`
            );
          } catch (_) {}
        }
      );
    };
    document.getElementById("sg-btn-ignore").onclick = () => overlay.remove();
  }

  setTimeout(runContentScan, 1200);
  setInterval(runContentScan, 5000);
})();
