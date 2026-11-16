const PC_API_BASE = "http://127.0.0.1:8765";

const DEFAULT_STATE = {
  firewall_enabled: true,
  scam_protection_enabled: false,
  cookie_guard_enabled: false,
  pc_connected: false,
  browser_device_id: "",
  is_pro_licensed: false,
  license_key: "",
  licensed_to: "Versi Trial (Gratis)",
  custom_domains_added: 0,
  blocked_domains: [
    "woukox.com",
    "www.woukox.com",
    "mrbeast-casino.com",
    "beast-crypto.com",
    "elon-giveaway.org",
    "stake-bonus-claim.com",
    "crypto-boom-bonus.com",
    "usdt-instant-claim.com",
    "free-2500-usdt.com",
    "discord-nitro-free.ru",
    "instagram-verify-badge.com"
  ],
  whitelisted_domains: [
    "google.com",
    "www.google.com",
    "youtube.com",
    "www.youtube.com",
    "instagram.com",
    "www.instagram.com",
    "facebook.com",
    "www.facebook.com",
    "x.com",
    "twitter.com",
    "github.com",
    "microsoft.com",
    "localhost",
    "127.0.0.1"
  ],
  stats: {
    total_requests_checked: 0,
    threats_blocked: 0,
    scam_pages_detected: 0
  }
};

function normalizeDomain(urlOrDomain) {
  if (!urlOrDomain) return "";
  try {
    if (urlOrDomain.includes("://")) {
      return new URL(urlOrDomain).hostname.toLowerCase();
    }
    return urlOrDomain.split("/")[0].split(":")[0].trim().toLowerCase();
  } catch (e) {
    return "";
  }
}

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

async function getState() {
  return new Promise((resolve) => {
    chrome.storage.local.get(DEFAULT_STATE, (items) => resolve(items));
  });
}

async function saveState(patch) {
  return new Promise((resolve) => {
    chrome.storage.local.set(patch, () => resolve());
  });
}

// Update declarativeNetRequest dynamic rules so blocked domains redirect to blocked.html
async function updateDynamicFirewallRules(blockedDomains, firewallEnabled) {
  try {
    const existing = await chrome.declarativeNetRequest.getDynamicRules();
    const removeRuleIds = existing.map((r) => r.id);

    if (!firewallEnabled || !blockedDomains || blockedDomains.length === 0) {
      if (removeRuleIds.length > 0) {
        await chrome.declarativeNetRequest.updateDynamicRules({ removeRuleIds, addRules: [] });
      }
      return;
    }

    const uniqueDomains = Array.from(
      new Set(
        blockedDomains
          .map((d) => d.replace(/^www\./, "").trim().toLowerCase())
          .filter((d) => d && d !== "localhost" && d !== "127.0.0.1")
      )
    ).slice(0, 400);

    const addRules = uniqueDomains.map((dom, idx) => ({
      id: idx + 1,
      priority: 1,
      action: {
        type: "redirect",
        redirect: {
          extensionPath: `/blocked.html?domain=${encodeURIComponent(dom)}&reason=${encodeURIComponent("Domain berada dalam daftar blokir ShieldGuard Firewall")}`
        }
      },
      condition: {
        urlFilter: `||${dom}^`,
        resourceTypes: ["main_frame", "sub_frame"]
      }
    }));

    await chrome.declarativeNetRequest.updateDynamicRules({
      removeRuleIds,
      addRules
    });
  } catch (err) {
    console.warn("ShieldGuard DNR update error:", err);
  }
}

