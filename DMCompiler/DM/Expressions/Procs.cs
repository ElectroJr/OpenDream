using DMCompiler.Compiler.DM;
using OpenDreamShared.Compiler;
using OpenDreamShared.Dream;
using OpenDreamShared.Dream.Procs;
using System;
using System.Linq;

namespace DMCompiler.DM.Expressions {
    // x() (only the identifier)
    class Proc : DMExpression {
        string _identifier;

        public Proc(Location location, string identifier) : base(location) {
            _identifier = identifier;
        }

        public override void EmitPushValue(DMObject dmObject, DMProc proc) {
            throw new CompileErrorException(Location, "attempt to use proc as value");
        }

        public override (DMReference Reference, bool Conditional) EmitReference(DMObject dmObject, DMProc proc) {
            if (dmObject.HasProc(_identifier)) {
                return (DMReference.CreateSrcProc(_identifier), false);
            } else if (DMObjectTree.TryGetGlobalProc(_identifier, out DMProc globalProc)) {
                return (DMReference.CreateGlobalProc(globalProc.Id), false);
            }
            
            DMCompiler.Emit(WarningCode.ItemDoesntExist, Location, $"Type {dmObject.Path} does not have a proc named \"{_identifier}\"");
            //Just... pretend there is one for the sake of argument.
            return (DMReference.CreateSrcProc(_identifier), false);
        }

        public DMProc GetProc(DMObject dmObject)
        {
            var procId = dmObject.GetProcs(_identifier)?[^1];
            return procId is null ? null : DMObjectTree.AllProcs[procId.Value];
        }
    }

    /// <remarks>
    /// This doesn't actually contain the GlobalProc itself;
    /// this is just a hopped-up string that we eventually deference to get the real global proc during compilation.
    /// </remarks>
    class GlobalProc : DMExpression {
        string _name;

        public GlobalProc(Location location, string name) : base(location) {
            _name = name;
        }

        public override void EmitPushValue(DMObject dmObject, DMProc proc) {
            DMCompiler.Emit(WarningCode.InvalidReference, Location, $"Attempt to use proc \"{_name}\" as value");
        }

        public override (DMReference Reference, bool Conditional) EmitReference(DMObject dmObject, DMProc proc) {
            DMProc globalProc = GetProc();

            return (DMReference.CreateGlobalProc(globalProc.Id), false);
        }

        public DMProc GetProc() {
            if (!DMObjectTree.TryGetGlobalProc(_name, out DMProc globalProc)) {
                DMCompiler.Emit(WarningCode.ItemDoesntExist, Location, $"No global proc named \"{_name}\"");
                return DMObjectTree.GlobalInitProc; // Just give this, who cares
            }

            return globalProc;
        }
    }

    /// <summary>
    /// . <br/>
    /// This is an LValue _and_ a proc!
    /// </summary>
    class ProcSelf : LValue {
        public ProcSelf(Location location)
            : base(location, null)
        {}

        public override (DMReference Reference, bool Conditional) EmitReference(DMObject dmObject, DMProc proc) {
            return (DMReference.Self, false);
        }
    }

    // ..
    class ProcSuper : DMExpression {
        public ProcSuper(Location location) : base(location) { }

        public override void EmitPushValue(DMObject dmObject, DMProc proc) {
            DMCompiler.Emit(WarningCode.InvalidReference, Location, $"Attempt to use proc \"..\" as value");
        }

        public override (DMReference Reference, bool Conditional) EmitReference(DMObject dmObject, DMProc proc) {
            if ((proc.Attributes & ProcAttributes.IsOverride) != ProcAttributes.IsOverride)
            {
                DMCompiler.Emit(WarningCode.PointlessParentCall, Location, "Calling parents via ..() in a proc definition does nothing");
            }
            return (DMReference.SuperProc, false);
        }
    }

    // x(y, z, ...)
    sealed class ProcCall : DMExpression {
        DMExpression _target;
        ArgumentList _arguments;

        public ProcCall(Location location, DMExpression target, ArgumentList arguments) : base(location) {
            _target = target;
            _arguments = arguments;
        }

        public (DMObject? ProcOwner, DMProc? Proc) GetTargetProc(DMObject dmObject) {
            return _target switch {
                Proc procTarget => (dmObject, procTarget.GetProc(dmObject)),
                GlobalProc procTarget => (null, procTarget.GetProc()),
                DereferenceProc derefTarget => derefTarget.GetProc(),
                _ => (null, null)
            };
        }

