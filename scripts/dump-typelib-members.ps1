<#
.SYNOPSIS
    Enumerate the members of named classes in a COM type library: every method and property of each class's
    interfaces, and every event of its event interfaces, with its parameters. The generator behind
    IDE/HexIDE.Runtime/Interpreter/Data/vb6-form-members.json.

.DESCRIPTION
    A bare name in a form module is looked up on the form before the project's globals (measured, #822). So the
    interpreter has to know which names ARE the form's: not only the members HexIDE implements, but every member
    VB6's Form has, so that `CurrentX = 0` is refused as not implemented rather than quietly made a new variable
    (#995). That list is a fact about VB6.OLB, and this script reads it from there instead of from documentation.

    For each class named, its coclass is found, and each interface it implements is walked: the default and any
    others, its event (source) interfaces, and each interface's own bases, stopping at IDispatch and IUnknown. A
    dual dispinterface is read through its vtable half, which keeps a property's get, let and set apart.

    Reads through LoadTypeLibEx with REGKIND_NONE, so nothing is registered and the machine is left untouched.

.PARAMETER Path
    A type library, as for dump-typelib-constants.ps1. VB's own objects are in VB98\VB6.OLB.

.PARAMETER Class
    The classes to dump, comma-separated (PowerShell's -File flattens arrays, so one string): `Form,MDIForm`.

.PARAMETER AsDataFile
    Emit the committed data file instead of the JSONL: one JSON document, the rows grouped by class. Its output is
    vb6-form-members.json exactly, so regenerating that file is this script and nothing else.

.EXAMPLE
    # On the oracle VM, in a 32-BIT host: these libraries are 32-bit, and LoadTypeLibEx on a 32-bit module from a
    # 64-bit process fails with TYPE_E_CANTLOADLIBRARY.
    & C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe -File dump-typelib-members.ps1 `
        -Path 'C:\Program Files (x86)\Microsoft Visual Studio\VB98\VB6.OLB' -Class Form

.NOTES
    Output is JSONL on stdout, one object per line:
      {kind:"LIB", library, path}
      {kind:"CLASS", library, class, interface, role}   role is "default", "other", "events" or "base"
      {kind:"MEMBER"|"EVENT", class, interface, name, invoke, params:[{name, optional}], hidden, restricted}
    where invoke is "method", "get", "let" or "set" for a function, and "field" for a dispinterface variable, and
    params are what a caller passes: a retval slot and a put's assigned value are left out. The library's help strings
    are left out too: they are Microsoft's prose, which this tree does not copy.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string] $Path, [Parameter(Mandatory)][string] $Class, [switch] $AsDataFile)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
// Aliased for the reason dump-typelib-constants.ps1 gives: the deprecated copies of these types make every one
// an ambiguous reference.
using ComTypes = System.Runtime.InteropServices.ComTypes;

public static class TlbMemberDump
{
    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void LoadTypeLibEx(string file, int regkind, out ComTypes.ITypeLib tlb);

    private const int REGKIND_NONE = 2;
    private const int MEMBERID_NIL = -1;
    private const short FUNCFLAG_FRESTRICTED = 0x1;
    private const short FUNCFLAG_FHIDDEN = 0x40;
    private const short VARFLAG_FRESTRICTED = 0x80;
    private const short VARFLAG_FHIDDEN = 0x40;
    private const short PARAMFLAG_FOPT = 0x10;
    private const short PARAMFLAG_FRETVAL = 0x8;

    private static string Json(string s)
    {
        if (s == null) return "null";
        var sb = new System.Text.StringBuilder("\"");
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c < 0x20 || c == 0x7f) sb.Append('\\').Append('u').Append(((int)c).ToString("x4"));
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }

    private static string NameOf(ComTypes.ITypeInfo ti, int memid)
    {
        string name, doc, helpFile;
        int helpContext;
        ti.GetDocumentation(memid, out name, out doc, out helpContext, out helpFile);
        return name;
    }

    private static ComTypes.TYPEATTR AttrOf(ComTypes.ITypeInfo ti)
    {
        IntPtr p;
        ti.GetTypeAttr(out p);
        try { return (ComTypes.TYPEATTR)Marshal.PtrToStructure(p, typeof(ComTypes.TYPEATTR)); }
        finally { ti.ReleaseTypeAttr(p); }
    }

    public static List<string> Dump(string path, string[] classes)
    {
        var rows = new List<string>();
        ComTypes.ITypeLib tlb;
        LoadTypeLibEx(path, REGKIND_NONE, out tlb);
        string libName, libDoc, helpFile;
        int helpContext;
        tlb.GetDocumentation(MEMBERID_NIL, out libName, out libDoc, out helpContext, out helpFile);
        rows.Add("{\"kind\":\"LIB\",\"library\":" + Json(libName) + ",\"path\":" + Json(path) + "}");

        var wanted = new HashSet<string>(classes, StringComparer.OrdinalIgnoreCase);
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int count = tlb.GetTypeInfoCount();
        for (int i = 0; i < count; i++)
        {
            ComTypes.TYPEKIND kind;
            tlb.GetTypeInfoType(i, out kind);
            if (kind != ComTypes.TYPEKIND.TKIND_COCLASS) continue;
            ComTypes.ITypeInfo co;
            tlb.GetTypeInfo(i, out co);
            string className = NameOf(co, MEMBERID_NIL);
            if (!wanted.Contains(className)) continue;
            found.Add(className);

            var attr = AttrOf(co);
            for (int j = 0; j < attr.cImplTypes; j++)
            {
                ComTypes.IMPLTYPEFLAGS flags;
                co.GetImplTypeFlags(j, out flags);
                int href;
                co.GetRefTypeOfImplType(j, out href);
                ComTypes.ITypeInfo iti;
                co.GetRefTypeInfo(href, out iti);
                bool source = (flags & ComTypes.IMPLTYPEFLAGS.IMPLTYPEFLAG_FSOURCE) != 0;
                bool isDefault = (flags & ComTypes.IMPLTYPEFLAGS.IMPLTYPEFLAG_FDEFAULT) != 0;
                string role = source ? "events" : isDefault ? "default" : "other";
                DumpInterface(rows, className, iti, role, source ? "EVENT" : "MEMBER", new HashSet<string>());
            }
        }
        foreach (var c in wanted)
            if (!found.Contains(c))
                rows.Add("{\"kind\":\"MISSING\",\"class\":" + Json(c) + "}");
        return rows;
    }

    private static void DumpInterface(List<string> rows, string className, ComTypes.ITypeInfo ti, string role,
        string memberKind, HashSet<string> seen)
    {
        var attr = AttrOf(ti);
        // A dual dispinterface's vtable half carries the property kinds; the dispatch half only DISPATCH_METHOD-like ids.
        if (attr.typekind == ComTypes.TYPEKIND.TKIND_DISPATCH && (attr.wTypeFlags & ComTypes.TYPEFLAGS.TYPEFLAG_FDUAL) != 0)
        {
            int dualRef;
            ti.GetRefTypeOfImplType(-1, out dualRef);
            ti.GetRefTypeInfo(dualRef, out ti);
            attr = AttrOf(ti);
        }
        string iname = NameOf(ti, MEMBERID_NIL);
        if (iname == "IDispatch" || iname == "IUnknown" || !seen.Add(iname)) return;
        rows.Add("{\"kind\":\"CLASS\",\"class\":" + Json(className) + ",\"interface\":" + Json(iname)
               + ",\"role\":" + Json(role) + "}");

        for (int f = 0; f < attr.cFuncs; f++)
        {
            IntPtr pf;
            ti.GetFuncDesc(f, out pf);
            try
            {
                var fd = (ComTypes.FUNCDESC)Marshal.PtrToStructure(pf, typeof(ComTypes.FUNCDESC));
                var names = new string[fd.cParams + 1];
                int got;
                ti.GetNames(fd.memid, names, names.Length, out got);
                string invoke =
                    fd.invkind == ComTypes.INVOKEKIND.INVOKE_PROPERTYGET ? "get"
                    : fd.invkind == ComTypes.INVOKEKIND.INVOKE_PROPERTYPUT ? "let"
                    : fd.invkind == ComTypes.INVOKEKIND.INVOKE_PROPERTYPUTREF ? "set"
                    : "method";
                // The parameters a caller passes. A vtable function's [out, retval] slot is how it returns its value,
                // not an argument, and a put's last parameter is the value assigned, which the caller supplies on the
                // right of the `=`: neither is listed, so `params` is what goes in the parentheses.
                var ps = new System.Text.StringBuilder("[");
                int size = Marshal.SizeOf(typeof(ComTypes.ELEMDESC));
                bool put = invoke == "let" || invoke == "set";
                int listed = 0;
                for (int k = 0; k < fd.cParams; k++)
                {
                    var ed = (ComTypes.ELEMDESC)Marshal.PtrToStructure(
                        new IntPtr(fd.lprgelemdescParam.ToInt64() + (long)k * size), typeof(ComTypes.ELEMDESC));
                    short pflags = (short)ed.desc.paramdesc.wParamFlags;
                    if ((pflags & PARAMFLAG_FRETVAL) != 0 || (put && k == fd.cParams - 1))
                        continue;
                    bool optional = (pflags & PARAMFLAG_FOPT) != 0 || k >= fd.cParams - fd.cParamsOpt;
                    string pname = k + 1 < got && names[k + 1] != null ? names[k + 1] : "arg" + k;
                    if (listed++ > 0) ps.Append(',');
                    ps.Append("{\"name\":").Append(Json(pname)).Append(",\"optional\":").Append(optional ? "true" : "false").Append('}');
                }
                ps.Append(']');
                rows.Add("{\"kind\":" + Json(memberKind) + ",\"class\":" + Json(className) + ",\"interface\":" + Json(iname)
                       + ",\"name\":" + Json(names[0]) + ",\"invoke\":" + Json(invoke) + ",\"params\":" + ps
                       + ",\"hidden\":" + (((int)fd.wFuncFlags & FUNCFLAG_FHIDDEN) != 0 ? "true" : "false")
                       + ",\"restricted\":" + (((int)fd.wFuncFlags & FUNCFLAG_FRESTRICTED) != 0 ? "true" : "false") + "}");
            }
            finally { ti.ReleaseFuncDesc(pf); }
        }

        for (int v = 0; v < attr.cVars; v++)
        {
            IntPtr pv;
            ti.GetVarDesc(v, out pv);
            try
            {
                var vd = (ComTypes.VARDESC)Marshal.PtrToStructure(pv, typeof(ComTypes.VARDESC));
                rows.Add("{\"kind\":" + Json(memberKind) + ",\"class\":" + Json(className) + ",\"interface\":" + Json(iname)
                       + ",\"name\":" + Json(NameOf(ti, vd.memid)) + ",\"invoke\":\"field\",\"params\":[]"
                       + ",\"hidden\":" + (((int)vd.wVarFlags & VARFLAG_FHIDDEN) != 0 ? "true" : "false")
                       + ",\"restricted\":" + (((int)vd.wVarFlags & VARFLAG_FRESTRICTED) != 0 ? "true" : "false") + "}");
            }
            finally { ti.ReleaseVarDesc(pv); }
        }

        for (int b = 0; b < attr.cImplTypes; b++)
        {
            int baseRef;
            ti.GetRefTypeOfImplType(b, out baseRef);
            ComTypes.ITypeInfo bti;
            ti.GetRefTypeInfo(baseRef, out bti);
            DumpInterface(rows, className, bti, "base", memberKind, seen);
        }
    }
}
'@ -Language CSharp

if (-not (Test-Path $Path)) { Write-Output ("MISSING`t" + $Path); exit 1 }
$rows = [TlbMemberDump]::Dump($Path, ($Class -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ }))
if (-not $AsDataFile) { $rows | ForEach-Object { Write-Output $_ }; exit 0 }

# The data file: the rows grouped by class, in the order the type library lists them, one member to a line. Written
# out here rather than through ConvertTo-Json, whose layout differs between PowerShell versions: the same library
# regenerates the same bytes whichever PowerShell runs this.
$library = $null
$classes = [ordered]@{}
foreach ($line in $rows) {
    $r = $line | ConvertFrom-Json
    if ($r.kind -eq 'LIB') { $library = $r.library; continue }
    if ($r.kind -eq 'MISSING') { throw "No class named $($r.class) in $Path" }
    if (-not $classes.Contains($r.class)) {
        $classes[$r.class] = [ordered]@{ interfaces = [System.Collections.Generic.List[string]]::new()
                                         members = [System.Collections.Generic.List[string]]::new()
                                         events = [System.Collections.Generic.List[string]]::new() }
    }
    $c = $classes[$r.class]
    # The row's own JSON with its kind, class and interface taken off, which the grouping now says.
    $body = $line -replace '^\{"kind":"[A-Z]+","class":"[^"]*","interface":"[^"]*",', '{'
    switch ($r.kind) {
        'CLASS' { $c.interfaces.Add('{"name":"' + $r.interface + '","role":"' + $r.role + '"}') }
        'EVENT' { $c.events.Add($body) }
        default { $c.members.Add($body) }
    }
}
$note = 'Generated by scripts/dump-typelib-members.ps1 -AsDataFile against VB98\\VB6.OLB on the oracle VM. Do not ' +
    'hand-edit: regenerate. Each member is as the type library declares it. invoke is method, get, let or set, and ' +
    'params are what a caller passes: a retval slot and the value a put assigns are left out. The help strings are ' +
    'left out, being Microsoft prose. Print, which VB6 compiles as special syntax, is not in the type library and so ' +
    'not here, and nor are the controls of a form, which VB adds per form.'
$out = [System.Collections.Generic.List[string]]::new()
$out.Add('{')
$out.Add('  "note": "' + $note + '",')
$out.Add('  "library": "' + $library + '",')
$out.Add('  "classes": {')
$names = @($classes.Keys)
for ($i = 0; $i -lt $names.Count; $i++) {
    $c = $classes[$names[$i]]
    $out.Add('    "' + $names[$i] + '": {')
    $out.Add('      "interfaces": [' + ($c.interfaces -join ', ') + '],')
    $out.Add('      "members": [')
    $out.Add('        ' + ($c.members -join (",`n        ")))
    $out.Add('      ],')
    $out.Add('      "events": [')
    $out.Add('        ' + ($c.events -join (",`n        ")))
    $out.Add('      ]')
    $out.Add('    }' + $(if ($i -lt $names.Count - 1) { ',' } else { '' }))
}
$out.Add('  }')
$out.Add('}')
$out | ForEach-Object { Write-Output $_ }
