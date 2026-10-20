; Inštalátor programu EviTA (Inno Setup 6)
; Zostavuje sa automaticky cez GitHub Actions, verziu a priečinok s programom dodá zostavenie.

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

#define AppName "EviTA"
#define AppExe "EviTA.exe"

[Setup]
AppId={{8F3C2A51-7D4B-4E8A-9B1C-2E5F6A7D8C90}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=tarzanislav
AppComments=Evidencia informačných technológií a príslušenstva
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\EviTA
DefaultGroupName=EviTA
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=EviTA_Setup_{#AppVersion}
SetupIconFile=..\src\Evita.App\Assets\evita.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
WizardStyle=modern
MinVersion=10.0.17763
CloseApplications=yes

[Languages]
#if FileExists(CompilerPath + "Languages\Slovak.isl")
Name: "sk"; MessagesFile: "compiler:Languages\Slovak.isl"
#else
Name: "en"; MessagesFile: "compiler:Default.isl"
#endif

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Dirs]
; spoločný priečinok s databázou – zapisovať doň môžu všetci používatelia počítača
Name: "{commonappdata}\EviTA"; Permissions: users-modify
Name: "{commonappdata}\EviTA\Data"; Permissions: users-modify
; Kalendárium – spoločné dáta (meniny, sviatky) pre všetky naše programy
Name: "{commonappdata}\Kalendarium"; Permissions: users-modify

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\EviTA"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\EviTA"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; mobil ako skener: povoliť pripojenie z miestnej siete (súkromná a doménová sieť, len porty EviTA)
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""EviTA mobil ako skener"""; Flags: runhidden
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""EviTA mobil ako skener"" dir=in action=allow program=""{app}\{#AppExe}"" protocol=TCP localport=8765-8769 profile=private,domain"; Flags: runhidden
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,EviTA}"; Flags: nowait postinstall skipifsilent
; po automatickej aktualizácii z programu (tichá inštalácia s parametrom /SPUSTIT=1) sa EviTA znova spustí
Filename: "{app}\{#AppExe}"; Flags: nowait runasoriginaluser; Check: SpustitPoAktualizacii

[UninstallRun]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""EviTA mobil ako skener"""; Flags: runhidden; RunOnceId: "ZmazatPravidloFirewallu"
; úloha pravidelnej zálohy v Plánovači úloh Windows (ak bola nastavená)
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""EviTA zaloha"" /F"; Flags: runhidden; RunOnceId: "ZmazatUlohuZalohy"

[Code]
function SpustitPoAktualizacii: Boolean;
begin
  Result := ExpandConstant('{param:SPUSTIT|0}') = '1';
end;

// Databáza (C:\ProgramData\EviTA) a zálohy (Dokumenty\EviTA) sa pri odinštalovaní nemažú.
