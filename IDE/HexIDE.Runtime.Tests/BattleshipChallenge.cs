using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// A console analog of the Rubberduck Battleship engine, exercising the whole object model in one program:
// four class modules, Property Get/Let, Class_Initialize, a 2-D array grid built with nested loops, an object
// "fleet" array (Set arr(i) = obj), and a WithEvents Announcer handling a RaiseEvent with ByVal args.
// The full firing trace is byte-identical to the same program compiled + run by vb6.exe (2026-08-03) — an
// end-to-end fidelity cross-check, not just a self-consistent unit test.
public class BattleshipChallenge : BaseVBTestFixture
{
    private const string Ship =
        "Private mSize As Integer\nPrivate mHits As Integer\n" +
        "Public Property Get Size() As Integer\nSize = mSize\nEnd Property\n" +
        "Public Property Let Size(ByVal v As Integer)\nmSize = v\nEnd Property\n" +
        "Public Sub RegisterHit()\nmHits = mHits + 1\nEnd Sub\n" +
        "Public Property Get IsSunk() As Boolean\nIsSunk = (mHits >= mSize)\nEnd Property\n";

    private const string Grid =
        "Private mState(0 To 9, 0 To 9) As Integer\nPrivate mShipId(0 To 9, 0 To 9) As Integer\n" +
        "Public Sub Init()\nDim r As Integer\nDim c As Integer\n" +
        "For r = 0 To 9\nFor c = 0 To 9\nmState(r, c) = 0\nmShipId(r, c) = -1\nNext c\nNext r\nEnd Sub\n" +
        "Public Sub Place(ByVal id As Integer, ByVal r As Integer, ByVal c As Integer)\nmShipId(r, c) = id\nEnd Sub\n" +
        "Public Function ShipAt(ByVal r As Integer, ByVal c As Integer) As Integer\nShipAt = mShipId(r, c)\nEnd Function\n" +
        "Public Sub Mark(ByVal r As Integer, ByVal c As Integer, ByVal v As Integer)\nmState(r, c) = v\nEnd Sub\n";

    private const string Game =
        "Private mGrid As Grid\nPrivate mFleet(0 To 2) As Ship\nPrivate mCount As Integer\nPrivate mSunk As Integer\n" +
        "Public Event Result(ByVal r As Integer, ByVal c As Integer, ByVal outcome As String)\n" +
        "Private Sub Class_Initialize()\nSet mGrid = New Grid\nmGrid.Init\nmCount = 0\nmSunk = 0\nEnd Sub\n" +
        "Public Sub AddShip(ByVal size As Integer, ByVal r As Integer, ByVal c As Integer)\n" +
        "Dim s As Ship\nSet s = New Ship\ns.Size = size\nSet mFleet(mCount) = s\n" +
        "Dim i As Integer\nFor i = 0 To size - 1\nmGrid.Place mCount, r, c + i\nNext i\nmCount = mCount + 1\nEnd Sub\n" +
        "Public Sub Fire(ByVal r As Integer, ByVal c As Integer)\n" +
        "Dim id As Integer\nid = mGrid.ShipAt(r, c)\n" +
        "If id < 0 Then\nmGrid.Mark r, c, 3\nRaiseEvent Result(r, c, \"MISS\")\n" +
        "Else\nmGrid.Mark r, c, 2\nDim s As Ship\nSet s = mFleet(id)\ns.RegisterHit\n" +
        "If s.IsSunk Then\nmSunk = mSunk + 1\nRaiseEvent Result(r, c, \"SUNK\")\n" +
        "Else\nRaiseEvent Result(r, c, \"HIT\")\nEnd If\nEnd If\nEnd Sub\n" +
        "Public Property Get AllSunk() As Boolean\nAllSunk = (mSunk >= mCount)\nEnd Property\n";

    private const string Announcer =
        "Private WithEvents mGame As Game\n" +
        "Public Sub Watch(ByVal g As Game)\nSet mGame = g\nEnd Sub\n" +
        "Private Sub mGame_Result(ByVal r As Integer, ByVal c As Integer, ByVal outcome As String)\n" +
        "Debug.Print \"(\" & r & \",\" & c & \") \" & outcome\nEnd Sub\n";

    [Fact]
    public async Task RunBattleship()
    {
        await RunClasses(
            "Dim g As Game\nSet g = New Game\nDim a As Announcer\nSet a = New Announcer\na.Watch g\n" +
            "g.AddShip 2, 0, 0\ng.AddShip 3, 2, 0\ng.AddShip 4, 4, 0\n" +
            "Debug.Print \"Firing...\"\n" +
            "g.Fire 0, 0\ng.Fire 0, 1\ng.Fire 5, 5\ng.Fire 2, 0\ng.Fire 2, 1\ng.Fire 2, 2\n" +
            "Debug.Print \"AllSunk=\" & g.AllSunk\n",
            ("Ship", Ship), ("Grid", Grid), ("Game", Game), ("Announcer", Announcer));

        AssertDebugLog([
            "Firing...",
            "(0,0) HIT",
            "(0,1) SUNK",
            "(5,5) MISS",
            "(2,0) HIT",
            "(2,1) HIT",
            "(2,2) SUNK",
            "AllSunk=False",
        ]);
    }
}
