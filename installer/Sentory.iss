#ifndef MyVersion
  #define MyVersion "1.3.33"
#endif
#ifndef MyNumericVersion
  #define MyNumericVersion "1.3.33.0"
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
#ifndef MyAppId
  #define MyAppId "{{8A13D670-DAA4-4A45-AC21-90076A2B9E79}"
#endif
#ifndef MyOutputBaseFilename
  #define MyOutputBaseFilename "Sentory-win-" + MyArch + "-setup"
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
AppId={#MyAppId}
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
OutputBaseFilename={#MyOutputBaseFilename}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardImageFile=Assets\SentoryWizard.bmp
WizardSmallImageFile=Assets\SentoryWizardSmall.bmp
WizardImageStretch=yes
ShowLanguageDialog=no
CloseApplications=force
RestartApplications=no
ChangesAssociations=no
ChangesEnvironment=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Messages]
english.WelcomeLabel1=Install Sentory
english.WelcomeLabel2=Keep photos and links from your messengers in one place.%n%nSetup will install Sentory {#MyVersion} on this PC.
english.FinishedHeadingLabel=Sentory is ready
korean.WelcomeLabel1=Sentory를 설치합니다
korean.WelcomeLabel2=메신저에서 보낸 사진과 링크를 한 곳에 정리합니다.%n%n계속하면 Sentory {#MyVersion} 설치를 시작합니다.
korean.FinishedHeadingLabel=설치가 끝났습니다
japanese.WelcomeLabel1=Sentory をインストールします
japanese.WelcomeLabel2=メッセンジャーで送信した写真とリンクを一か所にまとめます。%n%n続行すると Sentory {#MyVersion} のインストールを開始します。
japanese.FinishedHeadingLabel=インストールが完了しました

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Sentory"; Filename: "{app}\Sentory.exe"
Name: "{autodesktop}\Sentory"; Filename: "{app}\Sentory.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Sentory.exe"; Description: "{cm:LaunchProgram,Sentory}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\Sentory.exe"; Flags: nowait; Check: IsRegularSentoryUpdate
Filename: "{app}\Sentory.exe"; Parameters: "--verify-installation"; Flags: runhidden waituntilterminated; Check: IsSentoryUpdateTest

[Code]
const
  SentoryBackground = $00DCE4E9;
  SentorySurface = $00ECF3F7;
  SentoryText = $00222729;
  SentoryMutedText = $0061686D;
  SentoryLine = $00BCC7CE;
  SentoryMutexName = 'Local\Sentory.Desktop.Singleton';

function IsSentoryUpdate: Boolean;
begin
  Result := CompareText(
    ExpandConstant('{param:SENTORYUPDATE|0}'), '1') = 0;
end;

function IsSentoryUpdateTest: Boolean;
begin
  Result := IsSentoryUpdate and
    (CompareText(ExpandConstant('{param:SENTORYTEST|0}'), '1') = 0);
end;

function IsRegularSentoryUpdate: Boolean;
begin
  Result := IsSentoryUpdate and not IsSentoryUpdateTest;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Attempt: Integer;
begin
  Result := '';
  if not IsSentoryUpdate then
    exit;

  for Attempt := 0 to 119 do
  begin
    if not CheckForMutexes(SentoryMutexName) then
    begin
      Log('Sentory has exited; continuing the update.');
      exit;
    end;
    Sleep(250);
  end;

  Result := 'Sentory is still running. Please open Sentory and try the update again.';
end;

procedure ApplySentoryWizardStyle;
begin
  WizardForm.Caption := 'Sentory {#MyVersion}';
  WizardForm.Font.Name := 'Malgun Gothic';
  WizardForm.Font.Size := 10;
  WizardForm.Color := SentoryBackground;
  WizardForm.MainPanel.Color := SentorySurface;
  WizardForm.WelcomePage.Color := SentoryBackground;
  WizardForm.FinishedPage.Color := SentoryBackground;
  WizardForm.WelcomeLabel1.Font.Name := 'Georgia';
  WizardForm.WelcomeLabel1.Font.Size := 14;
  WizardForm.WelcomeLabel1.Font.Color := SentoryText;
  WizardForm.WelcomeLabel2.Font.Color := SentoryMutedText;
  WizardForm.FinishedHeadingLabel.Font.Name := 'Georgia';
  WizardForm.FinishedHeadingLabel.Font.Size := 14;
  WizardForm.FinishedHeadingLabel.Font.Color := SentoryText;
  WizardForm.FinishedLabel.Font.Color := SentoryMutedText;
  WizardForm.PageNameLabel.Font.Color := SentoryText;
  WizardForm.PageDescriptionLabel.Font.Color := SentoryMutedText;
  WizardForm.NextButton.Font.Style := [fsBold];
end;

procedure InitializeWizard;
begin
  ApplySentoryWizardStyle;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Sentory');
end;
