const params = new URLSearchParams(window.location.search);
const domain = params.get("domain") || "Domain tidak diketahui";
const reason = params.get("reason") || "Domain ini masuk dalam daftar hitam ShieldGuard Firewall.";

document.getElementById("blockedDomain").textContent = domain;
document.getElementById("blockedReason").textContent = reason;

document.getElementById("btnSafeBack").addEventListener("click", () => {
  if (window.history.length > 2) {
    window.history.go(-2);
  } else {
    window.location.href = "https://www.google.com";
  }
});

document.getElementById("btnOpenDashboard").addEventListener("click", () => {
  window.open("http://127.0.0.1:8765", "_blank");
});

document.getElementById("btnUnblock").addEventListener("click", () => {
  if (!confirm(`Apakah Anda yakin ingin membuka blokir untuk '${domain}'? Jika ini situs scam kripto/phishing, uang atau akun Anda berisiko dicuri.`)) {
    return;
  }
  chrome.runtime.sendMessage({ type: "UNBLOCK_DOMAIN", domain }, () => {
    window.location.href = `https://${domain}`;
  });
});

