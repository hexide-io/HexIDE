using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// VB6's Collection (#1471), which didn't exist, so a program that declared one stopped before it ran. Each transcript is what
/// <c>vb6.exe</c> logged for the same program under en-GB, native and p-code agreeing (the oracle's "Collection"). The
/// programs declare `As Collection` and Set it, since `Dim c As New Collection` is #435.
/// </summary>
public class CollectionTests : BaseVBTestFixture
{
    private static List<Vb6Value> Lines(string transcript) =>
        [.. transcript.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Select(l => new Vb6Value(l))];

    // Each statement's outcome: "ok", or the error it raised.
    private const string Helper = """
        Private Sub R(ByVal label As String)
            If Err.Number <> 0 Then Debug.Print label & " -> err " & Err.Number Else Debug.Print label & " -> ok"
            Err.Clear
        End Sub

        """;

    private const string Classes = """
        Public Name As String
        Private Sub Class_Initialize()
            Name = "unnamed"
        End Sub
        Public Sub Hello()
            Debug.Print "  hello " & Name
        End Sub
        Private Sub Class_Terminate()
            Debug.Print "  terminate " & Name
        End Sub

        """;

    private const string HolderClass = """
        Private m As Collection
        Private Sub Class_Initialize()
            Set m = New Collection
            Dim o As Class1
            Set o = New Class1: o.Name = "in holder": m.Add o
        End Sub
        Private Sub Class_Terminate()
            Debug.Print "  holder terminate, m.Count " & m.Count
        End Sub

        """;

    [Fact]
    public async Task The_issues_own_program_runs()
    {
        await Run("""
            Dim c As Collection, v As Variant
            Set c = New Collection
            c.Add "apple", "a"
            c.Add "pear"
            Debug.Print c.Count & " " & c("a") & " " & c(2)
            For Each v In c: Debug.Print v: Next

            """);

        AssertDebugLog(Lines("""
            2 apple pear
            apple
            pear
            """));
    }

    [Fact]
    public async Task An_index_is_a_key_when_it_is_a_String_and_a_position_otherwise()
    {
        await Run(Helper + """
            On Error Resume Next
            Dim c As Collection, v As Variant
            Set c = New Collection
            c.Add "apple", "a": R "Add apple, a"
            c.Add "pear": R "Add pear"
            Debug.Print "Count " & c.Count & ", TypeName(Count) " & TypeName(c.Count)
            Debug.Print "c(1) " & c(1) & ", c(2) " & c(2) & ", c(a) " & c("a") & ", c(A) " & c("A")
            Debug.Print "c.Item(1) " & c.Item(1) & ", c.Item(Index:=2) " & c.Item(Index:=2) & ", c!a " & c!a & ", c![A] " & c![A]
            v = "-": v = c(0): R "c(0) " & v
            v = "-": v = c(3): R "c(3) " & v
            v = "-": v = c(-1): R "c(-1) " & v
            v = "-": v = c("zz"): R "c(zz) " & v
            v = "-": v = c(1.5): R "c(1.5) " & v
            v = "-": v = c(2.5): R "c(2.5) " & v
            v = "-": v = c(0.5): R "c(0.5) " & v
            v = "-": v = c("2"): R "the String key 2 " & v
            v = "-": v = c(Empty): R "c(Empty) " & v
            v = "-": v = c(Null): R "c(Null) " & v
            v = "-": v = c(True): R "c(True) " & v
            v = "-": v = c(CByte(1)): R "c(CByte(1)) " & v
            v = "-": v = c(CCur(2)): R "c(CCur(2)) " & v
            v = "-": v = c(#1/1/1900#): R "c(date) " & v
            v = "-": v = c(2147483648#): R "c(2147483648#) " & v
            c.Add "dup", "a": R "Add dup, a"
            c.Add "dupA", "A": R "Add dupA, A"
            c.Add "one", "1": R "Add one, key 1 as a String"
            Debug.Print "c(1) " & c(1) & ", the String key 1 " & c("1")
            c.Add "five", 5: R "Add five, 5"
            c.Add "emptystr", "": R "Add with an empty key"
            Debug.Print "the empty key " & c("")
            c.Add "null", Null: R "Add key Null"
            c.Add "empty", Empty: R "Add key Empty"
            c.Add "space", "a ": R "Add key a with a space"
            c.Add "e-acute", "" & Chr(233): R "Add key e-acute"
            c.Add "E-acute", "" & Chr(201): R "Add key E-acute"
            Debug.Print "Count " & c.Count
            Debug.Print "TypeName " & TypeName(c) & ", VarType " & VarType(c) & ", IsObject " & IsObject(c)

            """);

        AssertDebugLog(Lines("""
            Add apple, a -> ok
            Add pear -> ok
            Count 2, TypeName(Count) Long
            c(1) apple, c(2) pear, c(a) apple, c(A) apple
            c.Item(1) apple, c.Item(Index:=2) pear, c!a apple, c![A] apple
            c(0) - -> err 9
            c(3) - -> err 9
            c(-1) - -> err 9
            c(zz) - -> err 5
            c(1.5) pear -> ok
            c(2.5) pear -> ok
            c(0.5) - -> err 9
            the String key 2 - -> err 5
            c(Empty) - -> err 9
            c(Null) - -> err 13
            c(True) - -> err 9
            c(CByte(1)) apple -> ok
            c(CCur(2)) pear -> ok
            c(date) pear -> ok
            c(2147483648#) - -> err 13
            Add dup, a -> err 457
            Add dupA, A -> err 457
            Add one, key 1 as a String -> ok
            c(1) apple, the String key 1 one
            Add five, 5 -> err 13
            Add with an empty key -> ok
            the empty key emptystr
            Add key Null -> err 13
            Add key Empty -> err 13
            Add key a with a space -> ok
            Add key e-acute -> ok
            Add key E-acute -> err 457
            Count 6
            TypeName Collection, VarType 9, IsObject True
            """));
    }

