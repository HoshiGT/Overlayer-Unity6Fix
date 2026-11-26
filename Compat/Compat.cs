// Overlayer.Unity6Compat — compatibility shim for Overlayer 3.49.0 on ADOFAI Unity 6 (r145+)
// Loaded from Overlayer/lib/ by Overlayer.Bootstrapper. The patched Overlayer.dll
// redirects broken game-member references here.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace SFB
{
    // Reimplementation of the removed Assembly-CSharp-firstpass SFB types
    // on top of the game's new UnityFileDialog.dll.
    public struct ExtensionFilter
    {
        public string Name;
        public string[] Extensions;

        public ExtensionFilter(string filterName, params string[] filterExtensions)
        {
            Name = filterName;
            Extensions = filterExtensions;
        }
    }

    public static class StandaloneFileBrowser
    {
        public static string[] OpenFilePanel(string title, string directory, ExtensionFilter[] extensions, bool multiselect)
        {
            using (var d = new UnityFileDialog.FileDialog())
            {
                d.SetTitle(title);
                if (!string.IsNullOrEmpty(directory)) d.SetDirectory(directory);
                if (extensions != null)
                    foreach (var e in extensions)
                        d.AddFilter(e.Name ?? "Files", e.Extensions ?? new string[0]);
                if (multiselect) return d.PickFiles() ?? new string[0];
                string f = d.PickFile();
                return string.IsNullOrEmpty(f) ? new string[0] : new[] { f };
            }
        }

        public static string SaveFilePanel(string title, string directory, string defaultName, string extension)
        {
            using (var d = new UnityFileDialog.FileDialog())
            {
                d.SetTitle(title);
                if (!string.IsNullOrEmpty(directory)) d.SetDirectory(directory);
                if (!string.IsNullOrEmpty(defaultName)) d.SetFileName(defaultName);
                if (!string.IsNullOrEmpty(extension)) d.AddFilter(extension, new[] { extension });
                return d.SaveFile() ?? "";
            }
        }
    }
}

namespace Overlayer.Unity6Compat
{
    public static class Compat
    {
        // --- scrController members that moved in the multi-planet refactor ---

        static scrPlayer Player(scrController c)
        {
            if (c == null) return null;
            return c.playerOne;
        }

        public static double Speed(scrController c)
        {
            PlanetarySystem ps = c != null ? c.planetarySystem : null;
            return ps != null ? ps.speed : 1.0;
        }

        public static bool IsCW(scrController c)
        {
            PlanetarySystem ps = c != null ? c.planetarySystem : null;
            return ps == null || ps.isCW;
        }

        public static scrFailBar FailBar(scrController c)
        {
            scrPlayer p = Player(c);
            return p != null ? p.failBar : null;
        }

        public static bool MidspinInfiniteMargin(scrController c)
        {
            scrPlayer p = Player(c);
            return p != null && p.midspinInfiniteMargin;
        }

        public static int ConsecMultipressCounter(scrController c)
        {
            scrPlayer p = Player(c);
            return p != null ? p.consecMultipressCounter : 0;
        }

        public static scrMistakesManager Mistakes(scrController c)
        {
            return c != null ? c.mistakesManager : null;
        }

        // --- scrMistakesManager members that moved to scrMarginTracker ---
        //
        // Overlayer 3.49.0 still indexes with the pre-XPerfect HitMargin values.
        // The game inserted three perfect grades and shifted every later value:
        //   old Perfect(3)      -> PerfectMinus(3) + XPerfect(4) + PerfectPlus(5)
        //   old LatePerfect(4)  -> LatePerfect(6)
        //   old VeryLate(5)     -> VeryLate(7)
        //   old TooLate(6)      -> TooLate(8)
        //   old Multipress(7)   -> Multipress(9)
        //   old FailMiss(8)     -> FailMiss(10)
        //   old FailOverload(9) -> FailOverload(11)
        //   old Auto(10)        -> Auto(12)
        //   old OverPress(11)   -> OverPress(13)
        // Without this, a real perfect (XPerfect) lands in the old LatePerfect
        // slot, which the overlay shows as the right-hand perfect.

        static readonly int[] emptyCounts = new int[16];
        static readonly int[] legacyCounts = new int[16];
        static readonly List<HitMargin> emptyMargins = new List<HitMargin>();