// Sync state with the PC Firewall Desktop App (http://127.0.0.1:8765)
async function syncWithPCFirewall() {
  try {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), 1800);
    const res = await fetch(`${PC_API_BASE}/api/status`, { signal: controller.signal });
    clearTimeout(timer);

    if (!res.ok) throw new Error("Status not OK");
    const data = await res.json();
    const current = await getState();

    // PC app is the master authority for licensing
    const isPro = data.is_pro_licensed === true;
    const licKey = isPro ? (data.license_key || "") : "";
    const licTo = isPro ? (data.licensed_to || "Pengguna Pro") : "Versi Trial (Gratis)";

    const patch = {
      pc_connected: true,
      is_pro_licensed: isPro,
      license_key: licKey,
      licensed_to: licTo,
      firewall_enabled: data.firewall_enabled !== false,
      scam_protection_enabled: data.scam_protection_enabled !== false,
      cookie_guard_enabled: data.cookie_guard_enabled !== false,
      blocked_domains: Array.isArray(data.blocked_domains) ? data.blocked_domains : DEFAULT_STATE.blocked_domains,
      whitelisted_domains: Array.isArray(data.whitelisted_domains) ? data.whitelisted_domains : DEFAULT_STATE.whitelisted_domains,
      stats: data.stats || DEFAULT_STATE.stats
    };

    await saveState(patch);
    await updateDynamicFirewallRules(patch.blocked_domains, patch.firewall_enabled);
    return patch;
  } catch (e) {
    const current = await getState();

    // In Mode Mandiri (PC App not running): Verify directly with Cloud License API
    // If Admin deleted or reset this key on the server, automatically revoke PRO!
    if (current.is_pro_licensed && current.license_key) {
      try {
        const cloudCtrl = new AbortController();
        const cloudTimer = setTimeout(() => cloudCtrl.abort(), 2000);
        const cloudRes = await fetch("http://70.151.150.190/api/license/verify", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ key: current.license_key, hwid: "" }),
          signal: cloudCtrl.signal
        });
        clearTimeout(cloudTimer);
        if (cloudRes.ok) {
          const cData = await cloudRes.json();
          if (cData.valid === false) {
            current.is_pro_licensed = false;
            current.license_key = "";
            current.licensed_to = "Versi Trial (Gratis)";
            await saveState({
              is_pro_licensed: false,
              license_key: "",
              licensed_to: "Versi Trial (Gratis)",
              pc_connected: false
            });
            await updateDynamicFirewallRules(current.blocked_domains, current.firewall_enabled);
            return { ...current, pc_connected: false };
          }
        }
      } catch (_) {}
    }

    await saveState({ pc_connected: false });
    await updateDynamicFirewallRules(current.blocked_domains, current.firewall_enabled);
    return { ...current, pc_connected: false };
  }
}

// Evaluate URL locally & via PC Firewall
async function checkUrlSafety(url) {
  if (!url || url.startsWith("chrome://") || url.startsWith("edge://") || url.startsWith("chrome-extension://") || url.startsWith("http://127.0.0.1:8765")) {
    return { allowed: true, reason: "Internal page" };
  }

  const state = await getState();
  if (!state.firewall_enabled) {
    return { allowed: true, reason: "Firewall disabled" };
  }

  const domain = normalizeDomain(url);
  if (!domain) return { allowed: true };

  for (const w of state.whitelisted_domains || []) {
    if (domain === w || domain.endsWith("." + w)) {
      return { allowed: true, reason: "Whitelisted" };
    }
  }

  for (const b of state.blocked_domains || []) {
    if (domain === b || domain.endsWith("." + b)) {
      return {
        allowed: false,
        domain,
        reason: `Domain '${domain}' diblokir oleh ShieldGuard Firewall (Situs Scam/Berbahaya).`
      };
    }
  }

  // Heuristic URL pattern checks
  const suspiciousPatterns = [/woukox/i, /beast.*casino/i, /crypto.*giveaway/i, /claim.*usdt/i, /bonus.*2500/i];
  for (const pat of suspiciousPatterns) {
    if (pat.test(url)) {
      return {
        allowed: false,
        domain,
        reason: `URL cocok dengan pola heuristik penipuan kripto/phishing (${pat}).`
      };
    }
  }

  // If connected to PC Firewall, ask PC Firewall too
  if (state.pc_connected) {
    try {
      const res = await fetch(`${PC_API_BASE}/api/check-url`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ url })
      });
      if (res.ok) {
        const data = await res.json();
        if (data.allowed === false) {
          return { allowed: false, domain, reason: data.reason };
        }
      }
    } catch (_) {}
  }

  return { allowed: true, domain };
}

chrome.runtime.onInstalled.addListener(async () => {
  await syncWithPCFirewall();
  chrome.alarms.create("syncPCFirewall", { periodInMinutes: 0.25 });
});

chrome.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === "syncPCFirewall") {
    syncWithPCFirewall();
  }
});

