namespace HexIDE.VisualDesigner.Commands;

internal class LockControlsCommand : IDesignerCommand
{
    private readonly bool _before;
    private readonly bool _after;

    public string Description { get; }

    internal LockControlsCommand(bool before, bool after)
    {
        _before = before;
        _after = after;
        Description = after ? "Lock Controls" : "Unlock Controls";
    }

    public void Execute(FormEditViewModel vm)
    {
        if (vm.FormDefinition != null)
        {
            vm.FormDefinition.LockControls = _after;
            vm.SaveForm();
        }
    }

    public void Undo(FormEditViewModel vm)
    {
        if (vm.FormDefinition != null)
        {
            vm.FormDefinition.LockControls = _before;
            vm.SaveForm();
        }
    }
}
