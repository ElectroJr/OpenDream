﻿using System.Diagnostics.CodeAnalysis;
using OpenDreamRuntime.Objects.MetaObjects;
using OpenDreamShared.Dream;

namespace OpenDreamRuntime.Objects {
    public sealed class DreamObjectDefinition {
        public readonly IDreamManager DreamManager = default!;
        public readonly IDreamObjectTree ObjectTree = default!;

        public readonly IDreamObjectTree.TreeEntry TreeEntry;
        public DreamPath Type => TreeEntry.Path;
        public DreamObjectDefinition? Parent => TreeEntry.ParentEntry?.ObjectDefinition;
        public IDreamMetaObject? MetaObject = null;
        public int? InitializationProc;
        public readonly Dictionary<string, int> Procs = new();
        public readonly Dictionary<string, int> OverridingProcs = new();
        public List<int>? Verbs;

        // Maps variables from their name to their initial value.
        public readonly Dictionary<string, DreamValue> Variables = new();
        // Maps /static variables from name to their index in the global variable table.
        public readonly Dictionary<string, int> GlobalVariables = new();

        public DreamObjectDefinition(DreamObjectDefinition copyFrom) {
            DreamManager = copyFrom.DreamManager;
            ObjectTree = copyFrom.ObjectTree;
            TreeEntry = copyFrom.TreeEntry;
            MetaObject = copyFrom.MetaObject;
            InitializationProc = copyFrom.InitializationProc;

            Variables = new Dictionary<string, DreamValue>(copyFrom.Variables);
            GlobalVariables = new Dictionary<string, int>(copyFrom.GlobalVariables);
            Procs = new Dictionary<string, int>(copyFrom.Procs);
            OverridingProcs = new Dictionary<string, int>(copyFrom.OverridingProcs);
            if (copyFrom.Verbs != null)
                Verbs = new List<int>(copyFrom.Verbs);
        }

        public DreamObjectDefinition(IDreamManager dreamManager, IDreamObjectTree objectTree, IDreamObjectTree.TreeEntry treeEntry) {
            DreamManager = dreamManager;
            ObjectTree = objectTree;
            TreeEntry = treeEntry;

            if (Parent != null) {
                InitializationProc = Parent.InitializationProc;
                Variables = new Dictionary<string, DreamValue>(Parent.Variables);
                GlobalVariables = new Dictionary<string, int>(Parent.GlobalVariables);
                if (Parent.Verbs != null)
                    Verbs = new List<int>(Parent.Verbs);
            }
        }

        public void SetVariableDefinition(string variableName, DreamValue value) {
            Variables[variableName] = value;
        }

        public void SetProcDefinition(string procName, int procId) {
            if (HasProc(procName)) {
                var proc = ObjectTree.Procs[procId];
                proc.SuperProc = GetProc(procName);
                OverridingProcs[procName] = procId;
            } else {
                Procs[procName] = procId;
            }
        }

        public DreamProc GetProc(string procName) {
            if (TryGetProc(procName, out DreamProc? proc)) {
                return proc;
            } else {
                throw new Exception("Object type '" + Type + "' does not have a proc named '" + procName + "'");
            }
        }

        public bool TryGetProc(string procName, [NotNullWhen(true)] out DreamProc? proc) {
            if (OverridingProcs.TryGetValue(procName, out var procId)) {
                proc = ObjectTree.Procs[procId];
                return true;
            } else if (Procs.TryGetValue(procName, out procId)) {
                proc = ObjectTree.Procs[procId];
                return true;
            } else if (Parent != null) {
                return Parent.TryGetProc(procName, out proc);
            } else {
                proc = null;
                return false;
            }
        }

        public bool HasProc(string procName) {
            if (Procs.ContainsKey(procName)) {
                return true;
            } else if (Parent != null) {
                return Parent.HasProc(procName);
            } else {
                return false;
            }
        }

        public bool HasVariable(string variableName) {
            return Variables.ContainsKey(variableName);
        }

        public bool TryGetVariable(string varName, out DreamValue value) {
            if (Variables.TryGetValue(varName, out value)) {
                return true;
            } else if (Parent != null) {
                return Parent.TryGetVariable(varName, out value);
            } else {
                return false;
            }
        }

        public bool IsSubtypeOf(IDreamObjectTree.TreeEntry ancestor) {
            // Unsigned underflow is desirable here
            return (TreeEntry.TreeIndex - ancestor.TreeIndex) <= ancestor.ChildCount;
        }
    }
}