    // Two keys are one where vb6.exe's text compare finds them equal (measured as keys and with StrComp, #1479): ß is ss and
    // æ is ae, and Chr(0) is ignored, while é, ª, ö and a no-break space stay apart from e, a, o and a space.
    [Fact]
    public async Task A_key_is_the_same_key_as_vb6s_text_compare_finds_it()
    {
        await Run("""
            Sub Pair(ByVal a As String, ByVal b As String, ByVal name As String)
                Dim c As New Collection
                On Error Resume Next
                Err.Clear
                c.Add 1, a
                c.Add 2, b
                Debug.Print name & " " & Err.Number
            End Sub
            Pair "ae", Chr(230), "ae"
            Pair "AE", Chr(198), "AE"
            Pair "ss", Chr(223), "ss"
            Pair "oe", Chr(156), "oe"
            Pair "th", Chr(254), "th"
            Pair "1", Chr(185), "superscript 1"
            Pair "a" & Chr(0), "a", "NUL"
            Pair "e", Chr(233), "e-acute"
            Pair "co-op", "coop", "co-op"
            Pair "a", Chr(170), "ordinal a"
            Pair "o", Chr(246), "o-umlaut"
            Pair "x" & Chr(160), "x ", "no-break space"
            """);

        AssertDebugLog(Lines("""
            ae 457
            AE 457
            ss 457
            oe 457
            th 457
            superscript 1 457
            NUL 457
            e-acute 0
            co-op 0
            ordinal a 0
            o-umlaut 0
            no-break space 0
            """));
    }

    // vb6.exe's words for 457, as the oracle has recorded them since the Collection was measured; HexIDE had others.
    [Fact]
    public async Task A_duplicate_key_is_457_in_vb6s_words()
    {
        await Run("""
            On Error Resume Next
            Dim c As New Collection
            c.Add 1, "k"
            c.Add 2, "K"
            Debug.Print Err.Number & " " & Err.Description
            """);

        AssertDebugLog([new Vb6Value("457 This key is already associated with an element of this collection")]);
    }

