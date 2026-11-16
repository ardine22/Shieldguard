let currentTabDomain = "";
let appState = {};

function extractDomain(url) {
  try {
    const u = new URL(url);
    if (u.protocol === "http:" || u.protocol === "https:") {
      return u.hostname.toLowerCase();
    }
    return "";
  } catch (_) {
    return "";
  }
}

// Standalone offline license verification inside popup.js
async function verifyLicenseKey(key) {
  if (!key || typeof key !== "string") return false;
  const clean = key.trim().toUpperCase();
  const parts = clean.split("-");
  if (parts.length !== 4 || parts[0] !== "SGPRO") return false;
  if (parts[1].length !== 4 || parts[2].length !== 4 || parts[3].length !== 4) return false;

  const seg1 = parts[1];
  const encoder = new TextEncoder();
  const data = encoder.encode("SG_SECRET_2026:" + seg1);
  const hashBuffer = await crypto.subtle.digest("SHA-256", data);
  const hashArray = Array.from(new Uint8Array(hashBuffer));
  const hex = hashArray.map((b) => b.toString(16).padStart(2, "0")).join("").toUpperCase();

  return parts[2] === hex.substring(0, 4) && parts[3] === hex.substring(4, 8);
}

// Get or generate persistent Device ID for browser
async function getExtensionHwid() {
  return new Promise((resolve) => {
    chrome.storage.local.get(["extension_hwid"], (res) => {
      if (res && res.extension_hwid) {
        resolve(res.extension_hwid);
      } else {
        const id = "EXT-" + Math.random().toString(36).substring(2, 8).toUpperCase() + "-" + Date.now().toString(36).toUpperCase();
        chrome.storage.local.set({ extension_hwid: id }, () => resolve(id));
      }
    });
  });
}

function renderState(state) {
  appState = state || {};
  const isPro = state.is_pro_licensed === true;

  // Header & Status
  const pcStatus = document.getElementById("pcSyncStatus");
  if (state.pc_connected) {
    pcStatus.textContent = "🟢 Terhubung ke PC Firewall";
    pcStatus.style.color = "#34d399";
  } else {
    pcStatus.textContent = "🟡 Mode Mandiri (Buka PC App untuk Sinkron)";
    pcStatus.style.color = "#fbbf24";
  }

  // License Card UI
  const badge = document.getElementById("badgeLicense");
  const licenseCard = document.getElementById("licenseCard");
  const trialSec = document.getElementById("trialSection");
  const proSec = document.getElementById("proSection");
  const trialNotice = document.getElementById("trialLimitNotice");

  if (isPro) {
    badge.textContent = "PRO";
    badge.className = "badge-pill pro";
    licenseCard.className = "license-box pro-mode";
    trialSec.style.display = "none";
    proSec.style.display = "block";
    document.getElementById("proOwnerName").textContent = state.licensed_to || "Pengguna Pro";
    document.getElementById("proKeyView").textContent = state.license_key || "SGPRO-LIFETIME";
    if (trialNotice) trialNotice.style.display = "none";
  } else {
    badge.textContent = "TRIAL";
    badge.className = "badge-pill trial";
    licenseCard.className = "license-box trial-mode";
    trialSec.style.display = "block";
    proSec.style.display = "none";
    if (trialNotice) trialNotice.style.display = "block";
  }

  // Main Toggle Button
  const mainBtn = document.getElementById("toggleMainBtn");
  if (state.firewall_enabled !== false) {
    mainBtn.textContent = "AKTIF";
    mainBtn.className = "power-btn on";
  } else {
    mainBtn.textContent = "NONAKTIF";
    mainBtn.className = "power-btn off";
  }

  document.getElementById("chkScam").checked = state.scam_protection_enabled !== false;
  document.getElementById("chkCookie").checked = state.cookie_guard_enabled !== false;

  const stats = state.stats || {};
  const domains = state.blocked_domains || [];
  document.getElementById("statBlocked").textContent = stats.threats_blocked || 0;
  document.getElementById("statScam").textContent = stats.scam_pages_detected || 0;
  document.getElementById("statDomains").textContent = domains.length;

  const listEl = document.getElementById("blockedList");
  listEl.innerHTML = "";
  domains
    .filter((d) => !d.startsWith("www."))
    .forEach((dom) => {
      const tag = document.createElement("div");
      tag.className = "tag";
      tag.innerHTML = `<span>${dom}</span><span class="del" data-dom="${dom}">✕</span>`;
      tag.querySelector(".del").addEventListener("click", () => {
        chrome.runtime.sendMessage({ type: "UNBLOCK_DOMAIN", domain: dom }, () => refreshPopup());
      });
      listEl.appendChild(tag);
    });
}

