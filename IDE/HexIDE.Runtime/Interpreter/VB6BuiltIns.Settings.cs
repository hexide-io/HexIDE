using System;
using System.Collections.Generic;
using System.Linq;

namespace HexIDE.Runtime.Interpreter;

// GetSetting, SaveSetting, DeleteSetting and GetAllSettings (#1562), measured on vb6.exe, native and p-code alike (the
// oracle's "GetSetting, SaveSetting, DeleteSetting and GetAllSettings"). Where the values live is the host's
// (IBasicStandardLibrary.Settings); what each statement does with them is here, the same over every store.
public partial class VB6BuiltIns
{
    private static void RegisterSettings(Dictionary<string, BuiltinFn> d)
    {
        d["GetSetting"] = (self, a, _) => self.GetSetting(a);
        d["GetAllSettings"] = (self, a, _) => self.GetAllSettings(a);
        d["SaveSetting"] = (self, a, _) => self.SaveSetting(a);
        d["DeleteSetting"] = (self, a, _) => self.DeleteSetting(a);
    }

    /// <summary>
    /// GetSetting(appname, section, key[, default]): the value as text, or the default where there is none, "" where none is
    /// given. Each name is converted first, as a store into a String converts it (Null is 94, an array 13), and then
    /// checked (an empty one is 5). The default is converted only when it is the answer: a Null default is no error where
    /// the key has a value. A value that is not text, as another program can write one, is 5.
    /// </summary>
    private Vb6Value GetSetting(IReadOnlyList<Vb6Value> a)
    {
        var (app, section, key) = (ElementText(a[0]), ElementText(a[1]), ElementText(a[2]));
        var path = SettingsPath(app, section);
        if (Find(stdLib.Settings.Values(path), SettingName(key)) is { } found)
            return new Vb6Value(found.Text ?? throw InvalidCall());
        return new Vb6Value(Supplied(a, 3) ? ElementText(ObjectAsValue.Scalar(a[3], null)) : "");
    }

    /// <summary>
    /// GetAllSettings(appname, section): a String array, 0 To the last setting by 0 To 1, each row a key and its value, in the
    /// order the store keeps them, the registry's being the order they were made in. A section that is missing, or has no
    /// values, gives Empty, not an empty array. One value that is not text is 5 for the whole call.
    /// </summary>
    private Vb6Value GetAllSettings(IReadOnlyList<Vb6Value> a)
    {
        var (app, section) = (ElementText(a[0]), ElementText(a[1]));
        var values = stdLib.Settings.Values(SettingsPath(app, section));
        if (values is null || values.Count == 0)
            return Vb6Value.Variant;
        var result = new Vb6Value(new Vb6Value.ValueType(Vb6Value.ValueType.String, true), new[] { (0, values.Count - 1), (0, 1) });
        var array = (VBArray)result.Value!;
        for (var i = 0; i < values.Count; i++)
        {
            array.SetValue(new[] { i, 0 }, new Vb6Value(values[i].Name));
            array.SetValue(new[] { i, 1 }, new Vb6Value(values[i].Text ?? throw InvalidCall()));
        }
        return result;
    }

    /// <summary>
    /// SaveSetting appname, section, key, setting: the setting as text, as a store into a String makes it, so 5 is "5" and a
    /// Date its short date. All four are converted before any is checked, so a Null setting is 94 even beside an empty
    /// appname.
    /// </summary>
    private Vb6Value SaveSetting(IReadOnlyList<Vb6Value> a)
    {
        var (app, section, key, setting) = (ElementText(a[0]), ElementText(a[1]), ElementText(a[2]), ElementText(a[3]));
        stdLib.Settings.SetValue(SettingsPath(app, section), SettingName(key), setting);
        return Vb6Value.Variant;
    }

    /// <summary>
    /// DeleteSetting appname[, section[, key]]: a key's value, a section, or with no section the program's every section.
    /// Anything missing is 5. Deleting is not recursive: a section with a section under it is 5 and is left as it was, and
    /// so is a program with one (measured). Deleting a section's last value leaves the section, empty.
    /// </summary>
    private Vb6Value DeleteSetting(IReadOnlyList<Vb6Value> a)
    {
        var app = ElementText(a[0]);
        var section = Supplied(a, 1) ? ElementText(a[1]) : null;
        var key = Supplied(a, 2) ? ElementText(a[2]) : null;
        var path = SettingsPath(app, section);
        var store = stdLib.Settings;
        if (key is not null)
        {
            if (!store.DeleteValue(path, SettingName(key)))
                throw InvalidCall();
            return Vb6Value.Variant;
        }
        var subKeys = store.SubKeys(path) ?? throw InvalidCall();
        if (section is not null)
        {
            if (subKeys.Count > 0)
                throw InvalidCall();
        }
        else
        {
            // The program's sections go one by one, so one with a section under it stops the delete. Which went before it is
            // not measured; here none does.
            var sections = subKeys.Select(s => path.Append(s).ToArray()).ToArray();
            if (sections.Any(s => store.SubKeys(s) is { Count: > 0 }))
                throw InvalidCall();
            foreach (var s in sections)
                store.DeleteKey(s);
        }
        store.DeleteKey(path);
        return Vb6Value.Variant;
    }

    private static VbSettingValue? Find(IReadOnlyList<VbSettingValue>? values, string name)
    {
        if (values is not null)
            foreach (var value in values)
                if (string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase))
                    return value;
        return null;
    }

    /// <summary>
    /// The key a program's name and a section name make. The section is a path below the program's key, as the program's
    /// name may be too: a backslash nests a key, and leading, trailing and doubled ones are passed over, so "\S", "S\" and
    /// "S" are one section and "\" alone is the program's own key (measured). Names are not trimmed. An empty name is 5,
    /// and so is a key name longer than the registry's 255 characters. A path naming no key at all is 5 here; vb6.exe's
    /// answer to that is not measured.
    /// </summary>
    private static IReadOnlyList<string> SettingsPath(string app, string? section)
    {
        if (app.Length == 0 || section is { Length: 0 })
            throw InvalidCall();
        var path = (section is null ? app : app + "\\" + section).Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (path.Length == 0 || path.Any(name => name.Length > 255))
            throw InvalidCall();
        return path;
    }

    private static string SettingName(string key) => key.Length == 0 ? throw InvalidCall() : key;
}
