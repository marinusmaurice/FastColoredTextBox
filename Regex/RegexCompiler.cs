//------------------------------------------------------------------------------
// <copyright file="RegexCompiler.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>                                                                
//------------------------------------------------------------------------------

// The RegexCompiler class is internal to the Regex package.
// It translates a block of RegexCode to MSIL, and creates a
// subclass of the RegexRunner type.


#if !SILVERLIGHT && !FULL_AOT_RUNTIME

namespace System.Text.RegularExpressions1 {

    using System.Collections;
	using System.Collections.Generic;
    using System.Threading;
    using System.Reflection;
    using System.Reflection.Emit;
    using System.Security;
    using System.Security.Policy;
    using System.Security.Permissions;
    using System.Diagnostics;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Runtime.Versioning;

    /*
     * RegexDynamicModule
     *
     * Because dynamic modules are expensive and not thread-safe, we create
     * one dynamic module per-thread, and cache as much information about it
     * as we can.
     *
     * While we're at it, we just create one RegexCompiler per thread
     * as well, and have RegexCompiler inherit from RegexDynamicModule.
     */
    internal abstract class RegexCompiler {
        // fields that never change (making them saves about 6% overall running time)

        internal static FieldInfo      _textbegF;
        internal static FieldInfo      _textendF;
        internal static FieldInfo      _textstartF;
        internal static FieldInfo      _textposF;
        internal static FieldInfo      _textF;
        internal static FieldInfo      _trackposF;
        internal static FieldInfo      _trackF;
        internal static FieldInfo      _stackposF;
        internal static FieldInfo      _stackF;
        internal static FieldInfo      _trackcountF;

        // note some methods

        internal static MethodInfo     _ensurestorageM;
        internal static MethodInfo     _captureM;
        internal static MethodInfo     _transferM;
        internal static MethodInfo     _uncaptureM;
        internal static MethodInfo     _ismatchedM;
        internal static MethodInfo     _matchlengthM;
        internal static MethodInfo     _matchindexM;
        internal static MethodInfo     _isboundaryM;
        internal static MethodInfo     _isECMABoundaryM;
        internal static MethodInfo     _chartolowerM; 
        internal static MethodInfo     _getcharM; 
        internal static MethodInfo     _crawlposM; 
        internal static MethodInfo     _charInSetM;
        internal static MethodInfo     _getCurrentCulture;
        internal static MethodInfo     _getInvariantCulture;
        internal static MethodInfo     _checkTimeoutM;
    #if DBG
        internal static MethodInfo     _dumpstateM;
    #endif

        internal ILGenerator     _ilg;

        // tokens representing local variables
        internal LocalBuilder      _textstartV;
        internal LocalBuilder      _textbegV;
        internal LocalBuilder      _textendV;
        internal LocalBuilder      _textposV;
        internal LocalBuilder      _textV;
        internal LocalBuilder      _trackposV;
        internal LocalBuilder      _trackV;
        internal LocalBuilder      _stackposV;
        internal LocalBuilder      _stackV;
        internal LocalBuilder      _tempV;
        internal LocalBuilder      _temp2V;
        internal LocalBuilder      _temp3V;


        internal RegexCode       _code;              // the RegexCode object (used for debugging only)
        internal int[]           _codes;             // the RegexCodes being translated
        internal String[]        _strings;           // the stringtable associated with the RegexCodes
        internal RegexPrefix     _fcPrefix;          // the possible first chars computed by RegexFCD
        internal RegexBoyerMoore _bmPrefix;          // a prefix as a boyer-moore machine
        internal int             _anchors;           // the set of anchors

        internal Label[]         _labels;            // a label for every operation in _codes
        internal BacktrackNote[] _notes;             // a list of the backtracking states to be generated
        internal int             _notecount;         // true count of _notes (allocation grows exponentially)
        internal int             _trackcount;        // count of backtracking states (used to reduce allocations)

        internal Label           _backtrack;         // label for backtracking


        internal int             _regexopcode;       // the current opcode being processed
        internal int             _codepos;           // the current code being translated
        internal int             _backpos;           // the current backtrack-note being translated

        internal RegexOptions    _options;           // options

        // special code fragments
        internal int[]           _uniquenote;        // _notes indices for code that should be emitted <= once
        internal int[]           _goto;              // indices for forward-jumps-through-switch (for allocations)

        // indices for unique code fragments
        internal const int stackpop               = 0;    // pop one
        internal const int stackpop2              = 1;    // pop two
        internal const int stackpop3              = 2;    // pop three
        internal const int capback                = 3;    // uncapture
        internal const int capback2               = 4;    // uncapture 2
        internal const int branchmarkback2        = 5;    // back2 part of branchmark
        internal const int lazybranchmarkback2    = 6;    // back2 part of lazybranchmark
        internal const int branchcountback2       = 7;    // back2 part of branchcount
        internal const int lazybranchcountback2   = 8;    // back2 part of lazybranchcount
        internal const int forejumpback           = 9;    // back part of forejump
        internal const int uniquecount            = 10;

        static RegexCompiler() {
            // <SECREVIEW> Regex only generates string manipulation, so this is ok.
            // </SECREVIEW>      

#if MONO_FEATURE_CAS
            new ReflectionPermission(PermissionState.Unrestricted).Assert();
#endif
            try {
                // note some fields
                _textbegF       = RegexRunnerField("runtextbeg");
                _textendF       = RegexRunnerField("runtextend");
                _textstartF     = RegexRunnerField("runtextstart");
                _textposF       = RegexRunnerField("runtextpos");
                _textF          = RegexRunnerField("runtext");
                _trackposF      = RegexRunnerField("runtrackpos");
                _trackF         = RegexRunnerField("runtrack");
                _stackposF      = RegexRunnerField("runstackpos");
                _stackF         = RegexRunnerField("runstack");
                _trackcountF    = RegexRunnerField("runtrackcount");

                // note some methods
                _ensurestorageM = RegexRunnerMethod("EnsureStorage");
                _captureM       = RegexRunnerMethod("Capture");
                _transferM      = RegexRunnerMethod("TransferCapture");
                _uncaptureM     = RegexRunnerMethod("Uncapture");
                _ismatchedM     = RegexRunnerMethod("IsMatched");
                _matchlengthM   = RegexRunnerMethod("MatchLength");
                _matchindexM    = RegexRunnerMethod("MatchIndex");
                _isboundaryM    = RegexRunnerMethod("IsBoundary");
                _charInSetM     = RegexRunnerMethod("CharInClass");
                _isECMABoundaryM= RegexRunnerMethod("IsECMABoundary");
                _crawlposM      = RegexRunnerMethod("Crawlpos");
                _checkTimeoutM  = RegexRunnerMethod("CheckTimeout");

                _chartolowerM   = typeof(Char).GetMethod("ToLower", new Type[] {typeof(Char), typeof(CultureInfo)});
                _getcharM       = typeof(String).GetMethod("get_Chars", new Type[] {typeof(int)});
                _getCurrentCulture   = typeof(CultureInfo).GetMethod("get_CurrentCulture");
                _getInvariantCulture = typeof(CultureInfo).GetMethod("get_InvariantCulture");
                

#if DBG
                _dumpstateM     = RegexRunnerMethod("DumpState");
#endif
            }
            finally {
#if MONO_FEATURE_CAS 
                CodeAccessPermission.RevertAssert();
#endif
            }
        }