    [Fact]
    public async Task Add_places_an_item_by_Before_or_After_and_checks_in_VB6s_order()
    {
        await Run(Helper + """
            On Error Resume Next
            Dim d As Collection, i As Long
            Set d = New Collection
            d.Add "x": d.Add "y"
            d.Add "b0", , 1: R "Add b0 before 1"
            d.Add "a9", , , 3: R "Add a9 after 3"
            d.Add "bk", "kk", 2: R "Add bk key kk before 2"
            d.Add "ak", , "kk": R "Add ak before key kk"
            d.Add "ak2", , , "kk": R "Add ak2 after key kk"
            For i = 1 To d.Count: Debug.Print "  d(" & i & ") " & d(i): Next
            d.Add "bad", , 0: R "Before 0"
            d.Add "bad", , d.Count + 1: R "Before Count + 1"
            d.Add "bad", , , 0: R "After 0"
            d.Add "bad", , 1, 1: R "Before and After"
            d.Add "bad", , "nokey": R "Before a missing key"
            d.Add "bad", , Null: R "Before Null"
            d.Add "bad", , Empty: R "Before Empty"
            d.Add "bad", "kk", 0: R "a duplicate key, Before 0"
            d.Add "bad", 5, , 0: R "a numeric key, After 0"
            d.Add "bad", 5, 1, 1: R "a numeric key, Before and After"
            d.Add "bad", "kk", "nokey": R "a duplicate key, Before a missing key"
            d.Add "last", , , d.Count: R "After Count"
            Debug.Print "Count " & d.Count & ", last " & d(d.Count)
            d.Add Item:="n1", Key:="nk": R "Add Item:=, Key:="
            d.Add "n2", Before:=1: R "Add Before:=1"
            d.Add After:=2, Item:="n3": R "Add After:=2, Item:="
            Debug.Print "d(1) " & d(1) & ", d(2) " & d(2) & ", d(3) " & d(3) & ", d!nk " & d!nk
            d.Remove 1: R "Remove 1"
            d.Remove "nk": R "Remove nk"
            d.Remove "nk": R "Remove nk again"
            d.Remove 0: R "Remove 0"
            d.Remove Null: R "Remove Null"
            d.Remove True: R "Remove True"
            d.Remove Index:=1: R "Remove Index:=1"
            d.Remove "KK": R "Remove KK"
            Debug.Print "Count " & d.Count & ", d(1) " & d(1)

            """);

        AssertDebugLog(Lines("""
            Add b0 before 1 -> ok
            Add a9 after 3 -> ok
            Add bk key kk before 2 -> ok
            Add ak before key kk -> ok
            Add ak2 after key kk -> ok
              d(1) b0
              d(2) ak
              d(3) bk
              d(4) ak2
              d(5) x
              d(6) y
              d(7) a9
            Before 0 -> err 9
            Before Count + 1 -> err 9
            After 0 -> err 9
            Before and After -> err 5
            Before a missing key -> err 5
            Before Null -> err 13
            Before Empty -> err 9
            a duplicate key, Before 0 -> err 457
            a numeric key, After 0 -> err 457
            a numeric key, Before and After -> err 5
            a duplicate key, Before a missing key -> err 457
            After Count -> ok
            Count 8, last last
            Add Item:=, Key:= -> ok
            Add Before:=1 -> ok
            Add After:=2, Item:= -> ok
            d(1) n2, d(2) b0, d(3) n3, d!nk n1
            Remove 1 -> ok
            Remove nk -> ok
            Remove nk again -> err 5
            Remove 0 -> err 9
            Remove Null -> err 13
            Remove True -> err 9
            Remove Index:=1 -> ok
            Remove KK -> ok
            Count 7, d(1) n3
            """));
    }

