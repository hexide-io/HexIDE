#ifndef AppVersion
  #error AppVersion must be supplied by the build
#endif
#ifndef PublishDir
  #error PublishDir must name the staged self-contained Desktop publish
#endif

#ifndef AppId
  #define AppId "{{B2630434-546D-47B0-A96E-EE613B99F71D}"
#endif
#ifndef AppName
  #define AppName "HexIDE"
#endif
#ifndef AppExe
  #define AppExe "HexIDE.exe"
#endif
#ifndef SetupOutputDir
  #define SetupOutputDir "..\IDE\bin"
#endif
#ifndef SetupBaseName
  #define SetupBaseName "hexide-windows-amd64-setup"
#endif

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Uninstallable=yes
UninstallDisplayIcon={app}\{#AppExe}
OutputDir={#SetupOutputDir}
OutputBaseFilename={#SetupBaseName}
Compression=lzma2
SolidCompression=yes

[Dirs]
Name: "{app}\addins"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
#ifdef AppUserModelId
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; AppUserModelID: "{#AppUserModelId}"
#else
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
#endif
