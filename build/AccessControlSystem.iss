; Access Control System installer (Inno Setup 6). Built by build.ps1 from dist\app.
; Installs per user (no admin prompt).
#define AppVersion GetEnv("ACS_VERSION")
#if AppVersion == ""
  #define AppVersion "0.9.0"
#endif

[Setup]
AppId={{5AD3E013-2339-4F41-B58A-28847CB608DC}
AppName=Access Control System
AppVersion={#AppVersion}
AppVerName=Access Control System {#AppVersion}
AppPublisher=Access Control System
AppComments=Đăng ký khuôn mặt và vân tay cho máy kiểm soát ra vào Hikvision
DefaultDirName={localappdata}\Programs\AccessControlSystem
DisableDirPage=auto
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=AccessControlSystem-Setup-{#AppVersion}
SetupIconFile=..\src\Acs.App\Assets\acs.ico
UninstallDisplayIcon={app}\AccessControlSystem.exe
UninstallDisplayName=Access Control System
Compression=lzma2/max
SolidCompression=yes
LZMAUseSeparateProcess=yes
LZMANumBlockThreads=2
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
ShowLanguageDialog=no

[Languages]
Name: "vi"; MessagesFile: "Vietnamese.isl"

[Tasks]
Name: "desktopicon"; Description: "Tạo biểu tượng ngoài màn hình"; GroupDescription: "Thêm:"
Name: "startup"; Description: "Chạy cùng Windows (để tự gửi dữ liệu còn chờ)"; GroupDescription: "Thêm:"

[Files]
Source: "..\dist\app\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\Access Control System"; Filename: "{app}\AccessControlSystem.exe"
Name: "{autodesktop}\Access Control System"; Filename: "{app}\AccessControlSystem.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "AccessControlSystem"; ValueData: """{app}\AccessControlSystem.exe"" --minimized"; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\AccessControlSystem.exe"; Description: "Mở Access Control System ngay"; Flags: nowait postinstall skipifsilent
; An update started from inside the app runs silently: open the app again when it is done.
Filename: "{app}\AccessControlSystem.exe"; Parameters: "--updated"; Flags: nowait; Check: IsUpdate

[Code]
// The app starts its own update with /UPDATE=1 and then closes.
function IsUpdate: Boolean;
begin
  Result := ExpandConstant('{param:update|0}') = '1';
end;

// Best effort: give the app up to 30 seconds to save and close before files are replaced.
// A copy started with ACS_DATA has a suffixed mutex name this wait cannot see; Inno's
// CloseApplications=yes handles that case.
function InitializeSetup: Boolean;
var
  i: Integer;
begin
  if IsUpdate then
    for i := 1 to 60 do
    begin
      if not CheckForMutexes('Local\AccessControlSystem-' + GetUserNameString) then Break;
      Sleep(500);
    end;
  Result := True;
end;
