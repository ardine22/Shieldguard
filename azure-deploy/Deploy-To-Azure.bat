@echo off
title Deploy ShieldGuard Pro ke Azure Student VM
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Deploy-To-Azure.ps1"
pause

