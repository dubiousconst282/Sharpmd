namespace Sharpmd.Compiler;

using DistIL.Analysis;
using DistIL.AsmIO;
using DistIL.IR;
using DistIL.Passes;
using DistIL.Util;

/// <summary> A more aggressive SROA pass specifically for structs. </summary>
public class AggressiveSROA : IMethodPass {
    public MethodPassResult Run(MethodTransformContext ctx) {
        bool changed = false;

        changed |= DestructSlots(ctx);
        changed |= PropagateFieldInserts(ctx.Method);

        return changed ? MethodInvalidations.DataFlow : MethodInvalidations.Everything;
    }

    // Split local slots of struct types.
    private bool DestructSlots(MethodTransformContext ctx) {
        int numChanges = 0;

        foreach (var slot in ctx.Method.LocalVars()) {
            if (slot.Type.Kind != TypeKind.Struct || !IsProfitableToDestructVar(ctx, slot)) continue;

            // TODO
        }
        return numChanges > 0;
    }
    private bool IsProfitableToDestructVar(MethodTransformContext ctx, LocalSlot slot) {
        int numFieldAccs = 0;
        int numEscapes = 0;

        foreach (var user in slot.Users()) {
            if (user is FieldAddrInst) {
                numFieldAccs++;
            } else if (user is CallInst call && !CanInlineMethodToMakeValueNonEscaping(ctx, call.Method, call.Args, slot)) {
                return false;
            } else if (!(user is LoadInst or StoreInst or CilIntrinsic.MemSet)) {
                numEscapes++;
            }
        }
        if (numFieldAccs == 0) {
            return false;
        }
        if (numEscapes > 0 && GetConstructCostEst(slot.Type) * numEscapes > numFieldAccs) {
            return false;
        }
        return true;
    }
    private int GetConstructCostEst(TypeDesc type) {
        return type.Fields.Count;
    }

    // Checks if it is cheap to inline the given method, and if doing so avoids the other given value from escaping.
    // This currently only checks the one method, and does not consider recursive calls.
    //
    // Trivia: this is possibly the longest/wordest identifier name I have ever written. Brainrot may be starting to affect me.
    private bool CanInlineMethodToMakeValueNonEscaping(MethodTransformContext ctx, MethodDesc methodDesc, ReadOnlySpan<Value> args, Value valueThatMustNotEscape) {
        if (methodDesc is not MethodDefOrSpec { Definition: var method }) return false;

        var advisor = ctx.Compilation.GetAnalysis<InliningAdvisor>();

        if (advisor.EarlyCheck(method) != InlineRejectReason.Accepted) {
            return false;
        }
        if (method.Body == null && !advisor.ImportBodyForInlining(method)) {
            return false;
        }

        var body = method.Body!;
        if (advisor.EvaluateInliningCost(body!, args) > 80) {
            return false;
        }

        for (int i = 0; i < args.Length; i++) {
            if (args[i] == valueThatMustNotEscape && IsEscaping(body.Args[i], isCtor: method.IsInstance && i == 0)) {
                return false;
            }
        }
        return true;
    }
    private static bool IsEscaping(TrackedValue obj, bool isCtor = false)
    {
        // Consider obj as escaping if it is being passed somewhere
        return !obj.Users().All(u => u is FieldAddrInst or FieldExtractInst or LoadInst || (isCtor && IsObjectCtorCall(u)));
    }
    private static bool IsObjectCtorCall(Instruction inst)
    {
        return inst is CallInst { Method.Name: ".ctor", Method.DeclaringType: var declType } &&
            declType.IsCorelibType(typeof(object));
    }

    // Propagate setfld chains to immediate getfld instructions.
    private bool PropagateFieldInserts(MethodBody method) {
        var cache = new Dictionary<(Value, FieldDesc), Value>();
        bool changed = false;

        foreach (var inst in method.Instructions()) {
            if (inst is FieldExtractInst extr) {
                var value = LookupField(extr.Obj, extr.Field);

                if (value != null) {
                    extr.ReplaceWith(value);
                    RemoveDeadInserts(extr.Obj);
                    changed = true;
                }
            }
        }
        return changed;
        
        Value? LookupField(Value obj, FieldDesc field) {
            // TODO: add equality to FieldDesc/MethodDesc
            ref var cached = ref cache.GetOrAddRef((obj, field), out bool exists);

            if (!exists) {
                cached = ResolveField(obj, field);
            }
            return cached;
        }
        Value? ResolveField(Value obj, FieldDesc field) {
            if (obj is FieldInsertInst rootInsr && !IsProfitableToPropagateInsert(rootInsr)) {
                return null;
            }

            // Traverse the entire insert chain to initialize cache
            var targetInsr = default(FieldInsertInst);

            while (obj is FieldInsertInst insr) {
                if (field == insr.Field && targetInsr == null) {
                    targetInsr = insr;
                }
                cache[(obj, field)] = insr.NewValue;
                obj = insr.Obj;
            }
            if (targetInsr != null) {
                return targetInsr.NewValue;
            }
            // TODO: consider traversing/replacing phis
            return null;
        }
    }

    private bool IsProfitableToPropagateInsert(FieldInsertInst insr) {
        return insr.Users().All(u => u is FieldExtractInst or FieldInsertInst);
    }
    private void RemoveDeadInserts(Value obj) {
        while (obj is FieldInsertInst insr && insr.NumUses == 0) {
            insr.Remove();
            obj = insr.Obj;
        }
    }
}