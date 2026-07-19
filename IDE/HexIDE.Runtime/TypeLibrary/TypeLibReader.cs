using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using Serilog;

namespace HexIDE.Runtime.TypeLibrary;

// Reads a COM type library (.tlb / .olb / .dll / .ocx) and returns a TypeLibInfo record.
// All COM work is Windows-only and guarded by OperatingSystem.IsWindows().
public static class TypeLibReader
{
    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = true)]
    private static extern int LoadTypeLib(string szFile, out ITypeLib? ppTLib);

    [DllImport("oleaut32.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int LoadRegTypeLib(ref Guid rguid, ushort wVerMajor, ushort wVerMinor,
        int lcid, out ITypeLib? ppTLib);

    private const short FuncFlagRestricted = 0x01;
    private const short FuncFlagHidden     = 0x40;
    private const short VarFlagHidden      = 0x40;

    public static TypeLibInfo? Read(string path)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try { return ReadCore(path); }
        catch (Exception ex)
        {
            Log.Warning(ex, "TypeLibReader: failed to read {Path}", path);
            return null;
        }
    }

    /// <summary>
    /// Loads a type library by registry GUID + version string (e.g. "2.0") without needing a file path.
    /// Returns null on non-Windows, unrecognised GUID, or COM load failure.
    /// </summary>
    public static TypeLibInfo? ReadByGuid(string guidString, string version)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try { return ReadByGuidCore(guidString, version); }
        catch (Exception ex)
        {
            Log.Warning(ex, "TypeLibReader: failed to load by GUID {Guid} v{Version}", guidString, version);
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static TypeLibInfo? ReadByGuidCore(string guidString, string version)
    {
        if (!Guid.TryParse(guidString, out var guid)) return null;

        var parts = version.Split('.');
        ushort major = parts.Length > 0 && ushort.TryParse(parts[0], out var maj) ? maj : (ushort)0;
        ushort minor = parts.Length > 1 && ushort.TryParse(parts[1], out var min) ? min : (ushort)0;

        int hr = LoadRegTypeLib(ref guid, major, minor, 0, out ITypeLib? typeLib);
        if (hr != 0 || typeLib == null) return null;
        try { return WalkTypeLib(typeLib, guidString); }
        finally { Marshal.ReleaseComObject(typeLib); }
    }

    [SupportedOSPlatform("windows")]
    private static TypeLibInfo? ReadCore(string path)
    {
        int hr = LoadTypeLib(path, out ITypeLib? typeLib);
        if (hr != 0 || typeLib == null) return null;
        try { return WalkTypeLib(typeLib, Path.GetFileNameWithoutExtension(path)); }
        finally { Marshal.ReleaseComObject(typeLib); }
    }

    [SupportedOSPlatform("windows")]
    private static TypeLibInfo WalkTypeLib(ITypeLib typeLib, string fallbackName)
    {
        GetLibDocumentation(typeLib, out string? libName, out string? libDoc);
        int count = typeLib.GetTypeInfoCount();
        var types = new List<TypeInfo>(count);
        for (int i = 0; i < count; i++)
        {
            typeLib.GetTypeInfo(i, out ITypeInfo? typeInfo);
            if (typeInfo == null) continue;
            try
            {
                var record = ReadTypeInfo(typeInfo);
                if (record != null) types.Add(record);
            }
            finally { Marshal.ReleaseComObject(typeInfo); }
        }
        return new TypeLibInfo(libName ?? fallbackName, libDoc, types);
    }

    [SupportedOSPlatform("windows")]
    private static TypeInfo? ReadTypeInfo(ITypeInfo typeInfo)
    {
        typeInfo.GetTypeAttr(out IntPtr pAttr);
        if (pAttr == IntPtr.Zero) return null;
        TYPEATTR attr;
        try { attr = Marshal.PtrToStructure<TYPEATTR>(pAttr); }
        finally { typeInfo.ReleaseTypeAttr(pAttr); }

        if ((attr.wTypeFlags & TYPEFLAGS.TYPEFLAG_FHIDDEN)     != 0) return null;
        if ((attr.wTypeFlags & TYPEFLAGS.TYPEFLAG_FRESTRICTED) != 0) return null;

        GetTypeDocumentation(typeInfo, out string? typeName, out string? typeDoc);
        if (string.IsNullOrEmpty(typeName)) return null;

        if (attr.typekind == TYPEKIND.TKIND_COCLASS)
            return ReadCoclass(typeInfo, attr, typeName!, typeDoc);

        return new TypeInfo(typeName!, MapTypekind(attr.typekind), typeDoc, ReadMembers(typeInfo, attr));
    }

    [SupportedOSPlatform("windows")]
    private static TypeInfo? ReadCoclass(ITypeInfo coclass, TYPEATTR attr, string name, string? doc)
    {
        for (int i = 0; i < attr.cImplTypes; i++)
        {
            try
            {
                coclass.GetImplTypeFlags(i, out IMPLTYPEFLAGS flags);
                bool isDefault = (flags & IMPLTYPEFLAGS.IMPLTYPEFLAG_FDEFAULT) != 0;
                bool isSource  = (flags & IMPLTYPEFLAGS.IMPLTYPEFLAG_FSOURCE)  != 0;
                if (!isDefault || isSource) continue;

                coclass.GetRefTypeOfImplType(i, out int href);
                coclass.GetRefTypeInfo(href, out ITypeInfo? defIface);
                if (defIface == null) continue;
                try
                {
                    defIface.GetTypeAttr(out IntPtr pDefAttr);
                    if (pDefAttr == IntPtr.Zero) continue;
                    TYPEATTR defAttr;
                    try { defAttr = Marshal.PtrToStructure<TYPEATTR>(pDefAttr); }
                    finally { defIface.ReleaseTypeAttr(pDefAttr); }
                    return new TypeInfo(name, TypeKind.Class, doc, ReadMembers(defIface, defAttr));
                }
                finally { Marshal.ReleaseComObject(defIface); }
            }
            catch { /* try next impl type */ }
        }
        return null;
    }

    [SupportedOSPlatform("windows")]
    private static List<TypeMemberInfo> ReadMembers(ITypeInfo typeInfo, TYPEATTR attr)
    {
        var members = new List<TypeMemberInfo>();
        for (int j = 0; j < attr.cFuncs; j++)
        {
            typeInfo.GetFuncDesc(j, out IntPtr pFunc);
            if (pFunc == IntPtr.Zero) continue;
            try
            {
                var func = Marshal.PtrToStructure<FUNCDESC>(pFunc);
                if ((func.wFuncFlags & FuncFlagRestricted) != 0) continue;
                if ((func.wFuncFlags & FuncFlagHidden)     != 0) continue;
                var m = ReadFuncMember(typeInfo, func);
                if (m != null) members.Add(m);
            }
            catch { /* skip malformed entry */ }
            finally { typeInfo.ReleaseFuncDesc(pFunc); }
        }
        for (int j = 0; j < attr.cVars; j++)
        {
            typeInfo.GetVarDesc(j, out IntPtr pVar);
            if (pVar == IntPtr.Zero) continue;
            try
            {
                var var_ = Marshal.PtrToStructure<VARDESC>(pVar);
                if ((var_.wVarFlags & VarFlagHidden) != 0) continue;
                var m = ReadVarMember(typeInfo, var_);
                if (m != null) members.Add(m);
            }
            catch { /* skip malformed entry */ }
            finally { typeInfo.ReleaseVarDesc(pVar); }
        }
        return members;
    }

    [SupportedOSPlatform("windows")]
    private static TypeMemberInfo? ReadFuncMember(ITypeInfo typeInfo, FUNCDESC func)
    {
        GetMemberDocumentation(typeInfo, func.memid, out string? name, out string? doc);
        if (string.IsNullOrEmpty(name)) return null;

        var (kind, isReadOnly) = MapInvokekind(func.invkind);
        string retType = ResolveTypeName(typeInfo, func.elemdescFunc.tdesc);

        int nameCount = func.cParams + 1;
        var namesBuf = new string[nameCount];
        typeInfo.GetNames(func.memid, namesBuf, nameCount, out int pcNames);

        var paramStrings = BuildParamStrings(typeInfo, func, namesBuf, pcNames);
        string sig = FormatFuncSignature(name!, kind, string.Join(", ", paramStrings), retType);
        return new TypeMemberInfo(name!, kind, sig, doc, isReadOnly);
    }

    [SupportedOSPlatform("windows")]
    private static TypeMemberInfo? ReadVarMember(ITypeInfo typeInfo, VARDESC var_)
    {
        GetMemberDocumentation(typeInfo, var_.memid, out string? name, out string? doc);
        if (string.IsNullOrEmpty(name)) return null;
        string typeName = ResolveTypeName(typeInfo, var_.elemdescVar.tdesc);
        return new TypeMemberInfo(name!, MemberKind.Constant, $"{name} As {typeName}", doc, false);
    }

    [SupportedOSPlatform("windows")]
    private static List<string> BuildParamStrings(ITypeInfo typeInfo, FUNCDESC func,
        string[] namesBuf, int pcNames)
    {
        var list = new List<string>(func.cParams);
        if (func.cParams == 0 || func.lprgelemdescParam == IntPtr.Zero) return list;

        int elemSize = Marshal.SizeOf<ELEMDESC>();
        for (int p = 0; p < func.cParams; p++)
        {
            var elemDesc = Marshal.PtrToStructure<ELEMDESC>(
                IntPtr.Add(func.lprgelemdescParam, p * elemSize));
            string typeName = ResolveTypeName(typeInfo, elemDesc.tdesc);

            string paramName = (p + 1 < pcNames && namesBuf[p + 1] is { Length: > 0 } n)
                ? n : $"p{p + 1}";
            bool optional = (elemDesc.desc.paramdesc.wParamFlags & PARAMFLAG.PARAMFLAG_FOPT) != 0;

            list.Add(optional ? $"[{paramName} As {typeName}]" : $"{paramName} As {typeName}");
        }
        return list;
    }

    private static string FormatFuncSignature(string name, MemberKind kind, string paramList, string retType)
        => kind switch
        {
            MemberKind.Method when retType is "void" or "" => $"Sub {name}({paramList})",
            MemberKind.Method                              => $"Function {name}({paramList}) As {retType}",
            MemberKind.PropertyGet                         => $"Property Get {name}({paramList}) As {retType}",
            MemberKind.PropertyLet                         => $"Property Let {name}({paramList})",
            MemberKind.PropertySet                         => $"Property Set {name}({paramList})",
            _                                              => $"{name}({paramList})"
        };

    [SupportedOSPlatform("windows")]
    private static string ResolveTypeName(ITypeInfo typeInfo, TYPEDESC tdesc)
    {
        try
        {
            return (VarEnum)tdesc.vt switch
            {
                VarEnum.VT_I2     or VarEnum.VT_UI2  => "Integer",
                VarEnum.VT_I4     or VarEnum.VT_UI4
                    or VarEnum.VT_INT or VarEnum.VT_UINT
                    or VarEnum.VT_HRESULT             => "Long",
                VarEnum.VT_I8     or VarEnum.VT_UI8  => "LongLong",
                VarEnum.VT_R4                         => "Single",
                VarEnum.VT_R8                         => "Double",
                VarEnum.VT_CY                         => "Currency",
                VarEnum.VT_DATE                       => "Date",
                VarEnum.VT_BSTR  or VarEnum.VT_LPSTR
                    or VarEnum.VT_LPWSTR              => "String",
                VarEnum.VT_DISPATCH or VarEnum.VT_UNKNOWN => "Object",
                VarEnum.VT_BOOL                       => "Boolean",
                VarEnum.VT_VARIANT                    => "Variant",
                VarEnum.VT_DECIMAL                    => "Decimal",
                VarEnum.VT_I1    or VarEnum.VT_UI1   => "Byte",
                VarEnum.VT_VOID                       => "void",
                VarEnum.VT_PTR                        => ResolvePointerType(typeInfo, tdesc),
                VarEnum.VT_USERDEFINED                => ResolveUserDefinedType(typeInfo, tdesc),
                VarEnum.VT_SAFEARRAY or VarEnum.VT_CARRAY => "Variant()",
                _                                     => "Variant"
            };
        }
        catch { return "Variant"; }
    }

    [SupportedOSPlatform("windows")]
    private static string ResolvePointerType(ITypeInfo typeInfo, TYPEDESC tdesc)
    {
        if (tdesc.lpValue == IntPtr.Zero) return "Object";
        var inner = Marshal.PtrToStructure<TYPEDESC>(tdesc.lpValue);
        return ResolveTypeName(typeInfo, inner);
    }

    [SupportedOSPlatform("windows")]
    private static string ResolveUserDefinedType(ITypeInfo typeInfo, TYPEDESC tdesc)
    {
        try
        {
            int hRefType = (int)tdesc.lpValue.ToInt64();
            typeInfo.GetRefTypeInfo(hRefType, out ITypeInfo? refInfo);
            if (refInfo == null) return "Object";
            try
            {
                GetTypeDocumentation(refInfo, out string? refName, out _);
                return string.IsNullOrEmpty(refName) ? "Object" : refName!;
            }
            finally { Marshal.ReleaseComObject(refInfo); }
        }
        catch { return "Object"; }
    }

    private static (MemberKind kind, bool isReadOnly) MapInvokekind(INVOKEKIND invkind) => invkind switch
    {
        INVOKEKIND.INVOKE_PROPERTYGET    => (MemberKind.PropertyGet, false),
        INVOKEKIND.INVOKE_PROPERTYPUT    => (MemberKind.PropertyLet, false),
        INVOKEKIND.INVOKE_PROPERTYPUTREF => (MemberKind.PropertySet, false),
        _                                => (MemberKind.Method,      false)
    };

    private static TypeKind MapTypekind(TYPEKIND typekind) => typekind switch
    {
        TYPEKIND.TKIND_ENUM   => TypeKind.Enum,
        TYPEKIND.TKIND_MODULE => TypeKind.Module,
        TYPEKIND.TKIND_ALIAS  => TypeKind.Alias,
        TYPEKIND.TKIND_RECORD or TYPEKIND.TKIND_UNION => TypeKind.Union,
        _                     => TypeKind.Class  // DISPATCH, INTERFACE, COCLASS
    };

    // ── Documentation helpers ─────────────────────────────────────────────────

    private static void GetLibDocumentation(ITypeLib typeLib, out string? name, out string? doc)
    {
        try { typeLib.GetDocumentation(-1, out name, out doc, out _, out _); }
        catch { name = null; doc = null; }
    }

    private static void GetTypeDocumentation(ITypeInfo typeInfo, out string? name, out string? doc)
    {
        try { typeInfo.GetDocumentation(-1, out name, out doc, out _, out _); }
        catch { name = null; doc = null; }
    }

    private static void GetMemberDocumentation(ITypeInfo typeInfo, int memid, out string? name, out string? doc)
    {
        try { typeInfo.GetDocumentation(memid, out name, out doc, out _, out _); }
        catch { name = null; doc = null; }
    }
}
