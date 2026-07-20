#ifndef MyVersion
  #define MyVersion "1.1.0"
#endif
#ifndef MyNumericVersion
  #define MyNumericVersion "1.1.0.0"
#endif
#ifndef MyArch
  #define MyArch "x64"
#endif
#ifndef SourceDir
  #error SourceDir must be provided by the release script.
#endif
#ifndef OutputDir
  #error OutputDir must be provided by the release script.
#endif
#ifndef LicenseFile
  #error LicenseFile must be provided by the release script.
#endif
#ifndef IconFile
  #error IconFile must be provided by the release script.
#endif

#if MyArch == "arm64"
  #define ArchitectureName "ARM64"
  #define AllowedArchitectures "arm64"
  #define InstallArchitectures "arm64"
#else
  #define ArchitectureName "x64"
  #define AllowedArchitectures "x64compatible and not arm64"
  #define InstallArchitectures "x64compatible"
#endif

[Setup]
AppId={{8A13D670-DAA4-4A45-AC21-90076A2B9E79}
AppName=Sentory
AppVersion={#MyVersion}
AppVerName=Sentory {#MyVersion} ({#ArchitectureName})
AppPublisher=NudeNyang
AppCopyright=Copyright © 2026 NudeNyang
VersionInfoVersion={#MyNumericVersion}
VersionInfoCompany=NudeNyang
VersionInfoDescription=Sentory {#ArchitectureName} Installer
VersionInfoCopyright=Copyright © 2026 NudeNyang
DefaultDirName={localappdata}\Programs\Sentory
DefaultGroupName=Sentory
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=lowest
ArchitecturesAllowed={#AllowedArchitectures}
ArchitecturesInstallIn64BitMode={#InstallArchitectures}
MinVersion=10.0.17763
SetupIconFile={#IconFile}
UninstallDisplayIcon={app}\Sentory.exe
LicenseFile={#LicenseFile}
OutputDir={#OutputDir}
OutputBaseFilename=Sentory-win-{#MyArch}-setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
RestartApplications=no
AppMutex=Local\Sentory.Desktop.Singleton
ChangesAssociations=no
ChangesEnvironment=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Sentory"; Filename: "{app}\Sentory.exe"
Name: "{autodesktop}\Sentory"; Filename: "{app}\Sentory.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Sentory.exe"; Description: "{cm:LaunchProgram,Sentory}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Sentory');
end;
