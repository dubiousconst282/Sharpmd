namespace Sharpmd.Compiler;

using System.Diagnostics;

using DistIL.Analysis;
using DistIL.AsmIO;
using DistIL.IR;
using DistIL.IR.Utils;
using DistIL.Passes;
using DistIL.Util;

// Mask generation and control flow flattening
public class PredicationPass : IMethodPass {
    readonly Dictionary<BasicBlock, Value> _blockMasks = new();
    readonly IRBuilder _builder = new();

    UniformValueAnalysis _uniformity = null!;
    LoopAnalysis _loopAnalysis = null!;

    public MethodPassResult Run(MethodTransformContext ctx) {
        // TODO: Simplify loops
        _loopAnalysis = ctx.GetAnalysis<LoopAnalysis>();
        _uniformity = ctx.GetAnalysis<UniformValueAnalysis>();

        GenerateMasks(ctx.Method);

        foreach(var loop in _loopAnalysis.Loops) {
            Debug.Assert(loop.Header.NumPreds == 2);

            var preheader = loop.GetPreheader()!;
            var latch = loop.GetLatch()!;

            var activeMask = loop.Header.InsertPhi(PrimType.Bool).SetName("activemask");

            activeMask.AddArg(
                new PhiArg(preheader, CreateEdgeMask(loop.Header, preheader)),
                new PhiArg(latch, CreateEdgeMask(loop.Header, latch))
            );
            _blockMasks[loop.Header] = activeMask;
        }
        
        // TODO: Mask gen

        // TODO: predication

        _blockMasks.Clear();

        return MethodInvalidations.None;
    }


    // Generate masks and replace phis with selects
    private void GenerateMasks(MethodBody method) {
        _blockMasks[method.EntryBlock] = ConstInt.Create(PrimType.Bool, 1);

        foreach (var block in method) {
            if (!_uniformity.IsDivergent(block)) continue;

            var mask = GetBlockMask(block);

            _builder.SetPosition(block.FirstNonHeader, InsertionDir.Before);

            foreach (var phi in block.Phis()) {
                if (_uniformity.IsUniform(phi)) continue;

                Debug.Assert(phi.NumArgs == 2); // TODO

                var csel = phi.GetValue(0);

                for (int i = 1; i < phi.NumArgs; i++) {
                    var (pred, val) = phi.GetArg(i);
                    csel = _builder.CreateSelect(GetBlockMask(pred), val, csel);
                }
                phi.ReplaceWith(csel);
            }
        }
    }
    
    // TODO: Move this to DistIL
    private void SimplifyLoop(LoopInfo loop) {
        var exits = loop.GetExitingBlocks();
        var latches = loop.GetLatches();

        if (exits.Count > 1 || latches.Count > 1) {
            throw new NotImplementedException();
        }
    }


    // https://github.com/cdl-saarland/rv/blob/release/16.x/src/transform/maskExpander.cpp
    // 
    
    private Value GetBlockMask(BasicBlock block) {
        ref var mask = ref _blockMasks.GetOrAddRef(block);

        if (mask == null) {
            foreach (var pred in block.Preds) {
                var edgeMask = CreateEdgeMask(block, pred);

                if (mask == null) {
                    mask = edgeMask;
                } else {
                    _builder.SetPosition(block.FirstNonHeader, InsertionDir.Before);
                    mask = _builder.CreateAnd(mask, edgeMask);
                }
            }
        }
        return mask!;
    }
    private Value CreateEdgeMask(BasicBlock block, BasicBlock pred) {
        if (pred.Last is BranchInst br) {
            if (!br.IsConditional) {
                return GetBlockMask(pred);
            }

            if (br.Then == block) {
                return br.Cond;
            } else {
                _builder.SetPosition(pred, InsertionDir.BeforeLast);
                return _builder.CreateNot(br.Cond);
            }
        }
        throw new NotSupportedException();
    }

    private void Flatten() {

    }
}