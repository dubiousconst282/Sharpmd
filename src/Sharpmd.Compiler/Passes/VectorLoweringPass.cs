namespace Sharpmd.Compiler;

using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

using DistIL;
using DistIL.AsmIO;
using DistIL.IR;
using DistIL.IR.Utils;
using DistIL.Util;

using TypeAttrs = System.Reflection.TypeAttributes;
using FieldAttrs = System.Reflection.FieldAttributes;
using DistIL.Passes;

public class VectorLoweringPass : IMethodPass {
    readonly Compilation _comp;

    readonly Dictionary<VectorType, VectorPack> _vectorPackCache = new();
    readonly Dictionary<Value, TypeDesc> _sourceTypes = new();
    
    readonly TypeDef t_SimdOps;
    
    readonly IRBuilder _builder = new();

    public VectorLoweringPass(Compilation comp) {
        _comp = comp;

        t_SimdOps = (TypeDef)comp.Resolver.Import(typeof(SimdOps));
    }
    

    public MethodPassResult Run(MethodTransformContext ctx) {
        Process(ctx.Method);
        return MethodInvalidations.Everything;
    }

    public void Process(MethodBody method)
    {
        foreach (var inst in method.Instructions()) {
            if (inst is VectorIntrinsic vinst) {
                _builder.SetPosition(inst, InsertionDir.After);

                var loweredPack = Lower(vinst);

                if (loweredPack != null) {
                    _sourceTypes[loweredPack] = inst.ResultType;
                    inst.ReplaceUses(loweredPack);
                }
                inst.Remove();
            }
            else if (inst.ResultType is VectorType vtype) {
                if (inst is PhiInst phi) {
                    _sourceTypes[inst] = vtype;
                    phi.SetResultType(GetRealType(vtype));
                }
                else if (inst is SelectInst) {
                    _builder.SetPosition(inst, InsertionDir.After);
                    
                    var loweredPack = EmitIntrinsic(vtype, "ConditionalSelect", inst.Operands.ToArray());
                    _sourceTypes[loweredPack] = vtype;
                    inst.ReplaceWith(loweredPack);
                }
                else {
                    throw new NotSupportedException();
                }
            }
        }

        // Fix up types in method def
        foreach (var arg in method.Args) {
            if (arg.ResultType is VectorType type) {
                arg.SetResultType(GetRealType(type));
            }
        }
        if (method.ReturnType is VectorType retType) {
            method.Definition.ReturnParam.Sig = GetRealType(retType);
        }

        _sourceTypes.Clear();
    }

    protected Value? Lower(VectorIntrinsic inst) {
        switch (inst) {
            case VectorIntrinsic.Splat:
                return EmitSplat((VectorType)inst.ResultType, inst.Args[0]);
                
            case VectorIntrinsic.Create: {
                // JIT knows how to collapse live ranges for Vector.Create() calls, so we don't need to worry about it.
                var type = (VectorType)inst.ResultType;
                return EmitIntrinsic(type, "Create", inst.Operands.ToArray(),
                                     m => m.Params.Length == inst.Operands.Length && m.Params[0].Type == type.ElemType);
            }
            
            case VectorIntrinsic.Binary bin:
                return EmitBinary(bin.Op, bin.Left, bin.Right, (VectorType)bin.ResultType);

            case VectorIntrinsic.Compare cmp:
                return EmitCompare(cmp.Op, cmp.Left, cmp.Right, (VectorType)cmp.ResultType);

            case VectorIntrinsic.GetMask:
                return EmitMoveMask(inst.Args[0]);

            case VectorIntrinsic.GetLane: {
                var type = GetSourceVectorType(inst.Args[0]);
                Debug.Assert(GetVectorPack(type).VecTypes!.Length == 1); // TODO
                return EmitIntrinsic(type, "GetElement", inst.Operands.ToArray());
            }

            case VectorIntrinsic.Convert conv:
                return EmitConvert(conv.Op, conv.Args[0], (VectorType)conv.ResultType);

            default:
                throw new NotImplementedException();
        }
    }