function refreshPopup() {
  chrome.storage.local.get(null, (localData) => {
    chrome.runtime.sendMessage({ type: "GET_STATE" }, (res) => {
      const merged = Object.assign({}, localData || {}, res || {});
      renderState(merged);
    });
  });
}

document.addEventListener("DOMContentLoaded", () => {
  chrome.tabs.query({ active: true, currentWindow: true }, (tabs) => {
    if (tabs && tabs[0] && tabs[0].url) {
      currentTabDomain = extractDomain(tabs[0].url);
      document.getElementById("currentDomain").textContent = currentTabDomain || "Halaman Internal Browser";
    }
  });

  refreshPopup();

  // License Activation Button - Direct + Background verification
  document.getElementById("btnActivateLicense").addEventListener("click", async () => {
    const nameInput = document.getElementById("licNameInput");
    const keyInput = document.getElementById("licKeyInput");
    const name = (nameInput.value || "").trim() || "Pengguna Pro";
    const key = (keyInput.value || "").trim().toUpperCase();

    if (!key) {
      alert("Silakan masukkan Serial Key Pro terlebih dahulu.");
      return;
    }

    const isValid = await verifyLicenseKey(key);
    if (!isValid) {
      alert("❌ Serial Key tidak valid!\n\nPastikan formatnya SGPRO-XXXX-XXXX-XXXX yang dihasilkan dari aplikasi 'ShieldGuard-Keygen-Admin.exe'.");
      return;
    }

    // Cloud HWID 1-Device Check
    const hwid = await getExtensionHwid();
    try {
      const cloudRes = await fetch("http://70.151.150.190/api/license/activate", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          key,
          hwid,
          buyer: name,
          device_name: "Chrome/Edge Browser"
        })
      });
      if (cloudRes.ok) {
        const cloudData = await cloudRes.json();
        if (cloudData.ok === false && cloudData.status === "DEVICE_MISMATCH") {
          alert("❌ PELANGGARAN LISENSI (1 LISENSI = 1 DEVICE)!\n\n" + cloudData.message);
          return;
        }
      }
    } catch (_) {}

    // 1. Save directly to local storage
    await new Promise((resolve) => {
      chrome.storage.local.set(
        {
          is_pro_licensed: true,
          license_key: key,
          licensed_to: name
        },
        resolve
      );
    });

    // 2. Also notify background service worker
    chrome.runtime.sendMessage({
      type: "ACTIVATE_LICENSE",
      name,
      key
    });

    // 3. Forward to PC Desktop app if running
    try {
      fetch("http://127.0.0.1:8765/api/activate-license", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ key, name })
      }).catch(() => {});
    } catch (_) {}

    alert(`🎉 Selamat! ShieldGuard Pro Extension berhasil diaktifkan untuk: ${name}`);
    refreshPopup();
  });

  // Revoke / Reset License to Trial
  const btnRevoke = document.getElementById("btnRevokeLicense");
  if (btnRevoke) {
    btnRevoke.addEventListener("click", () => {
      if (confirm("Apakah Anda yakin ingin mencabut lisensi PRO dari ekstensi ini dan kembali ke Versi Trial?")) {
        chrome.runtime.sendMessage({ type: "REVOKE_LICENSE" }, () => {
          chrome.storage.local.set(
            {
              is_pro_licensed: false,
              license_key: "",
              licensed_to: "Versi Trial (Gratis)"
            },
            () => {
              alert("Lisensi telah dicabut. Ekstensi kembali ke Versi Trial.");
              refreshPopup();
            }
          );
        });
      }
    });
  }

  // Buy License Link
  document.getElementById("btnBuyLicense").addEventListener("click", (e) => {
    e.preventDefault();
    chrome.tabs.create({ url: "http://127.0.0.1:8765/store#harga" });
  });

  document.getElementById("toggleMainBtn").addEventListener("click", () => {
    const nextVal = !(appState.firewall_enabled !== false);
    chrome.runtime.sendMessage({ type: "TOGGLE_SETTING", key: "firewall_enabled", value: nextVal }, () => refreshPopup());
  });

  document.getElementById("chkScam").addEventListener("change", (e) => {
    if (!appState.is_pro_licensed && e.target.checked) {
      e.target.checked = false;
      alert("Fitur Heuristik Scam Kripto adalah fitur eksklusif ShieldGuard PRO. Masukkan Serial Key Pro di kotak aktivasi atas!");
      return;
    }
    chrome.runtime.sendMessage({ type: "TOGGLE_SETTING", key: "scam_protection_enabled", value: e.target.checked }, (res) => {
      if (res && res.is_pro_feature) {
        alert(res.message);
        e.target.checked = false;
      }
      refreshPopup();
    });
  });

  document.getElementById("chkCookie").addEventListener("change", (e) => {
    if (!appState.is_pro_licensed && e.target.checked) {
      e.target.checked = false;
      alert("Fitur Pelindung Cookie & Token Akun (Anti-Self-XSS) adalah fitur eksklusif ShieldGuard PRO. Masukkan Serial Key Pro!");
      return;
    }
    chrome.runtime.sendMessage({ type: "TOGGLE_SETTING", key: "cookie_guard_enabled", value: e.target.checked }, (res) => {
      if (res && res.is_pro_feature) {
        alert(res.message);
        e.target.checked = false;
      }
      refreshPopup();
    });
  });

  document.getElementById("btnBlockCurrent").addEventListener("click", () => {
    if (!currentTabDomain || currentTabDomain === "127.0.0.1" || currentTabDomain === "localhost") {
      alert("Halaman ini tidak dapat diblokir.");
      return;
    }
    chrome.runtime.sendMessage(
      { type: "BLOCK_DOMAIN", domain: currentTabDomain, reason: "Blokir 1-Klik dari Popup Ekstensi" },
      (res) => {
        if (res && res.is_trial_limit) {
          alert(res.message);
          return;
        }
        refreshPopup();
        chrome.tabs.query({ active: true, currentWindow: true }, (tabs) => {
          if (tabs && tabs[0]) {
            const blockedUrl = chrome.runtime.getURL(
              `blocked.html?domain=${encodeURIComponent(currentTabDomain)}&reason=${encodeURIComponent("Diblokir oleh pengguna melalui tombol 1-Klik Ekstensi")}`
            );
            chrome.tabs.update(tabs[0].id, { url: blockedUrl });
          }
        });
      }
    );
  });

  document.getElementById("btnOpenPCDash").addEventListener("click", () => {
    chrome.tabs.create({ url: "http://127.0.0.1:8765" });
  });

  document.getElementById("btnAddDomain").addEventListener("click", () => {
    const input = document.getElementById("inputDomain");
    const val = input.value.trim();
    if (!val) return;
    chrome.runtime.sendMessage(
      { type: "BLOCK_DOMAIN", domain: val, reason: "Ditambahkan dari Popup Ekstensi Chrome/Edge" },
      (res) => {
        if (res && res.is_trial_limit) {
          alert(res.message);
          return;
        }
        input.value = "";
        refreshPopup();
      }
    );
  });
});
