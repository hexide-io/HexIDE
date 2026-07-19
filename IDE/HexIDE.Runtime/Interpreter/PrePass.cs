using System;
using System.Collections.Generic;

namespace HexIDE.Runtime.Interpreter;

public class PrePass : VB6BaseVisitor<object?>
{
    private readonly ExecutionEnvironment rootEnv;
    private readonly ExecutionState state;
    public Dictionary<string, (VB6Parser.SubStmtContext, ExecutionEnvironment)> subs = new();
    public List<VB6Parser.BlockContext> topLevelBlocks = new();
    public bool RequireVariableDefinitions { get; private set; }
    public int ArrayBase { get; private set; } = 0;

    public PrePass(ExecutionEnvironment rootEnv, ExecutionState state)
    {
        this.rootEnv = rootEnv;
        this.state = state;
    }

    public override object? VisitModuleBlock(VB6Parser.ModuleBlockContext context)
    {
        topLevelBlocks.Add(context.block());
        Visit(context.block());
        return default;
    }

    public override object? VisitOptionBaseStmt(VB6Parser.OptionBaseStmtContext context)
    {
        ArrayBase = int.Parse(context.INTEGERLITERAL().GetText());
        return default;
    }

    public override object? VisitOptionCompareStmt(VB6Parser.OptionCompareStmtContext context)
        => throw new NotImplementedException("Option compare not supported");

    public override object? VisitOptionPrivateModuleStmt(VB6Parser.OptionPrivateModuleStmtContext context)
        => throw new NotImplementedException("Option private module not supported");

    public override object? VisitOptionExplicitStmt(VB6Parser.OptionExplicitStmtContext context)
    {
        RequireVariableDefinitions = true;
        return default;
    }

    public override object? VisitVariableStmt(VB6Parser.VariableStmtContext context)
    {
        if (context.WITHEVENTS() != null)
            throw new NotImplementedException("WITHEVENTS not implemented");

        if (context.DIM() != null)
        {
            foreach (var subStmt in context.variableListStmt().variableSubStmt())
            {
                if (subStmt.typeHint() != null)
                    throw new NotImplementedException("DIM type hints not implemented");
                bool isArray = false;
                List<(int, int)>? dimensions = null;
                if (subStmt.LPAREN() != null && subStmt.RPAREN() != null) // array
                {
                    isArray = true;
                    if (subStmt.subscripts() != null)
                    {
                        dimensions = new List<(int, int)>();
                        int arrayLowerBound;
                        int arrayUpperBound;
                        foreach (var dimension in subStmt.subscripts().subscript())
                        {
                            var size = dimension.valueStmt();
                            if (size.Length == 2)
                            {
                                arrayLowerBound = int.Parse(size[0].GetText());
                                arrayUpperBound = int.Parse(size[1].GetText());
                            }
                            else if (size.Length == 1)
                            {
                                arrayLowerBound = ArrayBase;
                                arrayUpperBound = int.Parse(size[0].GetText());
                            }
                            else
                                throw new VBCompileErrorException("Either specify upper bound or lower and upper bound");
                            dimensions.Add((arrayLowerBound, arrayUpperBound));
                        }
                    }
                }

                Vb6Value.ValueType type = Vb6Value.ValueType.EmptyVariant;
                if (subStmt.asTypeClause() != null)
                {
                    if (subStmt.asTypeClause().NEW() != null)
                        throw new NotImplementedException("New as type not implemented");
                    if (subStmt.asTypeClause().fieldLength() != null)
                        throw new NotImplementedException("fieldLength as type not implemented");
                    if (subStmt.asTypeClause().type().complexType() != null)
                        throw new NotImplementedException("complex type as type not implemented");
                    if (subStmt.asTypeClause().type().baseType().STRING() != null)
                        type = Vb6Value.ValueType.String;
                    else if (subStmt.asTypeClause().type().baseType().INTEGER() != null)
                        type = Vb6Value.ValueType.Integer;
                    else if (subStmt.asTypeClause().type().baseType().SINGLE() != null)
                        type = Vb6Value.ValueType.Single;
                    else if (subStmt.asTypeClause().type().baseType().DOUBLE() != null)
                        type = Vb6Value.ValueType.Double;
                    else if (subStmt.asTypeClause().type().baseType().BOOLEAN() != null)
                        type = Vb6Value.ValueType.Boolean;
                    else
                        throw new NotImplementedException("base type " + subStmt.asTypeClause().type().baseType().GetChild(0) + " not implemented");
                }
                if (isArray)
                    type = new Vb6Value.ValueType(type, true);

                var value = dimensions != null ? new Vb6Value(type, dimensions) : new Vb6Value(type);
                var location = state.Alloc(value);
                rootEnv.DefineVariable(subStmt.ambiguousIdentifier().GetText(), location);
            }
        }
        else
            throw new NotImplementedException("non dim variables not supported");

        return default;
    }

    public override object? VisitDeclareStmt(VB6Parser.DeclareStmtContext context)
        => throw new NotImplementedException("DECLARE not supported");

    public override object? VisitEnumerationStmt(VB6Parser.EnumerationStmtContext context)
        => throw new NotImplementedException("Enum not supported");

    public override object? VisitEventStmt(VB6Parser.EventStmtContext context)
        => throw new NotImplementedException("Event not supported");

    public override object? VisitMacroIfThenElseStmt(VB6Parser.MacroIfThenElseStmtContext context)
        => throw new NotImplementedException("macro if then else not supported");

    public override object? VisitPropertyGetStmt(VB6Parser.PropertyGetStmtContext context)
        => throw new NotImplementedException("Property not implemented");

    public override object? VisitPropertySetStmt(VB6Parser.PropertySetStmtContext context)
        => throw new NotImplementedException("Property not implemented");

    public override object? VisitPropertyLetStmt(VB6Parser.PropertyLetStmtContext context)
        => throw new NotImplementedException("Property not implemented");

    public override object? VisitTypeStmt(VB6Parser.TypeStmtContext context)
        => throw new NotImplementedException("Type not implemented");

    public override object? VisitFunctionStmt(VB6Parser.FunctionStmtContext context)
    {
        throw new NotImplementedException("TODO");
    }

    public override object? VisitSubStmt(VB6Parser.SubStmtContext context)
    {
        subs[context.ambiguousIdentifier().GetText()] = (context, rootEnv.Clone());
        return default;
    }
}