    [Fact]
    public async Task A_Collection_is_held_by_reference_and_its_items_are_read_only()
    {
        await Run(Helper + """
            On Error Resume Next
            Dim c As Collection, e As Collection, v As Variant, i As Long, s As String
            Debug.Print "e Is Nothing " & (e Is Nothing)
            i = -1: i = e.Count: R "e.Count on Nothing " & i
            v = "-": v = e(1): R "e(1) on Nothing"
            e.Add 1: R "e.Add on Nothing"
            For Each v In e
                Debug.Print "  body, v " & TypeName(v)
            Next
            R "For Each on Nothing"
            Set c = New Collection
            Set e = c: e.Add "via e": Debug.Print "c.Count " & c.Count & ", e Is c " & (e Is c)
            Set e = New Collection: Debug.Print "e Is c after a new one " & (e Is c)
            s = "orig": c.Add s, "s": s = "new": Debug.Print "c(s) " & c("s")
            c(1) = "x": R "c(1) = x"
            c!s = "y": R "c!s = y"
            c.Item(1) = 5: R "c.Item(1) = 5"
            Set c(1) = New Collection: R "Set c(1) = New Collection"
            c.Foo: R "c.Foo"
            v = "-": v = c: R "v = c"
            Set v = c: Debug.Print "TypeName(v) " & TypeName(v) & ", v.Count " & v.Count & ", v(1) " & v(1) & ", v!s " & v!s
            Dim arr(2): arr(1) = "elem": c.Add arr, "arr": arr(1) = "changed"
            v = c("arr"): Debug.Print "the array item " & v(1) & ", " & TypeName(c("arr"))
            c.Add c, "self": Debug.Print "self " & (c("self") Is c)
            With c
                .Add 9, "nine"
                Debug.Print "With " & .Count & " " & .Item("nine") & " " & !nine
            End With
            Call c.Add(6, "six"): Debug.Print "Call c.Add " & c("six")
            Debug.Print "TypeOf c Is Collection " & (TypeOf c Is Collection) & ", Is Object " & (TypeOf c Is Object) _
                & ", Is VBA.Collection " & (TypeOf c Is VBA.Collection)

            """);

        AssertDebugLog(Lines("""
            e Is Nothing True
            e.Count on Nothing -1 -> err 91
            e(1) on Nothing -> err 91
            e.Add on Nothing -> err 91
              body, v String
            For Each on Nothing -> err 92
            c.Count 1, e Is c True
            e Is c after a new one False
            c(s) orig
            c(1) = x -> err 424
            c!s = y -> err 424
            c.Item(1) = 5 -> err 424
            Set c(1) = New Collection -> err 424
            c.Foo -> err 438
            v = c -> err 450
            TypeName(v) Collection, v.Count 2, v(1) via e, v!s orig
            the array item elem, Variant()
            self True
            With 5 9 9
            Call c.Add 6
            TypeOf c Is Collection True, Is Object True, Is VBA.Collection True
            """));
    }

