<#
.SYNOPSIS
    List the type names a COM type library declares, with the kind of each: the classes, interfaces and aliases a
    declaration may name after `As`. The generator behind IDE/HexIDE.Runtime/Interpreter/Data/vb6-inbox-types.json.

.DESCRIPTION
    A form's declarations run before Form_Initialize (measured, #822), and real forms declare variables of VB's own
    object types: `Private mParent As Form`, `Dim c As Control`, `Dim f As StdFont`. Which names those are is a fact
    about the libraries every project references, VBA, VBRUN, VB and stdole, and this script reads it from them rather
    than from a list written down by hand.

    Reads through LoadTypeLibEx with REGKIND_NONE, so nothing is registered and the machine is left untouched.

.PARAMETER Path
    One or more type libraries, as for dump-typelib-constants.ps1, comma-separated (PowerShell's -File flattens
    arrays): `C:\Windows\SysWOW64\MSVBVM60.DLL,C:\Windows\SysWOW64\MSVBVM60.DLL\3`.

.PARAMETER AsDataFile
    Emit the committed data file instead of the JSONL, so regenerating it is this script and nothing else.

.EXAMPLE
    # On the oracle VM, in a 32-BIT host, for the reason dump-typelib-constants.ps1 gives.
    & C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe -File dump-typelib-types.ps1 -AsDataFile `
        -Path 'C:\Windows\SysWOW64\MSVBVM60.DLL,C:\Windows\SysWOW64\MSVBVM60.DLL\3,C:\Program Files (x86)\Microsoft Visual Studio\VB98\VB6.OLB,C:\Windows\SysWOW64\stdole2.tlb'

.NOTES
    JSONL rows: {kind:"LIB", library, path} and {kind:"TYPE", library, name, typekind, hidden, restricted}, where
    typekind is coclass, interface, dispatch, alias, record, enum or module, and aliasOf is an alias's VARTYPE (3 is
    VT_I4, 19 VT_UI4, 29 VT_USERDEFINED, and so on), zero for anything else. The data file keeps aliasOf on aliases.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string] $Path, [switch] $AsDataFile)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ComTypes = System.Runtime.InteropServices.ComTypes;

public static class TlbTypeDump
{
    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void LoadTypeLibEx(string file, int regkind, out ComTypes.ITypeLib tlb);

    private const int REGKIND_NONE = 2;
    private const int MEMBERID_NIL = -1;

    public static List<string> Dump(string path)
    {
        var rows = new List<string>();
        ComTypes.ITypeLib tlb;
        LoadTypeLibEx(path, REGKIND_NONE, out tlb);
        string libName, doc, helpFile;
        int helpContext;
        tlb.GetDocumentation(MEMBERID_NIL, out libName, out doc, out helpContext, out helpFile);
        // The library's identity as a project's Object= line names it: {guid}#major.minor.
        IntPtr la;
        tlb.GetLibAttr(out la);
        ComTypes.TYPELIBATTR libAttr;
        try { libAttr = (ComTypes.TYPELIBATTR)Marshal.PtrToStructure(la, typeof(ComTypes.TYPELIBATTR)); }
        finally { tlb.ReleaseTLibAttr(la); }
        rows.Add("{\"kind\":\"LIB\",\"library\":\"" + libName + "\",\"guid\":\"" + libAttr.guid.ToString("B").ToUpperInvariant()
               + "\",\"version\":\"" + libAttr.wMajorVerNum + "." + libAttr.wMinorVerNum + "\"}");
        int count = tlb.GetTypeInfoCount();
        for (int i = 0; i < count; i++)
        {
            ComTypes.ITypeInfo ti;
            tlb.GetTypeInfo(i, out ti);
            string name;
            ti.GetDocumentation(MEMBERID_NIL, out name, out doc, out helpContext, out helpFile);
            IntPtr p;
            ti.GetTypeAttr(out p);
            ComTypes.TYPEATTR attr;
            try { attr = (ComTypes.TYPEATTR)Marshal.PtrToStructure(p, typeof(ComTypes.TYPEATTR)); }
            finally { ti.ReleaseTypeAttr(p); }
            string kind;
            switch (attr.typekind)
            {
                case ComTypes.TYPEKIND.TKIND_COCLASS: kind = "coclass"; break;
                case ComTypes.TYPEKIND.TKIND_INTERFACE: kind = "interface"; break;
                case ComTypes.TYPEKIND.TKIND_DISPATCH: kind = "dispatch"; break;
                case ComTypes.TYPEKIND.TKIND_ALIAS: kind = "alias"; break;
                case ComTypes.TYPEKIND.TKIND_RECORD: kind = "record"; break;
                case ComTypes.TYPEKIND.TKIND_ENUM: kind = "enum"; break;
                case ComTypes.TYPEKIND.TKIND_MODULE: kind = "module"; break;
                default: kind = attr.typekind.ToString(); break;
            }
            int flags = (int)attr.wTypeFlags;
            // What an alias stands for, as its VARTYPE: OLE_COLOR is a VT_UI4, IPictureDisp a VT_USERDEFINED naming a
            // dispinterface. Zero for a type that is not an alias.
            int aliasOf = attr.typekind == ComTypes.TYPEKIND.TKIND_ALIAS ? (int)attr.tdescAlias.vt : 0;
            rows.Add("{\"kind\":\"TYPE\",\"library\":\"" + libName + "\",\"name\":\"" + name + "\",\"typekind\":\"" + kind
                   + "\",\"aliasOf\":" + aliasOf
                   + ",\"hidden\":" + ((flags & (int)ComTypes.TYPEFLAGS.TYPEFLAG_FHIDDEN) != 0 ? "true" : "false")
                   + ",\"restricted\":" + ((flags & (int)ComTypes.TYPEFLAGS.TYPEFLAG_FRESTRICTED) != 0 ? "true" : "false")
                   // A coclass that is a control, as a form can hold it, rather than an object a control hands out.
                   + ",\"control\":" + ((flags & (int)ComTypes.TYPEFLAGS.TYPEFLAG_FCONTROL) != 0 ? "true" : "false") + "}");
        }
        return rows;
    }
}
'@ -Language CSharp

$rows = [System.Collections.Generic.List[string]]::new()
foreach ($p in ($Path -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })) {
    $file = $p -replace '\\\d+$', ''
    if (-not (Test-Path $file)) { throw "MISSING $p" }
    [TlbTypeDump]::Dump($p) | ForEach-Object { $rows.Add($_) }
}
if (-not $AsDataFile) { $rows | ForEach-Object { Write-Output $_ }; exit 0 }

# The data file: each library's types in the order the library lists them, one to a line, written out here rather than
# through ConvertTo-Json, whose layout differs between PowerShell versions.
$libs = [ordered]@{}
$current = $null
foreach ($line in $rows) {
    $r = $line | ConvertFrom-Json
    if ($r.kind -eq 'LIB') { $current = $r.library; $libs[$current] = [System.Collections.Generic.List[string]]::new(); continue }
    $alias = if ($r.typekind -eq 'alias') { ',"aliasOf":' + $r.aliasOf } else { '' }
    $libs[$current].Add('{"name":"' + $r.name + '","typekind":"' + $r.typekind + '"' + $alias + ',"hidden":' + $r.hidden.ToString().ToLowerInvariant() +
        ',"restricted":' + $r.restricted.ToString().ToLowerInvariant() + '}')
}
$note = 'Generated by scripts/dump-typelib-types.ps1 -AsDataFile against the in-box type libraries on the oracle VM. ' +
    'Do not hand-edit: regenerate. Every type each library declares, with its kind as the library has it.'
$out = [System.Collections.Generic.List[string]]::new()
$out.Add('{')
$out.Add('  "note": "' + $note + '",')
$out.Add('  "libraries": {')
$names = @($libs.Keys)
for ($i = 0; $i -lt $names.Count; $i++) {
    $out.Add('    "' + $names[$i] + '": [')
    $out.Add('      ' + ($libs[$names[$i]] -join (",`n      ")))
    $out.Add('    ]' + $(if ($i -lt $names.Count - 1) { ',' } else { '' }))
}
$out.Add('  }')
$out.Add('}')
$out | ForEach-Object { Write-Output $_ }