        static scrMarginTracker Tracker()
        {
            scrController ctrl = scrController.instance;
            scrPlayer p1 = ctrl != null ? ctrl.playerOne : null;
            if (p1 != null && p1.marginTracker != null) return p1.marginTracker;
            scrMarginTracker[] ts = scrMistakesManager.marginTrackers;
            return ts != null && ts.Length > 0 ? ts[0] : null;
        }

        static int At(int[] src, int index)
        {
            return src != null && (uint)index < (uint)src.Length ? src[index] : 0;
        }

        // Pack the live counts into the 12-value layout Overlayer was compiled against.
        // Callers index the returned array immediately (ldelem before the next call),
        // so a reused buffer is safe and the per-frame tag refresh does not allocate.
        static void PackLegacy(int[] src, int[] dst)
        {
            dst[0] = At(src, 0);                         // TooEarly
            dst[1] = At(src, 1);                         // VeryEarly
            dst[2] = At(src, 2);                         // EarlyPerfect
            dst[3] = At(src, 3) + At(src, 4) + At(src, 5); // PerfectMinus + XPerfect + PerfectPlus
            dst[4] = At(src, 6);                         // LatePerfect
            dst[5] = At(src, 7);                         // VeryLate
            dst[6] = At(src, 8);                         // TooLate
            dst[7] = At(src, 9);                         // Multipress
            dst[8] = At(src, 10);                        // FailMiss
            dst[9] = At(src, 11);                        // FailOverload
            dst[10] = At(src, 12);                       // Auto
            dst[11] = At(src, 13);                       // OverPress
            dst[12] = 0;
            dst[13] = 0;
            dst[14] = 0;
            dst[15] = 0;
        }

        public static int[] HitMarginsCount()
        {
            scrMarginTracker t = Tracker();
            if (t == null || t.hitMarginsCount == null) return emptyCounts;
            PackLegacy(t.hitMarginsCount, legacyCounts);
            return legacyCounts;
        }

        public static List<HitMargin> HitMargins()
        {
            scrMarginTracker t = Tracker();
            return t != null && t.hitMargins != null ? t.hitMargins : emptyMargins;
        }

        public static int GetHits(scrMistakesManager unused, HitMargin margin)
        {
            scrMarginTracker t = Tracker();
            int[] src = t != null ? t.hitMarginsCount : null;
            switch ((int)margin)
            {
                case 0: return At(src, 0);
                case 1: return At(src, 1);
                case 2: return At(src, 2);
                case 3: return At(src, 3) + At(src, 4) + At(src, 5);
                case 4: return At(src, 6);
                case 5: return At(src, 7);
                case 6: return At(src, 8);
                case 7: return At(src, 9);
                case 8: return At(src, 10);
                case 9: return At(src, 11);
                case 10: return At(src, 12);
                case 11: return At(src, 13);
                default: return At(src, (int)margin);
            }
        }

        // --- RDString ---

        public static string RDStringGet(string key, Dictionary<string, object> parameters, SA.GoogleDoc.LangSection unused)
        {
            return RDString.Get(key, parameters);
        }

        public static SystemLanguage[] AvailableLanguages()
        {
            return RDString.AvailableLanguages;
        }

        // --- ADOBase.isPlayingLevel (removed in r145) ---
        // r141 semantics: (isOfficialLevel || isFeaturedLevel) && !isLevelSelect

        public static bool IsPlayingLevel()
        {
            bool featured = false;
            string id = GCS.customLevelId;
            uint n;
            if (!string.IsNullOrEmpty(id) && uint.TryParse(id, out n))
            {
                Dictionary<uint, FeaturedLevels.LevelEntry> all = FeaturedLevels.all;
                featured = all != null && all.ContainsKey(n);
            }
            return (ADOBase.isOfficialLevel || featured) && !ADOBase.isLevelSelect;
        }

        // --- misc field -> property / type changes ---

        public static double TargetExitAngle(scrPlanet p)
        {
            return p != null ? p.targetExitAngle : 0.0;
        }

        // ring changed SpriteRenderer -> LineRenderer; caller only needs Component.transform
        public static Component PlanetRing(PlanetRenderer pr)
        {
            return pr != null ? (Component)pr.ring : null;
        }

        public static Material TMPMaterial(TMP_Asset asset)
        {
            return asset != null ? asset.material : null;
        }