    [Fact]
    public async Task An_empty_Collection_refuses_every_index_with_5_and_an_assigned_item_is_looked_up_first()
    {
        await Run(Helper + """
            On Error Resume Next
            Dim e As Collection, c As Collection, o As Object, w As Variant, v As Variant
            Set e = New Collection
            v = "-": v = e(1): R "e(1)"
            v = "-": v = e(0): R "e(0)"
            v = "-": v = e("k"): R "e(k)"
            v = "-": v = e(Null): R "e(Null)"
            v = "-": v = e(Empty): R "e(Empty)"
            v = "-": v = e.Item(Index:=1): R "e.Item(Index:=1)"
            e.Remove 1: R "e.Remove 1"
            e.Remove "k": R "e.Remove k"
            e.Add "x", , 1: R "e.Add Before 1"
            e.Add "x", , , 1: R "e.Add After 1"
            e.Add "x", 1: R "e.Add with key 1"
            Debug.Print "e.Count " & e.Count
            Set o = e
            v = "-": v = o(1): R "o(1), late-bound"
            Set c = New Collection: c.Add "a", "k": c.Add "b"
            c(1) = 5: R "c(1) = 5"
            c(3) = 5: R "c(3) = 5"
            c(0) = 5: R "c(0) = 5"
            c("k") = 5: R "c(k) = 5"
            c("zz") = 5: R "c(zz) = 5"
            c!k = 5: R "c!k = 5"
            c!zz = 5: R "c!zz = 5"
            c.Item(1) = 5: R "c.Item(1) = 5"
            c.Item(3) = 5: R "c.Item(3) = 5"
            Set c(1) = Nothing: R "Set c(1) = Nothing"
            Set c(3) = Nothing: R "Set c(3) = Nothing"
            e(1) = 5: R "e(1) = 5"
            e!k = 5: R "e!k = 5"
            Set o = c
            o(1) = 5: R "o(1) = 5"
            o(3) = 5: R "o(3) = 5"
            o.Item(1) = 5: R "o.Item(1) = 5"
            Set w = c
            w(1) = 5: R "w(1) = 5"
            w(3) = 5: R "w(3) = 5"
            Set w(1) = Nothing: R "Set w(1) = Nothing"
            w!k = 5: R "w!k = 5"
            w!zz = 5: R "w!zz = 5"
            w.Item(1) = 5: R "w.Item(1) = 5"
            w.Item(3) = 5: R "w.Item(3) = 5"
            o!k = 5: R "o!k = 5"
            Set o(1) = Nothing: R "Set o(1) = Nothing"
            Debug.Print "c(1) " & c(1) & ", c(2) " & c(2) & ", Count " & c.Count
            c.Remove 1: c.Remove 1
            v = "-": v = c(1): R "c(1), emptied by Remove"

            """);

        AssertDebugLog(Lines("""
            e(1) -> err 5
            e(0) -> err 5
            e(k) -> err 5
            e(Null) -> err 5
            e(Empty) -> err 5
            e.Item(Index:=1) -> err 5
            e.Remove 1 -> err 5
            e.Remove k -> err 5
            e.Add Before 1 -> err 5
            e.Add After 1 -> err 5
            e.Add with key 1 -> err 13
            e.Count 0
            o(1), late-bound -> err 5
            c(1) = 5 -> err 424
            c(3) = 5 -> err 9
            c(0) = 5 -> err 9
            c(k) = 5 -> err 424
            c(zz) = 5 -> err 5
            c!k = 5 -> err 424
            c!zz = 5 -> err 5
            c.Item(1) = 5 -> err 424
            c.Item(3) = 5 -> err 9
            Set c(1) = Nothing -> err 424
            Set c(3) = Nothing -> err 9
            e(1) = 5 -> err 5
            e!k = 5 -> err 5
            o(1) = 5 -> err 451
            o(3) = 5 -> err 9
            o.Item(1) = 5 -> err 451
            w(1) = 5 -> err 438
            w(3) = 5 -> err 438
            Set w(1) = Nothing -> err 438
            w!k = 5 -> err 451
            w!zz = 5 -> err 5
            w.Item(1) = 5 -> err 451
            w.Item(3) = 5 -> err 9
            o!k = 5 -> err 451
            Set o(1) = Nothing -> err 438
            c(1) a, c(2) b, Count 2
            c(1), emptied by Remove -> err 5
            """));
    }

