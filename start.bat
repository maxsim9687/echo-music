@echo off
cd /d "%~dp0"
if exist .venv\Scripts\python.exe (set PY=.venv\Scripts\python.exe) else (set PY=python)
start "" http://localhost:8765
%PY% server.py
