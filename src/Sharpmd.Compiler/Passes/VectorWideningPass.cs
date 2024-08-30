namespace Sharpmd.Compiler;

using System.Collections.Immutable;
using System.Diagnostics;

using DistIL;
using DistIL.Analysis;
using DistIL.AsmIO;
using DistIL.IR;
using DistIL.IR.Utils;
using DistIL.Passes;
using DistIL.Util;

using MethodAttrs = System.Reflection.MethodAttributes;

internal class VectorWideningPass {
    internal Compilation _comp;
    internal TypeDef _containerType;
    internal int _targetWidth;

    private Dictionary<TypeDesc, VectorType> _vectorTypeCache = new();
    public Dictionary<MethodBody, MethodBody> VectorizedMethods = new();
    private ArrayStack<MethodBody> _worklist = new();
    
    public VectorWideningPass(Compilation comp, int targetWidth) {
        _comp = comp;
        _containerType = comp.Module.CreateType(null, "<>_SpmdGen_" + comp.Module.TypeDefs.Count);
        _targetWidth = targetWidth;
    }

    public MethodBody ProcessCallGraph(MethodBody entryPoint) {
        Debug.Assert(!VectorizedMethods.ContainsKey(entryPoint));

        var vectorMethod = GetVectorizedMethod(entryPoint) ?? throw new InvalidOperationException();
        
        while (_worklist.TryPop(out var srcMethod)) {
            var destMethod = GetVectorizedMethod(srcMethod)!;
            var cloner = new VectorizingIRCloner(this, srcMethod, destMethod);

            foreach (var block in srcMethod) {
                cloner.AddMapping(block, destMethod.CreateBlock());
            }
            for (int i = 0; i < srcMethod.Args.Length; i++) {
                cloner.AddMapping(srcMethod.Args[i], destMethod.Args[i]);
            }
            cloner.Run(srcMethod.EntryBlock);
        }
        return vectorMethod;
    }

    internal MethodBody? GetVectorizedMethod(MethodBody srcMethod) {
        var srcDef = srcMethod.Definition;

        if (srcDef.Module != _comp.Module) {
            return null;
        }
        if (VectorizedMethods.TryGetValue(srcMethod, out var vectorBody)) {
            return vectorBody;
        }

        var newParams = ImmutableArray.CreateBuilder<ParamDef>();

        foreach (var arg in srcMethod.Args) {
            var type = arg.Param.Sig;

            if (!UniformValueAnalysis.IsUniform(srcDef, arg.Param)) {
                type = GetVectorType(arg.ResultType);
            }
            newParams.Add(new ParamDef(type, arg.Param.Name, arg.Param.Attribs));
        }
        var retType = srcDef.ReturnType;
    
        if (retType != PrimType.Void && !UniformValueAnalysis.IsUniform(srcDef, srcDef.ReturnParam)) {
            retType = GetVectorType(retType);
        }

        var vectorDef = _containerType.CreateMethod(srcDef.Name, retType, newParams.ToImmutable(), MethodAttrs.Public | MethodAttrs.Static | MethodAttrs.HideBySig);

        vectorDef.Body = new MethodBody(vectorDef);
        VectorizedMethods[srcMethod] = vectorDef.Body;

        _worklist.Push(srcMethod);

        return vectorDef.Body;
    }

    internal VectorType GetVectorType(TypeDesc laneType) {
        return _vectorTypeCache.GetOrAddRef(laneType) ??= new(laneType, _targetWidth);
    }
}

class VectorizingIRCloner : IRCloner {
    readonly VectorWideningPass _pass;
    readonly UniformValueAnalysis _uniformAnalysis;
    readonly IRBuilder _builder;

    public VectorizingIRCloner(VectorWideningPass pass, MethodBody srcMethod, MethodBody destMethod, GenericContext genericContext = default) : base(destMethod, genericContext) {
        _pass = pass;
        _uniformAnalysis = new UniformValueAnalysis(srcMethod, pass._comp.GetAnalysis<GlobalFunctionEffects>());
        _builder = new IRBuilder(default(BasicBlock)!, InsertionDir.After);
    }

