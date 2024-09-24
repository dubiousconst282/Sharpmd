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

public class VectorWideningPass {
    internal Compilation _comp;
    internal TypeDef _containerType;
    internal int _targetWidth;

    private Dictionary<TypeDesc, VectorType> _vectorTypeCache = new();
    public Dictionary<MethodBody, MethodBody> VectorizedMethods = new();
    private ArrayStack<MethodBody> _worklist = new();
    
    public VectorWideningPass(Compilation comp, int targetWidth) {
        _comp = comp;
        _containerType = comp.Module.CreateType(null, $"<>_SpmdGen_{comp.Module.TypeDefs.Count}_{targetWidth}");
        _targetWidth = targetWidth;
    }

    public void AddEntryPoint(MethodBody method) {
        _worklist.Push(method);
    }
    public void Process() {
        while (!_worklist.IsEmpty) {
            ProcessMethod(_worklist.Pop());
        }
    }

    private void ProcessMethod(MethodBody srcMethod) {
        var destMethod = GetVectorizedMethod(srcMethod)!;
        var ctx = new MethodTransformContext(_comp, srcMethod);
        var cloner = new VectorizingIRCloner(this, ctx, destMethod, default);

        for (int i = 0; i < srcMethod.Args.Length; i++) {
            cloner.AddMapping(srcMethod.Args[i], destMethod.Args[i]);
        }

        cloner.CloneWide();
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

        return vectorDef.Body;
    }

    internal VectorType GetVectorType(TypeDesc laneType) {
        return _vectorTypeCache.GetOrAddRef(laneType) ??= new(laneType, _targetWidth);
    }
}

class VectorizingIRCloner : IRCloner {
    readonly VectorWideningPass _pass;
    readonly UniformValueAnalysis _uniformity;
    readonly MaskGenerator _maskGen;
    readonly IRBuilder _builder;

    Value? _currExecMask;

    public VectorizingIRCloner(VectorWideningPass pass, MethodTransformContext ctx, MethodBody destMethod, GenericContext genCtx) : base(destMethod, genCtx) {
        _pass = pass;
        _uniformity = new UniformValueAnalysis(ctx.Method, pass._comp.GetAnalysis<GlobalFunctionEffects>());
        _maskGen = new MaskGenerator(ctx.Method, destMethod, _uniformity, ctx.GetAnalysis<LoopAnalysis>());

        _builder = new IRBuilder(default(BasicBlock)!, InsertionDir.After);
    }

    public void CloneWide() {
        var srcBlocks = _maskGen.GetSortedBlocks();

        foreach (var block in srcBlocks) {
            var destBlock = _maskGen.GetFlattenedBlock(block);

            for (var inst = block.First; inst != block.Last; inst = inst.Next!) {
                _builder.SetPosition(destBlock, InsertionDir.After);
                var clonedInst = Clone(inst);

                if (clonedInst is Instruction { Block: null } newInst) {
                    destBlock.InsertLast(newInst);
                }
            }
        }
    }

    protected override Value CreateClone(Instruction inst) {
        Debug.Assert(!inst.IsBranch);

        if (_uniformity.IsUniform(inst)) {
            return base.CreateClone(inst);
        }

        if (inst is PhiInst phi) {
            if (_maskGen.IsSelectionPhi(phi)) {
                return ConvertPhiToSelect(phi);
            } else {
                Debug.Assert(_builder.Block.FirstNonHeader == null); // TODO: can csels and phis be mixed?
                return base.CreateClone(phi);
            }
        }

        if (inst is BinaryInst bin) {
            return _builder.Emit(new VectorIntrinsic.Binary(bin.Op, _pass.GetVectorType(bin.ResultType), Remap(bin.Left), Remap(bin.Right)));
        }
        if (inst is CompareInst cmp) {
            Debug.Assert(cmp.Left.ResultType.Kind.GetStorageType() == cmp.Right.ResultType.Kind.GetStorageType());
            return _builder.Emit(new VectorIntrinsic.Compare(cmp.Op, _pass.GetVectorType(cmp.Left.ResultType), Remap(cmp.Left), Remap(cmp.Right)));
        }
        if (inst is LoadInst load && EmitGather((TypeDesc)Remap(load.ElemType), Remap(load.Address)) is { } gatherInst) {
            return gatherInst;
        }
        if (inst is ArrayAddrInst arrd && _uniformity.IsUniform(arrd.Array) && arrd.ElemType.IsValueType) {
            return WidenUniformArrayAddr(arrd);
        }
        if (inst is ConvertInst conv && !conv.CheckOverflow) {
            return WidenConvert(conv.SrcUnsigned ? conv.SrcType.GetUnsigned() : conv.SrcType, conv.DestType, Remap(conv.Value));
        }
        if (inst is SelectInst csel) {
            return new SelectInst(Remap(csel.Cond), Remap(csel.IfTrue), Remap(csel.IfFalse), _pass.GetVectorType(csel.ResultType));
        }

        // TODO: group multiple instructions and generate scalarization loop?
        return Scalarize(inst);
    }

    private Value ConvertPhiToSelect(PhiInst phi) {
        var result = Remap(phi.GetValue(0));
        var type = _pass.GetVectorType((TypeDesc)Remap(phi.ResultType));

        for (int i = 1; i < phi.NumArgs; i++) {
            var (pred, val) = phi.GetArg(i);
            result = _builder.CreateSelect(_maskGen.GetBlockMask(pred), Remap(val), result, type);
        }
        return result;
    }

    private Value Scalarize(Instruction inst) {
        var laneType = inst.ResultType;
        var vectorType = _pass.GetVectorType(laneType);
        var lanes = new Value[vectorType.Width];

        for (int i = 0; i < vectorType.Width; i++) {
            lanes[i] = base.CreateClone(inst);

            // TODO: masking
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

    private Value? EmitGather(TypeDesc elemType, Value address) {
        return null;
    }

    private Value WidenUniformArrayAddr(ArrayAddrInst inst) {
        var array = Remap(inst.Array);
        var index = Remap(inst.Index);
        
        if (!inst.InBounds) {
            EmitBoundsCheck(index, _builder.CreateArrayLen(array));
        }
        var basePtr = LoopStrengthReduction.CreateGetDataPtrRange(_builder, array, getCount: false).BasePtr;
        return _builder.Emit(new VectorIntrinsic.OffsetUniformPtr(_pass.GetVectorType(inst.ResultType), basePtr, index));
    }

    private void EmitBoundsCheck(Value index, Value length) {
        var mask = _builder.Emit(new VectorIntrinsic.Compare(CompareOp.Ult, _pass.GetVectorType(PrimType.UInt32), index, length));
        var cond = _builder.Emit(new CompareInst(CompareOp.Ne, EmitMoveMask(mask), ConstInt.CreateL(0)));
        _builder.CreateCall(_pass._comp.Resolver.Import(typeof(SimdOps)).FindMethod("ConditionalThrow_IndexOutOfRange"), cond);
    }
    
    private Value WidenConvert(TypeKind srcType, TypeKind dstType, Value value) {
        var op = ConvertOp.BitCast;
        
        if (srcType.IsFloat() && dstType.IsFloat()) {
            op = dstType.BitSize() > srcType.BitSize() ? ConvertOp.FExt : ConvertOp.FTrunc;
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
        return _builder.Emit(new VectorIntrinsic.GetLane(value, laneIdx));
    }

    private Value EmitMoveMask(Value vector) {
        return _builder.Emit(new VectorIntrinsic.GetMask(vector));
    }
}