    private Value EmitBinary(BinaryOp op, Value a, Value b, VectorType type) {
        string intrinsicName = op switch {
            BinaryOp.Add or BinaryOp.FAdd => "Add",
            BinaryOp.Sub or BinaryOp.FSub => "Subtract",
            BinaryOp.Mul or BinaryOp.FMul => "Multiply",
            BinaryOp.SDiv or BinaryOp.UDiv or BinaryOp.FDiv => "Divide",
            BinaryOp.And => "BitwiseAnd",
            BinaryOp.Or => "BitwiseOr",
            BinaryOp.Xor => "Xor",
            // BinaryOp.Shl => "ShiftLeft",
            // BinaryOp.Shra => "ShiftRightArithmetic",
            // BinaryOp.Shrl => "ShiftRightLogical",
        };

        TypeKind laneType = type.ElemType.Kind;

        if (op is BinaryOp.SDiv or BinaryOp.SRem) {
            laneType = laneType.GetSigned();
        }
        else if (op is BinaryOp.UDiv or BinaryOp.URem) {
            laneType = laneType.GetUnsigned();
        }
        else if (op is >= BinaryOp.FAdd and <= BinaryOp.FRem) {
            Debug.Assert(laneType.IsFloat());
        }
        
        if (type.ElemType.Kind != laneType) {
            type = new VectorType(PrimType.GetFromKind(laneType), type.Width);
        }
        a = CoerceOperand(type, a);
        b = CoerceOperand(type, b);
        return EmitIntrinsic(type, intrinsicName, [a, b]);
    }

    private Value EmitCompare(CompareOp op, Value a, Value b, VectorType type) {
        TypeKind laneType = type.ElemType.Kind;

        if (op.IsFloat()) {
            Debug.Assert(laneType.IsFloat());
        }
        else if (op.IsUnsigned()) {
            Debug.Assert(laneType.IsInt());
            laneType = laneType.GetUnsigned();
        }
        else if (op.IsSigned()) {
            Debug.Assert(laneType.IsInt());
            laneType = laneType.GetSigned();
        }

        if (type.ElemType.Kind != laneType) {
            type = new VectorType(PrimType.GetFromKind(laneType), type.Width);
        }
        a = CoerceOperand(type, a);
        b = CoerceOperand(type, b);

        string intrinsicName = op switch {
            CompareOp.Eq or CompareOp.Ne or CompareOp.FOeq or CompareOp.FUne => "Equals",
            CompareOp.Slt or CompareOp.Ult or CompareOp.FOlt or CompareOp.FUlt => "LessThan",
            CompareOp.Sgt or CompareOp.Ugt or CompareOp.FOgt or CompareOp.FUgt => "GreaterThan",
            CompareOp.Sle or CompareOp.Ule or CompareOp.FOle or CompareOp.FUle => "LessThanOrEqual",
            CompareOp.Sge or CompareOp.Uge or CompareOp.FOge or CompareOp.FUge => "GreaterThanOrEqual",
        };
        var result = EmitIntrinsic(type, intrinsicName, [a, b]);

        if (op is CompareOp.Ne or CompareOp.FUne or CompareOp.FUlt or CompareOp.FUgt or CompareOp.FUle or CompareOp.FUge) {
            result = EmitIntrinsic(type, "OnesComplement", [result]);
        }
        return result;
    }
    
    private Value EmitConvert(ConvertOp op, Value value, VectorType destType) {
        var srcType = GetSourceVectorType(value);
        var srcKind = srcType.ElemType.Kind;
        var destKind = destType.ElemType.Kind;

        if (op == ConvertOp.BitCast) {
            Debug.Assert(srcKind.BitSize() == destKind.BitSize());
            return EmitIntrinsic(srcType, "As" + destKind, [value]);
        }

        Debug.Assert(srcType.Width == destType.Width);

        // Narrow/widen until sizes match
        while (srcKind.BitSize() != destKind.BitSize()) {
            throw new NotImplementedException();
        }

        string intrinsicName = op switch {
            ConvertOp.I2F or ConvertOp.F2I => "ConvertTo" + destKind,
        };
        return EmitIntrinsic(destType, intrinsicName, [value]);
    }

    private Value CoerceOperand(VectorType destType, Value oper) {
        var type = _sourceTypes.GetValueOrDefault(oper, oper.ResultType);

        if (type is VectorType vtype && vtype != destType) {
            return EmitConvert(ConvertOp.BitCast, oper, destType);
        }
        else if (IsScalarType(type)) {
            return EmitSplat(destType, oper);
        }
        return oper;
    }

    private Value EmitSplat(VectorType type, Value value) {
        return EmitIntrinsic(type, "Create", [value], m => m.Params.Length == 1 && m.Params[0].Type == type.ElemType);
    }
    private Value EmitMoveMask(Value vector) {
        var mask = EmitIntrinsic(GetSourceVectorType(vector), "ExtractMostSignificantBits", [vector]);

        if (mask.ResultType != PrimType.UInt64) {
            mask = _builder.CreateConvert(mask, PrimType.UInt64);
        }
        return mask;
    }
    