// Secondary tab navigation guard
chrome.tabs.onUpdated.addListener(async (tabId, changeInfo, tab) => {
  const targetUrl = changeInfo.url || tab.url;
  if (!targetUrl || targetUrl.includes("/blocked.html")) return;

  const check = await checkUrlSafety(targetUrl);
  if (!check.allowed) {
    const blockedPage = chrome.runtime.getURL(
      `blocked.html?domain=${encodeURIComponent(check.domain || targetUrl)}&url=${encodeURIComponent(targetUrl)}&reason=${encodeURIComponent(check.reason)}`
    );
    chrome.tabs.update(tabId, { url: blockedPage });
  }
});

// Message handling for Popup, Content Script, and Blocked Page
chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  (async () => {
    if (msg.type === "GET_STATE") {
      const synced = await syncWithPCFirewall();
      sendResponse(synced);
      return;
    }

    // License Revocation / Reset to Trial
    if (msg.type === "REVOKE_LICENSE" || msg.type === "RESET_LICENSE") {
      await saveState({
        is_pro_licensed: false,
        license_key: "",
        licensed_to: "Versi Trial (Gratis)",
        scam_protection_enabled: false,
        cookie_guard_enabled: false
      });
      sendResponse({ ok: true });
      return;
    }

    // License Activation Handler
    if (msg.type === "ACTIVATE_LICENSE") {
      const key = (msg.key || "").trim().toUpperCase();
      const owner = (msg.name || "").trim() || "Pengguna Pro";
      const valid = await verifyLicenseKey(key);

      if (!valid) {
        sendResponse({
          ok: false,
          message: "Serial Key tidak valid! Pastikan format SGPRO-XXXX-XXXX-XXXX yang diterbitkan oleh penjual resmi."
        });
        return;
      }

      // 1. Cloud 1-Device Lock Check & Registration (Direct to Azure Server)
      let cloudHwid = "";
      try {
        const cState = await getState();
        if (!cState.browser_device_id) {
          cloudHwid = "BROWSER-" + Math.random().toString(36).substring(2, 10).toUpperCase();
          await saveState({ browser_device_id: cloudHwid });
        } else {
          cloudHwid = cState.browser_device_id;
        }

        const cloudCtrl = new AbortController();
        const cloudTimer = setTimeout(() => cloudCtrl.abort(), 3500);
        const cloudRes = await fetch("http://70.151.150.190/api/license/activate", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            key: key,
            buyer: owner,
            hwid: cloudHwid,
            device_name: "Chrome Browser Extension"
          }),
          signal: cloudCtrl.signal
        });
        clearTimeout(cloudTimer);

        if (cloudRes.ok) {
          const cData = await cloudRes.json();
          if (cData.ok === false && cData.status === "DEVICE_MISMATCH") {
            sendResponse({
              ok: false,
              message: cData.message || "❌ GAGAL: Serial Key ini sudah terikat pada perangkat lain!"
            });
            return;
          }
        }
      } catch (_) {}

      await saveState({
        is_pro_licensed: true,
        license_key: key,
        licensed_to: owner,
        scam_protection_enabled: true,
        cookie_guard_enabled: true
      });

      // Forward activation to PC Firewall app if connected
      try {
        await fetch(`${PC_API_BASE}/api/activate-license`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ key, name: owner })
        });
      } catch (_) {}

      sendResponse({
        ok: true,
        message: `🎉 Selamat! ShieldGuard Pro Extension berhasil diaktifkan untuk: ${owner}`
      });
      return;
    }

    if (msg.type === "BLOCK_DOMAIN") {
      const state = await getState();
      const dom = normalizeDomain(msg.domain);
      if (!dom) {
        sendResponse({ ok: false, message: "Domain tidak valid" });
        return;
      }

      // Monetization Gate: Trial users can only add up to 3 custom domains
      const currentCustom = state.custom_domains_added || 0;
      if (!state.is_pro_licensed && currentCustom >= 3) {
        sendResponse({
          ok: false,
          is_trial_limit: true,
          message: "Batas Trial Tercapai: Versi Gratis dibatasi maksimal 3 domain kustom. Masukkan Serial Key Pro untuk menambah domain tanpa batas!"
        });
        return;
      }

      const updated = Array.from(new Set([...(state.blocked_domains || []), dom, `www.${dom}`]));
      await saveState({
        blocked_domains: updated,
        custom_domains_added: currentCustom + 1
      });
      await updateDynamicFirewallRules(updated, state.firewall_enabled);

      // Push to PC Firewall if running
      try {
        await fetch(`${PC_API_BASE}/api/block-domain`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ domain: dom, reason: msg.reason || "Diblokir dari Ekstensi Chrome/Edge" })
        });
      } catch (_) {}

      sendResponse({ ok: true, blocked_domains: updated });
      return;
    }

    if (msg.type === "UNBLOCK_DOMAIN") {
      const dom = normalizeDomain(msg.domain).replace(/^www\./, "");
      const state = await getState();
      const updated = (state.blocked_domains || []).filter((d) => d !== dom && d !== `www.${dom}`);
      await saveState({ blocked_domains: updated });
      await updateDynamicFirewallRules(updated, state.firewall_enabled);

      try {
        await fetch(`${PC_API_BASE}/api/unblock-domain`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ domain: dom })
        });
      } catch (_) {}

      sendResponse({ ok: true, blocked_domains: updated });
      return;
    }

    if (msg.type === "TOGGLE_SETTING") {
      const { key, value } = msg;
      const state = await getState();

      // Pro features restriction check
      if ((key === "scam_protection_enabled" || key === "cookie_guard_enabled") && value && !state.is_pro_licensed) {
        sendResponse({
          ok: false,
          is_pro_feature: true,
          message: "Fitur Proteksi Heuristik Scam & Cookie Guard adalah fitur eksklusif ShieldGuard PRO. Silakan masukkan Serial Key Pro untuk mengaktifkannya!"
        });
        return;
      }

      await saveState({ [key]: value });
      const updatedState = await getState();
      await updateDynamicFirewallRules(updatedState.blocked_domains, updatedState.firewall_enabled);

      try {
        await fetch(`${PC_API_BASE}/api/toggle`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ key, value })
        });
      } catch (_) {}

      sendResponse({ ok: true });
      return;
    }

    if (msg.type === "SCAN_CONTENT") {
      const state = await getState();
      if (!state.firewall_enabled || !state.scam_protection_enabled) {
        sendResponse({ allowed: true, risk_score: 0 });
        return;
      }

      // First try PC Firewall API
      let remoteResult = null;
      try {
        const res = await fetch(`${PC_API_BASE}/api/scan-content`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            url: msg.url || "",
            title: msg.title || "",
            text: (msg.text || "").slice(0, 15000)
          })
        });
        if (res.ok) {
          remoteResult = await res.json();
        }
      } catch (_) {}

      const textLower = `${msg.title || ""} ${msg.text || ""}`.toLowerCase();
      const triggers = [
        "launch of my very own crypto casino",
        "giving away $2,500 to everyone who registers",
        "special promo code: boom",
        "promo code: boom",
        "withdraw the bonus instantly",
        "your withdrawal of $2500.00 was successfully",
        "woukox.com"
      ];
      const matched = triggers.filter((t) => textLower.includes(t));
      const hasScam = (remoteResult && !remoteResult.allowed) || matched.length > 0;

      if (hasScam) {
        // If user is in TRIAL mode, let them know scam was detected but full automated blocking requires Pro
        if (!state.is_pro_licensed) {
          sendResponse({
            allowed: true,
            is_trial_warning: true,
            risk_score: 95,
            matched: matched.length ? matched : (remoteResult ? remoteResult.matched : []),
            reason: "Terdeteksi indikasi Penipuan Kasino Kripto / Giveaway Palsu (MrBeast / $2,500)! Mode Trial: Upgrade ke ShieldGuard Pro untuk pemblokiran otomatis."
          });
          return;
        }

        sendResponse({
          allowed: false,
          is_pro: true,
          risk_score: 100,
          matched: matched.length ? matched : (remoteResult ? remoteResult.matched : []),
          reason: "Terdeteksi Penipuan Kasino Kripto / Giveaway Palsu (Modus MrBeast / $2,500 / BOOM)."
        });
        return;
      }

      sendResponse({ allowed: true, risk_score: 0, matched: [] });
      return;
    }

    if (msg.type === "LOG_EVENT") {
      try {
        await fetch(`${PC_API_BASE}/api/log-event`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(msg.payload || {})
        });
      } catch (_) {}
      sendResponse({ ok: true });
      return;
    }
  })();
  return true;
});
