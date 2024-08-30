namespace Sharpmd.Compiler;

using DistIL.Analysis;
using DistIL.AsmIO;
using DistIL.IR;
using DistIL.IR.Utils;
using DistIL.Passes;
using DistIL.Util;

// Mask generation and control flow flattening
internal class PredicationPass : IMethodPass {
    public MethodPassResult Run(MethodTransformContext ctx) {
        // TODO: Simplify loops
        var loopAnalysis = ctx.GetAnalysis<LoopAnalysis>();
        
        // TODO: Mask gen

        // TODO: predication

        return MethodInvalidations.None;
    }

    // Generate masks and replace phis with selects
    private void GenerateMasks(MethodBody method) {
        var blockMasks = new Dictionary<BasicBlock, Value>();
        var builder = new IRBuilder(method.EntryBlock);

        blockMasks[method.EntryBlock] = ConstInt.Create(PrimType.Bool, 1);

        Value GetBlockMask(BasicBlock block) {
            ref var mask = ref blockMasks.GetOrAddRef(block);

            if (mask == null) {
                builder.SetPosition(block.FirstNonHeader, InsertionDir.Before);

                // OR masks from respective predecessor blocks
                foreach (var pred in block.Preds) {
                    var predMask = GetBlockMask(pred);

                    if (mask == null) {
                        mask = predMask;
                    } else {
                        mask = builder.CreateOr(mask, predMask);
                    }
                }
            }
            return mask;
        }


    }

    private void Flatten() {

    }
}