    [Fact]
    public async Task Through_an_Object_or_a_Variant_the_errors_come_at_run_time()
    {
        await Run(Helper + """
            On Error Resume Next
            Dim o As Object, w As Variant, v As Variant
            Set o = New Collection: o.Add 1, "a"
            Debug.Print "o(1) " & o(1) & ", o(a) " & o("a") & ", o.Item(1) " & o.Item(1) & ", o!a " & o!a & ", o.Count " & o.Count
            o.Add: R "o.Add"
            o.Add 1, , , , 5: R "o.Add with five"
            o.Add 1, Item:=2: R "o.Add 1, Item:=2"
            o.Add 1, Kay:="z": R "o.Add 1, Kay:="
            o.Add Key:="b": R "o.Add Key:= alone"
            v = "-": v = o.Item: R "o.Item"
            v = "-": v = o.Count(1): R "o.Count(1)"
            o.Remove: R "o.Remove"
            o.Remove 1, 2: R "o.Remove 1, 2"
            v = "-": v = o(): R "o()"
            v = "-": v = o(1, 2): R "o(1, 2)"
            v = "-": v = o: R "v = o"
            v = "-": v = o.Foo: R "o.Foo"
            o = 5: R "o = 5"
            Debug.Print "TypeName(o) " & TypeName(o)
            Set w = New Collection
            w.Add 1, Kay:="z": R "w.Add 1, Kay:="
            w.Add: R "w.Add"
            Debug.Print "w.Count " & w.Count
            w = 5: R "w = 5"
            Debug.Print "TypeName(w) " & TypeName(w)

            """);

        AssertDebugLog(Lines("""
            o(1) 1, o(a) 1, o.Item(1) 1, o!a 1, o.Count 1
            o.Add -> err 450
            o.Add with five -> err 450
            o.Add 1, Item:=2 -> err 448
            o.Add 1, Kay:= -> err 448
            o.Add Key:= alone -> err 450
            o.Item -> err 450
            o.Count(1) -> err 451
            o.Remove -> err 450
            o.Remove 1, 2 -> err 450
            o() -> err 450
            o(1, 2) -> err 450
            v = o -> err 450
            o.Foo -> err 438
            o = 5 -> err 438
            TypeName(o) Collection
            w.Add 1, Kay:= -> err 448
            w.Add -> err 450
            w.Count 0
            w = 5 -> ok
            TypeName(w) Integer
            """));
    }

    [Fact]
    public async Task For_Each_goes_on_to_whatever_now_follows_the_item_it_is_on()
    {
        await Run(Helper + """
            On Error Resume Next
            Dim f As Collection, x As Variant, y As Variant, i As Long
            Set f = New Collection: f.Add "a": f.Add "b": f.Add "c"
            For Each x In f: Debug.Print "plain " & x: Next
            Debug.Print "after the loop, TypeName(x) " & TypeName(x)
            For Each x In f
                Debug.Print "remove current " & x
                If x = "a" Then f.Remove 1
            Next
            f.Add "d"
            For Each x In f
                Debug.Print "remove next " & x
                If x = "b" Then f.Remove 2
            Next
            i = 0
            For Each x In f
                Debug.Print "add at the end " & x
                i = i + 1
                If i = 1 Then f.Add "z"
            Next
            i = 0
            For Each x In f
                Debug.Print "add before " & x
                i = i + 1
                If i = 1 Then f.Add "first", , 1
            Next
            Set f = New Collection: f.Add "a": f.Add "b": f.Add "c": f.Add "d"
            For Each x In f
                Debug.Print "remove current and next " & x
                If x = "a" Then f.Remove 1: f.Remove 1
            Next
            Set f = New Collection: f.Add "a": f.Add "b": f.Add "c"
            For Each x In f
                Debug.Print "remove everything " & x
                If x = "b" Then f.Remove 1: f.Remove 1: f.Remove 1
            Next
            Debug.Print "Count " & f.Count
            Set f = New Collection: f.Add "a": f.Add "b": f.Add "c"
            For Each x In f
                If x = "b" Then Exit For
            Next
            Debug.Print "after Exit For, x " & x
            For Each x In f
                For Each y In f
                    If x = "a" Then Debug.Print "nested " & x & y
                Next
            Next
            Set f = New Collection: f.Add "a": f.Add "b"
            For Each x In f
                Debug.Print "replace the Collection " & x
                Set f = New Collection: f.Add "new"
            Next
            Debug.Print "f(1) " & f(1)
            Dim g As Collection: Set g = New Collection
            x = "unchanged"
            For Each x In g: Debug.Print "never": Next
            Debug.Print "after an empty loop, TypeName(x) " & TypeName(x)
            Dim k As Collection: Set k = New Collection: k.Add 1: k.Add 2
            For Each x In k: x = x * 10: Next
            Debug.Print "k(1) after writing x " & k(1)
            Dim o As Object
            Set f = New Collection: f.Add "s": f.Add 2
            For Each o In f: Debug.Print "Object loop variable " & TypeName(o): Next
            R "Object loop variable over a String"
            Set f = New Collection: f.Add 1: f.Add CByte(2): f.Add "3": f.Add 4.5!: f.Add Null: f.Add Empty: f.Add #1/2/2003#
            For Each x In f: Debug.Print "TypeName " & TypeName(x): Next

            """);

        AssertDebugLog(Lines("""
            plain a
            plain b
            plain c
            after the loop, TypeName(x) Empty
            remove current a
            remove current b
            remove current c
            remove next b
            remove next d
            add at the end b
            add at the end d
            add at the end z
            add before b
            add before d
            add before z
            remove current and next a
            remove current and next c
            remove current and next d
            remove everything a
            remove everything b
            Count 0
            after Exit For, x b
            nested aa
            nested ab
            nested ac
            replace the Collection a
            f(1) new
            after an empty loop, TypeName(x) Empty
            k(1) after writing x 1
            Object loop variable Nothing
            Object loop variable over a String -> err 424
            TypeName Integer
            TypeName Byte
            TypeName String
            TypeName Single
            TypeName Null
            TypeName Empty
            TypeName Date
            """));
    }

