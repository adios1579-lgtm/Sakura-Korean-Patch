@echo off
chcp 65001 >nul
rem SAKURA～설월화～ 한글패치 누적 업데이트 만들기. 새 버전 번호와 수정 안내를 물어본다.
"C:\Users\Admin\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe" -X utf8 -B "%~dp0build_release.py" update
pause