    private Value EmitIntrinsic(VectorType type, string name, Value[] args, Predicate<MethodDef>? filter = null) {
        var pack = GetVectorPack(type);
        
        var method = FindIntrinsic(pack.VecTypes![0], name, filter);

        if (pack.VecTypes.Length == 1) {
            return _builder.CreateCall(method, args);
        }
        var result = new Undef(pack.WrapperType!) as Value;

        for (int i = 0; i < pack.VecTypes.Length; i++) {
            if (i > 0 && pack.VecTypes[i - 1] != pack.VecTypes[i]) {
                method = FindIntrinsic(pack.VecTypes[i], name, filter);
            }

            var packArgs = new Value[args.Length];
            for (int j = 0; j < args.Length; j++) {
                if (args[j].ResultType == pack.WrapperType) {
                    packArgs[j] = _builder.CreateFieldLoad(args[j].ResultType.Fields[i], args[j]);
                } else {
                    packArgs[j] = args[j];
                }
            }

            var vector = _builder.CreateCall(method, packArgs);
            result = _builder.CreateFieldInsert(pack.WrapperType!.Fields[i], result, vector);
        }
        return result;
    }

    private MethodDesc FindIntrinsic(TypeDesc vectorType, string name, Predicate<MethodDef>? filter) {
        var extClass = GetVectorExtClass(vectorType);

        foreach (var method in extClass.Methods) {
            if (method.Name == name && method.IsStatic && (filter == null || filter.Invoke(method))) {
                return method.IsGeneric ? method.GetSpec([vectorType.GenericParams[0]]) : method;
            }
        }
        throw new InvalidOperationException();
    }

    // Vector128`1  ->  Vector128
    private TypeDef GetVectorExtClass(TypeDesc vectorType) {
        return _comp.Resolver.CoreLib.FindType(
            "System.Runtime.Intrinsics", vectorType.Name[0..^2],
            throwIfNotFound: true)!;
    }

    
    private VectorPack GetVectorPack(VectorType type) {
        return _vectorPackCache.GetOrAddRef(type) ??= new(_comp, type);
    }
    private VectorType GetSourceVectorType(Value value) {
        return (VectorType)_sourceTypes.GetValueOrDefault(value, value.ResultType);
    }
    private TypeDesc GetRealType(VectorType type) {
        var pack = GetVectorPack(type);
        return pack.VecTypes!.Length >= 2 ? pack.WrapperType! : pack.VecTypes[0]!;
    }
    private bool IsScalarType(TypeDesc type) {
        return type.Kind != TypeKind.Struct;
    }
}

class VectorPack {
    public readonly TypeSpec[]? VecTypes;
    public readonly TypeDef? WrapperType;
    public readonly Dictionary<string, MethodDesc> FnCache = new();

    public VectorPack(Compilation comp, VectorType type) {
        var laneType = type.ElemType;

        if (laneType.IsInt() || laneType.IsFloat()) {
            int laneBits = laneType.Kind.BitSize();

            var packs = new List<TypeSpec>();

            for (int width = type.Width; width > 0; ) {
                var (genType, vecBits) = (width * laneBits) switch {
                    >= 512 => (typeof(Vector512<>), 512),    
                    >= 256 => (typeof(Vector256<>), 256),
                    >= 128 => (typeof(Vector128<>), 128),
                };
                packs.Add(comp.Resolver.Import(genType).GetSpec([laneType]));
                width -= vecBits / laneBits;
            }
            VecTypes = packs.ToArray();

            if (VecTypes.Length >= 2) {
                WrapperType = comp.GetAuxType().CreateNestedType(
                    $"Vector_{laneType.Name}_x{type.Width}", 
                    TypeAttrs.Sealed | TypeAttrs.SequentialLayout | TypeAttrs.BeforeFieldInit,
                    baseType: comp.Resolver.SysTypes.ValueType);

                for (int i = 0; i < VecTypes.Length; i++) {
                    WrapperType.CreateField("_" + i, VecTypes[i], FieldAttrs.Public);
                }
            }
        } else {
            // TODO: generate InlineArray for scalarized vectors
            WrapperType = comp.GetAuxType().CreateNestedType(
                $"Vector_{laneType.Name}_x{type.Width}", 
                TypeAttrs.Public | TypeAttrs.Sealed | TypeAttrs.SequentialLayout | TypeAttrs.BeforeFieldInit,
                baseType: comp.Resolver.SysTypes.ValueType);

            for (int i = 0; i < type.Width; i++) {
                WrapperType.CreateField("_" + i, type.ElemType, FieldAttrs.Public);
            }
        }
    }
}