namespace Sharpmd.CompilerTests;

using DistIL;
using DistIL.AsmIO;
using DistIL.CodeGen.Cil;
using DistIL.IR;
using DistIL.IR.Utils;
using DistIL.Passes;
using DistIL.Util;

using Sharpmd.Compiler;

[Collection("ModuleResolver")]
public class PassRegressionTests
{
    readonly ModuleDef _testAsm;
    readonly Compilation _comp;

    public PassRegressionTests(ModuleResolverFixture mrf)
    {
        _testAsm = mrf.Resolver.Create("TestAsm");

        _comp = new Compilation(_testAsm, new VoidLogger(), new CompilationSettings());
    }

    // TODO: these should ideally be theories for each decl (+ fixture for parsed sources) to make debugging easier

    [Fact]
    public void Test_VectorWiden()
    {
        CheckDisasm("VectorWiden.ethil", (body, sw) => {
            var newBody = new VectorWideningPass(_comp, 4).ProcessCallGraph(body);

            IRPrinter.ExportPlain(newBody, sw);
            sw.Write("\n\n");
        });
    }

    private void CheckIR(string filename, IMethodPass pass)
    {
        CheckDisasm(filename, (body, sw) => {
            pass.Run(new MethodTransformContext(_comp, body));

            IRPrinter.ExportPlain(body, sw);
            sw.Write("\n\n");
        });
    }
    
    private void CheckCIL(string filename)
    {
        CheckDisasm(filename, (body, sw) => {
            var ilasm = ILGenerator.GenerateCode(body);

            sw.WriteLine($"// {body.Definition}");

            foreach (var inst in ilasm.Instructions) {
                sw.WriteLine(inst.ToString());
            }
            sw.WriteLine();
        });
    }
    
    private void CheckDisasm(string filename, Action<MethodBody, StringWriter> processAndDisasm)
    {
        var selfType = _testAsm.CreateType("RegressionTests", $"_Test_{Path.GetFileNameWithoutExtension(filename)}");
        string source = File.ReadAllText("Cases/" + filename);

        string fixedSource = $"import RegressionTests from TestAsm\nimport Self = {selfType.Name}\n\n{source}";
        var ctx = IRParser.Populate(fixedSource, _comp.Resolver);
        ctx.ThrowIfError();

        var decls = ctx.DeclaredMethods.ToDictionary(e => e.Definition.Name);

        var sw = new StringWriter();

        foreach (var (name, body) in decls) {
            processAndDisasm.Invoke(body, sw);
        }

        var result = FileChecker.Check(source, sw.ToString(), StringComparison.Ordinal);
        if (!result.IsSuccess) {
            Directory.CreateDirectory("regress_fail");
            File.WriteAllText($"regress_fail/{filename.Replace('/', '_')}.txt", sw.ToString());
            
            Assert.True(result.IsSuccess, result.Failures[0].Message);
        }
    }

    // TODO: Fix importer cases
}