    [Fact]
    public async Task An_object_item_is_held_until_its_last_reference_goes()
    {
        await RunClasses(Helper + """
            Private Sub Scoped()
                Dim lc As Collection, t As Class1
                Set lc = New Collection: Set t = New Class1
                t.Name = "scoped": lc.Add t: Set t = Nothing
                Debug.Print "leaving Scoped"
            End Sub
            Private Function MakeC() As Collection
                Set MakeC = New Collection
                Dim o As Class1
                Set o = New Class1
                o.Name = "from MakeC"
                MakeC.Add o
            End Function
            On Error Resume Next
            Dim c As Collection, o As Class1, v As Variant, h As Holder
            Set c = New Collection
            Set o = New Class1: o.Name = "one": c.Add o, "one"
            Set o = New Class1: o.Name = "two": c.Add o
            Set o = New Class1: o.Name = "three": c.Add o
            Set o = Nothing
            Debug.Print "after Set o = Nothing"
            c(1).Hello
            c("one").Name = "uno": Debug.Print "renamed " & c(1).Name & " " & c!one.Name
            c.Item(2).Hello
            Set o = c(2): Debug.Print "o Is c(2) " & (o Is c(2)): Set o = Nothing
            Debug.Print "IsObject(c(1)) " & IsObject(c(1)) & ", TypeName(c(1)) " & TypeName(c(1))
            Debug.Print "remove 2:": c.Remove 2: Debug.Print "removed"
            For Each o In c: Debug.Print "loop " & o.Name: Next
            Debug.Print "after the loop, o Is Nothing " & (o Is Nothing)
            Debug.Print "remove one by key:": c.Remove "one": Debug.Print "removed"
            Debug.Print "Set c = Nothing:": Set c = Nothing: Debug.Print "after"
            Set c = New Collection
            Set o = New Class1: o.Name = "a": c.Add o: Set o = Nothing
            Set o = New Class1: o.Name = "b": c.Add o: Set o = Nothing
            Debug.Print "Set c = New Collection:": Set c = New Collection: Debug.Print "replaced"
            c.Add New Class1: c(1).Name = "direct"
            Debug.Print "remove:": c.Remove 1: Debug.Print "removed"
            Set o = New Class1: o.Name = "held": c.Add o
            Debug.Print "remove while o holds it:": c.Remove 1: Debug.Print "removed, o.Name " & o.Name
            Set o = Nothing
            Set o = New Class1: o.Name = "twice": c.Add o: c.Add o: Set o = Nothing
            Debug.Print "remove the first of two:": c.Remove 1: Debug.Print "removed"
            Debug.Print "remove the second:": c.Remove 1: Debug.Print "removed"
            Set o = New Class1: o.Name = "in loop": c.Add o: Set o = Nothing
            For Each v In c
                Debug.Print "the loop holds " & v.Name
                c.Remove 1
                Debug.Print "removed inside the loop"
            Next
            Debug.Print "after the loop"
            Set o = New Class1: o.Name = "exit": c.Add o: Set o = Nothing
            For Each v In c: Exit For: Next
            Debug.Print "after Exit For, v " & TypeName(v)
            c.Remove 1: Debug.Print "removed"
            Debug.Print "v = Empty:": v = Empty: Debug.Print "emptied"
            Dim outer As Collection: Set outer = New Collection: outer.Add New Collection: outer(1).Add "inner"
            Debug.Print "outer(1).Item(1) " & outer(1).Item(1) & ", outer(1).Count " & outer(1).Count
            Scoped
            Debug.Print "after Scoped"
            Set c = MakeC()
            Debug.Print "MakeC Count " & c.Count & ", c(1).Name " & c(1).Name
            Debug.Print "Set c = Nothing:": Set c = Nothing: Debug.Print "done"
            Debug.Print "MakeC discarded:": MakeC.Add 5: Debug.Print "done"
            Debug.Print "a Holder:": Set h = New Holder: Debug.Print "made"
            Debug.Print "Set h = Nothing:": Set h = Nothing: Debug.Print "done"
            Set c = New Class1: R "Set c = New Class1"
            Debug.Print "c Is Nothing " & (c Is Nothing)
            Set o = New Collection: R "Set o = New Collection"
            Debug.Print "o Is Nothing " & (o Is Nothing)
            Debug.Print "end"

            """, ("Class1", Classes), ("Holder", HolderClass));

        AssertDebugLog(Lines("""
            after Set o = Nothing
              hello one
            renamed uno uno
              hello two
            o Is c(2) True
            IsObject(c(1)) True, TypeName(c(1)) Class1
            remove 2:
              terminate two
            removed
            loop uno
            loop three
            after the loop, o Is Nothing True
            remove one by key:
              terminate uno
            removed
            Set c = Nothing:
              terminate three
            after
            Set c = New Collection:
              terminate a
              terminate b
            replaced
            remove:
              terminate direct
            removed
            remove while o holds it:
            removed, o.Name held
              terminate held
            remove the first of two:
            removed
            remove the second:
              terminate twice
            removed
            the loop holds in loop
            removed inside the loop
              terminate in loop
            after the loop
            after Exit For, v Class1
            removed
            v = Empty:
              terminate exit
            emptied
            outer(1).Item(1) inner, outer(1).Count 1
            leaving Scoped
              terminate scoped
            after Scoped
            MakeC Count 1, c(1).Name from MakeC
            Set c = Nothing:
              terminate from MakeC
            done
            MakeC discarded:
              terminate from MakeC
            done
            a Holder:
            made
            Set h = Nothing:
              holder terminate, m.Count 1
              terminate in holder
            done
              terminate unnamed
            Set c = New Class1 -> err 13
            c Is Nothing True
            Set o = New Collection -> err 13
            o Is Nothing True
            end
            """));
    }