    protected override Value CreateClone(Instruction inst) {
        _builder.SetPosition(GetMapping(inst.Block), InsertionDir.After);

        if (inst.IsBranch) {
            var clone = base.CreateClone(inst);
            
            if (clone is BranchInst br && br.Cond?.ResultType is VectorType) {
                br.Cond = EmitMoveMask(br.Cond);
            }
            return clone;
        }

        if (_uniformAnalysis.IsUniform(inst)) {
            return base.CreateClone(inst);
        }

        if (inst is PhiInst phi) {
            var clonedPhi = (PhiInst)base.CreateClone(phi);
            clonedPhi.SetResultType(_pass.GetVectorType(phi.ResultType));
            return clonedPhi;
        }

        if (inst is BinaryInst bin) {
            return _builder.Emit(new VectorIntrinsic.Binary(bin.Op, _pass.GetVectorType(bin.ResultType), Remap(bin.Left), Remap(bin.Right)));
        }
        if (inst is CompareInst cmp) {
            Debug.Assert(cmp.Left.ResultType.Kind.GetStorageType() == cmp.Right.ResultType.Kind.GetStorageType());
            return _builder.Emit(new VectorIntrinsic.Compare(cmp.Op, _pass.GetVectorType(cmp.Left.ResultType), Remap(cmp.Left), Remap(cmp.Right)));
        }
        if (inst is ArrayAddrInst arrd && _uniformAnalysis.IsUniform(arrd.Array) && arrd.ElemType.IsValueType) {
            return EmitUniformArrayAddr(arrd);
        }
        if (inst is ConvertInst conv && !conv.CheckOverflow) {
            return EmitConvert(conv.SrcUnsigned ? conv.SrcType.GetUnsigned() : conv.SrcType, conv.DestType, Remap(conv.Value));
        }
        if (inst is SelectInst csel) {
            return new SelectInst(Remap(csel.Cond), Remap(csel.IfTrue), Remap(csel.IfFalse), _pass.GetVectorType(csel.ResultType));
        }
        //if (inst is LoadInst load && EmitGather((TypeDesc)Remap(load.ElemType), Remap(load.Address)) is var gatherInst) {
        //    return gatherInst;
        //}

        // TODO: group multiple instructions and generate scalarization loop
        return Scalarize(inst);
    }

    private Value Scalarize(Instruction inst) {
        var laneType = inst.ResultType;
        var vectorType = _pass.GetVectorType(laneType);
        var lanes = new Value[vectorType.Width];

        for (int i = 0; i < vectorType.Width; i++) {
            lanes[i] = base.CreateClone(inst);

            if (lanes[i] is Instruction clonedInst) {
                for (int j = 0; j < clonedInst.Operands.Length; j++) {
                    var oper = clonedInst.Operands[j];

                    if (oper.ResultType is VectorType) {
                        clonedInst.ReplaceOperand(j, EmitGetLane(oper, i));
                    }
                }
                _builder.Emit(clonedInst);
            }
        }
        if (inst.HasResult) {
            return _builder.Emit(new VectorIntrinsic.Create(vectorType, lanes));
        }
        return ConstNull.Create(); // dummy
    }

    private Value EmitUniformArrayAddr(ArrayAddrInst inst) {
        var array = Remap(inst.Array);
        var index = Remap(inst.Index);
        
        if (!inst.InBounds) {
            EmitBoundsCheck(index, EmitSplat(PrimType.UInt32, _builder.CreateArrayLen(array)));
        }
        var basePtr = LoopStrengthReduction.CreateGetDataPtrRange(_builder, array, getCount: false).BasePtr;
        return _builder.Emit(new VectorIntrinsic.OffsetUniformPtr(_pass.GetVectorType(inst.ResultType), basePtr, index));
    }

    private void EmitBoundsCheck(Value index, Value length) {
        var mask = _builder.Emit(new VectorIntrinsic.Compare(CompareOp.Ult, _pass.GetVectorType(PrimType.UInt32), index, length));
        _builder.CreateCall(_pass._comp.Resolver.Import(typeof(SimdOps)).FindMethod("CheckThrowInBoundsMask"), EmitMoveMask(mask));
    }
    
    private Value EmitConvert(TypeKind srcType, TypeKind dstType, Value value) {
        var op = ConvertOp.BitCast;
        
        if (srcType.IsFloat() && dstType.IsFloat()) {
            op = srcType.BitSize() > dstType.BitSize() ? ConvertOp.FExt : ConvertOp.FTrunc;
        }
        else if ((srcType.IsFloat() && dstType.IsInt()) || (srcType.IsInt() && dstType.IsFloat())) {
            op = srcType.IsFloat() ? ConvertOp.F2I : ConvertOp.I2F;
        }
        else if (dstType.BitSize() > srcType.BitSize()) {
            op = dstType.IsUnsigned() ? ConvertOp.ZeroExt : ConvertOp.SignExt;
        }
        else if (dstType.BitSize() < srcType.BitSize()) {
            op = ConvertOp.Trunc;
        }
        return _builder.Emit(new VectorIntrinsic.Convert(op, dstType, value));
    }

    private Value EmitSplat(TypeDesc laneType, Value value) {
        return _builder.Emit(new VectorIntrinsic.Splat(_pass.GetVectorType(laneType), value));
    }

    private Value EmitGetLane(Value value, int laneIdx) {
        if (value is VectorIntrinsic.Splat splat) {
            return splat.Args[0];
        }
        if (value is VectorIntrinsic.Create create) {
            return create.Args[laneIdx];
        }
        if (value is VectorIntrinsic.OffsetUniformPtr lea) {
            return _builder.CreatePtrOffset(lea.Args[0], EmitGetLane(lea.Args[1], laneIdx), lea.ElemType);
        }
        return _builder.Emit(new VectorIntrinsic.GetLane(value, ConstInt.CreateI(laneIdx)));
    }

    private Value EmitMoveMask(Value vector) {
        return _builder.Emit(new VectorIntrinsic.GetMask(vector));
    }
}