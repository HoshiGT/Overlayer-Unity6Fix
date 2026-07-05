// Overlayer.Unity6Compat — compatibility shim for Overlayer 3.49.0 on ADOFAI Unity 6 (r145+)
// Loaded from Overlayer/lib/ by Overlayer.Bootstrapper. The patched Overlayer.dll
// redirects broken game-member references here.
using System.Collections.Generic;
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

        static readonly int[] emptyCounts = new int[64];
        static readonly List<HitMargin> emptyMargins = new List<HitMargin>();

        static scrMarginTracker Tracker()
        {
            scrMarginTracker[] ts = scrMistakesManager.marginTrackers;
            return ts != null && ts.Length > 0 ? ts[0] : null;
        }

        public static int[] HitMarginsCount()
        {
            scrMarginTracker t = Tracker();
            return t != null && t.hitMarginsCount != null ? t.hitMarginsCount : emptyCounts;
        }

        public static List<HitMargin> HitMargins()
        {
            scrMarginTracker t = Tracker();
            return t != null && t.hitMargins != null ? t.hitMargins : emptyMargins;
        }

        public static int GetHits(scrMistakesManager unused, HitMargin margin)
        {
            scrMarginTracker t = Tracker();
            return t != null ? t.GetHits(margin) : 0;
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
    }
}
