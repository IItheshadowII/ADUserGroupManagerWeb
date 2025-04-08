@echo off
echo 🔧 Publicando backend sin SingleFile ni Trimming...
dotnet publish "ADUserGroupManagerWeb.csproj" -c Release -o "C:\Deploy\Backend"
echo ✅ Listo. Carpeta publicada: C:\Deploy\Backend
pause