        // --- AccessTools.FieldRefAccess replacements (Adofai scripting cctor) ---

        static Dictionary<HitMargin, scrHitTextMesh[]> dummyHitTexts = new Dictionary<HitMargin, scrHitTextMesh[]>();

        // Old field lived on scrController; it moved to scrHitTextManager (and went
        // non-public, so it needs its own FieldRef). The delegate still receives a
        // scrController (ignored) and returns a ref to the real dict.
        static AccessTools.FieldRef<scrHitTextManager, Dictionary<HitMargin, scrHitTextMesh[]>> htmCachedHitTexts;

        static ref Dictionary<HitMargin, scrHitTextMesh[]> CachedHitTextsRef(scrController unused)
        {
            scrPlayerManager pm = scrPlayerManager.instance;
            scrHitTextManager htm = pm != null ? pm.hitTextManager : null;
            if (htm != null)
            {
                if (htmCachedHitTexts == null)
                    htmCachedHitTexts = AccessTools.FieldRefAccess<scrHitTextManager, Dictionary<HitMargin, scrHitTextMesh[]>>("cachedHitTexts");
                return ref htmCachedHitTexts(htm);
            }
            return ref dummyHitTexts;
        }

        public static AccessTools.FieldRef<scrController, Dictionary<HitMargin, scrHitTextMesh[]>> CachedHitTexts(string unused)
        {
            return CachedHitTextsRef;
        }

        // scrHitTextMesh.text changed TextMesh -> TextMeshPro, so a TextMesh FieldRef can no
        // longer alias it. The stored delegate is never invoked after patching (SetJudgeText
        // is rewritten to call SetHitText below); this only keeps the cctor from throwing.
        static TextMesh unusedTextMesh;

        static ref TextMesh SHTMTextRef(scrHitTextMesh unused)
        {
            return ref unusedTextMesh;
        }

        public static AccessTools.FieldRef<scrHitTextMesh, TextMesh> SHTMText(string unused)
        {
            return SHTMTextRef;
        }

        // Functional replacement used by the rewritten SetJudgeText lambda.
        public static void SetHitText(scrHitTextMesh mesh, string value)
        {
            if (mesh == null) return;
            TextMeshPro t = mesh.text;
            if (t != null) t.text = value;
        }

        // --- ADOFAI r150 Accuracy calculation (fixes Midspin / FailedFloor / Auto / playerHitMarginCount) ---

        static AccessTools.FieldRef<scrMarginTracker, HitMargin[]> trackerNotCountedRef;

        static HitMargin[] GetNotCountedHitMargins(scrMarginTracker t)
        {
            if (t == null) return null;
            if (trackerNotCountedRef == null)
            {
                try { trackerNotCountedRef = AccessTools.FieldRefAccess<scrMarginTracker, HitMargin[]>("_notCountedHitMargins"); }
                catch { }
            }
            return trackerNotCountedRef != null ? trackerNotCountedRef(t) : null;
        }

