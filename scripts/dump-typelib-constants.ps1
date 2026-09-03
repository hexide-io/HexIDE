<#
.SYNOPSIS
    Enumerate every constant in a COM type library — enum members AND module constants — with its
    declaring library, container and value. The generator behind docs/vb6-inbox-constants.md.

.DESCRIPTION
    HexIDE holds VB6's in-box constants as a FLAT name-to-value dictionary, which cannot represent the
    thing this script exists to measure: the same name can be declared by two libraries with two
    different values. `vbCancel` is 2 in VBA.VbMsgBoxResult and 0 in VBRUN.DragConstants, and VB6
    resolves `VBRUN.vbCancel` to 0 — so the library qualifier selects, and a flat table must answer one
    of them wrongly.

    Both TKIND_ENUM and TKIND_MODULE are collected, because VB6 splits its constants across the two and
    the split decides addressability: an enum name is both a type and a qualifier, a module name is only
    a qualifier. `VbVarType.vbLong` is an enum member; `Constants.vbCrLf` is a module constant.

    Reads through LoadTypeLibEx with REGKIND_NONE, so nothing is registered and the machine is left
    untouched.

.PARAMETER Path
    A type library. May carry a RESOURCE INDEX — `MSVBVM60.DLL\3` — which is how a module holding more
    than one typelib is addressed, and the only way to reach VBRUN: it shares MSVBVM60.DLL with VBA,
    which is at index 1. Pass one path per invocation; PowerShell's -File flattens arrays.

