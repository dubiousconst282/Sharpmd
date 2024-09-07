using DistIL;
using DistIL.AsmIO;
using DistIL.IR;
using DistIL.IR.Utils;
using DistIL.Passes;
using DistIL.Util;

using Sharpmd.Compiler;

string rootDir = FindTestSourceDir();

var resolver = new ModuleResolver();
resolver.AddTrustedSearchPaths();

var dummyAsm = resolver.Create("DummyAsm");
var comp = new Compilation(dummyAsm, new VoidLogger(), new CompilationSettings());

int numFailures = 0;
int numTests = 0;

// TODO: CommandLineParser
if (args is ["run", ..]) {
    int methodNameArgIdx = Array.IndexOf(args, "--method");
    RunPassTest(args[1], methodNameArgIdx < 0 ? null : args[methodNameArgIdx + 1]);
} else {
    foreach (string testName in Directory.GetFiles(rootDir, "*.distil", SearchOption.AllDirectories)) {
        RunPassTest(Path.GetRelativePath(rootDir, testName), null);
    }
}

Console.WriteLine($"Finished running {numTests} tests. Failures: {numFailures}");

if (numFailures > 0) {
    Environment.ExitCode = 1;
}

void RunPassTest(string testName, string? methodNameFilter)
{
    testName = testName.Replace('\\','/');

    Console.WriteLine($"Running test '{testName}'");
    string dir = Path.GetFileName(Path.GetDirectoryName(testName))!;

    IMethodPass pass = dir switch {
        "Extrinsifier" => new ExtrinsifierPass(comp),
        "AggressiveSROA" => new AggressiveSROA(),
        "VectorLowering" => new VectorLoweringPass(comp),
    };

    CheckDisasm(testName, (body, sw) => {
        if (methodNameFilter != null && body.Definition.Name != methodNameFilter) return;

        pass.Run(new MethodTransformContext(comp, body));

        IRPrinter.ExportPlain(body, sw);
        sw.Write("\n\n");
    });
}

void CheckDisasm(string testName, Action<MethodBody, StringWriter> processAndDisasm)
{
    var selfType = dummyAsm.CreateType("RegressionTests", $"_Test_{testName.Replace('/', '_').Replace('.', '_')}");
    string source = File.ReadAllText(Path.Combine(rootDir, testName));

    string fixedSource = $"import RegressionTests from DummyAsm\nimport Self = {selfType.Name}\n\n{source}";
    var ctx = IRParser.Populate(fixedSource, resolver);

    if (ctx.HasErrors) {
        PrintFailure(testName, string.Join("\n", ctx.Errors.Select(e => e.GetDetailedMessage())));
        numTests++;
        return;
    }

    var sw = new StringWriter();

    foreach (var body in ctx.DeclaredMethods) {
        processAndDisasm.Invoke(body, sw);
        numTests++;
    }

    var result = FileChecker.Check(source, sw.ToString(), StringComparison.Ordinal);
    
    if (!result.IsSuccess || args.Contains("--dump-all")) {
        File.WriteAllText(Path.Combine(rootDir, testName + ".actual"), sw.ToString());
    }

    if (!result.IsSuccess) {
        foreach (var fail in result.Failures) {
            PrintFailure(testName, fail.Message);
        }
    }
}

void PrintFailure(string testName, string message) {
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"FAIL: '{testName}' -- {message}");
    Console.ResetColor();
    numFailures++;
}

static string FindTestSourceDir() {
    string? path = AppContext.BaseDirectory;
    
    while (!string.IsNullOrEmpty(path)) {
        if (Path.Exists(path + "/Sharpmd.CompilerTests.csproj")) {
            return path + "/Cases/";
        }
        path = Path.GetDirectoryName(path);
    }
    throw new InvalidOperationException("Could not find test source directory");
}