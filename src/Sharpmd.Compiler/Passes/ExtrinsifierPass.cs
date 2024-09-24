namespace Sharpmd.Compiler;

using DistIL;
using DistIL.AsmIO;
using DistIL.IR;
using DistIL.IR.Utils;
using DistIL.Passes;

using Sharpmd.Redirects;

/// <summary> Scalarizes well known CoreLib intrinsics to amend vectorization. </summary>
public class ExtrinsifierPass : IMethodPass {
    readonly Dictionary<TypeDef, TypeDef> _typeRedirects = new();

    public ExtrinsifierPass(Compilation comp) {
        var runtimeModule = comp.Resolver.Import(typeof(SimdOps)).Module;

        foreach (var type in runtimeModule.TypeDefs) {
            var attr = type.GetCustomAttribs().Find(typeof(RedirectAttribute));
            
            if (attr != null) {
                _typeRedirects.Add((TypeDef)attr.Args[0], type);
            }
        }
    }
    
    static IMethodPass IMethodPass.Create<TSelf>(Compilation comp) => new ExtrinsifierPass(comp);

    public MethodPassResult Run(MethodTransformContext ctx) {
        var builder = new IRBuilder(ctx.Method.EntryBlock, InsertionDir.After);
        bool changed = false;

        foreach (var inst in ctx.Method.Instructions()) {
            if (inst is CallInst or NewObjInst { ResultType.IsValueType: true }) {
                builder.SetPosition(inst, InsertionDir.Before);
                
                var newValue = inst switch {
                    CallInst call => ProcessCall(builder, call),
                    NewObjInst alloc => ProcessAlloc(builder, alloc),
                };

                if (newValue != null) {
                    inst.ReplaceWith(newValue);
                    changed = true;
                }
            }
        }

        return changed ? MethodInvalidations.DataFlow : MethodInvalidations.None;
    }

    /// <summary> Attempts to extrinsify the given call.</summary>
    /// <param name="builder"> Builder where new instructions are to be created. </param>
    /// <returns> The new value that replaces <paramref name="call"/>. </returns>
    public Value? ProcessCall(IRBuilder builder, CallInst call) {
        if (call.Method is not MethodDefOrSpec method) return null;
        var declType = method.DeclaringType;
        
        if (_typeRedirects.TryGetValue(declType.Definition, out var redirType)) {
            var redirMethod = redirType.FindMethod(
                method.Name, 
                new MethodSig(method.ReturnSig, method.ParamSig, false, method.GenericParams.Count),
                throwIfNotFound: false);
            
            if (redirMethod != null) {
                return builder.CreateCall(redirMethod, call.Args.ToArray());
            }
        }

        if (!declType.IsCorelibType()) return null;

        if (declType.Namespace == "System.Runtime.CompilerServices" && declType.Name == "Unsafe") {
            return Process_UnsafeIntrinsic(builder, method, call.Args);
        }
        if (declType.Namespace == "System.Numerics") {
            if (declType.Name.StartsWith("Vector")) {
                return Process_SNVectorOp(builder, method, call.Args);
            }
        }
        return null;
    }
    
    private Value? ProcessAlloc(IRBuilder builder, NewObjInst alloc) {
        if (alloc.Constructor is not MethodDefOrSpec method) return null;
        var declType = method.DeclaringType;
        
        if (!declType.IsCorelibType()) return null;

        if (declType.Namespace == "System.Numerics") {
            if (declType.Name.StartsWith("Vector")) {
                return Create_SNVector(builder, declType.Definition, alloc.Args);
            }
        }
        return null;
    }

    private static Value? Process_UnsafeIntrinsic(IRBuilder builder, MethodDefOrSpec method, ReadOnlySpan<Value> args) {
        #pragma warning disable format
        return method.Name switch {
            "Read"              => builder.CreateLoad(args[0], method.GenericParams[0]),
            "ReadUnaligned"     => builder.CreateLoad(args[0], method.GenericParams[0], PointerFlags.Unaligned),
            "Write"             => builder.CreateStore(args[0], args[1], method.GenericParams[0]),
            "WriteUnaligned"    => builder.CreateStore(args[0], args[1], method.GenericParams[0], PointerFlags.Unaligned),
            "Add"               => builder.CreatePtrOffset(args[0], args[1], method.GenericParams[0]),
            "AddByteOffset"     => builder.CreatePtrOffset(args[0], args[1], PrimType.Byte),
            "IsAddressLessThan" => builder.CreateUlt(args[0], args[1]),
            "IsAddressGreaterThan" => builder.CreateUgt(args[0], args[1]),
            "AreSame"           => builder.CreateEq(args[0], args[1]),
            "Unbox"             => builder.Emit(new CilIntrinsic.UnboxRef(method.GenericParams[0], args[0])),
            "SizeOf"            => builder.Emit(new CilIntrinsic.SizeOf(method.GenericParams[0])),
            _ => null
        };
        #pragma warning restore format
    }

    // Undo the mildly idiotic horizontal SIMD opts for System.Numeric.Vector operators
    private static Value? Process_SNVectorOp(IRBuilder builder, MethodDefOrSpec method, ReadOnlySpan<Value> args) {
        var binOp = method.Name switch {
            "op_Addition" or "Add" => BinaryOp.FAdd,
            "op_Subtraction" or "Subtract" => BinaryOp.FSub,
            "op_Multiply" or "Multiply" => BinaryOp.FMul,
            "op_Division" or "Divide" => BinaryOp.FDiv,
            _ => default(BinaryOp?)
        };
        if (binOp != null) {
            var result = new Undef(method.ReturnType) as Value;

            foreach (var field in method.ReturnType.Fields) {
                var left = GetComp(args[0], field);
                var right = GetComp(args[1], field);
                result = builder.CreateFieldInsert(field, result, builder.CreateBin(binOp.Value, left, right));
            }
            return result;
        }
        return null;

        Value GetComp(Value value, FieldDesc field) 
            => IsSNVector(value.ResultType) ? builder.CreateFieldLoad(field, value) : value;
    }
    
    private Value? Create_SNVector(IRBuilder builder, TypeDef type, ReadOnlySpan<Value> args) {
        // Check if we know how to scalarize this constructor:
        //   new Vector(float)
        //   new Vector(float, float...)
        //   new Vector(SubVector, float...)
        for (int i = 0; i < args.Length; i++) {
            var vtype = args[i].ResultType;
            if (!vtype.IsFloat() && !IsSNVector(vtype)) {
                return null;
            }
        }
        
        var result = new Undef(type) as Value;
        int numComp = type.Fields.Count;
        
        for (int i = 0, j = 0; j < numComp; i++) {
            var arg = args[Math.Min(i, args.Length - 1)];

            if (arg.ResultType.IsFloat()) {
                result = builder.CreateFieldInsert(type.Fields[j++], result, arg);
            } else {
                foreach (var field in arg.ResultType.Fields) {
                    var subcomp = builder.CreateFieldLoad(field, arg);
                    result = builder.CreateFieldInsert(type.Fields[j++], result, subcomp);
                }
            }
        }

        return result;

    }
    private static bool IsSNVector(TypeDesc type) {
        return type.IsCorelibType() && type.Namespace == "System.Numerics" && type.Name.StartsWith("Vector");
    }
}