namespace HexIDE.VisualDesigner.Commands;

internal class LockControlsCommand : IDesignerCommand
{
    private readonly bool _before;
    private readonly bool _after;

    public string Description { get; }
    public DesignerUndoName DisplayName { get; }

    internal LockControlsCommand(bool before, bool after)
    {
        _before = before;
        _after = after;
        Description = after ? "Lock Controls" : "Unlock Controls";
        DisplayName = DesignerUndoName.Localized(after ? "Str.Designer.Undo.LockControls" : "Str.Designer.Undo.UnlockControls");
    }

    public void Execute(FormEditViewModel vm)
    {
        if (vm.FormDefinition != null)
        {
            vm.FormDefinition.LockControls = _after;
            vm.SaveLockControls();
        }
    }

    public void Undo(FormEditViewModel vm)
    {
        if (vm.FormDefinition != null)
        {
            vm.FormDefinition.LockControls = _before;
            vm.SaveLockControls();
        }
    }
}