        private static FieldInfo RegexRunnerField(String fieldname) {
            return typeof(RegexRunner).GetField(fieldname, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        }

        private static MethodInfo RegexRunnerMethod(String methname) {
            return typeof(RegexRunner).GetMethod(methname, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        }


        /* 
         * Entry point to dynamically compile a regular expression.  The expression is compiled to 
         * an in-memory assembly.
         */
        internal static RegexRunnerFactory Compile(RegexCode code, RegexOptions options) {
            RegexLWCGCompiler c = new RegexLWCGCompiler();
            RegexRunnerFactory factory;

            // <SECREVIEW> Regex only generates string manipulation, so this is ok.
            // </SECREVIEW>         
#if MONO_FEATURE_CAS
            new ReflectionPermission(PermissionState.Unrestricted).Assert();
#endif
            try {
                factory = c.FactoryInstanceFromCode(code, options);
            }
            finally {
#if MONO_FEATURE_CAS
                CodeAccessPermission.RevertAssert();
#endif
            }
            return factory;
        }

        /* 
         * Compile regular expressions into an assembly on disk.
         */
        [ResourceExposure(ResourceScope.Machine)]
        [ResourceConsumption(ResourceScope.Machine)]
        [SuppressMessage("Microsoft.Security","CA2106:SecureAsserts", Justification="Microsoft: SECREVIEW : Regex only generates string manipulation, so this is OK")]
        internal static void CompileToAssembly(RegexCompilationInfo[] regexes, AssemblyName an, CustomAttributeBuilder[] attribs, String resourceFile) {
            RegexTypeCompiler c = new RegexTypeCompiler(an, attribs, resourceFile);
        
            for (int i=0; i<regexes.Length; i++) {
                if (regexes[i] == null) {
                    throw new ArgumentNullException("regexes", SR.GetString(SR.ArgumentNull_ArrayWithNullElements));
                }
                String pattern = regexes[i].Pattern;
                RegexOptions options = regexes[i].Options;
                String fullname;
                if (regexes[i].Namespace.Length == 0)
                    fullname = regexes[i].Name;
                else
                    fullname = regexes[i].Namespace + "." + regexes[i].Name;

                TimeSpan mTimeout = regexes[i].MatchTimeout;
        
                RegexTree tree = RegexParser.Parse(pattern, options);
                RegexCode code = RegexWriter.Write(tree);
        
                Type factory;
        
#if MONO_FEATURE_CAS
                new ReflectionPermission(PermissionState.Unrestricted).Assert();
#endif
                try {
                    factory = c.FactoryTypeFromCode(code, options, fullname);
                    c.GenerateRegexType(pattern, options, fullname, regexes[i].IsPublic, code, tree, factory, mTimeout);
                }
                finally {
#if MONO_FEATURE_CAS
                    CodeAccessPermission.RevertAssert();
#endif
                }
            }
        
            c.Save();
        }
        

        /*
         * Keeps track of an operation that needs to be referenced in the backtrack-jump
         * switch table, and that needs backtracking code to be emitted (if flags != 0)
         */
        internal sealed class BacktrackNote {
            internal BacktrackNote(int flags, Label label, int codepos) {
                _codepos = codepos;
                _flags = flags;
                _label = label;
            }

            internal int _codepos;
            internal int _flags;
            internal Label _label;
        }

        /*
         * Adds a backtrack note to the list of them, and returns the index of the new
         * note (which is also the index for the jump used by the switch table)
         */
        internal int AddBacktrackNote(int flags, Label l, int codepos) {
            if (_notes == null || _notecount >= _notes.Length) {
                BacktrackNote[] newnotes = new BacktrackNote[_notes == null ? 16 : _notes.Length * 2];
                if (_notes != null)
                    System.Array.Copy(_notes, 0, newnotes, 0, _notecount);
                _notes = newnotes;
            }

            _notes[_notecount] = new BacktrackNote(flags, l, codepos);

            return _notecount++;
        }

        /*
         * Adds a backtrack note for the current operation; creates a new label for
         * where the code will be, and returns the switch index.
         */
        internal int AddTrack() {
            return AddTrack(RegexCode.Back);
        }

        /*
         * Adds a backtrack note for the current operation; creates a new label for
         * where the code will be, and returns the switch index.
         */
        internal int AddTrack(int flags) {
            return AddBacktrackNote(flags, DefineLabel(), _codepos);
        }

        /*
         * Adds a switchtable entry for the specified position (for the forward
         * logic; does not cause backtracking logic to be generated)
         */
        internal int AddGoto(int destpos) {
            if (_goto[destpos] == -1)
                _goto[destpos] = AddBacktrackNote(0, _labels[destpos], destpos);

            return _goto[destpos];
        }

        /*
         * Adds a note for backtracking code that only needs to be generated once;
         * if it's already marked to be generated, returns the switch index
         * for the unique piece of code.
         */
        internal int AddUniqueTrack(int i) {
            return AddUniqueTrack(i, RegexCode.Back);
        }

        /*
         * Adds a note for backtracking code that only needs to be generated once;
         * if it's already marked to be generated, returns the switch index
         * for the unique piece of code.
         */
        internal int AddUniqueTrack(int i, int flags) {
            if (_uniquenote[i] == -1)
                _uniquenote[i] = AddTrack(flags);

            return _uniquenote[i];
        }

        /*
         * A macro for _ilg.DefineLabel
         */
        internal Label DefineLabel() {
            return _ilg.DefineLabel();
        }

        /*
         * A macro for _ilg.MarkLabel
         */
        internal void MarkLabel(Label l) {
            _ilg.MarkLabel(l);
        }

        /*
         * Returns the ith operand of the current operation
         */
        internal int Operand(int i) {
            return _codes[_codepos + i + 1];
        }

        /*
         * True if the current operation is marked for the leftward direction
         */
        internal bool IsRtl() {
            return(_regexopcode & RegexCode.Rtl) != 0;
        }

        /*
         * True if the current operation is marked for case insensitive operation
         */
        internal bool IsCi() {
            return(_regexopcode & RegexCode.Ci) != 0;
        }

#if DBG
        /*
         * True if we need to do the backtrack logic for the current operation
         */
        internal bool IsBack() {
            return(_regexopcode & RegexCode.Back) != 0;
        }

        /*
         * True if we need to do the second-backtrack logic for the current operation
         */
        internal bool IsBack2() {
            return(_regexopcode & RegexCode.Back2) != 0;
        }
#endif

        /*
         * Returns the raw regex opcode (masking out Back and Rtl)
         */
        internal int Code() {
            return _regexopcode & RegexCode.Mask;
        }

        internal void Ldstr(string str) {
            _ilg.Emit(OpCodes.Ldstr, str);
        }

        /*
         * A macro for the various forms of Ldc
         */
        internal void Ldc(int i) {
            if (i <= 127 && i >= -128)
                _ilg.Emit(OpCodes.Ldc_I4_S, (byte)i);
            else
                _ilg.Emit(OpCodes.Ldc_I4, i);
        }

        internal void LdcI8(long i) {
            if (i <= Int32.MaxValue && i >= Int32.MinValue) {
                Ldc((Int32) i);
                _ilg.Emit(OpCodes.Conv_I8);
            } else {
                _ilg.Emit(OpCodes.Ldc_I8, i);
            }
        }

        /*
         * A macro for _ilg.Emit(OpCodes.Dup)
         */
        internal void Dup() {
            _ilg.Emit(OpCodes.Dup);
        }

        /*
         * A macro for _ilg.Emit(OpCodes.Ret)
         */
        internal void Ret() {
            _ilg.Emit(OpCodes.Ret);
        }

        /*
         * A macro for _ilg.Emit(OpCodes.Pop)
         */
        internal void Pop() {
            _ilg.Emit(OpCodes.Pop);
        }

        /*
         * A macro for _ilg.Emit(OpCodes.Add)
         */
        internal void Add() {
            _ilg.Emit(OpCodes.Add);
        }

        /*
         * A macro for _ilg.Emit(OpCodes.Add); a true flag can turn it into a Sub
         */
        internal void Add(bool negate) {
            if (negate)
                _ilg.Emit(OpCodes.Sub);
            else
                _ilg.Emit(OpCodes.Add);
        }

        /*
         * A macro for _ilg.Emit(OpCodes.Sub)
         */
        internal void Sub() {
            _ilg.Emit(OpCodes.Sub);
        }

        /*
         * A macro for _ilg.Emit(OpCodes.Sub); a true flag can turn it into a Add
         */
        internal void Sub(bool negate) {
            if (negate)
                _ilg.Emit(OpCodes.Add);
            else
                _ilg.Emit(OpCodes.Sub);
        }

        /*
         * A macro for _ilg.Emit(OpCodes.Ldloc);
         */
        internal void Ldloc(LocalBuilder lt) {
            _ilg.Emit(OpCodes.Ldloc_S, lt);
        }

        /*
         * A macro for _ilg.Emit(OpCodes.Stloc);
         */
        internal void Stloc(LocalBuilder lt) {
            _ilg.Emit(OpCodes.Stloc_S, lt);
        }

        /*
         * A macro for _ilg.Emit(OpCodes.Ldarg_0);
         */
        internal void Ldthis() {
            _ilg.Emit(OpCodes.Ldarg_0);
        }

        /*
         * A macro for Ldthis(); Ldfld();
         */
        internal void Ldthisfld(FieldInfo ft) {
            Ldthis();
            _ilg.Emit(OpCodes.Ldfld, ft);
        }

        /*
         * A macro for Ldthis(); Ldfld(); Stloc();
         */
        internal void Mvfldloc(FieldInfo ft, LocalBuilder lt) {
            Ldthisfld(ft);
            Stloc(lt);
        }

        /*
         * A macro for Ldthis(); Ldthisfld(); Stloc();
         */
        internal void Mvlocfld(LocalBuilder lt, FieldInfo ft) {
            Ldthis();
            Ldloc(lt);
            Stfld(ft);
        }

        /*
         * A macro for _ilg.Emit(OpCodes.Stfld);
         */
        internal void Stfld(FieldInfo ft) {
            _ilg.Emit(OpCodes.Stfld, ft);
        }

        /*
         * A macro for _ilg.Emit(OpCodes.Callvirt);
         */
        internal void Callvirt(MethodInfo mt) {
            _ilg.Emit(OpCodes.Callvirt, mt);
        }

        /*
         * A macro for _ilg.Emit(OpCodes.Call);
         */
        internal void Call(MethodInfo mt) {
            _ilg.Emit(OpCodes.Call, mt);
        }

        /*
         * A macro for _ilg.Emit(OpCodes.Newobj);
         */
        internal void Newobj(ConstructorInfo ct) {
            _ilg.Emit(OpCodes.Newobj, ct);
        }

        /*
         * A macro for _ilg.Emit(OpCodes.Brfalse) (long form)
         */
        internal void BrfalseFar(Label l) {
            _ilg.Emit(OpCodes.Brfalse, l);
        }

        /*
         * A macro for _ilg.Emit(OpCodes.Brtrue) (long form)
         */
        internal void BrtrueFar(Label l) {
            _ilg.Emit(OpCodes.Brtrue, l);
        }

        /*
         * A macro for _ilg.Emit(OpCodes.Br) (long form)
         */
        internal void BrFar(Label l) {
            _ilg.Emit(OpCodes.Br, l);
        }

        /*
         * A macro for _ilg.Emit(OpCodes.Ble) (long form)
         */
        internal void BleFar(Label l) {
            _ilg.Emit(OpCodes.Ble, l);
        }

        /*
         * A macro for _ilg.Emit(OpCodes.Blt) (long form)
         */
        internal void BltFar(Label l) {
            _ilg.Emit(OpCodes.Blt, l);
        }

        /*
         * A macro for _ilg.Emit(OpCodes.Bge) (long form)
         */
        internal void BgeFar(Label l) {
            _ilg.Emit(OpCodes.Bge, l);
        }

        /*
         * A macro for _ilg.Emit(OpCodes.Bgt) (long form)
         */
        internal void BgtFar(Label l) {
            _ilg.Emit(OpCodes.Bgt, l);
        }

        /*
         * A macro for _ilg.Emit(OpCodes.Bne) (long form)
         */
        internal void BneFar(Label l) {
            _ilg.Emit(OpCodes.Bne_Un, l);
        }

        /*
         * A macro for _ilg.Emit(OpCodes.Beq) (long form)
         */
        internal void BeqFar(Label l) {
            _ilg.Emit(OpCodes.Beq, l);
        }

        /*
         * A macro for _ilg.Emit(OpCodes.Brfalse_S) (short jump)
         */
        internal void Brfalse(Label l) {
            _ilg.Emit(OpCodes.Brfalse_S, l);
        }

        /*
         * A macro for _ilg.Emit(OpCodes.Br_S) (short jump)
         */
        internal void Br(Label l) {
            _ilg.Emit(OpCodes.Br_S, l);
        }

        /*
         * A macro for _ilg.Emit(OpCodes.Ble_S) (short jump)
         */
        internal void Ble(Label l) {
            _ilg.Emit(OpCodes.Ble_S, l);
        }

        /*
         * A macro for _ilg.Emit(OpCodes.Blt_S) (short jump)
         */
        internal void Blt(Label l) {
            _ilg.Emit(OpCodes.Blt_S, l);
        }

        /*
         * A macro for _ilg.Emit(OpCodes.Bge_S) (short jump)
         */
        internal void Bge(Label l) {
            _ilg.Emit(OpCodes.Bge_S, l);
        }

        /*
         * A macro for _ilg.Emit(OpCodes.Bgt_S) (short jump)
         */
        internal void Bgt(Label l) {
            _ilg.Emit(OpCodes.Bgt_S, l);
        }

        /*
         * A macro for _ilg.Emit(OpCodes.Bleun_S) (short jump)
         */
        internal void Bgtun(Label l) {
            _ilg.Emit(OpCodes.Bgt_Un_S, l);
        }

        /*
         * A macro for _ilg.Emit(OpCodes.Bne_S) (short jump)
         */
        internal void Bne(Label l) {
            _ilg.Emit(OpCodes.Bne_Un_S, l);
        }

        /*
         * A macro for _ilg.Emit(OpCodes.Beq_S) (short jump)
         */
        internal void Beq(Label l) {
            _ilg.Emit(OpCodes.Beq_S, l);
        }

        /*
         * A macro for the Ldlen instruction
         */
        internal void Ldlen() {
            _ilg.Emit(OpCodes.Ldlen);
        }

        /*
         * Loads the char to the right of the current position
         */
        internal void Rightchar() {
            Ldloc(_textV);
            Ldloc(_textposV);
            Callvirt(_getcharM);
        }

        /*
         * Loads the char to the right of the current position and advances the current position
         */
        internal void Rightcharnext() {
            Ldloc(_textV);
            Ldloc(_textposV);
            Dup();
            Ldc(1);
            Add();
            Stloc(_textposV);
            Callvirt(_getcharM);
        }

        /*
         * Loads the char to the left of the current position
         */
        internal void Leftchar() {
            Ldloc(_textV);
            Ldloc(_textposV);
            Ldc(1);
            Sub();
            Callvirt(_getcharM);
        }

        /*
         * Loads the char to the left of the current position and advances (leftward)
         */
        internal void Leftcharnext() {
            Ldloc(_textV);
            Ldloc(_textposV);
            Ldc(1);
            Sub();
            Dup();
            Stloc(_textposV);
            Callvirt(_getcharM);
        }

        /*
         * Creates a backtrack note and pushes the switch index it on the tracking stack
         */
        internal void Track() {
            ReadyPushTrack();
            Ldc(AddTrack());
            DoPush();
        }

        /*
         * Pushes the current switch index on the tracking stack so the backtracking
         * logic will be repeated again next time we backtrack here.
         *
         * <

*/
        internal void Trackagain() {
            ReadyPushTrack();
            Ldc(_backpos);
            DoPush();
        }

        /*
         * Saves the value of a local variable on the tracking stack
         */
        internal void PushTrack(LocalBuilder lt) {
            ReadyPushTrack();
            Ldloc(lt);
            DoPush();
        }

        /*
         * Creates a backtrack note for a piece of code that should only be generated once,
         * and emits code that pushes the switch index on the backtracking stack.
         */
        internal void TrackUnique(int i) {
            ReadyPushTrack();
            Ldc(AddUniqueTrack(i));
            DoPush();
        }

        /*
         * Creates a second-backtrack note for a piece of code that should only be
         * generated once, and emits code that pushes the switch index on the
         * backtracking stack.
         */
        internal void TrackUnique2(int i) {
            ReadyPushTrack();
            Ldc(AddUniqueTrack(i, RegexCode.Back2));
            DoPush();
        }

        /*
         * Prologue to code that will push an element on the tracking stack
         */
        internal void ReadyPushTrack() {
            _ilg.Emit(OpCodes.Ldloc_S, _trackV);
            _ilg.Emit(OpCodes.Ldloc_S, _trackposV);
            _ilg.Emit(OpCodes.Ldc_I4_1);
            _ilg.Emit(OpCodes.Sub);
            _ilg.Emit(OpCodes.Dup);
            _ilg.Emit(OpCodes.Stloc_S, _trackposV);
        }

        /*
         * Pops an element off the tracking stack (leave it on the operand stack)
         */
        internal void PopTrack() {
            _ilg.Emit(OpCodes.Ldloc_S, _trackV);
            _ilg.Emit(OpCodes.Ldloc_S, _trackposV);
            _ilg.Emit(OpCodes.Dup);
            _ilg.Emit(OpCodes.Ldc_I4_1);
            _ilg.Emit(OpCodes.Add);
            _ilg.Emit(OpCodes.Stloc_S, _trackposV);
            _ilg.Emit(OpCodes.Ldelem_I4);
        }

        /*
         * Retrieves the top entry on the tracking stack without popping
         */
        internal void TopTrack() {
            _ilg.Emit(OpCodes.Ldloc_S, _trackV);
            _ilg.Emit(OpCodes.Ldloc_S, _trackposV);
            _ilg.Emit(OpCodes.Ldelem_I4);
        }

        /*
         * Saves the value of a local variable on the grouping stack
         */
        internal void PushStack(LocalBuilder lt) {
            ReadyPushStack();
            _ilg.Emit(OpCodes.Ldloc_S, lt);
            DoPush();
        }

        /*
         * Prologue to code that will replace the ith element on the grouping stack
         */
        internal void ReadyReplaceStack(int i) {
            _ilg.Emit(OpCodes.Ldloc_S, _stackV);
            _ilg.Emit(OpCodes.Ldloc_S, _stackposV);
            if (i != 0) {
                Ldc(i);
                _ilg.Emit(OpCodes.Add);
            }
        }

        /*
         * Prologue to code that will push an element on the grouping stack
         */
        internal void ReadyPushStack() {
            _ilg.Emit(OpCodes.Ldloc_S, _stackV);
            _ilg.Emit(OpCodes.Ldloc_S, _stackposV);
            _ilg.Emit(OpCodes.Ldc_I4_1);
            _ilg.Emit(OpCodes.Sub);
            _ilg.Emit(OpCodes.Dup);
            _ilg.Emit(OpCodes.Stloc_S, _stackposV);
        }

        /*
         * Retrieves the top entry on the stack without popping
         */
        internal void TopStack() {
            _ilg.Emit(OpCodes.Ldloc_S, _stackV);
            _ilg.Emit(OpCodes.Ldloc_S, _stackposV);
            _ilg.Emit(OpCodes.Ldelem_I4);
        }

        /*
         * Pops an element off the grouping stack (leave it on the operand stack)
         */
        internal void PopStack() {
            _ilg.Emit(OpCodes.Ldloc_S, _stackV);
            _ilg.Emit(OpCodes.Ldloc_S, _stackposV);
            _ilg.Emit(OpCodes.Dup);
            _ilg.Emit(OpCodes.Ldc_I4_1);
            _ilg.Emit(OpCodes.Add);
            _ilg.Emit(OpCodes.Stloc_S, _stackposV);
            _ilg.Emit(OpCodes.Ldelem_I4);
        }

        /*
         * Pops 1 element off the grouping stack and discards it
         */
        internal void PopDiscardStack() {
            PopDiscardStack(1);
        }

        /*
         * Pops i elements off the grouping stack and discards them
         */
        internal void PopDiscardStack(int i) {
            _ilg.Emit(OpCodes.Ldloc_S, _stackposV);
            Ldc(i);
            _ilg.Emit(OpCodes.Add);
            _ilg.Emit(OpCodes.Stloc_S, _stackposV);
        }

        /*
         * Epilogue to code that will replace an element on a stack (use Ld* in between)
         */
        internal void DoReplace() {
            _ilg.Emit(OpCodes.Stelem_I4);
        }

        /*
         * Epilogue to code that will push an element on a stack (use Ld* in between)
         */
        internal void DoPush() {
            _ilg.Emit(OpCodes.Stelem_I4);
        }

        /*
         * Jump to the backtracking switch
         */
        internal void Back() {
            _ilg.Emit(OpCodes.Br, _backtrack);
        }

        /*
         * Branch to the MSIL corresponding to the regex code at i
         *
         * A trick: since track and stack space is gobbled up unboundedly
         * only as a result of branching backwards, this is where we check
         * for sufficient space and trigger reallocations.
         *
         * If the "goto" is backwards, we generate code that checks
         * available space against the amount of space that would be needed
         * in the worst case by code that will only go forward; if there's
         * not enough, we push the destination on the tracking stack, then
         * we jump to the place where we invoke the allocator.
         *
         * Since forward gotos pose no threat, they just turn into a Br.
         */
        internal void Goto(int i) {
            if (i < _codepos) {
                Label l1 = DefineLabel();

                // When going backwards, ensure enough space.
                Ldloc(_trackposV);
                Ldc(_trackcount * 4);
                Ble(l1);
                Ldloc(_stackposV);
                Ldc(_trackcount * 3);
                BgtFar(_labels[i]);
                MarkLabel(l1); 
                ReadyPushTrack();
                Ldc(AddGoto(i));
                DoPush();
                BrFar(_backtrack);
            }
            else {
                BrFar(_labels[i]);
            }
        }

        /*
         * Returns the position of the next operation in the regex code, taking
         * into account the different numbers of arguments taken by operations
         */
        internal int NextCodepos() {
            return _codepos + RegexCode.OpcodeSize(_codes[_codepos]);
        }

        /*
         * The label for the next (forward) operation
         */
        internal Label AdvanceLabel() {
            return _labels[NextCodepos()];
        }

        /*
         * Goto the next (forward) operation
         */
        internal void Advance() {
            _ilg.Emit(OpCodes.Br, AdvanceLabel());
        }

        internal void CallToLower()
        {
            if ((_options & RegexOptions.CultureInvariant) != 0)
                Call(_getInvariantCulture);
            else
                Call(_getCurrentCulture);
            
            Call(_chartolowerM);
        }

        /*
         * Generates the first section of the MSIL. This section contains all
         * the forward logic, and corresponds directly to the regex codes.
         *
         * In the absence of backtracking, this is all we would need.
         */
        internal void GenerateForwardSection() {
            int codepos;

            _labels = new Label[_codes.Length];
            _goto   = new int[_codes.Length];

            // initialize

            for (codepos = 0; codepos < _codes.Length; codepos += RegexCode.OpcodeSize(_codes[codepos])) {
                _goto[codepos]   = -1;
                _labels[codepos] = _ilg.DefineLabel();
            }

            _uniquenote   = new int[uniquecount];
            for (int i = 0; i < uniquecount; i++)
                _uniquenote[i] = -1;

            // emit variable initializers

            Mvfldloc(_textF,      _textV);
            Mvfldloc(_textstartF, _textstartV);
            Mvfldloc(_textbegF,   _textbegV);
            Mvfldloc(_textendF,   _textendV);
            Mvfldloc(_textposF,   _textposV);
            Mvfldloc(_trackF,     _trackV);
            Mvfldloc(_trackposF,  _trackposV);
            Mvfldloc(_stackF,     _stackV);
            Mvfldloc(_stackposF,  _stackposV);

            _backpos = -1;

            for (codepos = 0; codepos < _codes.Length; codepos += RegexCode.OpcodeSize(_codes[codepos])) {
                MarkLabel(_labels[codepos]);
                _codepos = codepos;
                _regexopcode = _codes[codepos];
                GenerateOneCode();
            }
        }

        /*
         * Generates the middle section of the MSIL. This section contains the
         * big switch jump that allows us to simulate a stack of addresses,
         * and it also contains the calls that expand the tracking and the
         * grouping stack when they get too full.
         */
        internal void GenerateMiddleSection() {
#pragma warning disable 219
            Label l1 = DefineLabel();
#pragma warning restore 219
            Label[] table;
            int i;

            // Backtrack switch
            MarkLabel(_backtrack);

            // first call EnsureStorage 
            Mvlocfld(_trackposV, _trackposF);
            Mvlocfld(_stackposV, _stackposF);
            Ldthis();
            Callvirt(_ensurestorageM);
            Mvfldloc(_trackposF, _trackposV);
            Mvfldloc(_stackposF, _stackposV);
            Mvfldloc(_trackF, _trackV);
            Mvfldloc(_stackF, _stackV);


            PopTrack();

            table = new Label[_notecount];
            for (i = 0; i < _notecount; i++)
                table[i] = _notes[i]._label;

            _ilg.Emit(OpCodes.Switch, table);

        }

        /*
         * Generates the last section of the MSIL. This section contains all of
         * the backtracking logic.
         */
        internal void GenerateBacktrackSection() {
            int i;

            for (i = 0; i < _notecount; i++) {
                BacktrackNote n = _notes[i];
                if (n._flags != 0) {
                    _ilg.MarkLabel(n._label);
                    _codepos = n._codepos;
                    _backpos = i;
                    _regexopcode = _codes[n._codepos] | n._flags;
                    GenerateOneCode();
                }
            }
        }

        /*
         * Generates FindFirstChar
         */
        // !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
        // !!!! This function must be kept synchronized with FindFirstChar in      !!!!
        // !!!! RegexInterpreter.cs                                                !!!!
        // !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
        internal void GenerateFindFirstChar() {
            _textposV       = DeclareInt();
            _textV          = DeclareString();
            _tempV          = DeclareInt();
            _temp2V         = DeclareInt();

            if (0 != (_anchors & (RegexFCD.Beginning | RegexFCD.Start | RegexFCD.EndZ | RegexFCD.End))) {
                if (!_code._rightToLeft) {
                    if (0 != (_anchors & RegexFCD.Beginning)) {
                        Label l1 = DefineLabel();
                        Ldthisfld(_textposF);
                        Ldthisfld(_textbegF);
                        Ble(l1);
                        Ldthis();
                        Ldthisfld(_textendF);
                        Stfld(_textposF);
                        Ldc(0);
                        Ret();
                        MarkLabel(l1);
                    }

                    if (0 != (_anchors & RegexFCD.Start)) {
                        Label l1 = DefineLabel();
                        Ldthisfld(_textposF);
                        Ldthisfld(_textstartF);
                        Ble(l1);
                        Ldthis();
                        Ldthisfld(_textendF);
                        Stfld(_textposF);
                        Ldc(0);
                        Ret();
                        MarkLabel(l1);
                    }

                    if (0 != (_anchors & RegexFCD.EndZ)) {
                        Label l1 = DefineLabel();
                        Ldthisfld(_textposF);
                        Ldthisfld(_textendF);
                        Ldc(1);
                        Sub();
                        Bge(l1);
                        Ldthis();
                        Ldthisfld(_textendF);
                        Ldc(1);
                        Sub();
                        Stfld(_textposF);
                        MarkLabel(l1);
                    }

                    if (0 != (_anchors & RegexFCD.End)) {
                        Label l1 = DefineLabel();
                        Ldthisfld(_textposF);
                        Ldthisfld(_textendF);
                        Bge(l1);
                        Ldthis();
                        Ldthisfld(_textendF);
                        Stfld(_textposF);
                        MarkLabel(l1);
                    }
                }
                else {
                    if (0 != (_anchors & RegexFCD.End)) {
                        Label l1 = DefineLabel();
                        Ldthisfld(_textposF);
                        Ldthisfld(_textendF);
                        Bge(l1);
                        Ldthis();
                        Ldthisfld(_textbegF);
                        Stfld(_textposF);
                        Ldc(0);
                        Ret();
                        MarkLabel(l1);
                    }

                    if (0 != (_anchors & RegexFCD.EndZ)) {
                        Label l1 = DefineLabel();
                        Label l2 = DefineLabel();
                        Ldthisfld(_textposF);
                        Ldthisfld(_textendF);
                        Ldc(1);
                        Sub();
                        Blt(l1);
                        Ldthisfld(_textposF);
                        Ldthisfld(_textendF);
                        Beq(l2);
                        Ldthisfld(_textF);
                        Ldthisfld(_textposF);
                        Callvirt(_getcharM);
                        Ldc((int)'\n');
                        Beq(l2);
                        MarkLabel(l1);
                        Ldthis();
                        Ldthisfld(_textbegF);
                        Stfld(_textposF);
                        Ldc(0);
                        Ret();
                        MarkLabel(l2);
                    }

                    if (0 != (_anchors & RegexFCD.Start)) {
                        Label l1 = DefineLabel();
                        Ldthisfld(_textposF);
                        Ldthisfld(_textstartF);
                        Bge(l1);
                        Ldthis();
                        Ldthisfld(_textbegF);
                        Stfld(_textposF);
                        Ldc(0);
                        Ret();
                        MarkLabel(l1);
                    }

                    if (0 != (_anchors & RegexFCD.Beginning)) {
                        Label l1 = DefineLabel();
                        Ldthisfld(_textposF);
                        Ldthisfld(_textbegF);
                        Ble(l1);
                        Ldthis();
                        Ldthisfld(_textbegF);
                        Stfld(_textposF);
                        MarkLabel(l1);
                    }
                }

                // <


                Ldc(1);
                Ret();
            }
            else if (_bmPrefix != null && _bmPrefix._negativeUnicode == null) {
                // Compiled Boyer-Moore string matching
                // <


                LocalBuilder chV      = _tempV;
                LocalBuilder testV    = _tempV;
                LocalBuilder limitV   = _temp2V;
                Label      lDefaultAdvance  = DefineLabel();
                Label      lAdvance         = DefineLabel();
                Label      lFail            = DefineLabel();
                Label      lStart           = DefineLabel();
#pragma warning disable 219
                Label      lOutOfRange      = DefineLabel();
#pragma warning restore 219
                Label      lPartialMatch    = DefineLabel();


                int chLast;
                int i;
                int beforefirst;
                int last;
                Label[] table;

                if (!_code._rightToLeft) {
                    beforefirst = -1;
                    last = _bmPrefix._pattern.Length - 1;
                }
                else {
                    beforefirst = _bmPrefix._pattern.Length;
                    last = 0;
                }

                chLast = _bmPrefix._pattern[last];

                Mvfldloc(_textF, _textV);
                if (!_code._rightToLeft)
                    Ldthisfld(_textendF);
                else
                    Ldthisfld(_textbegF);
                Stloc(limitV);

                Ldthisfld(_textposF);
                if (!_code._rightToLeft) {
                    Ldc(_bmPrefix._pattern.Length - 1);
                    Add();
                }
                else {
                    Ldc(_bmPrefix._pattern.Length);
                    Sub();
                }
                Stloc(_textposV);
                Br(lStart);

                MarkLabel(lDefaultAdvance);

                if (!_code._rightToLeft)
                    Ldc(_bmPrefix._pattern.Length);
                else
                    Ldc(-_bmPrefix._pattern.Length);

                MarkLabel(lAdvance);

                Ldloc(_textposV);
                Add();
                Stloc(_textposV);

                MarkLabel(lStart);

                Ldloc(_textposV);
                Ldloc(limitV);
                if (!_code._rightToLeft)
                    BgeFar(lFail);
                else
                    BltFar(lFail);

                Rightchar();
                if (_bmPrefix._caseInsensitive)
                    CallToLower();

                Dup();
                Stloc(chV);
                Ldc(chLast);
                BeqFar(lPartialMatch);

                Ldloc(chV);
                Ldc(_bmPrefix._lowASCII);
                Sub();
                Dup();
                Stloc(chV);
                Ldc(_bmPrefix._highASCII - _bmPrefix._lowASCII);
                Bgtun(lDefaultAdvance);

                table = new Label[_bmPrefix._highASCII - _bmPrefix._lowASCII + 1];

                for (i = _bmPrefix._lowASCII; i <= _bmPrefix._highASCII; i++) {
                    if (_bmPrefix._negativeASCII[i] == beforefirst)
                        table[i - _bmPrefix._lowASCII] = lDefaultAdvance;
                    else
                        table[i - _bmPrefix._lowASCII] = DefineLabel();
                }

                Ldloc(chV);
                _ilg.Emit(OpCodes.Switch, table);

                for (i = _bmPrefix._lowASCII; i <= _bmPrefix._highASCII; i++) {
                    if (_bmPrefix._negativeASCII[i] == beforefirst)
                        continue;

                    MarkLabel(table[i - _bmPrefix._lowASCII]);

                    Ldc(_bmPrefix._negativeASCII[i]);
                    BrFar(lAdvance);
                }

                MarkLabel(lPartialMatch);

                Ldloc(_textposV);
                Stloc(testV);

                for (i = _bmPrefix._pattern.Length - 2; i >= 0; i--) {
                    Label lNext = DefineLabel();
                    int charindex;

                    if (!_code._rightToLeft)
                        charindex = i;
                    else
                        charindex = _bmPrefix._pattern.Length - 1 - i;

                    Ldloc(_textV);
                    Ldloc(testV);
                    Ldc(1);
                    Sub(_code._rightToLeft);
                    Dup();
                    Stloc(testV);
                    Callvirt(_getcharM);
                    if (_bmPrefix._caseInsensitive)
                        CallToLower();
                    
                    Ldc(_bmPrefix._pattern[charindex]);
                    Beq(lNext);
                    Ldc(_bmPrefix._positive[charindex]);
                    BrFar(lAdvance);

                    MarkLabel(lNext);

                }

                Ldthis();
                Ldloc(testV);
                if (_code._rightToLeft) {
                    Ldc(1);
                    Add();
                }
                Stfld(_textposF);
                Ldc(1);
                Ret();

                MarkLabel(lFail);

                Ldthis();
                if (!_code._rightToLeft)
                    Ldthisfld(_textendF);
                else
                    Ldthisfld(_textbegF);
                Stfld(_textposF);
                Ldc(0);
                Ret();
            }
            else if (_fcPrefix == null) {
                Ldc(1);
                Ret();
            }
            else {
                LocalBuilder cV   = _temp2V;
#pragma warning disable 219
                LocalBuilder chV  = _tempV;
#pragma warning restore 219
                Label      l1   = DefineLabel();
                Label      l2   = DefineLabel();
                Label      l3   = DefineLabel();
                Label      l4   = DefineLabel();
                Label      l5   = DefineLabel();

                Mvfldloc(_textposF, _textposV);
                Mvfldloc(_textF, _textV);

                if (!_code._rightToLeft) {
                    Ldthisfld(_textendF);
                    Ldloc(_textposV);
                }
                else {
                    Ldloc(_textposV);
                    Ldthisfld(_textbegF);
                }
                Sub();
                Stloc(cV);

                Ldloc(cV);
                Ldc(0);
                BleFar(l4);

                MarkLabel(l1);

                Ldloc(cV);
                Ldc(1);
                Sub();
                Stloc(cV);

                if (_code._rightToLeft)
                    Leftcharnext();
                else
                    Rightcharnext();

                if (_fcPrefix.CaseInsensitive)
                    CallToLower();
                
                if (!RegexCharClass.IsSingleton(_fcPrefix.Prefix)) {
                    Ldstr(_fcPrefix.Prefix);
                    Call(_charInSetM);

                    BrtrueFar(l2);
                }
                else {
                    Ldc(RegexCharClass.SingletonChar(_fcPrefix.Prefix));
                    Beq(l2);
                }

                MarkLabel(l5);

                Ldloc(cV);
                Ldc(0);
                if (!RegexCharClass.IsSingleton(_fcPrefix.Prefix))
                    BgtFar(l1);
                else
                    Bgt(l1);

                Ldc(0);
                BrFar(l3);

                MarkLabel(l2);

                /*          // CURRENTLY DISABLED
                            // If for some reason we have a prefix we didn't use, use it now.
                
                            if (_bmPrefix != null) {
                                if (!_code._rightToLeft) {
                                    Ldthisfld(_textendF);
                                    Ldloc(_textposV);
                                }
                                else {
                                    Ldloc(_textposV);
                                    Ldthisfld(_textbegF);
                                }
                                Sub();
                                Ldc(_bmPrefix._pattern.Length - 1);
                                BltFar(l5);
                                
                                for (int i = 1; i < _bmPrefix._pattern.Length; i++) {
                                    Ldloc(_textV);
                                    Ldloc(_textposV);
                                    if (!_code._rightToLeft) {
                                        Ldc(i - 1);
                                        Add();
                                    }
                                    else {
                                        Ldc(i);
                                        Sub();
                                    }
                                    Callvirt(_getcharM);
                                    if (!_code._rightToLeft)
                                        Ldc(_bmPrefix._pattern[i]);
                                    else
                                        Ldc(_bmPrefix._pattern[_bmPrefix._pattern.Length - 1 - i]);
                                    BneFar(l5);
                                }
                            }
                */

                Ldloc(_textposV);
                Ldc(1);
                Sub(_code._rightToLeft);
                Stloc(_textposV);
                Ldc(1);

                MarkLabel(l3);

                Mvlocfld(_textposV, _textposF);
                Ret();

                MarkLabel(l4);
                Ldc(0);
                Ret();
            }

        }

        /*
         * Generates a very simple method that sets the _trackcount field.
         */
        internal void GenerateInitTrackCount() {
            Ldthis();
            Ldc(_trackcount);
            Stfld(_trackcountF);
            Ret();
        }

        /*
         * Declares a local int
         */
        internal LocalBuilder DeclareInt() {
            return _ilg.DeclareLocal(typeof(int));            
        }

        /*
         * Declares a local int array
         */
        internal LocalBuilder DeclareIntArray() {
            return _ilg.DeclareLocal(typeof(int[]));
        }

        /*
         * Declares a local string
         */
        internal LocalBuilder DeclareString() {
            return _ilg.DeclareLocal(typeof(string));
        }
        
        /*
         * Generates the code for "RegexRunner.Go"
         */
        internal void GenerateGo() {
            // declare some locals

            _textposV       = DeclareInt();
            _textV          = DeclareString();
            _trackposV      = DeclareInt();
            _trackV         = DeclareIntArray();
            _stackposV      = DeclareInt();
            _stackV         = DeclareIntArray();
            _tempV          = DeclareInt();
            _temp2V         = DeclareInt();
            _temp3V         = DeclareInt();
            _textbegV       = DeclareInt();
            _textendV       = DeclareInt();
            _textstartV     = DeclareInt();

            // clear some tables

            _labels = null;
            _notes = null;
            _notecount = 0;

            // globally used labels

            _backtrack = DefineLabel();

            // emit the code!

            GenerateForwardSection();
            GenerateMiddleSection();
            GenerateBacktrackSection();
        }

#if DBG
        /*
         * Some simple debugging stuff
         */
        internal static MethodInfo _debugWriteLine = typeof(Debug).GetMethod("WriteLine", new Type[] {typeof(string)});

        /*
         * Debug only: emit code to print out a message
         */
        internal void Message(String str) {
            Ldstr(str);
            Call(_debugWriteLine);
        }

#endif

        /*
         * The main translation function. It translates the logic for a single opcode at
         * the current position. The structure of this function exactly mirrors
         * the structure of the inner loop of RegexInterpreter.Go().
         *
         * The C# code from RegexInterpreter.Go() that corresponds to each case is
         * included as a comment.
         *
         * Note that since we're generating code, we can collapse many cases that are
         * dealt with one-at-a-time in RegexIntepreter. We can also unroll loops that
         * iterate over constant strings or sets.
         */
        internal void GenerateOneCode() {
#if DBG
            if ((_options & RegexOptions.Debug) != 0) {
                Mvlocfld(_textposV, _textposF);
                Mvlocfld(_trackposV, _trackposF);
                Mvlocfld(_stackposV, _stackposF);
                Ldthis();
                Callvirt(_dumpstateM);
                StringBuilder sb = new StringBuilder();
                if (_backpos > 0)
                    sb.AppendFormat("{0:D6} ", _backpos);
                else
                    sb.Append("       ");
                sb.Append(_code.OpcodeDescription(_codepos));
                if (IsBack())
                    sb.Append(" Back");
                if (IsBack2())
                    sb.Append(" Back2");
                Message(sb.ToString());
            }
#endif

            // Before executing any RegEx code in the unrolled loop,
            // we try checking for the match timeout:

            Ldthis();            
            Callvirt(_checkTimeoutM);

            // Now generate the IL for the RegEx code saved in _regexopcode.
            // We unroll the loop done by the RegexCompiler creating as very long method
            // that is longer if the pattern is longer:

            switch (_regexopcode) {
                case RegexCode.Stop:
                    //: return;
                    Mvlocfld(_textposV, _textposF);       // update _textpos
                    Ret();
                    break;

                case RegexCode.Nothing:
                    //: break Backward;
                    Back();
                    break;

                case RegexCode.Goto:
                    //: Goto(Operand(0));
                    Goto(Operand(0));
                    break;

                case RegexCode.Testref:
                    //: if (!_match.IsMatched(Operand(0)))
                    //:     break Backward;
                    Ldthis();
                    Ldc(Operand(0));
                    Callvirt(_ismatchedM);
                    BrfalseFar(_backtrack);
                    break;

                case RegexCode.Lazybranch:
                    //: Track(Textpos());
                    PushTrack(_textposV);
                    Track();
                    break;

                case RegexCode.Lazybranch | RegexCode.Back:
                    //: Trackframe(1);
                    //: Textto(Tracked(0));
                    //: Goto(Operand(0));
                    PopTrack();
                    Stloc(_textposV);
                    Goto(Operand(0));
                    break;

                case RegexCode.Nullmark:
                    //: Stack(-1);
                    //: Track();
                    ReadyPushStack();
                    Ldc(-1);
                    DoPush();
                    TrackUnique(stackpop);
                    break;

                case RegexCode.Setmark:
                    //: Stack(Textpos());
                    //: Track();
                    PushStack(_textposV);
                    TrackUnique(stackpop);
                    break;

                case RegexCode.Nullmark | RegexCode.Back:
                case RegexCode.Setmark | RegexCode.Back:
                    //: Stackframe(1);
                    //: break Backward;
                    PopDiscardStack();
                    Back();
                    break;

                case RegexCode.Getmark:
                    //: Stackframe(1);
                    //: Track(Stacked(0));
                    //: Textto(Stacked(0));
                    ReadyPushTrack();
                    PopStack();
                    Dup();
                    Stloc(_textposV);
                    DoPush();

                    Track();
                    break;

                case RegexCode.Getmark | RegexCode.Back:
                    //: Trackframe(1);
                    //: Stack(Tracked(0));
                    //: break Backward;
                    ReadyPushStack();
                    PopTrack();
                    DoPush();
                    Back();
                    break;

                case RegexCode.Capturemark:
                    //: if (!IsMatched(Operand(1)))
                    //:     break Backward;
                    //: Stackframe(1);
                    //: if (Operand(1) != -1)
                    //:     TransferCapture(Operand(0), Operand(1), Stacked(0), Textpos());
                    //: else
                    //:     Capture(Operand(0), Stacked(0), Textpos());
                    //: Track(Stacked(0));

                    //: Stackframe(1);
                    //: Capture(Operand(0), Stacked(0), Textpos());
                    //: Track(Stacked(0));

                    if (Operand(1) != -1) {
                        Ldthis();
                        Ldc(Operand(1));
                        Callvirt(_ismatchedM);
                        BrfalseFar(_backtrack);
                    }

                    PopStack();
                    Stloc(_tempV);

                    if (Operand(1) != -1) {
                        Ldthis();
                        Ldc(Operand(0));
                        Ldc(Operand(1));
                        Ldloc(_tempV);
                        Ldloc(_textposV);
                        Callvirt(_transferM);
                    }
                    else {
                        Ldthis();
                        Ldc(Operand(0));
                        Ldloc(_tempV);
                        Ldloc(_textposV);
                        Callvirt(_captureM);
                    }

                    PushTrack(_tempV);

                    if (Operand(0) != -1 && Operand(1) != -1)
                        TrackUnique(capback2);
                    else
                        TrackUnique(capback);

                    break;


                case RegexCode.Capturemark | RegexCode.Back:
                    //: Trackframe(1);
                    //: Stack(Tracked(0));
                    //: Uncapture();
                    //: if (Operand(0) != -1 && Operand(1) != -1)
                    //:     Uncapture();
                    //: break Backward;
                    ReadyPushStack();
                    PopTrack();
                    DoPush();
                    Ldthis();
                    Callvirt(_uncaptureM);
                    if (Operand(0) != -1 && Operand(1) != -1) {
                        Ldthis();
                        Callvirt(_uncaptureM);
                    }
                    Back();
                    break;

                case RegexCode.Branchmark:
                    //: Stackframe(1);
                    //: 
                    //: if (Textpos() != Stacked(0))
                    //: {                                   // Nonempty match -> loop now
                    //:     Track(Stacked(0), Textpos());   // Save old mark, textpos
                    //:     Stack(Textpos());               // Make new mark
                    //:     Goto(Operand(0));               // Loop
                    //: }
                    //: else
                    //: {                                   // Empty match -> straight now
                    //:     Track2(Stacked(0));             // Save old mark
                    //:     Advance(1);                     // Straight
                    //: }
                    //: continue Forward;
                    {
                        LocalBuilder mark = _tempV;
                        Label      l1   = DefineLabel();

                        PopStack();
                        Dup();
                        Stloc(mark);                            // Stacked(0) -> temp
                        PushTrack(mark);
                        Ldloc(_textposV);
                        Beq(l1);                                // mark == textpos -> branch

                        // (matched != 0)

                        PushTrack(_textposV);
                        PushStack(_textposV);
                        Track();
                        Goto(Operand(0));                       // Goto(Operand(0))

                        // else

                        MarkLabel(l1);
                        TrackUnique2(branchmarkback2);
                        break;
                    }

                case RegexCode.Branchmark | RegexCode.Back:
                    //: Trackframe(2);
                    //: Stackframe(1);
                    //: Textto(Tracked(1));                     // Recall position
                    //: Track2(Tracked(0));                     // Save old mark
                    //: Advance(1);
                    PopTrack();
                    Stloc(_textposV);
                    PopStack();
                    Pop();
                    // track spot 0 is already in place
                    TrackUnique2(branchmarkback2);
                    Advance();
                    break;

                case RegexCode.Branchmark | RegexCode.Back2:
                    //: Trackframe(1);
                    //: Stack(Tracked(0));                      // Recall old mark
                    //: break Backward;                         // Backtrack
                    ReadyPushStack();
                    PopTrack();
                    DoPush();
                    Back();
                    break;


                case RegexCode.Lazybranchmark:
                    //: StackPop();
                    //: int oldMarkPos = StackPeek();
                    //: 
                    //: if (Textpos() != oldMarkPos) {         // Nonempty match -> next loop
                    //: {                                   // Nonempty match -> next loop
                    //:     if (oldMarkPos != -1)
                    //:         Track(Stacked(0), Textpos());   // Save old mark, textpos
                    //:     else
                    //:         TrackPush(Textpos(), Textpos());   
                    //: }
                    //: else
                    //: {                                   // Empty match -> no loop
                    //:     Track2(Stacked(0));             // Save old mark
                    //: }
                    //: Advance(1);
                    //: continue Forward;
                    {
                        LocalBuilder mark = _tempV;
                        Label      l1   = DefineLabel();
                        Label      l2   = DefineLabel();
                        Label      l3   = DefineLabel();

                        PopStack();
                        Dup();
                        Stloc(mark);                      // Stacked(0) -> temp

                        // if (oldMarkPos != -1)
                        Ldloc(mark);
                        Ldc(-1);
                        Beq(l2);                                // mark == -1 -> branch
                            PushTrack(mark);
                            Br(l3);
                        // else
                            MarkLabel(l2);
                            PushTrack(_textposV);
                        MarkLabel(l3);
                            
                        // if (Textpos() != mark)
                        Ldloc(_textposV);
                        Beq(l1);                                // mark == textpos -> branch
                            PushTrack(_textposV);
                            Track();
                            Br(AdvanceLabel());                 // Advance (near)
                        // else
                            MarkLabel(l1);
                            ReadyPushStack();                   // push the current textPos on the stack. 
							        // May be ignored by 'back2' or used by a true empty match.
                            Ldloc(mark);                        

                            DoPush();
                            TrackUnique2(lazybranchmarkback2);

                        break;
                    }

                case RegexCode.Lazybranchmark | RegexCode.Back:
                    //: Trackframe(2);
                    //: Track2(Tracked(0));                     // Save old mark
                    //: Stack(Textpos());                       // Make new mark
                    //: Textto(Tracked(1));                     // Recall position
                    //: Goto(Operand(0));                       // Loop

                    PopTrack();
                    Stloc(_textposV);
                    PushStack(_textposV);
                    TrackUnique2(lazybranchmarkback2);
                    Goto(Operand(0));
                    break;

                case RegexCode.Lazybranchmark | RegexCode.Back2:
                    //: Stackframe(1);
                    //: Trackframe(1);
                    //: Stack(Tracked(0));                  // Recall old mark
                    //: break Backward;
                    ReadyReplaceStack(0);
                    PopTrack();
                    DoReplace();
                    Back();
                    break;

                case RegexCode.Nullcount:
                    //: Stack(-1, Operand(0));
                    //: Track();
                    ReadyPushStack();
                    Ldc(-1);
                    DoPush();
                    ReadyPushStack();
                    Ldc(Operand(0));
                    DoPush();
                    TrackUnique(stackpop2);
                    break;

                case RegexCode.Setcount:
                    //: Stack(Textpos(), Operand(0));
                    //: Track();
                    PushStack(_textposV);
                    ReadyPushStack();
                    Ldc(Operand(0));
                    DoPush();
                    TrackUnique(stackpop2);
                    break;


                case RegexCode.Nullcount | RegexCode.Back:
                case RegexCode.Setcount | RegexCode.Back:
                    //: Stackframe(2);
                    //: break Backward;
                    PopDiscardStack(2);
                    Back();
                    break;


                case RegexCode.Branchcount:
                    //: Stackframe(2);
                    //: int mark = Stacked(0);
                    //: int count = Stacked(1);
                    //: 
                    //: if (count >= Operand(1) || Textpos() == mark && count >= 0)
                    //: {                                   // Max loops or empty match -> straight now
                    //:     Track2(mark, count);            // Save old mark, count
                    //:     Advance(2);                     // Straight
                    //: }
                    //: else
                    //: {                                   // Nonempty match -> count+loop now
                    //:     Track(mark);                    // remember mark
                    //:     Stack(Textpos(), count + 1);    // Make new mark, incr count
                    //:     Goto(Operand(0));               // Loop
                    //: }
                    //: continue Forward;
                    {
                        LocalBuilder count = _tempV;
                        LocalBuilder mark  = _temp2V;
                        Label      l1    = DefineLabel();
                        Label      l2    = DefineLabel();

                        PopStack();
                        Stloc(count);                           // count -> temp
                        PopStack();
                        Dup();
                        Stloc(mark);                            // mark -> temp2
                        PushTrack(mark);

                        Ldloc(_textposV);
                        Bne(l1);                                // mark != textpos -> l1
                        Ldloc(count);
                        Ldc(0);
                        Bge(l2);                                // count >= 0 && mark == textpos -> l2

                        MarkLabel(l1);
                        Ldloc(count);
                        Ldc(Operand(1));
                        Bge(l2);                                // count >= Operand(1) -> l2

                        // else
                        PushStack(_textposV);
                        ReadyPushStack();
                        Ldloc(count);                           // mark already on track
                        Ldc(1);
                        Add();
                        DoPush();
                        Track();
                        Goto(Operand(0));

                        // if (count >= Operand(1) || Textpos() == mark)
                        MarkLabel(l2);
                        PushTrack(count);                       // mark already on track
                        TrackUnique2(branchcountback2);
                        break;
                    }

                case RegexCode.Branchcount | RegexCode.Back:
                    //: Trackframe(1);
                    //: Stackframe(2);
                    //: if (Stacked(1) > 0)                     // Positive -> can go straight
                    //: {
                    //:     Textto(Stacked(0));                 // Zap to mark
                    //:     Track2(Tracked(0), Stacked(1) - 1); // Save old mark, old count
                    //:     Advance(2);                         // Straight
                    //:     continue Forward;
                    //: }
                    //: Stack(Tracked(0), Stacked(1) - 1);      // recall old mark, old count
                    //: break Backward;
                    {

                        LocalBuilder count = _tempV;
                        Label      l1    = DefineLabel();
                        PopStack();
                        Ldc(1);
                        Sub();
                        Dup();
                        Stloc(count);
                        Ldc(0);
                        Blt(l1);

                        // if (count >= 0)
                        PopStack();
                        Stloc(_textposV);
                        PushTrack(count);                       // Tracked(0) is alredy on the track
                        TrackUnique2(branchcountback2);
                        Advance();

                        // else
                        MarkLabel(l1);
                        ReadyReplaceStack(0);
                        PopTrack();
                        DoReplace();
                        PushStack(count);
                        Back();
                        break;
                    }

                case RegexCode.Branchcount | RegexCode.Back2:
                    //: Trackframe(2);
                    //: Stack(Tracked(0), Tracked(1));      // Recall old mark, old count
                    //: break Backward;                     // Backtrack

                    PopTrack();
                    Stloc(_tempV);
                    ReadyPushStack();
                    PopTrack();
                    DoPush();
                    PushStack(_tempV);
                    Back();
                    break;

                case RegexCode.Lazybranchcount:
                    //: Stackframe(2);
                    //: int mark = Stacked(0);
                    //: int count = Stacked(1);
                    //:
                    //: if (count < 0)
                    //: {                                   // Negative count -> loop now
                    //:     Track2(mark);                   // Save old mark
                    //:     Stack(Textpos(), count + 1);    // Make new mark, incr count
                    //:     Goto(Operand(0));               // Loop
                    //: }
                    //: else
                    //: {                                   // Nonneg count or empty match -> straight now
                    //:     Track(mark, count, Textpos());  // Save mark, count, position
                    //: }
                    {
                        LocalBuilder count = _tempV;
                        LocalBuilder mark  = _temp2V;
                        Label      l1    = DefineLabel();
#pragma warning disable 219
                        Label      l2    = DefineLabel();
                        Label      l3    = _labels[NextCodepos()];
#pragma warning restore 219

                        PopStack();
                        Stloc(count);                           // count -> temp
                        PopStack();
                        Stloc(mark);                            // mark -> temp2

                        Ldloc(count);
                        Ldc(0);
                        Bge(l1);                                // count >= 0 -> l1

                        // if (count < 0)
                        PushTrack(mark);
                        PushStack(_textposV);
                        ReadyPushStack();
                        Ldloc(count);
                        Ldc(1);
                        Add();
                        DoPush();
                        TrackUnique2(lazybranchcountback2);
                        Goto(Operand(0));

                        // else
                        MarkLabel(l1);
                        PushTrack(mark);
                        PushTrack(count);
                        PushTrack(_textposV);
                        Track();
                        break;
                    }

                case RegexCode.Lazybranchcount | RegexCode.Back:
                    //: Trackframe(3);
                    //: int mark = Tracked(0);
                    //: int textpos = Tracked(2);
                    //: if (Tracked(1) < Operand(1) && textpos != mark)
                    //: {                                       // Under limit and not empty match -> loop
                    //:     Textto(Tracked(2));                 // Recall position
                    //:     Stack(Textpos(), Tracked(1) + 1);   // Make new mark, incr count
                    //:     Track2(Tracked(0));                 // Save old mark
                    //:     Goto(Operand(0));                   // Loop
                    //:     continue Forward;
                    //: }
                    //: else
                    //: {
                    //:     Stack(Tracked(0), Tracked(1));      // Recall old mark, count
                    //:     break Backward;                     // backtrack
                    //: }
                    {
                        Label       l1 = DefineLabel();
                        LocalBuilder  cV = _tempV;
                        PopTrack();
                        Stloc(_textposV);
                        PopTrack();
                        Dup();
                        Stloc(cV);
                        Ldc(Operand(1));
                        Bge(l1);                                // Tracked(1) >= Operand(1) -> l1

                        Ldloc(_textposV);
                        TopTrack();
                        Beq(l1);                                // textpos == mark -> l1

                        PushStack(_textposV);
                        ReadyPushStack();
                        Ldloc(cV);
                        Ldc(1);
                        Add();
                        DoPush();
                        TrackUnique2(lazybranchcountback2);
                        Goto(Operand(0));

                        MarkLabel(l1);
                        ReadyPushStack();
                        PopTrack();
                        DoPush();
                        PushStack(cV);
                        Back();
                        break;
                    }

                case RegexCode.Lazybranchcount | RegexCode.Back2:
                    // <





                    ReadyReplaceStack(1);
                    PopTrack();
                    DoReplace();
                    ReadyReplaceStack(0);
                    TopStack();
                    Ldc(1);
                    Sub();
                    DoReplace();
                    Back();
                    break;


                case RegexCode.Setjump:
                    //: Stack(Trackpos(), Crawlpos());
                    //: Track();
                    ReadyPushStack();
                    Ldthisfld(_trackF);
                    Ldlen();
                    Ldloc(_trackposV);
                    Sub();
                    DoPush();
                    ReadyPushStack();
                    Ldthis();
                    Callvirt(_crawlposM);
                    DoPush();
                    TrackUnique(stackpop2);
                    break;

                case RegexCode.Setjump | RegexCode.Back:
                    //: Stackframe(2);
                    PopDiscardStack(2);
                    Back();
                    break;


                case RegexCode.Backjump:
                    //: Stackframe(2);
                    //: Trackto(Stacked(0));
                    //: while (Crawlpos() != Stacked(1))
                    //:     Uncapture();
                    //: break Backward;
                    {
                        Label      l1    = DefineLabel();
                        Label      l2    = DefineLabel();

                        PopStack();
                        Ldthisfld(_trackF);
                        Ldlen();
                        PopStack();
                        Sub();
                        Stloc(_trackposV);
                        Dup();
                        Ldthis();
                        Callvirt(_crawlposM);
                        Beq(l2);

                        MarkLabel(l1);
                        Ldthis();
                        Callvirt(_uncaptureM);
                        Dup();
                        Ldthis();
                        Callvirt(_crawlposM);
                        Bne(l1);

                        MarkLabel(l2);
                        Pop();
                        Back();
                        break;
                    }

                case RegexCode.Forejump:
                    //: Stackframe(2);
                    //: Trackto(Stacked(0));
                    //: Track(Stacked(1));
                    PopStack();
                    Stloc(_tempV);
                    Ldthisfld(_trackF);
                    Ldlen();
                    PopStack();
                    Sub();
                    Stloc(_trackposV);
                    PushTrack(_tempV);
                    TrackUnique(forejumpback);
                    break;

                case RegexCode.Forejump | RegexCode.Back:
                    //: Trackframe(1);
                    //: while (Crawlpos() != Tracked(0))
                    //:     Uncapture();
                    //: break Backward;
                    {
                        Label      l1    = DefineLabel();
                        Label      l2    = DefineLabel();

                        PopTrack();

                        Dup();
                        Ldthis();
                        Callvirt(_crawlposM);
                        Beq(l2);

                        MarkLabel(l1);
                        Ldthis();
                        Callvirt(_uncaptureM);
                        Dup();
                        Ldthis();
                        Callvirt(_crawlposM);
                        Bne(l1);

                        MarkLabel(l2);
                        Pop();
                        Back();
                        break;
                    }

                case RegexCode.Bol:
                    //: if (Leftchars() > 0 && CharAt(Textpos() - 1) != '\n')
                    //:     break Backward;
                    {
                        Label      l1    = _labels[NextCodepos()];
                        Ldloc(_textposV);
                        Ldloc(_textbegV);
                        Ble(l1);
                        Leftchar();
                        Ldc((int)'\n');
                        BneFar(_backtrack);
                        break;
                    }

                case RegexCode.Eol:
                    //: if (Rightchars() > 0 && CharAt(Textpos()) != '\n')
                    //:     break Backward;
                    {
                        Label      l1    = _labels[NextCodepos()];
                        Ldloc(_textposV);
                        Ldloc(_textendV);
                        Bge(l1);
                        Rightchar();
                        Ldc((int)'\n');
                        BneFar(_backtrack);
                        break;
                    }

                case RegexCode.Boundary:
                case RegexCode.Nonboundary:
                    //: if (!IsBoundary(Textpos(), _textbeg, _textend))
                    //:     break Backward;
                    Ldthis();
                    Ldloc(_textposV);
                    Ldloc(_textbegV);
                    Ldloc(_textendV);
                    Callvirt(_isboundaryM);
                    if (Code() == RegexCode.Boundary)
                        BrfalseFar(_backtrack);
                    else
                        BrtrueFar(_backtrack);
                    break;

                case RegexCode.ECMABoundary:
                case RegexCode.NonECMABoundary:
                    //: if (!IsECMABoundary(Textpos(), _textbeg, _textend))
                    //:     break Backward;
                    Ldthis();
                    Ldloc(_textposV);
                    Ldloc(_textbegV);
                    Ldloc(_textendV);
                    Callvirt(_isECMABoundaryM);
                    if (Code() == RegexCode.ECMABoundary)
                        BrfalseFar(_backtrack);
                    else
                        BrtrueFar(_backtrack);
                    break;

                case RegexCode.Beginning:
                    //: if (Leftchars() > 0)
                    //:    break Backward;
                    Ldloc(_textposV);
                    Ldloc(_textbegV);
                    BgtFar(_backtrack);
                    break;

                case RegexCode.Start:
                    //: if (Textpos() != Textstart())
                    //:    break Backward;
                    Ldloc(_textposV);
                    Ldthisfld(_textstartF);
                    BneFar(_backtrack);
                    break;

                case RegexCode.EndZ:
                    //: if (Rightchars() > 1 || Rightchars() == 1 && CharAt(Textpos()) != '\n')
                    //:    break Backward;
                    Ldloc(_textposV);
                    Ldloc(_textendV);
                    Ldc(1);
                    Sub();
                    BltFar(_backtrack);
                    Ldloc(_textposV);
                    Ldloc(_textendV);
                    Bge(_labels[NextCodepos()]);
                    Rightchar();
                    Ldc((int)'\n');
                    BneFar(_backtrack);
                    break;

                case RegexCode.End:
                    //: if (Rightchars() > 0)
                    //:    break Backward;
                    Ldloc(_textposV);
                    Ldloc(_textendV);
                    BltFar(_backtrack);
                    break;

                case RegexCode.One:
                case RegexCode.Notone:
                case RegexCode.Set:
                case RegexCode.One      | RegexCode.Rtl:
                case RegexCode.Notone   | RegexCode.Rtl:
                case RegexCode.Set      | RegexCode.Rtl:
                case RegexCode.One      | RegexCode.Ci:
                case RegexCode.Notone   | RegexCode.Ci:
                case RegexCode.Set      | RegexCode.Ci:
                case RegexCode.One      | RegexCode.Ci  | RegexCode.Rtl:
                case RegexCode.Notone   | RegexCode.Ci  | RegexCode.Rtl:
                case RegexCode.Set      | RegexCode.Ci  | RegexCode.Rtl:

                    //: if (Rightchars() < 1 || Rightcharnext() != (char)Operand(0))
                    //:    break Backward;
                    Ldloc(_textposV);

                    if (!IsRtl()) {
                        Ldloc(_textendV);
                        BgeFar(_backtrack);
                        Rightcharnext();
                    }
                    else {
                        Ldloc(_textbegV);
                        BleFar(_backtrack);
                        Leftcharnext();
                    }

                    if (IsCi())
                        CallToLower();

                    if (Code() == RegexCode.Set) {

                        Ldstr(_strings[Operand(0)]);
                        Call(_charInSetM);

                        BrfalseFar(_backtrack);
                    }
                    else {
                        Ldc(Operand(0));
                        if (Code() == RegexCode.One)
                            BneFar(_backtrack);
                        else
                            BeqFar(_backtrack);
                    }
                    break;

                case RegexCode.Multi:
                case RegexCode.Multi | RegexCode.Ci:
                    //
                    // <






                    //: String Str = _strings[Operand(0)];
                    //: int i, c;
                    //: if (Rightchars() < (c = Str.Length))
                    //:     break Backward;
                    //: for (i = 0; c > 0; i++, c--)
                    //:     if (Str[i] != Rightcharnext())
                    //:         break Backward;
                    {
                        int i;
                        String str;

                        str = _strings[Operand(0)];

                        Ldc(str.Length);
                        Ldloc(_textendV);
                        Ldloc(_textposV);
                        Sub();
                        BgtFar(_backtrack);

                        // unroll the string
                        for (i = 0; i < str.Length; i++) {
                            Ldloc(_textV);
                            Ldloc(_textposV);
                            if (i != 0) {
                                Ldc(i);
                                Add();
                            }
                            Callvirt(_getcharM);
                            if (IsCi())
                                CallToLower();
                            
                            Ldc((int)str[i]);
                            BneFar(_backtrack);
                        }

                        Ldloc(_textposV);
                        Ldc(str.Length);
                        Add();
                        Stloc(_textposV);
                        break;
                    }


                case RegexCode.Multi | RegexCode.Rtl:
                case RegexCode.Multi | RegexCode.Ci  | RegexCode.Rtl:
                    //: String Str = _strings[Operand(0)];
                    //: int c;
                    //: if (Leftchars() < (c = Str.Length))
                    //:     break Backward;
                    //: while (c > 0)
                    //:     if (Str[--c] != Leftcharnext())
                    //:         break Backward;
                    {
                        int i;
                        String str;

                        str = _strings[Operand(0)];

                        Ldc(str.Length);
                        Ldloc(_textposV);
                        Ldloc(_textbegV);
                        Sub();
                        BgtFar(_backtrack);

                        // unroll the string
                        for (i = str.Length; i > 0;) {
                            i--;
                            Ldloc(_textV);
                            Ldloc(_textposV);
                            Ldc(str.Length - i);
                            Sub();
                            Callvirt(_getcharM);
                            if (IsCi()) 
                            {
                                CallToLower();
                            }
                            Ldc((int)str[i]);
                            BneFar(_backtrack);
                        }

                        Ldloc(_textposV);
                        Ldc(str.Length);
                        Sub();
                        Stloc(_textposV);

                        break;
                    }

                case RegexCode.Ref:
                case RegexCode.Ref | RegexCode.Rtl:
                case RegexCode.Ref | RegexCode.Ci:
                case RegexCode.Ref | RegexCode.Ci | RegexCode.Rtl:
                    //: int capnum = Operand(0);
                    //: int j, c;
                    //: if (!_match.IsMatched(capnum)) {
                    //:     if (!RegexOptions.ECMAScript)
                    //:         break Backward;
                    //: } else {
                    //:     if (Rightchars() < (c = _match.MatchLength(capnum)))
                    //:         break Backward;
                    //:     for (j = _match.MatchIndex(capnum); c > 0; j++, c--)
                    //:         if (CharAt(j) != Rightcharnext())
                    //:             break Backward;
                    //: }
                    {
                        LocalBuilder lenV     = _tempV;
                        LocalBuilder indexV   = _temp2V;
                        Label      l1       = DefineLabel();

                        Ldthis();
                        Ldc(Operand(0));
                        Callvirt(_ismatchedM);
                        if ((_options & RegexOptions.ECMAScript) != 0)
                            Brfalse(AdvanceLabel());
                        else
                            BrfalseFar(_backtrack); // !IsMatched() -> back

                        Ldthis();
                        Ldc(Operand(0));
                        Callvirt(_matchlengthM);
                        Dup();
                        Stloc(lenV);
                        if (!IsRtl()) {
                            Ldloc(_textendV);
                            Ldloc(_textposV);
                        }
                        else {
                            Ldloc(_textposV);
                            Ldloc(_textbegV);
                        }
                        Sub();
                        BgtFar(_backtrack);         // Matchlength() > Rightchars() -> back

                        Ldthis();
                        Ldc(Operand(0));
                        Callvirt(_matchindexM);
                        if (!IsRtl()) {
                            Ldloc(lenV);
                            Add(IsRtl());
                        }
                        Stloc(indexV);              // index += len

                        Ldloc(_textposV);
                        Ldloc(lenV);
                        Add(IsRtl());
                        Stloc(_textposV);           // texpos += len

                        MarkLabel(l1);
                        Ldloc(lenV);
                        Ldc(0);
                        Ble(AdvanceLabel());
                        Ldloc(_textV);
                        Ldloc(indexV);
                        Ldloc(lenV);
                        if (IsRtl()) {
                            Ldc(1);
                            Sub();
                            Dup();
                            Stloc(lenV);
                        }
                        Sub(IsRtl());
                        Callvirt(_getcharM);
                        if (IsCi())
                            CallToLower();
                        
                        Ldloc(_textV);
                        Ldloc(_textposV);
                        Ldloc(lenV);
                        if (!IsRtl()) {
                            Dup();
                            Ldc(1);
                            Sub();
                            Stloc(lenV);
                        }
                        Sub(IsRtl());
                        Callvirt(_getcharM);
                        if (IsCi())
                            CallToLower();
                        
                        Beq(l1);
                        Back();
                        break;
                    }


                case RegexCode.Onerep:
                case RegexCode.Notonerep:
                case RegexCode.Setrep:
                case RegexCode.Onerep | RegexCode.Rtl:
                case RegexCode.Notonerep | RegexCode.Rtl:
                case RegexCode.Setrep | RegexCode.Rtl:
                case RegexCode.Onerep | RegexCode.Ci:
                case RegexCode.Notonerep | RegexCode.Ci:
                case RegexCode.Setrep | RegexCode.Ci:
                case RegexCode.Onerep | RegexCode.Ci | RegexCode.Rtl:
                case RegexCode.Notonerep | RegexCode.Ci | RegexCode.Rtl:
                case RegexCode.Setrep | RegexCode.Ci | RegexCode.Rtl:
                    //: int c = Operand(1);
                    //: if (Rightchars() < c)
                    //:     break Backward;
                    //: char ch = (char)Operand(0);
                    //: while (c-- > 0)
                    //:     if (Rightcharnext() != ch)
                    //:         break Backward;
                    {
                        LocalBuilder lenV = _tempV;
                        Label      l1   = DefineLabel();

                        int c = Operand(1);

                        if (c == 0)
                            break;

                        Ldc(c);
                        if (!IsRtl()) {
                            Ldloc(_textendV);
                            Ldloc(_textposV);
                        }
                        else {
                            Ldloc(_textposV);
                            Ldloc(_textbegV);
                        }
                        Sub();
                        BgtFar(_backtrack);         // Matchlength() > Rightchars() -> back

                        Ldloc(_textposV);
                        Ldc(c);
                        Add(IsRtl());
                        Stloc(_textposV);           // texpos += len

                        Ldc(c);
                        Stloc(lenV);

                        MarkLabel(l1);
                        Ldloc(_textV);
                        Ldloc(_textposV);
                        Ldloc(lenV);
                        if (IsRtl()) {
                            Ldc(1);
                            Sub();
                            Dup();
                            Stloc(lenV);
                            Add();
                        }
                        else {
                            Dup();
                            Ldc(1);
                            Sub();
                            Stloc(lenV);
                            Sub();
                        }
                        Callvirt(_getcharM);
                        if (IsCi())
                            CallToLower();
                        
                        if (Code() == RegexCode.Setrep) {
                            Ldstr(_strings[Operand(0)]);
                            Call(_charInSetM);

                            BrfalseFar(_backtrack);
                        }
                        else {
                            Ldc(Operand(0));
                            if (Code() == RegexCode.Onerep)
                                BneFar(_backtrack);
                            else
                                BeqFar(_backtrack);
                        }
                        Ldloc(lenV);
                        Ldc(0);
                        if (Code() == RegexCode.Setrep)
                            BgtFar(l1);
                        else
                            Bgt(l1);
                        break;
                    }


                case RegexCode.Oneloop:
                case RegexCode.Notoneloop:
                case RegexCode.Setloop:
                case RegexCode.Oneloop | RegexCode.Rtl:
                case RegexCode.Notoneloop | RegexCode.Rtl:
                case RegexCode.Setloop | RegexCode.Rtl:
                case RegexCode.Oneloop | RegexCode.Ci:
                case RegexCode.Notoneloop | RegexCode.Ci:
                case RegexCode.Setloop | RegexCode.Ci:
                case RegexCode.Oneloop | RegexCode.Ci | RegexCode.Rtl:
                case RegexCode.Notoneloop | RegexCode.Ci | RegexCode.Rtl:
                case RegexCode.Setloop | RegexCode.Ci | RegexCode.Rtl:
                    //: int c = Operand(1);
                    //: if (c > Rightchars())
                    //:     c = Rightchars();
                    //: char ch = (char)Operand(0);
                    //: int i;
                    //: for (i = c; i > 0; i--)
                    //: {
                    //:     if (Rightcharnext() != ch)
                    //:     {
                    //:         Leftnext();
                    //:         break;
                    //:     }
                    //: }
                    //: if (c > i)
                    //:     Track(c - i - 1, Textpos() - 1);

                    {
                        LocalBuilder cV   = _tempV;
                        LocalBuilder lenV = _temp2V;
                        Label      l1   = DefineLabel();
                        Label      l2   = DefineLabel();

                        int c = Operand(1);

                        if (c == 0)
                            break;
                        if (!IsRtl()) {
                            Ldloc(_textendV);
                            Ldloc(_textposV);
                        }
                        else {
                            Ldloc(_textposV);
                            Ldloc(_textbegV);
                        }
                        Sub();
                        if (c != Int32.MaxValue) {
                            Label l4 = DefineLabel();
                            Dup();
                            Ldc(c);
                            Blt(l4);
                            Pop();
                            Ldc(c);
                            MarkLabel(l4);
                        }
                        Dup();
                        Stloc(lenV);
                        Ldc(1);
                        Add();
                        Stloc(cV);

                        MarkLabel(l1);
                        Ldloc(cV);
                        Ldc(1);
                        Sub();
                        Dup();
                        Stloc(cV);
                        Ldc(0);
                        if (Code() == RegexCode.Setloop)
                            BleFar(l2);
                        else
                            Ble(l2);

                        if (IsRtl())
                            Leftcharnext();
                        else
                            Rightcharnext();
                        if (IsCi()) 
                            CallToLower();
                        
                        if (Code() == RegexCode.Setloop) {
                            Ldstr(_strings[Operand(0)]);
                            Call(_charInSetM);

                            BrtrueFar(l1);
                        }
                        else {
                            Ldc(Operand(0));
                            if (Code() == RegexCode.Oneloop)
                                Beq(l1);
                            else
                                Bne(l1);
                        }

                        Ldloc(_textposV);
                        Ldc(1);
                        Sub(IsRtl());
                        Stloc(_textposV);

                        MarkLabel(l2);
                        Ldloc(lenV);
                        Ldloc(cV);
                        Ble(AdvanceLabel());

                        ReadyPushTrack();
                        Ldloc(lenV);
                        Ldloc(cV);
                        Sub();
                        Ldc(1);
                        Sub();
                        DoPush();

                        ReadyPushTrack();
                        Ldloc(_textposV);
                        Ldc(1);
                        Sub(IsRtl());
                        DoPush();

                        Track();
                        break;
                    }

                case RegexCode.Oneloop | RegexCode.Back:
                case RegexCode.Notoneloop | RegexCode.Back:
                case RegexCode.Setloop | RegexCode.Back:
                case RegexCode.Oneloop | RegexCode.Rtl | RegexCode.Back:
                case RegexCode.Notoneloop | RegexCode.Rtl | RegexCode.Back:
                case RegexCode.Setloop | RegexCode.Rtl | RegexCode.Back:
                case RegexCode.Oneloop | RegexCode.Ci | RegexCode.Back:
                case RegexCode.Notoneloop | RegexCode.Ci | RegexCode.Back:
                case RegexCode.Setloop | RegexCode.Ci | RegexCode.Back:
                case RegexCode.Oneloop | RegexCode.Ci | RegexCode.Rtl | RegexCode.Back:
                case RegexCode.Notoneloop | RegexCode.Ci | RegexCode.Rtl | RegexCode.Back:
                case RegexCode.Setloop | RegexCode.Ci | RegexCode.Rtl | RegexCode.Back:
                    //: Trackframe(2);
                    //: int i   = Tracked(0);
                    //: int pos = Tracked(1);
                    //: Textto(pos);
                    //: if (i > 0)
                    //:     Track(i - 1, pos - 1);
                    //: Advance(2);
                    PopTrack();
                    Stloc(_textposV);
                    PopTrack();
                    Stloc(_tempV);
                    Ldloc(_tempV);
                    Ldc(0);
                    BleFar(AdvanceLabel());
                    ReadyPushTrack();
                    Ldloc(_tempV);
                    Ldc(1);
                    Sub();
                    DoPush();
                    ReadyPushTrack();
                    Ldloc(_textposV);
                    Ldc(1);
                    Sub(IsRtl());
                    DoPush();
                    Trackagain();
                    Advance();
                    break;

                case RegexCode.Onelazy:
                case RegexCode.Notonelazy:
                case RegexCode.Setlazy:
                case RegexCode.Onelazy | RegexCode.Rtl:
                case RegexCode.Notonelazy | RegexCode.Rtl:
                case RegexCode.Setlazy | RegexCode.Rtl:
                case RegexCode.Onelazy | RegexCode.Ci:
                case RegexCode.Notonelazy | RegexCode.Ci:
                case RegexCode.Setlazy | RegexCode.Ci:
                case RegexCode.Onelazy | RegexCode.Ci | RegexCode.Rtl:
                case RegexCode.Notonelazy | RegexCode.Ci | RegexCode.Rtl:
                case RegexCode.Setlazy | RegexCode.Ci | RegexCode.Rtl:
                    //: int c = Operand(1);
                    //: if (c > Rightchars())
                    //:     c = Rightchars();
                    //: if (c > 0)
                    //:     Track(c - 1, Textpos());
                    {
                        LocalBuilder cV   = _tempV;

                        int c = Operand(1);

                        if (c == 0)
                            break;

                        if (!IsRtl()) {
                            Ldloc(_textendV);
                            Ldloc(_textposV);
                        }
                        else {
                            Ldloc(_textposV);
                            Ldloc(_textbegV);
                        }
                        Sub();
                        if (c != Int32.MaxValue) {
                            Label l4 = DefineLabel();
                            Dup();
                            Ldc(c);
                            Blt(l4);
                            Pop();
                            Ldc(c);
                            MarkLabel(l4);
                        }
                        Dup();
                        Stloc(cV);
                        Ldc(0);
                        Ble(AdvanceLabel());
                        ReadyPushTrack();
                        Ldloc(cV);
                        Ldc(1);
                        Sub();
                        DoPush();
                        PushTrack(_textposV);
                        Track();
                        break;
                    }

                case RegexCode.Onelazy | RegexCode.Back:
                case RegexCode.Notonelazy | RegexCode.Back:
                case RegexCode.Setlazy | RegexCode.Back:
                case RegexCode.Onelazy | RegexCode.Rtl | RegexCode.Back:
                case RegexCode.Notonelazy | RegexCode.Rtl | RegexCode.Back:
                case RegexCode.Setlazy | RegexCode.Rtl | RegexCode.Back:
                case RegexCode.Onelazy | RegexCode.Ci | RegexCode.Back:
                case RegexCode.Notonelazy | RegexCode.Ci | RegexCode.Back:
                case RegexCode.Setlazy | RegexCode.Ci | RegexCode.Back:
                case RegexCode.Onelazy | RegexCode.Ci | RegexCode.Rtl | RegexCode.Back:
                case RegexCode.Notonelazy | RegexCode.Ci | RegexCode.Rtl | RegexCode.Back:
                case RegexCode.Setlazy | RegexCode.Ci | RegexCode.Rtl | RegexCode.Back:
                    //: Trackframe(2);
                    //: int pos = Tracked(1);
                    //: Textto(pos);
                    //: if (Rightcharnext() != (char)Operand(0))
                    //:     break Backward;
                    //: int i = Tracked(0);
                    //: if (i > 0)
                    //:     Track(i - 1, pos + 1);

                    PopTrack();
                    Stloc(_textposV);
                    PopTrack();
                    Stloc(_temp2V);

                    if (!IsRtl())
                        Rightcharnext();
                    else
                        Leftcharnext();

                    if (IsCi())
                        CallToLower();

                    if (Code() == RegexCode.Setlazy) {
                        Ldstr(_strings[Operand(0)]);
                        Call(_charInSetM);

                        BrfalseFar(_backtrack);
                    }
                    else {
                        Ldc(Operand(0));
                        if (Code() == RegexCode.Onelazy)
                            BneFar(_backtrack);
                        else
                            BeqFar(_backtrack);
                    }

                    Ldloc(_temp2V);
                    Ldc(0);
                    BleFar(AdvanceLabel());
                    ReadyPushTrack();
                    Ldloc(_temp2V);
                    Ldc(1);
                    Sub();
                    DoPush();
                    PushTrack(_textposV);
                    Trackagain();
                    Advance();
                    break;

                default:
                    throw new NotImplementedException(SR.GetString(SR.UnimplementedState));
            }
        }
    }

    internal class RegexTypeCompiler : RegexCompiler {
        private static int _typeCount = 0;
#if !MONO
        private static LocalDataStoreSlot _moduleSlot = Thread.AllocateDataSlot();
#endif

        private  AssemblyBuilder _assembly;
        private  ModuleBuilder  _module;

        // state of the type builder
        private  TypeBuilder     _typebuilder;
        private  MethodBuilder   _methbuilder;

        [ResourceExposure(ResourceScope.Machine)]
        [ResourceConsumption(ResourceScope.Machine)]
        [SuppressMessage("Microsoft.Security","CA2106:SecureAsserts", Justification="Microsoft: SECREVIEW : Regex only generates string manipulation, so this is OK")]
        internal RegexTypeCompiler(AssemblyName an, CustomAttributeBuilder[] attribs, String resourceFile) {
            // SECREVIEW : Regex only generates string manipulation, so this is
            //           : ok.
            //
#if MONO_FEATURE_CAS
            new ReflectionPermission(PermissionState.Unrestricted).Assert();
#endif
            try {
                Debug.Assert(an != null, "AssemblyName should not be null");

                List<CustomAttributeBuilder> assemblyAttributes = new List<CustomAttributeBuilder>();

                ConstructorInfo transparencyCtor = typeof(SecurityTransparentAttribute).GetConstructor(Type.EmptyTypes);
                CustomAttributeBuilder transparencyAttribute = new CustomAttributeBuilder(transparencyCtor, new object[0]);
                assemblyAttributes.Add(transparencyAttribute);

#if MONO_FEATURE_CAS
                ConstructorInfo securityRulesCtor = typeof(SecurityRulesAttribute).GetConstructor(new Type[] { typeof(SecurityRuleSet) });
                CustomAttributeBuilder securityRulesAttribute =
                    new CustomAttributeBuilder(securityRulesCtor, new object[] { SecurityRuleSet.Level2 });
                assemblyAttributes.Add(securityRulesAttribute);
#endif
	
                //TODO: MVM _assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(an, AssemblyBuilderAccess.RunAndSave, assemblyAttributes);
                _assembly = AssemblyBuilder.DefineDynamicAssembly(an, AssemblyBuilderAccess.Run);
                _module = _assembly.DefineDynamicModule(an.Name + ".dll");

                if (attribs != null) {
                    for (int i=0; i<attribs.Length; i++) {
                        _assembly.SetCustomAttribute(attribs[i]);
                    }
                }

                if (resourceFile != null) {
#if FEATURE_PAL
                    // unmanaged resources are not supported
                    throw new ArgumentOutOfRangeException("resourceFile");
#else
                    //TODO: MVM _assembly.DefineUnmanagedResource(resourceFile);
#endif
                }
            }
            finally {
#if MONO_FEATURE_CAS
                CodeAccessPermission.RevertAssert();
#endif
            }
        }

        /*
         * The top-level driver. Initializes everything then calls the Generate* methods.
         */
        internal Type FactoryTypeFromCode(RegexCode code, RegexOptions options, String typeprefix) {
            String runnertypename;
            String runnerfactoryname;
            Type runnertype;
            Type factory;
        
            _code       = code;
            _codes      = code._codes;
            _strings    = code._strings;
            _fcPrefix   = code._fcPrefix;
            _bmPrefix   = code._bmPrefix;
            _anchors    = code._anchors;
            _trackcount = code._trackcount;
            _options    = options;
        
            // pick a name for the class
            int typenum = Interlocked.Increment(ref _typeCount);
            string typenumString = typenum.ToString(CultureInfo.InvariantCulture);
            runnertypename = typeprefix + "Runner" + typenumString ;
            runnerfactoryname = typeprefix + "Factory" + typenumString;
        
            // Generate a RegexRunner class
            // (blocks are simply illustrative)
        
            DefineType(runnertypename, false, typeof(RegexRunner));
            {
                DefineMethod("Go", null);
                {
                    GenerateGo();
                    BakeMethod();
                }
        
                DefineMethod("FindFirstChar", typeof(bool));
                {
                    GenerateFindFirstChar();
                    BakeMethod();
                }
        
                DefineMethod("InitTrackCount", null);
                {
                    GenerateInitTrackCount();
                    BakeMethod();
                }
        
                runnertype = BakeType();
            }
        
            // Generate a RegexRunnerFactory class
        
            DefineType(runnerfactoryname, false, typeof(RegexRunnerFactory));
            {
                DefineMethod("CreateInstance", typeof(RegexRunner));
                {
                    GenerateCreateInstance(runnertype);
                    BakeMethod();
                }
        
                factory = BakeType();
            }
        
            return factory;
        }
        
        internal void GenerateRegexType(String pattern, RegexOptions opts, String name, bool ispublic, RegexCode code, RegexTree tree, Type factory, TimeSpan matchTimeout) {
            FieldInfo patternF                = RegexField("pattern");
            FieldInfo optionsF                = RegexField("roptions");
            FieldInfo factoryF                = RegexField("factory");
            FieldInfo capsF                   = RegexField("caps");
            FieldInfo capnamesF               = RegexField("capnames");
            FieldInfo capslistF               = RegexField("capslist");
            FieldInfo capsizeF                = RegexField("capsize");
            FieldInfo internalMatchTimeoutF   = RegexField("internalMatchTimeout");
            Type[] noTypeArray                = new Type[0];
            ConstructorBuilder defCtorBuilder, tmoutCtorBuilder;
        
            DefineType(name, ispublic, typeof(Regex));
            {
                // Define default constructor:
                _methbuilder = null;
                MethodAttributes ma = System.Reflection.MethodAttributes.Public;
                defCtorBuilder = _typebuilder.DefineConstructor(ma, CallingConventions.Standard, noTypeArray);
                _ilg = defCtorBuilder.GetILGenerator();
                {
                    // call base constructor
                    Ldthis();
                    _ilg.Emit(OpCodes.Call, typeof(Regex).GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                                                                         null, new Type[0], new ParameterModifier[0]));
                    // set pattern
                    Ldthis();
                    Ldstr(pattern);
                    Stfld(patternF);

                    // set options
                    Ldthis();
                    Ldc((int) opts);
                    Stfld(optionsF);

                    // Set timeout (no need to validate as it should have happened in RegexCompilationInfo):
                    Ldthis();
                    LdcI8(matchTimeout.Ticks);
                    Call(typeof(TimeSpan).GetMethod("FromTicks", BindingFlags.Static | BindingFlags.Public));
                    Stfld(internalMatchTimeoutF);

                    // set factory
                    Ldthis();
                    Newobj(factory.GetConstructor(noTypeArray));
                    Stfld(factoryF);

                    // set caps
                    if (code._caps != null)
#if SILVERLIGHT
                        GenerateCreateType(typeof(Dictionary<Int32, Int32>), capsF, code._caps);
#else
                        GenerateCreateHashtable(capsF, code._caps);
#endif

                    // set capnames
                    if (tree._capnames != null)
#if SILVERLIGHT
                        GenerateCreateType(typeof(Dictionary<String, Int32>), capnamesF, tree._capnames);
#else
                        GenerateCreateHashtable(capnamesF, tree._capnames);
#endif


                    // set capslist
                    if (tree._capslist != null) {
                        Ldthis();
                        Ldc(tree._capslist.Length);
                        _ilg.Emit(OpCodes.Newarr, typeof(String));  // create new string array
                        Stfld(capslistF);

                        for (int i=0; i< tree._capslist.Length; i++) {
                            Ldthisfld(capslistF);

                            Ldc(i);
                            Ldstr(tree._capslist[i]);
                            _ilg.Emit(OpCodes.Stelem_Ref);
                        }
                    }

                    // set capsize
                    Ldthis();
                    Ldc(code._capsize);
                    Stfld(capsizeF);

                    // set runnerref and replref by calling InitializeReferences()
                    Ldthis();
                    Call(typeof(Regex).GetMethod("InitializeReferences", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));


                    Ret();
                }

                // Constructor with the timeout parameter:
                _methbuilder = null;
                ma = System.Reflection.MethodAttributes.Public;
                tmoutCtorBuilder = _typebuilder.DefineConstructor(ma, CallingConventions.Standard, new Type[] { typeof(TimeSpan) });
                _ilg = tmoutCtorBuilder.GetILGenerator();
                {
                    // Call the default constructor:
                    Ldthis();
                    _ilg.Emit(OpCodes.Call, defCtorBuilder);

                    // Validate timeout:
                    _ilg.Emit(OpCodes.Ldarg_1);
                    Call(typeof(Regex).GetMethod("ValidateMatchTimeout", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));

                    // Set timeout:
                    Ldthis();
                    _ilg.Emit(OpCodes.Ldarg_1);
                    Stfld(internalMatchTimeoutF);

                    Ret();
                }
            }
        
            // bake the constructor and type, then save the assembly
            defCtorBuilder = null;
            tmoutCtorBuilder = null;
            _typebuilder.CreateType();
            _ilg = null;
            _typebuilder = null;
        }

#if SILVERLIGHT
        internal void GenerateCreateType<TKey>(Type myCollectionType, FieldInfo field, Dictionary<TKey,int> ht) {
            MethodInfo addMethod = myCollectionType.GetMethod("Add", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            
            Ldthis();
            Newobj(myCollectionType.GetConstructor(new Type[0]));
#else
        internal void GenerateCreateHashtable(FieldInfo field, Hashtable ht) {
            MethodInfo addMethod = typeof(Hashtable).GetMethod("Add", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            
            Ldthis();
            Newobj(typeof(Hashtable).GetConstructor(new Type[0]));
#endif

            Stfld(field);
        
            IDictionaryEnumerator en = ht.GetEnumerator();
            while (en.MoveNext()) {
                Ldthisfld(field);
        
                if (en.Key is int) {
                    Ldc((int) en.Key);  
#if !SILVERLIGHT
                    _ilg.Emit(OpCodes.Box, typeof(Int32));
#endif
                }
                else 
                    Ldstr((String) en.Key);
        
                Ldc((int) en.Value);
#if !SILVERLIGHT
                _ilg.Emit(OpCodes.Box, typeof(Int32));
#endif   
                Callvirt(addMethod);
            }
        }

        private FieldInfo RegexField(String fieldname) {
            return typeof(Regex).GetField(fieldname, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        }

        // Note that we save the assembly to the current directory, and we believe this is not a
        // problem because this should only be used by tools, not at runtime.
        [ResourceExposure(ResourceScope.None)]
        [ResourceConsumption(ResourceScope.Machine, ResourceScope.Machine)]
        internal void Save() {
           //TODO: MVM _assembly.Save(_assembly.GetName().Name + ".dll");
        }

        /*
         * Generates a very simple factory method.
         */
        internal void GenerateCreateInstance(Type newtype) {
            Newobj(newtype.GetConstructor(new Type[0]));
            Ret();
        }

        /*
         * Begins the definition of a new type with a specified base class
         */
        internal void DefineType(String typename, bool ispublic, Type inheritfromclass) {
            if (ispublic)
                _typebuilder = _module.DefineType(typename, TypeAttributes.Class | TypeAttributes.Public, inheritfromclass);
            else
                _typebuilder = _module.DefineType(typename, TypeAttributes.Class | TypeAttributes.NotPublic, inheritfromclass);
        
        }
        
        /*
         * Begins the definition of a new method (no args) with a specified return value
         */
        internal void DefineMethod(String methname, Type returntype) {
            MethodAttributes ma = System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Virtual;
        
            _methbuilder = _typebuilder.DefineMethod(methname, ma, returntype, null);
            _ilg = _methbuilder.GetILGenerator();
        }
        
        /*
         * Ends the definition of a method
         */
        internal void BakeMethod() {
            _methbuilder = null;
        }
        
        /*
         * Ends the definition of a class and returns the type
         */
        internal Type BakeType() {
            Type retval = _typebuilder.CreateType();
            _typebuilder = null;
        
            return retval;
        }
        
    }

    internal class RegexLWCGCompiler : RegexCompiler {
        private static int _regexCount = 0;
        private static Type[] _paramTypes = new Type[] {typeof(RegexRunner)};
        
        internal RegexLWCGCompiler() {
        }
        
        /*
         * The top-level driver. Initializes everything then calls the Generate* methods.
         */
        internal RegexRunnerFactory FactoryInstanceFromCode(RegexCode code, RegexOptions options) {
            _code       = code;
            _codes      = code._codes;
            _strings    = code._strings;
            _fcPrefix   = code._fcPrefix;
            _bmPrefix   = code._bmPrefix;
            _anchors    = code._anchors;
            _trackcount = code._trackcount;
            _options    = options;
        
            // pick a unique number for the methods we generate
            int regexnum = Interlocked.Increment(ref _regexCount);
            string regexnumString = regexnum.ToString(CultureInfo.InvariantCulture);
            
            DynamicMethod goMethod = DefineDynamicMethod("Go" + regexnumString, null, typeof(CompiledRegexRunner));
            GenerateGo();
    
            DynamicMethod firstCharMethod = DefineDynamicMethod("FindFirstChar" + regexnumString, typeof(bool), typeof(CompiledRegexRunner));
            GenerateFindFirstChar();
    
            DynamicMethod trackCountMethod = DefineDynamicMethod("InitTrackCount" + regexnumString, null, typeof(CompiledRegexRunner));
            GenerateInitTrackCount();

            return new CompiledRegexRunnerFactory(goMethod, firstCharMethod, trackCountMethod);
        }
        
        /*
         * Begins the definition of a new method (no args) with a specified return value
         */
        internal DynamicMethod DefineDynamicMethod(String methname, Type returntype, Type hostType) {
            // We're claiming that these are static methods, but really they are instance methods.
            // By giving them a parameter which represents "this", we're tricking them into 
            // being instance methods.  

            MethodAttributes attribs =  MethodAttributes.Public | MethodAttributes.Static;
            CallingConventions conventions = CallingConventions.Standard;
                            
            DynamicMethod dm = new DynamicMethod(methname, attribs, conventions, returntype, _paramTypes, hostType, false /*skipVisibility*/);
            _ilg = dm.GetILGenerator();
            return dm;
        }

    }
    
}
#endif
