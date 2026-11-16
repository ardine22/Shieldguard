@echo off
title ShieldGuard Pro Firewall (Administrator Mode)
powershell -Command "Start-Process -FilePath '%~dp0produk-siap-jual\ShieldGuardFirewall.exe' -Verb RunAs"
