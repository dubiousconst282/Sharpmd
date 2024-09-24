namespace Sharpmd.Compiler;

using System.Diagnostics;

using DistIL.Analysis;
using DistIL.AsmIO;
using DistIL.IR;
using DistIL.IR.Utils;
using DistIL.Util;

public class MaskGenerator {
    readonly MethodBody _srcMethod, _destMethod;
    readonly Dictionary<BasicBlock, Value> _blockMasks = new();
    readonly Dictionary<(BasicBlock Src, BasicBlock Dest), Value> _edgeMasks = new();

    UniformValueAnalysis _uniformity = null!;
    LoopAnalysis _loopAnalysis = null!;
    IRBuilder _builder;

    public MaskGenerator(MethodBody srcMethod, MethodBody destMethod, UniformValueAnalysis uniformity, LoopAnalysis loopAnalysis) {
        _srcMethod = srcMethod;
        _destMethod = destMethod;
        _uniformity = uniformity;
        _loopAnalysis = loopAnalysis;
        
        _blockMasks[srcMethod.EntryBlock] = ConstInt.Create(PrimType.Bool, 1);
        
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

        _destMethod.CreateBlock();   
    }

    // Returns a new array of all source method blocks, sorted in topological order.
    public ReadOnlySpan<BasicBlock> GetSortedBlocks() {
        var blocks = new BasicBlock[_srcMethod.NumBlocks];
        int i = blocks.Length;
        _srcMethod.TraverseDepthFirst(postVisit: b => blocks[--i] = b);
        return blocks.AsSpan(i);
    }

    public BasicBlock GetFlattenedBlock(BasicBlock srcBlock) {
        return _destMethod.EntryBlock;
    }

    // TODO: Move this to DistIL
    private void SimplifyLoop(LoopInfo loop) {
        var exits = loop.GetExitingBlocks();
        var latches = loop.GetLatches();

        if (exits.Count > 1 || latches.Count > 1) {
            throw new NotImplementedException();
        }
    }
    
    public Value GetBlockMask(BasicBlock block) {
        return _blockMasks.GetOrAddRef(block) ??= CreateBlockMask(block);
    }

    public Value GetEdgeMask(BasicBlock block, BasicBlock pred) {
        return _edgeMasks.GetOrAddRef((block, pred)) ??= CreateEdgeMask(block, pred);
    }
    
    public bool IsSelectionPhi(PhiInst phi) {
        return !_uniformity.IsUniform(phi);
    }

    private Value CreateBlockMask(BasicBlock block) {
        var mask = default(Value);

        foreach (var pred in block.Preds) {
            var edgeMask = CreateEdgeMask(block, pred);

            if (mask == null) {
                mask = edgeMask;
            } else {
                _builder.SetPosition(block.FirstNonHeader, InsertionDir.Before);
                mask = _builder.CreateAnd(mask, edgeMask);
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
}