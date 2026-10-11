; Mebelim Printer Agent — o'rnatuvchi (Inno Setup 6).
; Odatda release.ps1 yig'adi (versiyani /DAppVer bilan beradi). Qo'lda:
;   dotnet publish -c Release
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\MebelimPrinterAgent.iss
; Natija: installer\Output\MebelimPrinterAgent-Setup.exe (nomi o'zgarmas — yuklab olish havolasi doimiy).
;
; Admin huquqisiz, foydalanuvchi papkasiga (%LOCALAPPDATA%\Programs): UAC oynasi chiqmaydi va
; agent o'zini o'zi yangilay oladi (Program Files'ga oddiy foydalanuvchi yoza olmaydi).

#ifndef AppVer
  #define AppVer "2.0.0"
#endif
#define Exe "MebelimPrinterAgent.exe"

[Setup]
AppId={{19A35967-9DE1-4A78-A2ED-B6278A1521F6}
AppName=Mebelim Printer Agent
AppVersion={#AppVer}
AppPublisher=Mebelim
DefaultDirName={autopf}\MebelimPrinterAgent
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
OutputDir=Output
OutputBaseFilename=MebelimPrinterAgent-Setup
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#Exe}
UninstallDisplayName=Mebelim Printer Agent

[Files]
Source: "..\bin\Release\net8.0-windows\win-x64\publish\{#Exe}"; DestDir: "{app}"; Flags: ignoreversion

[Registry]
; Windows'ga kirganda avtomatik ishga tushadi
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MebelimPrinterAgent"; ValueData: """{app}\{#Exe}"""; Flags: uninsdeletevalue
; Eski BekPrintAgent avtoyuklanishi (u ham 1337-portni egallaydi)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "BekPrintAgent"; Flags: deletevalue dontcreatekey

[Run]
Filename: "{app}\{#Exe}"; Flags: nowait

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#Exe} /F"; Flags: runhidden; RunOnceId: "StopAgent"

[UninstallDelete]
Type: files; Name: "{app}\{#Exe}.old"
Type: files; Name: "{app}\{#Exe}.new"

[Code]
procedure Kill(const Name: String);
var
  Code: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM ' + Name + ' /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

// Yangilashda ishlab turgan agent faylni band qilmasin; eski BekPrintAgent to'xtatiladi
// (uning Program Files'dagi fayllarini o'chirish admin so'raydi — zarari yo'q, qoladi).
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Kill('{#Exe}');
  Kill('BekPrintAgent.exe');
  Result := '';
end;
