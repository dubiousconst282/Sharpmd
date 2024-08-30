namespace Sharpmd.Compiler;

using DistIL.Analysis;
using DistIL.AsmIO;
using DistIL.IR;
using DistIL.IR.Utils;

public class UniformValueAnalysis : IMethodAnalysis, IPrintDecorator {
    readonly MethodBody _method;
    readonly GlobalFunctionEffects _funcInfo;
    readonly Dictionary<Value, ValueUniformityInfo> _cache = new();

    public UniformValueAnalysis(MethodBody method, GlobalFunctionEffects funcInfo) {
        _method = method;
        _funcInfo = funcInfo;

        if (method.Definition.IsInstance) {
            _cache.Add(method.Args[0], new() { Kind = ValueUniformityKind.Uniform });
        }
    }

    static IMethodAnalysis IMethodAnalysis.Create(IMethodAnalysisManager mgr)
        => new UniformValueAnalysis(mgr.Method, mgr.Compilation.GetAnalysis<GlobalFunctionEffects>());

    public bool IsUniform(Value val) => GetInfo(val).Kind == ValueUniformityKind.Uniform;

    public ValueUniformityInfo GetInfo(Value val) {
        if (val is Const or Undef) {
            return ValueUniformityKind.Uniform;
        }
        if (!_cache.TryGetValue(val, out var info)) {
            if (val is Instruction inst) {
                info = ComputeInfo(inst);
            } else if (val is Argument arg) {
                info = IsUniform(_method.Definition, arg.Param) ? ValueUniformityKind.Uniform : ValueUniformityKind.Varying;
            }
            _cache[val] = info;
        }
        return info;
    }

    public static bool IsUniform(MethodDef method, ParamDef par) {
        if (method.IsInstance && par == method.Params[0]) {
            return true;
        }
        var uniformAttr = par.GetCustomAttribs().Find(typeof(UniformAttribute));
        return uniformAttr != null;
    }

    private ValueUniformityKind ComputeInfo(Instruction inst) {
        if (inst is CallInst call) {
            var funcEffects = _funcInfo.GetEffects(call.Method);
            return funcEffects.MayOnlyThrowOrReadMem ? CheckAllValuesUniform(call.Args) : ValueUniformityKind.Varying;
        }
        else if (inst is PtrOffsetInst lea && IsUniform(lea.BasePtr)) {
            return IsUniform(lea.Index) ? ValueUniformityKind.Uniform : ValueUniformityKind.VaryingOffset;
        }
        else if (inst is ArrayAddrInst arrd && IsUniform(arrd.Array)) {
            return IsUniform(arrd.Index) ? ValueUniformityKind.Uniform : ValueUniformityKind.VaryingOffset;
        }
        else if (inst is FieldAddrInst flda && (flda.IsStatic || IsUniform(flda.Obj))) {
            return ValueUniformityKind.Uniform;
        }
        else if (!inst.HasSideEffects || inst is LoadInst) {
            return CheckAllValuesUniform(inst.Operands);
        }

        return ValueUniformityKind.Varying;
    }

    private ValueUniformityKind CheckAllValuesUniform(ReadOnlySpan<Value> values) {
        foreach (var oper in values) {
            if (!IsUniform(oper)) {
                return ValueUniformityKind.Varying;
            }
        }
        return ValueUniformityKind.Uniform;
    }
    
    void IPrintDecorator.DecorateInst(PrintContext ctx, Instruction inst) {
        if (inst.IsBranch) return;

        var info = GetInfo(inst);
        ctx.Print($"  {info.Kind.ToString().ToLower()}", PrintToner.Comment);
    }
}

public readonly struct ValueUniformityInfo {
    /// <summary> Indicates if the value is the same across all lanes. </summary>
    public ValueUniformityKind Kind { get; init; } = ValueUniformityKind.Unknown;

    /// <summary> Indicates the lane increment step if it contains sequential values, otherwise, 0. </summary>
    public int Stride { get; init; }
    public bool IsSequential => Kind == ValueUniformityKind.Varying && Stride != 0;
    
    public ValueUniformityInfo() {
    }

    public static implicit operator ValueUniformityInfo(ValueUniformityKind kind) => new() { Kind = kind }; 
}
public enum ValueUniformityKind {
    /// <summary> Functionally the same as <see cref="Varying"/>. </summary>
    Unknown,
    Varying,
    /// <summary> Value is the same across all lanes. </summary>
    Uniform,
    /// <summary> Uniform pointer offset by varying value, resulting of a LEA/GEP instruction chain. </summary>
    VaryingOffset,
}