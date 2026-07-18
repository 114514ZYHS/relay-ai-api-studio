@echo off
setlocal
set "ROOT=%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo .NET Framework C# compiler was not found.
  exit /b 1
)
if not exist "%ROOT%dist" mkdir "%ROOT%dist"
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /debug- /codepage:65001 /win32manifest:"%ROOT%src\RelayAIStudio.manifest" /win32icon:"%ROOT%src\Relay.ico" /out:"%ROOT%dist\RelayAIStudio.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll "%ROOT%src\RelayAIStudio.cs"
if errorlevel 1 exit /b %errorlevel%
echo Built: %ROOT%dist\RelayAIStudio.exe
