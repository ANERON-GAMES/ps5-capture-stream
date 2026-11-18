@echo off
title ANERON DEVICES
color 0F
mode con cols=84 lines=30
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Devices.ps1"
