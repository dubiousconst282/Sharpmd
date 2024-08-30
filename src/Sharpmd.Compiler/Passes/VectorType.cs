namespace Sharpmd.Compiler;

using DistIL.AsmIO;
using DistIL.IR;

public class VectorType : CompoundType {
    public override TypeKind Kind => TypeKind.Struct;
    public override StackType StackType => StackType.Struct;

    protected override string Postfix => "[x" + Width + "]";
    public int Width { get; }
    
    public VectorType(TypeDesc elemType, int width) : base(elemType) {
        Width = width;
    }

    protected override CompoundType New(TypeDesc elemType) {
        return new VectorType(elemType, Width);
    }
    
    public override void Print(PrintContext ctx, bool includeNs = false) {
        ElemType.Print(ctx, includeNs);
        ctx.Print(Postfix, PrintToner.Number);
    }
}