    [Theory]
    [InlineData("c.Add", "*Argument not optional*")]
    [InlineData("c.Add 1, , , , 5", "*Wrong number of arguments or invalid property assignment*")]
    [InlineData("c.Add 1, Kay:=\"a\"", "*Named argument not found*")]
    [InlineData("v = c.Item", "*Argument not optional*")]
    [InlineData("v = c(1, 2)", "*Wrong number of arguments or invalid property assignment*")]
    [InlineData("v = c.Count(1)", "*Wrong number of arguments or invalid property assignment*")]
    [InlineData("c.Remove", "*Argument not optional*")]
    [InlineData("c.Remove 1, 2", "*Wrong number of arguments or invalid property assignment*")]
    [InlineData("c.Count = 5", "*Function call on left-hand side of assignment must return Variant or Object*")]
    [InlineData("c = 5", "*Argument not optional*")]
    [InlineData("For Each y In c: Next", "*For Each control variable must be Variant or Object*")]
    [InlineData("For Each sv In c: Next", "*For Each control variable must be Variant or Object*")]
    public async Task What_VB6_refuses_to_compile_is_refused(string statement, string message)
    {
        var run = () => Run("""
            Dim c As Collection, v As Variant, y As Long, sv As String
            Set c = New Collection
            c.Add 1

            """ + statement + "\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage(message);
    }
}
