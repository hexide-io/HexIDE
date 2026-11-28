namespace HexIDE.VisualDesigner;

public interface IDesignerCommand
{
    string Description { get; }
    DesignerUndoName DisplayName { get; }
    void Execute(FormEditViewModel vm);
    void Undo(FormEditViewModel vm);
}
