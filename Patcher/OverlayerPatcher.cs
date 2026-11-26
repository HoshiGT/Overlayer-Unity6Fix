// Rewrites Overlayer 3.49.0's broken Unity6/r145 references to Overlayer.Unity6Compat.
// usage: OverlayerPatcher <in.dll> <out.dll> <compat.dll> <managedDir>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

class OverlayerPatcher
{
    static ModuleDefinition mod;
    static TypeDefinition compat;
    static Dictionary<string, MethodReference> C = new Dictionary<string, MethodReference>();
    static int patched = 0;

    static void Log(string s) { Console.WriteLine("  " + s); }

    static int Main(string[] args)
    {
        string inp = args[0], outp = args[1], compatPath = args[2], managed = args[3];

        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(managed);
        resolver.AddSearchDirectory(Path.Combine(managed, "UnityModManager"));
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(inp)));
        resolver.AddSearchDirectory(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(inp)), "lib"));
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(compatPath)));

        var asm = AssemblyDefinition.ReadAssembly(inp, new ReaderParameters { AssemblyResolver = resolver });
        mod = asm.MainModule;

        var compatAsm = AssemblyDefinition.ReadAssembly(compatPath, new ReaderParameters { AssemblyResolver = resolver });
        compat = compatAsm.MainModule.GetType("Overlayer.Unity6Compat.Compat");
        foreach (var m in compat.Methods.Where(m => m.IsPublic && m.IsStatic))
            C[m.Name] = mod.ImportReference(m);

        var gameAsm = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "Assembly-CSharp.dll"),
                                                      new ReaderParameters { AssemblyResolver = resolver });
        var game = gameAsm.MainModule;
        var releasesType = mod.ImportReference(game.GetType("Releases"));
        var scrPlayerType = mod.ImportReference(game.GetType("scrPlayer"));
        var difficultyType = mod.ImportReference(game.GetType("Difficulty"));
        var hitMarginType = mod.ImportReference(game.GetType("HitMargin"));
        var consecField = mod.ImportReference(game.GetType("scrPlayer").Fields.First(f => f.Name == "consecMultipressCounter"));
        var accStatsType = mod.GetType("Overlayer.Tags.AccuracyStats");
        var mainType = mod.GetType("Overlayer.Main");
        var mainGetAss = mainType.Methods.First(m => m.Name == "get_Ass");

        // ---- pass 1: retarget SFB TypeRefs from Assembly-CSharp-firstpass to the compat assembly ----
        // the compat method imports above already added an AssemblyNameReference; reuse it
        var compatRef = mod.AssemblyReferences.FirstOrDefault(r => r.Name == compatAsm.Name.Name);
        if (compatRef == null)
        {
            compatRef = new AssemblyNameReference(compatAsm.Name.Name, compatAsm.Name.Version);
            mod.AssemblyReferences.Add(compatRef);
        }
        foreach (var tr in mod.GetTypeReferences())
        {
            if (tr.FullName == "SFB.ExtensionFilter" || tr.FullName == "SFB.StandaloneFileBrowser")
            {
                tr.Scope = compatRef;
                patched++;
                Log("typeref -> compat: " + tr.FullName);
            }
        }

        // ---- pass 2: per-instruction rewrites ----
        foreach (var t in mod.GetTypes())
        {
            foreach (var m in t.Methods)
            {
                if (!m.HasBody) continue;
                bool inOnDamagePostfix = t.FullName.Contains("Hit__OnDamage") && m.Name == "Postfix";
                bool inSetJudgeText = m.Name.Contains("SetJudgeText");
                var ins = m.Body.Instructions;

                for (int i = 0; i < ins.Count; i++)
                {
                    var x = ins[i];

                    if (x.OpCode == OpCodes.Ldfld && x.Operand is FieldReference fr)
                    {
                        string dt = fr.DeclaringType.Name, fn = fr.Name;
                        string repl = null;
                        if (dt == "scrController")
                        {
                            if (fn == "speed") repl = "Speed";
                            else if (fn == "isCW") repl = "IsCW";
                            else if (fn == "failbar") repl = "FailBar";
                            else if (fn == "midspinInfiniteMargin") repl = "MidspinInfiniteMargin";
                            else if (fn == "mistakesManager") repl = "Mistakes";
                            else if (fn == "consecMultipressCounter")
                            {
                                if (inOnDamagePostfix)
                                {   // __instance becomes scrPlayer; use its real field
                                    x.Operand = consecField;
                                    patched++; Log("ldfld -> scrPlayer.consecMultipressCounter in " + m.FullName);
                                    continue;
                                }
                                repl = "ConsecMultipressCounter";
                            }
                        }
                        else if (dt == "scrPlanet" && fn == "targetExitAngle") repl = "TargetExitAngle";
                        else if (dt == "PlanetRenderer" && fn == "ring") repl = "PlanetRing";
                        else if (dt == "TMP_Asset" && fn == "material") repl = "TMPMaterial";

                        if (repl != null)
                        {
                            // in-place mutation keeps branch targets pointing at this instruction valid
                            x.OpCode = OpCodes.Call; x.Operand = C[repl];
                            patched++; Log("ldfld " + dt + "." + fn + " -> Compat." + repl + " in " + m.FullName);
                        }
                    }
                    else if (x.OpCode == OpCodes.Ldsfld && x.Operand is FieldReference sfr)
                    {
                        string dt = sfr.DeclaringType.Name, fn = sfr.Name;
                        string repl = null;
                        if (dt == "scrMistakesManager" && fn == "hitMarginsCount") repl = "HitMarginsCount";
                        else if (dt == "scrMistakesManager" && fn == "hitMargins") repl = "HitMargins";
                        else if (dt == "RDString" && fn == "AvailableLanguages") repl = "AvailableLanguages";
                        if (repl != null)
                        {
                            x.OpCode = OpCodes.Call; x.Operand = C[repl];
                            patched++; Log("ldsfld " + dt + "." + fn + " -> Compat." + repl + " in " + m.FullName);
                        }
                    }
                    else if ((x.OpCode == OpCodes.Call || x.OpCode == OpCodes.Callvirt) && x.Operand is MethodReference mr)
                    {
                        string dt = mr.DeclaringType.Name, mn = mr.Name;
                        if (dt == "scrMistakesManager" && mn == "GetHits" && mr.Parameters.Count == 1)
                        {
                            x.OpCode = OpCodes.Call; x.Operand = C["GetHits"];
                            patched++; Log("GetHits -> Compat.GetHits in " + m.FullName);
                        }
                        else if (dt == "RDString" && mn == "Get" && mr.Parameters.Count == 3)
                        {
                            x.OpCode = OpCodes.Call; x.Operand = C["RDStringGet"];
                            patched++; Log("RDString.Get(3) -> Compat.RDStringGet in " + m.FullName);
                        }
                        else if (dt == "ADOBase" && mn == "get_isPlayingLevel")
                        {
                            x.OpCode = OpCodes.Call; x.Operand = C["IsPlayingLevel"];
                            patched++; Log("get_isPlayingLevel -> Compat.IsPlayingLevel in " + m.FullName);
                        }
                        else if (mn == "FieldRefAccess" && mr is GenericInstanceMethod gim && gim.GenericArguments.Count == 2)
                        {
                            string g0 = gim.GenericArguments[0].Name;
                            if (g0 == "scrController")
                            {
                                x.OpCode = OpCodes.Call; x.Operand = C["CachedHitTexts"];
                                patched++; Log("FieldRefAccess<scrController,..> -> Compat.CachedHitTexts in " + m.FullName);
                            }
                            else if (g0 == "scrHitTextMesh")
                            {
                                x.OpCode = OpCodes.Call; x.Operand = C["SHTMText"];
                                patched++; Log("FieldRefAccess<scrHitTextMesh,..> -> Compat.SHTMText in " + m.FullName);
                            }
                        }
                        else if (inSetJudgeText && mn == "set_text" && dt == "TextMesh")
                        {
                            x.OpCode = OpCodes.Call; x.Operand = C["SetHitText"];
                            patched++; Log("TextMesh.set_text -> Compat.SetHitText in " + m.FullName);
                        }
                        else if (inSetJudgeText && mn == "Invoke"
                                 && mr.DeclaringType is GenericInstanceType git
                                 && git.GenericArguments.Count == 2
                                 && git.GenericArguments[0].Name == "scrHitTextMesh")
                        {
                            // only FieldRef<scrHitTextMesh,TextMesh>::Invoke — NOT the
                            // FieldRef<scrController,Dictionary<HitMargin,scrHitTextMesh[]>> one,
                            // whose FullName also contains "scrHitTextMesh"
                            // FieldRef<scrHitTextMesh,TextMesh>::Invoke — drop it plus the ldind.ref after it
                            if (i + 1 < ins.Count && ins[i + 1].OpCode == OpCodes.Ldind_Ref)
                            {
                                ins[i + 1].OpCode = OpCodes.Nop; ins[i + 1].Operand = null;
                            }
                            x.OpCode = OpCodes.Nop; x.Operand = null;
                            patched++; Log("nop FieldRef.Invoke+ldind in " + m.FullName);
                        }
                    }
                    if (inSetJudgeText && x.OpCode == OpCodes.Ldsfld && x.Operand is FieldReference jf
                        && jf.Name == "sHTM_text")
                    {
                        x.OpCode = OpCodes.Nop; x.Operand = null;
                        patched++; Log("nop ldsfld sHTM_text in " + m.FullName);
                    }

                    if (x.OpCode == OpCodes.Ldtoken && x.Operand is TypeReference gtr && gtr.Name == "GCNS"
                        && t.Name == "LazyPatchAttribute" && m.Name == ".cctor")
                    {
                        x.Operand = releasesType;
                        patched++; Log("ldtoken GCNS -> Releases in LazyPatchAttribute..cctor");
                    }
                }

                // pass 2b: neuter InjectStartRadius (scrController.startRadius no longer exists)
                if (m.Name == "InjectStartRadius" && t.Name == "Adofai")
                {
                    m.Body.Instructions.Clear();
                    m.Body.ExceptionHandlers.Clear();
                    m.Body.Variables.Clear();
                    m.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
                    patched++; Log("InjectStartRadius -> no-op");
                }
            }

            // ---- pass 3: LazyPatch attribute retargets + Postfix param retype ----
            foreach (var provider in new IEnumerable<CustomAttribute>[] { t.CustomAttributes }
                     .Concat(t.Methods.Select(m2 => (IEnumerable<CustomAttribute>)m2.CustomAttributes)))
            {
                foreach (var a in provider)
                {
                    if (a.AttributeType.Name != "LazyPatchAttribute") continue;
                    var ca = a.ConstructorArguments;
                    if (ca.Count >= 3 && (string)ca[1].Value == "scrController" && (string)ca[2].Value == "OnDamage")
                    {
                        a.ConstructorArguments[1] = new CustomAttributeArgument(mod.TypeSystem.String, "scrPlayer");
                        patched++; Log("LazyPatch OnDamage: scrController -> scrPlayer");
                    }
                    if (ca.Count >= 3 && (string)ca[1].Value == "scrMistakesManager" && (string)ca[2].Value == "CalculatePercentAcc")
                    {
                        a.ConstructorArguments[2] = new CustomAttributeArgument(mod.TypeSystem.String, "CalculateTotalAccuracy");
                        patched++; Log("LazyPatch CalculatePercentAcc -> CalculateTotalAccuracy");
                    }
                    if (ca.Count >= 3 && (string)ca[1].Value == "scrMisc" && (string)ca[2].Value == "GetHitMargin")
                    {
                        a.ConstructorArguments[2] = new CustomAttributeArgument(mod.TypeSystem.String, "GetHitMarginInDeg");
                        patched++; Log("LazyPatch GetHitMargin -> GetHitMarginInDeg on " + t.FullName);
                    }
                }
            }
            if (t.FullName.Contains("Hit__OnDamage"))
            {
                var pf = t.Methods.FirstOrDefault(m2 => m2.Name == "Postfix");
                if (pf != null && pf.Parameters.Count > 0 && pf.Parameters[0].ParameterType.Name == "scrController")
                {
                    pf.Parameters[0].ParameterType = scrPlayerType;
                    patched++; Log("Postfix __instance: scrController -> scrPlayer");
                }
            }

            // ---- pass 3b: r150 AccuracyStats__CalculatePercentAcc -> Compat.CalcAccuracy ----
            if (t.FullName.Contains("AccuracyStats__CalculatePercentAcc"))
            {
                var pf = t.Methods.FirstOrDefault(m2 => m2.Name == "Postfix");
                if (pf != null && pf.HasBody && accStatsType != null)
                {
                    pf.Body.Instructions.Clear();
                    pf.Body.ExceptionHandlers.Clear();
                    pf.Body.Variables.Clear();
                    var il = pf.Body.GetILProcessor();
                    il.Append(Instruction.Create(OpCodes.Ldarg_0));
                    il.Append(Instruction.Create(OpCodes.Ldsflda, accStatsType.Fields.First(f => f.Name == "Accuracy")));
                    il.Append(Instruction.Create(OpCodes.Ldsflda, accStatsType.Fields.First(f => f.Name == "XAccuracy")));
                    il.Append(Instruction.Create(OpCodes.Ldsflda, accStatsType.Fields.First(f => f.Name == "AbsXAccuracy")));
                    il.Append(Instruction.Create(OpCodes.Ldsflda, accStatsType.Fields.First(f => f.Name == "MaxAccuracy")));
                    il.Append(Instruction.Create(OpCodes.Ldsflda, accStatsType.Fields.First(f => f.Name == "MaxXAccuracy")));
                    il.Append(Instruction.Create(OpCodes.Ldsflda, accStatsType.Fields.First(f => f.Name == "AbsMaxXAccuracy")));
                    il.Append(Instruction.Create(OpCodes.Call, C["CalcAccuracy"]));
                    il.Append(Instruction.Create(OpCodes.Ret));
                    patched++; Log("Rewrote AccuracyStats__CalculatePercentAcc.Postfix -> Compat.CalcAccuracy");
                }
            }

            // ---- pass 3c: r150 P_scrMisc (Hit / Combo / Scores) -> GetHitMarginInDeg Postfixes ----
            if (t.FullName.EndsWith("/Hit__GetHitMargin") || t.FullName.EndsWith("/Combo__GetHitMargin") || t.FullName.EndsWith("/Scores__GetHitMargin"))
            {
                string compatMethod = t.FullName.EndsWith("/Hit__GetHitMargin") ? "OnHitInDeg"
                    : t.FullName.EndsWith("/Combo__GetHitMargin") ? "OnComboInDeg"
                    : "OnScoresInDeg";
                var hm = t.Methods.FirstOrDefault(m2 => m2.Name == "Prefix" || m2.Name == "Postfix");
                if (hm != null && hm.HasBody)
                {
                    hm.Name = "Postfix";
                    hm.ReturnType = mod.TypeSystem.Void;
                    hm.Parameters.Clear();
                    hm.Parameters.Add(new ParameterDefinition("difficulty", ParameterAttributes.None, difficultyType));
                    hm.Parameters.Add(new ParameterDefinition("hitAngle", ParameterAttributes.None, mod.TypeSystem.Single));
                    hm.Parameters.Add(new ParameterDefinition("refAngle", ParameterAttributes.None, mod.TypeSystem.Single));
                    hm.Parameters.Add(new ParameterDefinition("clockwise", ParameterAttributes.None, mod.TypeSystem.Boolean));
                    hm.Parameters.Add(new ParameterDefinition("floorBpm", ParameterAttributes.None, mod.TypeSystem.Single));
                    hm.Parameters.Add(new ParameterDefinition("conductorPitch", ParameterAttributes.None, mod.TypeSystem.Single));
                    hm.Parameters.Add(new ParameterDefinition("marginScale", ParameterAttributes.None, mod.TypeSystem.Double));
                    hm.Parameters.Add(new ParameterDefinition("__result", ParameterAttributes.None, hitMarginType));
                    hm.Body.Instructions.Clear();
                    hm.Body.ExceptionHandlers.Clear();
                    hm.Body.Variables.Clear();
                    var il = hm.Body.GetILProcessor();
                    for (int p = 0; p < 8; p++)
                        il.Append(Instruction.Create(OpCodes.Ldarg, hm.Parameters[p]));
                    il.Append(Instruction.Create(OpCodes.Call, C[compatMethod]));
                    il.Append(Instruction.Create(OpCodes.Ret));
                    patched++; Log("Rewrote " + t.FullName + ".Postfix -> Compat." + compatMethod);
                }
            }

            // ---- pass 3d: Hit.Reset default HitMargin (3=PerfectMinus -> 4=XPerfect) ----
            if (t.FullName == "Overlayer.Tags.Hit")
            {
                var rm = t.Methods.FirstOrDefault(m2 => m2.Name == "Reset");
                if (rm != null && rm.HasBody && rm.Body.Instructions.Count > 0 && rm.Body.Instructions[0].OpCode == OpCodes.Ldc_I4_3)
                {
                    rm.Body.Instructions[0].OpCode = OpCodes.Ldc_I4_4;
                    patched++; Log("Hit.Reset initial HitMargin 3 -> 4 (XPerfect)");
                }
            }

            // ---- pass 3e: ComboStats.SpecialPlayMark r150 HitMargin names ----
            if (t.FullName == "Overlayer.Tags.ComboStats")
            {
                var spm = t.Methods.FirstOrDefault(m2 => m2.Name == "SpecialPlayMark");
                if (spm != null && spm.HasBody)
                {
                    foreach (var ins in spm.Body.Instructions)
                    {
                        if (ins.OpCode == OpCodes.Ldstr && ins.Operand is string s)
                        {
                            if (s == "Perfect")
                            {
                                ins.Operand = "PerfectMinus|XPerfect|PerfectPlus";
                                patched++; Log("SpecialPlayMark: Perfect -> PerfectMinus|XPerfect|PerfectPlus");
                            }
                            else if (s == "VeryEarly|EarlyPerfect|Perfect|LatePerfect|VeryLate")
                            {
                                ins.Operand = "VeryEarly|EarlyPerfect|PerfectMinus|XPerfect|PerfectPlus|LatePerfect|VeryLate";
                                patched++; Log("SpecialPlayMark: expanded Perfect in FC+ mask");
                            }
                        }
                    }
                }
            }

            // ---- pass 3f: Overlayer.Main.OnToggle -> Compat.SyncR150Hooks(toggle, Ass) for GetHitMarginInSec ----
            if (t.FullName == "Overlayer.Main")
            {
                var ot = t.Methods.FirstOrDefault(m2 => m2.Name == "OnToggle");
                if (ot != null && ot.HasBody && ot.Body.Instructions.Count >= 2)
                {
                    var ins = ot.Body.Instructions;
                    var last = ins[ins.Count - 1];
                    var prev = ins[ins.Count - 2];
                    if (last.OpCode == OpCodes.Ret && prev.OpCode == OpCodes.Ldc_I4_1)
                    {
                        // Mutate prev in-place so all branch targets jumping to prev execute SyncR150Hooks first
                        prev.OpCode = OpCodes.Ldarg_1;
                        prev.Operand = null;
                        var il = ot.Body.GetILProcessor();
                        var callAss = Instruction.Create(OpCodes.Call, mainGetAss);
                        var callSync = Instruction.Create(OpCodes.Call, C["SyncR150Hooks"]);
                        var pushTrue = Instruction.Create(OpCodes.Ldc_I4_1);
                        il.InsertAfter(prev, callAss);
                        il.InsertAfter(callAss, callSync);
                        il.InsertAfter(callSync, pushTrue);
                        patched++; Log("Injected Compat.SyncR150Hooks into Overlayer.Main.OnToggle");
                    }
                }
            }
        }

        Console.WriteLine("total patches applied: " + patched);
        if (patched < 60) { Console.Error.WriteLine("suspiciously few patches — aborting"); return 2; }
        asm.Write(outp);
        Console.WriteLine("written: " + outp);
        return 0;
    }
}
