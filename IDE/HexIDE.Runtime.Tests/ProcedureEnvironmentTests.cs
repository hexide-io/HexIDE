using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Debugging;

namespace HexIDE.Runtime.Tests;

public class ProcedureEnvironmentTests : BaseVBTestFixture
{
    [Fact]
    public void ProcedureNames_ExcludeModuleBindings()
    {
        rootEnv.DefineVariable("outer", 12);
        var proc = rootEnv.CreateProcedureScope();
        proc.variableToLocation.Should().BeEmpty();
        proc.TryGetOwnVariableLocation("outer", out _).Should().BeFalse();
        proc.TryGetVariableLocation("OUTER", out var slot).Should().BeTrue();
        slot.Should().Be(12);
    }

    [Fact]
    public void ModuleLookup_DoesNotCopyBindings()
    {
        rootEnv.DefineVariable("outer", 12);
        var proc = rootEnv.CreateProcedureScope();
        rootEnv.DefineVariable("outer", 13);
        proc.TryGetVariableLocation("outer", out var slot).Should().BeTrue();
        slot.Should().Be(13);
    }

    [Fact]
    public void WritesAndImplicitNames_KeepTheirOwningScope()
    {
        context.AllocVariable(rootEnv, "outer", new Vb6Value(1));
        var proc = rootEnv.CreateProcedureScope();
        context.TryUpdateVariable(proc, "outer", new Vb6Value(2)).Should().BeTrue();
        context.TryGetVariable(rootEnv, "outer", out var value).Should().BeTrue();
        value.Should().Be(new Vb6Value(2));
        context.TryUpdateVariable(proc, "implicit", new Vb6Value(3)).Should().BeFalse();
        context.AllocVariable(proc, "implicit", new Vb6Value(3));
        rootEnv.TryGetVariableLocation("implicit", out _).Should().BeFalse();
        proc.variableToLocation.Keys.Should().Equal("implicit");
    }

    [Fact]
    public void ShadowingAndLocals_KeepBothSlotsVisible()
    {
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, "");
        var module = vb.PrimaryModule;
        context.AllocVariable(module.ModuleEnv, "field", new Vb6Value(7));
        context.AllocVariable(module.ModuleEnv, "other", new Vb6Value(8));
        var proc = module.ModuleEnv.CreateProcedureScope();
        context.AllocVariable(proc, "FIELD", new Vb6Value(9));
        proc.Parameters.Add("FIELD");
        var scope = DebugInspector.Build(proc, module, vb, "Work");
        // A standard module is listed by its name, as VB6's Locals lists it (measured, #2700).
        scope.Locals.Select(n => n.Name).Should().Equal("Module1", "field");
        scope.Locals.Single(n => n.Name == "field").Value.Should().Be("9");
        scope.Locals.Single(n => n.Name == "Module1").Expand().Select(n => n.Value).Should().Equal("7", "8");
        proc.variableToLocation.Keys.Should().Equal("field");

        // The same ByVal-style fresh slot must hide an instance field, with both visible in Locals.
        var classDef = new ModuleInfo("Box", InterpreterModuleKind.Class, module.PrePass, module.ModuleEnv);
        var instanceEnv = classDef.ModuleEnv.Clone();
        var instance = new VbObject(classDef, instanceEnv);
        var method = instanceEnv.CreateProcedureScope();
        context.AllocVariable(method, "Me", Vb6Value.NewObject(instance));
        context.AllocVariable(method, "FIELD", new Vb6Value(9));
        method.Parameters.Add("FIELD");
        var methodScope = DebugInspector.Build(method, classDef, vb, "Work");
        methodScope.Locals.Select(n => n.Name).Should().Equal("Me", "field");
        methodScope.Locals.Single(n => n.Name == "field").Value.Should().Be("9");
        methodScope.Locals.Single(n => n.Name == "Me").Expand().Select(n => n.Value).Should().Equal("7", "8");
        method.variableToLocation.Keys.Should().Equal("Me", "field");
    }
}
