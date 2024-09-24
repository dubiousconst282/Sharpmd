namespace Sharpmd.Compiler;

using System.Diagnostics;

using DistIL.AsmIO;
using DistIL.IR;

public class VectorIntrinsic : IntrinsicInst {
    public override string Namespace => "SIMD";
    public override string Name => GetType().Name;

    public VectorIntrinsic(TypeDesc resultType, Value[] args) : base(resultType, args) {
    }

    public class Splat(VectorType type, Value scalar) : VectorIntrinsic(type, [scalar]) { }
    public class Create(VectorType type, Value[] lanes) : VectorIntrinsic(type, lanes) { }

    // TODO: consider reusing existing scalar instructions
    // Note: mixed vector/scalar operands are currently valid and handled by lowering.
    public class Binary(BinaryOp op, VectorType type, Value left, Value right) : VectorIntrinsic(type, [left, right]) {
        public BinaryOp Op { get; set; } = op;
        public Value Left => Operands[0];
        public Value Right => Operands[1];

        public override string Name => Op.ToString();
    }
    public class Compare(CompareOp op, VectorType type, Value left, Value right) : VectorIntrinsic(type, [left, right]) {
        public CompareOp Op { get; set; } = op;
        public Value Left => Operands[0];
        public Value Right => Operands[1];
        
        public override string Name => "Cmp_" + Op.ToString();
    }
    public class Math(VectorMathOp op, VectorType type, Value[] args) : VectorIntrinsic(type, args) {
        public VectorMathOp Op { get; set; } = op;
        
        public override string Name => Op.ToString();
    }
    public class Convert(ConvertOp op, TypeKind destType, Value value) : VectorIntrinsic(GetResultType(op, (VectorType)value.ResultType, destType), [value]) {
        public ConvertOp Op { get; set; } = op;
        
        public override string Name => "Conv_" + Op.ToString();

        private static VectorType GetResultType(ConvertOp op, VectorType srcType, TypeKind destType) {
            int width = srcType.Width;

            if (op == ConvertOp.BitCast) {
                int srcLaneBits = srcType.ElemType.Kind.BitSize();
                int dstLaneBits = destType.BitSize();
                Debug.Assert(srcLaneBits % dstLaneBits == 0);

                width = width * srcLaneBits / dstLaneBits;
            }
            return new VectorType(PrimType.GetFromKind(destType), width);
        }
    }

    public class OffsetUniformPtr(VectorType type, Value basePtr, Value offset) : VectorIntrinsic(type, [basePtr, offset]) {
        public TypeDesc ElemType => ((PointerType)type.ElemType).ElemType;

    }
    public class MemGather(VectorType type, Value address, Value? mask = null) : VectorIntrinsic(type, mask == null ? [address] : [address, mask]) {
        public Value Address => Operands[0];
        public Value? Mask => Operands.Length >= 2 ? Operands[1] : null;
        public TypeDesc ElemType => ((PointerType)type.ElemType).ElemType;
    }
    public class MemScatter(VectorType type, Value address, Value value, Value? mask = null) : VectorIntrinsic(PrimType.Void, mask == null ? [address, value] : [address, value, mask]) {
        public Value Address => Operands[0];
        public Value DestValue => Operands[1];
        public Value? Mask => Operands.Length >= 3 ? Operands[2] : null;
        public TypeDesc ElemType => ((PointerType)type.ElemType).ElemType;
    }

    public class GetLane(Value vector, int laneIdx) : VectorIntrinsic(((VectorType)vector.ResultType).ElemType, [vector, ConstInt.CreateI(laneIdx)]) {
        public int LaneIdx => (int)((ConstInt)Args[1]).Value;
    }
    public class GetMask(Value vector) : VectorIntrinsic(PrimType.UInt64, [vector]) { }
}

public enum VectorMathOp {
    Abs, Min, Max,
    Floor, Ceil, Round,
    Fma,
    Sqrt,
}
public enum ConvertOp {
    BitCast,
    ZeroExt, SignExt,
    Trunc,
    Saturate,
    I2F, F2I,
    FExt, FTrunc,
}