.EXAMPLE
    # Run on the oracle VM in a 32-BIT host — these libraries are all 32-bit, and LoadTypeLibEx on a
    # 32-bit module from a 64-bit process fails with TYPE_E_CANTLOADLIBRARY.
    & C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe -File dump-typelib-constants.ps1 `
        -Path 'C:\Windows\SysWOW64\MSVBVM60.DLL\3'

.NOTES
    The four libraries a Standard EXE references by default, and where they live:
      stdole  OLE Automation                            C:\Windows\SysWOW64\stdole2.tlb
      VBA     Visual Basic For Applications             C:\Windows\SysWOW64\MSVBVM60.DLL      (index 1)
      VBRUN   Visual Basic runtime objects and procs    C:\Windows\SysWOW64\MSVBVM60.DLL\3
      VB      Visual Basic objects and procedures       ...\VB98\VB6.OLB
    VB declares NO constants at all — measured, and contrary to the reasonable guess that the control
    constants (AlignConstants, BorderStyleConstants) live there. They are all in VBRUN.

    Output is JSONL on stdout — one object per line, {kind, library, container, name, value, doc},
    where kind is LIB, ENUM or MODULE.

    JSON rather than tab-separated because a constant's VALUE can be a raw tab, CR, LF or NUL: vbTab,
    vbCrLf, vbLf and vbNullChar are literally those characters. A tab-separated transport eats or
    splits exactly the eleven rows that carry one, and does it silently — the first run of this script
    reported vbTab's value as the empty string for that reason.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string[]] $Path)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
// ComTypes is ALIASED rather than imported: TYPEKIND, TYPEATTR and VARDESC exist in both
// System.Runtime.InteropServices (deprecated) and ...ComTypes, so importing both makes every one of them
// an ambiguous reference and nothing compiles.
using ComTypes = System.Runtime.InteropServices.ComTypes;

public static class TlbEnumDump
{
    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void LoadTypeLibEx(string file, int regkind, out ComTypes.ITypeLib tlb);

    private const int REGKIND_NONE = 2;
    private const int MEMBERID_NIL = -1;

    /// <summary>JSON-encode a string, escaping EVERY control character rather than the usual handful.
    /// A constant's value here can be a raw tab, CR, LF or NUL, and those must survive the trip out.</summary>
    private static string Json(string s)
    {
        if (s == null) return "null";
        var sb = new System.Text.StringBuilder("\"");
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c < 0x20 || c == 0x7f) sb.Append("\\u").Append(((int)c).ToString("x4"));
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }

    public static List<string> Dump(string path)
    {
        var rows = new List<string>();
        ComTypes.ITypeLib tlb;
        LoadTypeLibEx(path, REGKIND_NONE, out tlb);

        string libName, libDoc, helpFile;
        int helpContext;
        tlb.GetDocumentation(MEMBERID_NIL, out libName, out libDoc, out helpContext, out helpFile);
        rows.Add("{\"kind\":\"LIB\",\"library\":" + Json(libName)
               + ",\"doc\":" + Json(libDoc ?? "") + ",\"path\":" + Json(path) + "}");

        int count = tlb.GetTypeInfoCount();
        for (int i = 0; i < count; i++)
        {
            ComTypes.TYPEKIND kind;
            tlb.GetTypeInfoType(i, out kind);

            // ENUM *and* MODULE. VB6 puts some constants in an enum (VbVarType.vbLong) and others in a
            // module (Constants.vbCrLf), and the difference decides how they may be addressed — an enum
            // name is a type and a qualifier, a module name is only a qualifier. An inventory that
            // collected only TKIND_ENUM would silently omit every vbCrLf-shaped constant.
            if (kind != ComTypes.TYPEKIND.TKIND_ENUM && kind != ComTypes.TYPEKIND.TKIND_MODULE) continue;
            string kindName = kind == ComTypes.TYPEKIND.TKIND_ENUM ? "ENUM" : "MODULE";

            ComTypes.ITypeInfo ti;
            tlb.GetTypeInfo(i, out ti);

            string enumName, enumDoc;
            ti.GetDocumentation(MEMBERID_NIL, out enumName, out enumDoc, out helpContext, out helpFile);

            IntPtr pAttr;
            ti.GetTypeAttr(out pAttr);
            int vars;
            try { vars = ((ComTypes.TYPEATTR)Marshal.PtrToStructure(pAttr, typeof(ComTypes.TYPEATTR))).cVars; }
            finally { ti.ReleaseTypeAttr(pAttr); }

            for (int j = 0; j < vars; j++)
            {
                IntPtr pVar;
                ti.GetVarDesc(j, out pVar);
                try
                {
                    var vd = (ComTypes.VARDESC)Marshal.PtrToStructure(pVar, typeof(ComTypes.VARDESC));
                    string memberName, memberDoc;
                    ti.GetDocumentation(vd.memid, out memberName, out memberDoc, out helpContext, out helpFile);

                    // An enum member is VAR_CONST, so lpvarValue points at a VARIANT holding the value.
                    // Rendered invariantly: these become C# literals and a decimal comma would be a defect.
                    string value = "?";
                    if (vd.desc.lpvarValue != IntPtr.Zero)
                    {
                        object v = Marshal.GetObjectForNativeVariant(vd.desc.lpvarValue);
                        value = Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
                    }

                    // A module holds methods as well as constants; only VAR_CONST entries are values.
                    if (kindName == "MODULE" && vd.varkind != ComTypes.VARKIND.VAR_CONST) continue;

                    // One JSON object per line, NOT tab-separated. The values of VBA.Constants are
                    // vbTab, vbCrLf, vbLf, vbNullChar — literally a tab, a CRLF, an LF and a NUL — so a
                    // tab-separated transport eats or splits precisely the eleven rows that carry a
                    // control character, and does it silently. That is not a hypothetical: the first
                    // run of this script reported vbTab's value as the empty string.
                    rows.Add("{\"kind\":" + Json(kindName)
                           + ",\"library\":" + Json(libName)
                           + ",\"container\":" + Json(enumName)
                           + ",\"name\":" + Json(memberName)
                           + ",\"value\":" + Json(value)
                           + ",\"doc\":" + Json(memberDoc ?? "") + "}");
                }
                finally { ti.ReleaseVarDesc(pVar); }
            }
        }
        return rows;
    }
}
'@ -Language CSharp

foreach ($p in $Path) {
    # A path may carry a typelib RESOURCE INDEX — `MSVBVM60.DLL\3` — which is how a module holding more
    # than one typelib is addressed, and how VBRUN is reached (it shares MSVBVM60.DLL with VBA). That is
    # not a filesystem path, so the existence check has to look at the file part only.
    $file = $p -replace '\\\d+$', ''
    if (-not (Test-Path $file)) { Write-Output ("MISSING`t" + $p); continue }
    try { [TlbEnumDump]::Dump($p) | ForEach-Object { Write-Output $_ } }
    catch { Write-Output ("ERROR`t" + $p + "`t" + $_.Exception.Message) }
}
