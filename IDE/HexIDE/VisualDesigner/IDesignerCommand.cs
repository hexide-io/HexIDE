namespace HexIDE.VisualDesigner;

public interface IDesignerCommand
{
    string Description { get; }
    void Execute(FormEditViewModel vm);
    void Undo(FormEditViewModel vm);
}