        public static void CalcAccuracy(
            scrMistakesManager mm,
            out double accuracy,
            out double xAccuracy,
            out double absXAccuracy,
            out double maxAccuracy,
            out double maxXAccuracy,
            out double absMaxXAccuracy)
        {
            accuracy = double.NaN;
            xAccuracy = double.NaN;
            absXAccuracy = double.NaN;
            maxAccuracy = double.NaN;
            maxXAccuracy = double.NaN;
            absMaxXAccuracy = double.NaN;

            double cpFactor = scnEditor.instance ? 1.0 : Math.Pow(0.9875, scrController.checkpointsUsed);

            if (scrController.coopMode && mm != null)
            {
                accuracy = mm.percentAcc * 100.0;
                xAccuracy = mm.percentXAcc * 100.0;
                absXAccuracy = cpFactor > 0.0 ? xAccuracy / cpFactor : xAccuracy;
                absMaxXAccuracy = mm.maxPossibleXAcc * 100.0;
                maxXAccuracy = absMaxXAccuracy * cpFactor;
                maxAccuracy = accuracy;
                return;
            }

            scrMarginTracker t = Tracker();
            if (t == null) return;

            int perfectHits = t.GetHits(HitMarginHelper.PerfectHitMargins);
            int semiPerfectHits = t.GetHits(HitMarginHelper.SemiPerfectHitMargins);
            int failedFloors = t.GetHits(HitMargin.FailedFloor);
            int good = perfectHits + semiPerfectHits;
            int hitCount = t.hitMargins != null ? t.hitMargins.Count : 0;
            int total = hitCount + t.GetDeaths();

            double baseAcc = (good == total) ? 1.0 : (double)good / total;
            double bonus = (perfectHits - failedFloors) * 0.0001;
            accuracy = 100.0 * (baseAcc + bonus);

            double weightedHits = t.GetHitsWithWeights(HitMarginHelper.PlayerHitMarginTypes);
            int xDenom = t.playerHitMarginCount + failedFloors;
            absXAccuracy = xDenom > 0 ? 100.0 * (weightedHits / xDenom) : double.NaN;
            xAccuracy = absXAccuracy * cpFactor;

            scrLevelMaker lm = ADOBase.lm;
            scrController ctrl = scrController.instance;
            if (lm != null && lm.listFloors != null && ctrl != null)
            {
                int currSeq = ctrl.currentSeqID;
                int floorCount = lm.listFloors.Count;
                if (currSeq >= 0 && currSeq < floorCount && lm.listFloors[currSeq] != null)
                {
                    int leftTiles = (floorCount - currSeq) - (lm.listFloors[currSeq].midSpin ? 1 : 0);
                    if (leftTiles < 0) leftTiles = 0;

                    int maxGood = good + leftTiles;
                    int maxTotal = total + leftTiles;
                    double maxBase = (maxGood == maxTotal) ? 1.0 : (double)maxGood / maxTotal;
                    double maxBonus = (perfectHits + leftTiles - failedFloors) * 0.0001;
                    maxAccuracy = 100.0 * (maxBase + maxBonus);
                }

                if (lm.PlayerHitFloors != null)
                {
                    HitMargin[] notCounted = GetNotCountedHitMargins(t);
                    int notCountedHits = notCounted != null ? t.GetHits(notCounted) : 0;
                    int remX = lm.PlayerHitFloors.Count - t.playerHitMarginCount + notCountedHits - t.startSeqID + failedFloors;
                    int maxXDenom = t.playerHitMarginCount + remX;
                    if (maxXDenom > 0)
                    {
                        absMaxXAccuracy = 100.0 * ((weightedHits + remX) / maxXDenom);
                        maxXAccuracy = absMaxXAccuracy * cpFactor;
                    }
                    else
                    {
                        absMaxXAccuracy = t.maxPossibleXAcc * 100.0;
                        maxXAccuracy = absMaxXAccuracy * cpFactor;
                    }
                }
            }
        }

        // --- ADOFAI r150 HitMargin bridge (GetHitMarginInDeg + GetHitMarginInSec) ---

        [ThreadStatic]
        static bool inInternalHitCalc;

        static Harmony r150Harmony;
        static bool overlayerBound;

        static FieldInfo fHitLenient, fHitNormal, fHitStrict, fHitCurrent;
        static Action<Difficulty, HitMargin> dHitIncreaseCount;
        static Action<HitMargin> dHitIncreaseCCount;

        static FieldInfo fCombo, fMaxCombo;
        static Action<Difficulty, HitMargin> dCombosSet;
        static Action dSetMarginCombos;

        static Action<HitMargin, HitMargin, HitMargin, HitMargin> dSetScores;

        static IDictionary lazyPatchesDict;
        static PropertyInfo pLazyPatchPatched;

        public static HitMargin FoldToLegacy(HitMargin raw)
        {
            switch ((int)raw)
            {
                case 0: return (HitMargin)0;  // TooEarly
                case 1: return (HitMargin)1;  // VeryEarly
                case 2: return (HitMargin)2;  // EarlyPerfect
                case 3:                       // PerfectMinus
                case 4:                       // XPerfect
                case 5: return (HitMargin)3;  // PerfectPlus -> legacy Perfect(3)
                case 6: return (HitMargin)4;  // LatePerfect -> legacy 4
                case 7: return (HitMargin)5;  // VeryLate -> legacy 5
                case 8: return (HitMargin)6;  // TooLate -> legacy 6
                case 9: return (HitMargin)7;  // Multipress -> legacy 7
                case 10: return (HitMargin)8; // FailMiss -> legacy 8
                case 11: return (HitMargin)9; // FailOverload -> legacy 9
                case 12: return (HitMargin)10;// Auto -> legacy 10
                case 13: return (HitMargin)11;// OverPress -> legacy 11
                default: return raw;          // Midspin(14), FailedFloor(15)
            }
        }