        public override void EmitPushValue(DMObject dmObject, DMProc proc) {
            (DMObject? procOwner, DMProc? targetProc) = GetTargetProc(dmObject);
            DoCompiletimeLinting(procOwner, targetProc);
            if ((targetProc?.Attributes & ProcAttributes.Unimplemented) == ProcAttributes.Unimplemented) {
                DMCompiler.UnimplementedWarning(Location, $"{procOwner?.Path.ToString() ?? "/"}.{targetProc.Name}() is not implemented");
            }

            (DMReference procRef, bool conditional) = _target.EmitReference(dmObject, proc);

            if (conditional) {
                var skipLabel = proc.NewLabelName();
                proc.JumpIfNullDereference(procRef, skipLabel);
                if (_arguments.Length == 0 && _target is ProcSuper) {
                    proc.PushProcArguments();
                } else {
                    _arguments.EmitPushArguments(dmObject, proc);
                }
                proc.Call(procRef);
                proc.AddLabel(skipLabel);
            } else {
                if (_arguments.Length == 0 && _target is ProcSuper) {
                    proc.PushProcArguments();
                } else {
                    _arguments.EmitPushArguments(dmObject, proc);
                }
                proc.Call(procRef);
            }
        }

        /// <summary>
        /// This is a good place to do some compile-time linting of any native procs that require it,
        /// such as native procs that check ahead of time if the number of arguments is correct (like matrix() or sin())
        /// </summary>
        protected void DoCompiletimeLinting(DMObject procOwner, DMProc targetProc) {
            if(procOwner is null || procOwner.Path == DreamPath.Root) {
                if (targetProc is null)
                    return;
                if(targetProc.Name == "matrix") {
                    switch(_arguments.Length) {
                        case 0:
                        case 1: // NOTE: 'case 1' also ends up referring to the arglist situation. FIXME: Make this lint work for that, too?
                        case 6: 
                            break; // Normal cases
                        case 2:
                        case 3: // These imply that they're trying to use the undocumented matrix signatures.
                        case 4: // The lint is to just check that the last argument is a numeric constant that is a valid matrix "opcode."
                            var lastArg = _arguments.Expressions.Last().Expr;
                            if (lastArg is null)
                                break;
                            if(lastArg.TryAsConstant(out Constant constant)) {
                                if(constant is not Number opcodeNumber) {
                                    DMCompiler.Emit(WarningCode.SuspiciousMatrixCall, _arguments.Location,
                                    "Arguments for matrix() are invalid - either opcode is invalid or not enough arguments");
                                    break;
                                }
                                //Note that it is possible for the numeric value to not be an opcode itself,
                                //but the call is still valid.
                                //This is because of MATRIX_MODIFY; things like MATRIX_INVERT | MATRIX_MODIFY are okay!
                                const int notModifyBits = ~(int)MatrixOpcode.Modify;
                                if (!Enum.IsDefined((MatrixOpcode) ((int)opcodeNumber.Value & notModifyBits))) {
                                    //NOTE: This still does let some certain weird opcodes through,
                                    //like a MODIFY with no other operation present.
                                    //Not sure if that is a parity behaviour or not!
                                    DMCompiler.Emit(WarningCode.SuspiciousMatrixCall, _arguments.Location,
                                    "Arguments for matrix() are invalid - either opcode is invalid or not enough arguments");
                                    break;
                                }
                            }
                            break;
                        case 5: // BYOND always runtimes but DOES compile, here
                            DMCompiler.Emit(WarningCode.SuspiciousMatrixCall, _arguments.Location,
                                $"Calling matrix() with 5 arguments will always error when called at runtime");
                            break;
                        default: // BYOND always compiletimes here
                            DMCompiler.Emit(WarningCode.TooManyArguments, _arguments.Location,
                                $"Too many arguments to matrix() - got {_arguments.Length} arguments, expecting 6 or less");
                            break;

                    }
                }
            }
        }

        public override bool TryAsJsonRepresentation(out object json) {
            json = null;
            DMCompiler.UnimplementedWarning(Location, $"DMM overrides for expression {GetType()} are not implemented");
            return true; //TODO
        }
    }


}
