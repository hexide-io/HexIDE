#ifndef AppVersion
  #error AppVersion must be supplied by the build
#endif
#ifndef PublishDir
  #error PublishDir must name the staged self-contained Desktop publish
#endif

[Setup]
AppId={{B2630434-546D-47B0-A96E-EE613B99F71D}
AppName=HexIDE
AppVersion={#AppVersion}
DefaultDirName={autopf}\HexIDE
DefaultGroupName=HexIDE
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Uninstallable=yes
UninstallDisplayIcon={app}\HexIDE.exe
OutputDir=..\IDE\bin
OutputBaseFilename=HexIDE-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes

[Dirs]
Name: "{app}\addins"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\HexIDE"; Filename: "{app}\HexIDE.exe"