        public static bool IsPerfectRaw(HitMargin raw)
        {
            int v = (int)raw;
            return v == 3 || v == 4 || v == 5;
        }

        static HitMargin FixRawMargin(scrController ctrl, HitMargin m)
        {
            if (ctrl != null && ctrl.gameworld)
            {
                if (ctrl.noFailInfiniteMargin) return HitMargin.FailMiss;
                if (MidspinInfiniteMargin(ctrl) || (RDC.auto && !RDC.useOldAuto)) return HitMargin.XPerfect;
            }
            return m;
        }

        static void EnsureOverlayerBindings(Assembly asm)
        {
            if (overlayerBound) return;
            if (asm == null)
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.GetName().Name == "Overlayer") { asm = a; break; }
                }
            }
            if (asm == null) return;

            try
            {
                BindingFlags S = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                Type tHit = asm.GetType("Overlayer.Tags.Hit");
                if (tHit != null)
                {
                    fHitLenient = tHit.GetField("Lenient", S);
                    fHitNormal = tHit.GetField("Normal", S);
                    fHitStrict = tHit.GetField("Strict", S);
                    fHitCurrent = tHit.GetField("Current", S);
                    MethodInfo mInc = tHit.GetMethod("IncreaseCount", S);
                    MethodInfo mIncC = tHit.GetMethod("IncreaseCCount", S);
                    if (mInc != null) dHitIncreaseCount = (Action<Difficulty, HitMargin>)Delegate.CreateDelegate(typeof(Action<Difficulty, HitMargin>), mInc);
                    if (mIncC != null) dHitIncreaseCCount = (Action<HitMargin>)Delegate.CreateDelegate(typeof(Action<HitMargin>), mIncC);
                }

                Type tCombo = asm.GetType("Overlayer.Tags.ComboStats");
                if (tCombo != null)
                {
                    fCombo = tCombo.GetField("Combo", S);
                    fMaxCombo = tCombo.GetField("MaxCombo", S);
                    MethodInfo mCS = tCombo.GetMethod("Combos_Set", S);
                    MethodInfo mSMC = tCombo.GetMethod("SetMarginCombos", S);
                    if (mCS != null) dCombosSet = (Action<Difficulty, HitMargin>)Delegate.CreateDelegate(typeof(Action<Difficulty, HitMargin>), mCS);
                    if (mSMC != null) dSetMarginCombos = (Action)Delegate.CreateDelegate(typeof(Action), mSMC);
                }

                Type tScores = asm.GetType("Overlayer.Tags.Scores");
                if (tScores != null)
                {
                    MethodInfo mSS = tScores.GetMethod("SetScores", S);
                    if (mSS != null) dSetScores = (Action<HitMargin, HitMargin, HitMargin, HitMargin>)Delegate.CreateDelegate(typeof(Action<HitMargin, HitMargin, HitMargin, HitMargin>), mSS);
                }

                Type tLP = asm.GetType("Overlayer.Core.Patches.LazyPatch");
                if (tLP != null)
                {
                    FieldInfo fPatches = tLP.GetField("Patches", S);
                    lazyPatchesDict = fPatches != null ? fPatches.GetValue(null) as IDictionary : null;
                    pLazyPatchPatched = tLP.GetProperty("Patched", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                }

                overlayerBound = true;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        static bool IsLazyPatched(string id)
        {
            if (lazyPatchesDict == null || pLazyPatchPatched == null) return false;
            if (!lazyPatchesDict.Contains(id)) return false;
            object lp = lazyPatchesDict[id];
            return lp != null && (bool)pLazyPatchPatched.GetValue(lp, null);
        }

        static bool ComputeThreeInDeg(
            Difficulty difficulty, float hitAngle, float refAngle, bool clockwise,
            float floorBpm, float conductorPitch, double marginScale, HitMargin result,
            out HitMargin l, out HitMargin n, out HitMargin s, out HitMargin c, out bool isSafe)
        {
            l = n = s = c = result;
            isSafe = false;
            if (inInternalHitCalc || difficulty != GCS.difficulty) return false;

            scrController ctrl = scrController.instance;
            scrFloor floor = ctrl != null ? ctrl.currFloor : null;
            if (floor != null && floor.freeroam) return false;
            isSafe = floor != null && floor.isSafe;

            inInternalHitCalc = true;
            try
            {
                l = difficulty == Difficulty.Lenient ? result : scrMisc.GetHitMarginInDeg(Difficulty.Lenient, hitAngle, refAngle, clockwise, floorBpm, conductorPitch, marginScale);
                n = difficulty == Difficulty.Normal ? result : scrMisc.GetHitMarginInDeg(Difficulty.Normal, hitAngle, refAngle, clockwise, floorBpm, conductorPitch, marginScale);
                s = difficulty == Difficulty.Strict ? result : scrMisc.GetHitMarginInDeg(Difficulty.Strict, hitAngle, refAngle, clockwise, floorBpm, conductorPitch, marginScale);
            }
            finally
            {
                inInternalHitCalc = false;
            }

            l = FixRawMargin(ctrl, l);
            n = FixRawMargin(ctrl, n);
            s = FixRawMargin(ctrl, s);
            c = GCS.difficulty == Difficulty.Lenient ? l : (GCS.difficulty == Difficulty.Normal ? n : s);
            return true;
        }

        static bool ComputeThreeInSec(
            Difficulty difficulty, double timeDiff,
            float floorBpm, float conductorPitch, double marginScale, HitMargin result,
            out HitMargin l, out HitMargin n, out HitMargin s, out HitMargin c, out bool isSafe)
        {
            l = n = s = c = result;
            isSafe = false;
            if (inInternalHitCalc || difficulty != GCS.difficulty) return false;

            scrController ctrl = scrController.instance;
            scrFloor floor = ctrl != null ? ctrl.currFloor : null;
            if (floor != null && floor.freeroam) return false;
            isSafe = floor != null && floor.isSafe;

            inInternalHitCalc = true;
            try
            {
                l = difficulty == Difficulty.Lenient ? result : scrMisc.GetHitMarginInSec(Difficulty.Lenient, timeDiff, floorBpm, conductorPitch, marginScale);
                n = difficulty == Difficulty.Normal ? result : scrMisc.GetHitMarginInSec(Difficulty.Normal, timeDiff, floorBpm, conductorPitch, marginScale);
                s = difficulty == Difficulty.Strict ? result : scrMisc.GetHitMarginInSec(Difficulty.Strict, timeDiff, floorBpm, conductorPitch, marginScale);
            }
            finally
            {
                inInternalHitCalc = false;
            }

            l = FixRawMargin(ctrl, l);
            n = FixRawMargin(ctrl, n);
            s = FixRawMargin(ctrl, s);
            c = GCS.difficulty == Difficulty.Lenient ? l : (GCS.difficulty == Difficulty.Normal ? n : s);
            return true;
        }

        static void SetHitFields(HitMargin l, HitMargin n, HitMargin s, HitMargin c)
        {
            if (fHitLenient != null) fHitLenient.SetValue(null, l);
            if (fHitNormal != null) fHitNormal.SetValue(null, n);
            if (fHitStrict != null) fHitStrict.SetValue(null, s);
            if (fHitCurrent != null) fHitCurrent.SetValue(null, c);
        }

        static void ApplyHit(HitMargin l, HitMargin n, HitMargin s, HitMargin c, bool isSafe)
        {
            EnsureOverlayerBindings(null);
            SetHitFields(l, n, s, c);
            if (!isSafe)
            {
                if (dHitIncreaseCount != null)
                {
                    dHitIncreaseCount(Difficulty.Lenient, FoldToLegacy(l));
                    dHitIncreaseCount(Difficulty.Normal, FoldToLegacy(n));
                    dHitIncreaseCount(Difficulty.Strict, FoldToLegacy(s));
                }
                if (dHitIncreaseCCount != null)
                {
                    dHitIncreaseCCount(FoldToLegacy(c));
                }
            }
        }

        static void ApplyCombo(HitMargin l, HitMargin n, HitMargin s, HitMargin c, bool isSafe)
        {
            if (isSafe) return;
            EnsureOverlayerBindings(null);
            // Keep Hit.Lenient/Normal/Strict fresh in case only Combo tags are enabled
            if (!IsLazyPatched("Tags.P_scrMisc.Hit__GetHitMargin"))
                SetHitFields(l, n, s, c);

            if (fCombo != null && fMaxCombo != null)
            {
                if (IsPerfectRaw(c))
                {
                    int next = (int)fCombo.GetValue(null) + 1;
                    fCombo.SetValue(null, next);
                    int max = (int)fMaxCombo.GetValue(null);
                    if (next > max) fMaxCombo.SetValue(null, next);
                }
                else
                {
                    fCombo.SetValue(null, 0);
                }
            }
            if (dCombosSet != null)
            {
                dCombosSet(Difficulty.Lenient, l);
                dCombosSet(Difficulty.Normal, n);
                dCombosSet(Difficulty.Strict, s);
            }
            if (dSetMarginCombos != null)
                dSetMarginCombos();
        }

        static void ApplyScores(HitMargin l, HitMargin n, HitMargin s, HitMargin c, bool isSafe)
        {
            if (isSafe) return;
            EnsureOverlayerBindings(null);
            if (dSetScores != null)
                dSetScores(FoldToLegacy(l), FoldToLegacy(n), FoldToLegacy(s), FoldToLegacy(c));
        }

        public static void OnHitInDeg(Difficulty difficulty, float hitAngle, float refAngle, bool clockwise, float floorBpm, float conductorPitch, double marginScale, HitMargin result)
        {
            if (ComputeThreeInDeg(difficulty, hitAngle, refAngle, clockwise, floorBpm, conductorPitch, marginScale, result, out var l, out var n, out var s, out var c, out var isSafe))
                ApplyHit(l, n, s, c, isSafe);
        }

        public static void OnComboInDeg(Difficulty difficulty, float hitAngle, float refAngle, bool clockwise, float floorBpm, float conductorPitch, double marginScale, HitMargin result)
        {
            if (ComputeThreeInDeg(difficulty, hitAngle, refAngle, clockwise, floorBpm, conductorPitch, marginScale, result, out var l, out var n, out var s, out var c, out var isSafe))
                ApplyCombo(l, n, s, c, isSafe);
        }

        public static void OnScoresInDeg(Difficulty difficulty, float hitAngle, float refAngle, bool clockwise, float floorBpm, float conductorPitch, double marginScale, HitMargin result)
        {
            if (ComputeThreeInDeg(difficulty, hitAngle, refAngle, clockwise, floorBpm, conductorPitch, marginScale, result, out var l, out var n, out var s, out var c, out var isSafe))
                ApplyScores(l, n, s, c, isSafe);
        }

        static void GetHitMarginInSecPostfix(Difficulty difficulty, double timeDiff, float floorBpm, float conductorPitch, double marginScale, HitMargin __result)
        {
            if (!ComputeThreeInSec(difficulty, timeDiff, floorBpm, conductorPitch, marginScale, __result, out var l, out var n, out var s, out var c, out var isSafe))
                return;

            EnsureOverlayerBindings(null);
            if (IsLazyPatched("Tags.P_scrMisc.Hit__GetHitMargin"))
                ApplyHit(l, n, s, c, isSafe);
            if (IsLazyPatched("Tags.P_scrMisc.ComboStats__GetHitMargin"))
                ApplyCombo(l, n, s, c, isSafe);
            if (IsLazyPatched("Tags.P_scrMisc.Scores__GetHitMargin"))
                ApplyScores(l, n, s, c, isSafe);
        }

        public static void SyncR150Hooks(bool enabled, Assembly overlayerAsm)
        {
            try
            {
                if (enabled)
                {
                    overlayerBound = false;
                    EnsureOverlayerBindings(overlayerAsm);
                    if (r150Harmony == null)
                        r150Harmony = new Harmony("Overlayer.Unity6Compat.R150");
                    r150Harmony.UnpatchAll(r150Harmony.Id);
                    MethodInfo secMethod = typeof(scrMisc).GetMethod("GetHitMarginInSec", BindingFlags.Public | BindingFlags.Static);
                    MethodInfo postfix = typeof(Compat).GetMethod(nameof(GetHitMarginInSecPostfix), BindingFlags.NonPublic | BindingFlags.Static);
                    if (secMethod != null && postfix != null)
                    {
                        r150Harmony.Patch(secMethod, postfix: new HarmonyMethod(postfix));
                    }
                }
                else
                {
                    if (r150Harmony != null)
                        r150Harmony.UnpatchAll(r150Harmony.Id);
                    overlayerBound = false;
                    lazyPatchesDict = null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }
}
