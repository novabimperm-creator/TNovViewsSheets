using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TNovCommon;

namespace TNovViewsSheets
{
    /// <summary>
    /// Merges several per-sheet DWG files into one consolidated DWG using AutoCAD's
    /// COM automation.
    ///
    /// Late binding (reflection / IDispatch) is used deliberately so this assembly
    /// does NOT need a build-time reference to a specific AutoCAD interop DLL —
    /// it works with whatever AutoCAD version is installed at runtime.
    ///
    /// Why we don't just InsertBlock the source DWGs directly:
    ///   Revit puts the title block in the sheet DWG's PAPER SPACE, with viewports
    ///   referencing the view geometry in MODEL SPACE. AcadModelSpace.InsertBlock
    ///   only pulls the source's Model Space, so the title block is dropped and
    ///   layouts come out empty/half-empty.
    ///
    /// What we do instead:
    ///   For each source DWG, use AutoCAD's EXPORTLAYOUT command to "bake" the
    ///   sheet layout — title block, viewport frames AND the geometry visible
    ///   through the viewports — into a flat single-space DWG (everything in the
    ///   new file's Model Space). Then the simple InsertBlock approach gives us
    ///   the full sheet as a single block reference.
    ///
    /// Note on accoreconsole: we previously tried switching to headless
    /// accoreconsole.exe to avoid the GUI flash per sheet. It can't be used —
    /// EXPORTLAYOUT crashes accoreconsole with an access violation (the command
    /// needs GUI internals). COM-launched AutoCAD with Visible=false is the
    /// best available headless approximation.
    /// </summary>
    public class AutoCadMerger
    {
        private const string ProgIdAcad = "AutoCAD.Application";
        private const double TileGapMm = 50.0; // visual gap between tiled sheets
        private const double GridAspect = 1.4; // target columns/rows ratio for tiling

        // A3 landscape mm — last-resort footprint when AutoCAD cannot compute extents
        // for an inserted block (eNullExtents). Lets one bad sheet not break tiling.
        private static readonly double[] FallbackMin = { 0.0, 0.0, 0.0 };
        private static readonly double[] FallbackMax = { 420.0, 297.0, 0.0 };

        public bool IsAutoCadAvailable() => Type.GetTypeFromProgID(ProgIdAcad) != null;

        public void Merge(IList<string> dwgFiles, string outputDwgPath, ExportMode mode)
        {
            if (dwgFiles == null || dwgFiles.Count == 0)
                throw new ArgumentException("No DWG files to merge.", nameof(dwgFiles));
            if (string.IsNullOrWhiteSpace(outputDwgPath))
                throw new ArgumentException("Output path is empty.", nameof(outputDwgPath));

            Type acadType = Type.GetTypeFromProgID(ProgIdAcad)
                ?? throw new InvalidOperationException("AutoCAD COM ProgID not registered. Is AutoCAD installed?");

            object acadApp = null;
            object newDoc = null;
            int acadPid = 0;
            CancellationTokenSource watcherCts = null;
            var tempFlats = new List<string>(); // temp flat DWGs to clean up at the end
            var sheetNames = new List<string>(dwgFiles.Count); // for layout naming, parallel to tempFlats
            var tempExtents = new List<(double[] Min, double[] Max)>(dwgFiles.Count); // for tiling, parallel to tempFlats

            try
            {
                Logger.Log($"AutoCadMerger.Merge mode={mode} files={dwgFiles.Count} -> {outputDwgPath}");

                // Always launch a fresh, dedicated AutoCAD instance instead of attaching
                // to a running one. Attaching is risky: if the user has AutoCAD open with
                // their own work, we'd be hiding their window and mutating their session
                // — and if the previous merge crashed, GetActiveObject would hand us a
                // half-broken instance that hangs the next run.
                var preExisting = new HashSet<int>(
                    Process.GetProcessesByName("acad").Select(p => p.Id));
                Logger.Log($"pre-existing acad.exe PIDs: [{string.Join(",", preExisting)}]");
                acadApp = Activator.CreateInstance(acadType);
                Logger.Log("Activator.CreateInstance(acadType) OK");
                // Identify *our* acad.exe by set-difference from before launch — the new
                // PID is the one to scope the dialog watcher to.
                acadPid = Process.GetProcessesByName("acad")
                    .Select(p => p.Id)
                    .FirstOrDefault(id => !preExisting.Contains(id));
                Logger.Log($"our acad PID: {acadPid}");

                Set(acadApp, "Visible", false);
                Logger.Log("Visible=false set");
                // NOTE: do NOT minimize via WindowState here. On AutoCAD 2021/2022
                // an early WindowState change while the splash is still active can
                // trip an exit path where AutoCAD terminates a few hundred ms later
                // (user-observed: "Открывается автокад и потом ничего, он
                // просто закрывается"). Visible=false plus the synchronous Win32
                // pass below is enough.

                // Background watcher: AutoCAD 2021's EXPORTLAYOUT pops a modal
                // "Файл создан. Открыть его?" dialog AFTER writing the temp DWG,
                // and that's NOT suppressed by FILEDIA/EXPERT or any documented
                // sysvar. While the dialog sits up, AutoCAD's command processor
                // is blocked — so subsequent Documents.Add calls fail with
                // "Ошибка файлера" until it's dismissed. The watcher also keeps
                // hiding the main MDI frame in case Visible=false didn't stick.
                if (acadPid != 0)
                {
                    // Synchronous first pass — Visible=false above is honored only
                    // after the message-pump cycle, so the main window is briefly
                    // visible. Force-hide it via Win32 immediately.
                    HideAndDismiss(acadPid);
                    watcherCts = new CancellationTokenSource();
                    StartDialogWatcher(acadPid, watcherCts.Token);
                    Logger.Log("watcher started");
                }

                object documents = Get(acadApp, "Documents");
                Logger.Log("got Documents collection");

                // Set system variables once on the initial Drawing1 document before
                // opening any sheet DWG. These are global — setting them via any open
                // document persists for the whole session.
                //
                //   FILEDIA=0   — Save-As dialog stays away.
                //   EXPERT=5    — skip "are you sure" warnings (e.g. EXPORTLAYOUT).
                //   CMDECHO=0   — quieter command stream.
                //   DWGCHECK=0  — CRITICAL: suppresses the modal "This drawing was last
                //                 saved by an unrecognized application" warning that
                //                 AutoCAD pops on every Documents.Open of a Revit-
                //                 produced DWG. Without this, the user has to click OK
                //                 once per sheet — no headless operation is possible.
                //   SDI=0       — keep multi-doc interface so we can run EXPORTLAYOUT
                //                 on one doc while the merged target stays open.
                //   SECURELOAD=0 — don't prompt before loading external content from
                //                 the temp folder where Revit dropped the sheets.
                ApplyHeadlessSysVars(documents);

                // Phase 1: flatten every source DWG via EXPORTLAYOUT, and record each
                // flat file's content extents (used by the tiling layout in Phase 2).
                foreach (string file in dwgFiles)
                {
                    if (!File.Exists(file)) continue;
                    string flat = FlattenLayoutToTemp(acadApp, documents, file,
                        out double[] minE, out double[] maxE);
                    if (flat == null) continue;

                    tempFlats.Add(flat);
                    sheetNames.Add(Path.GetFileNameWithoutExtension(file));
                    tempExtents.Add((minE, maxE));
                }

                if (tempFlats.Count == 0)
                    throw new InvalidOperationException(
                        "EXPORTLAYOUT produced no output for any of the sheet DWGs. " +
                        "Check that the source files have a non-empty paperspace layout.");

                // Phase 2: build the merged document from the flat temp files.
                newDoc = CreateBlankTargetDoc(documents);

                if (mode == ExportMode.MultiLayout)
                    BuildMultiLayout(newDoc, tempFlats, sheetNames, tempExtents);
                else
                    BuildTiledModelSpace(newDoc, tempFlats, tempExtents);

                // The "-NPLT" suffix marks Revit's non-plotting view crop frames /
                // viewport boundaries. After flatten+InsertBlock they end up as
                // visible magenta rectangles in Model Space; turn the layer Off so
                // they don't show but stay in the file if the user needs them later.
                TurnOffNonPlotLayers(newDoc);

                Invoke(newDoc, "SaveAs", outputDwgPath);
            }
            catch (Exception ex)
            {
                Logger.Log("Merge "+ex.Message,4);
                throw;
            }
            finally
            {
                Logger.Log("Merge finally: cleanup start");
                try { watcherCts?.Cancel(); } catch { /* ignore */ }
                try { if (newDoc != null) Invoke(newDoc, "Close", false); } catch { /* ignore */ }
                if (acadApp != null)
                {
                    try { Invoke(acadApp, "Quit"); } catch { /* ignore */ }
                    try { Marshal.FinalReleaseComObject(acadApp); } catch { /* ignore */ }
                }

                foreach (string t in tempFlats)
                {
                    try { if (File.Exists(t)) File.Delete(t); } catch { /* leave it for the OS */ }
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                Logger.Log("Merge finally: cleanup done");
            }
        }

        private static void ApplyHeadlessSysVars(object documents)
        {
            // The sysvars below are global once set on any open document, but the
            // initial Drawing1 may not have fully initialized yet. Best effort —
            // ignore individual failures and rely on a second pass per opened doc
            // inside FlattenLayoutToTemp.
            try
            {
                int initialCount = (int)Get(documents, "Count");
                if (initialCount <= 0) return;
                object initial = Invoke(documents, "Item", 0);
                TrySetVar(initial, "FILEDIA", (short)0);
                TrySetVar(initial, "EXPERT", (short)5);
                TrySetVar(initial, "CMDECHO", (short)0);
                TrySetVar(initial, "DWGCHECK", (short)0);
                TrySetVar(initial, "SDI", (short)0);
                TrySetVar(initial, "SECURELOAD", (short)0);
                TrySetVar(initial, "NOMUTT", (short)1);
            }
            catch { /* non-critical */ }
        }

        private static void TrySetVar(object doc, string name, object value)
        {
            try { Invoke(doc, "SetVariable", name, value); } catch { /* ignore */ }
        }

        /// <summary>
        /// Create the merged target as a blank doc. Documents.Add("") used to be
        /// enough but on some installations it throws COMException "Ошибка
        /// файлера" — usually because the default-template setting in the user's
        /// Options points at a missing file. Try no-args first, then a known
        /// shipped template path, then "" as a last resort, surfacing all errors
        /// if everything fails.
        /// </summary>
        private static object CreateBlankTargetDoc(object documents)
        {
            var attempts = new List<Func<object>>
            {
                () => Invoke(documents, "Add"),
                () =>
                {
                    string tpl = FindShippedTemplate();
                    return tpl != null ? Invoke(documents, "Add", tpl) : null;
                },
                () => Invoke(documents, "Add", ""),
            };

            var errors = new List<string>();
            foreach (var a in attempts)
            {
                try
                {
                    var doc = a();
                    if (doc != null) return doc;
                }
                catch (Exception ex)
                {
                    errors.Add(ex.GetBaseException().Message);
                }
            }
            throw new InvalidOperationException(
                "Documents.Add не сработал ни в одном варианте. " +
                "Проверьте Options → Files → Drawing Template Settings в AutoCAD. " +
                "Подробно: " + string.Join(" | ", errors));
        }

        private static string FindShippedTemplate()
        {
            // Walk %LOCALAPPDATA%\Autodesk\AutoCAD * for any Template\acadiso.dwt.
            // Different AutoCAD versions and locales live under different child
            // folders; globbing by file name is the robust pick.
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Autodesk");
            if (!Directory.Exists(root)) return null;
            try
            {
                foreach (var acDir in Directory.GetDirectories(root, "AutoCAD *"))
                {
                    foreach (var tpl in Directory.GetFiles(acDir, "acadiso.dwt", SearchOption.AllDirectories))
                        return tpl;
                    foreach (var tpl in Directory.GetFiles(acDir, "acad.dwt", SearchOption.AllDirectories))
                        return tpl;
                }
            }
            catch { /* permission / IO issues — give up gracefully */ }
            return null;
        }

        // ---------- Phase 1: EXPORTLAYOUT each source sheet to a flat temp DWG ----------

        private static string FlattenLayoutToTemp(object acadApp, object documents, string sourceFile,
            out double[] minExtents, out double[] maxExtents)
        {
            minExtents = null;
            maxExtents = null;

            object sourceDoc = null;
            short prevFiledia = 1;
            bool restoreFiledia = false;

            try
            {
                Logger.Log($"Open: {sourceFile}");
                sourceDoc = Invoke(documents, "Open", sourceFile, true);
                Logger.Log("Open OK");

                // Some AutoCAD builds re-show the main window on Documents.Open
                // even when Visible was previously false. Re-hide via Visible
                // only — see comment in Merge() for why WindowState changes are
                // intentionally not used here.
                try { Set(acadApp, "Visible", false); } catch { /* ignore */ }

                // EXPORTLAYOUT acts on the ACTIVE document; make sure that's us.
                try { Invoke(sourceDoc, "Activate"); } catch { /* some versions auto-activate on Open */ }

                // Pick the sheet layout (first non-Model layout) and make it active —
                // EXPORTLAYOUT bakes the *current* layout.
                object sheetLayout = FindSheetLayout(sourceDoc);
                if (sheetLayout == null) return null;
                try { Set(sourceDoc, "ActiveLayout", sheetLayout); } catch { return null; }

                // Suppress UI on this doc as well — DWGCHECK and others can have
                // already fired on Open, but re-apply for any subsequent prompts.
                try
                {
                    object cur = Invoke(sourceDoc, "GetVariable", "FILEDIA");
                    prevFiledia = Convert.ToInt16(cur);
                    Invoke(sourceDoc, "SetVariable", "FILEDIA", (short)0);
                    restoreFiledia = true;
                }
                catch { /* old version without GetVariable on doc — fall through */ }

                TrySetVar(sourceDoc, "CMDECHO", (short)0);
                TrySetVar(sourceDoc, "EXPERT", (short)5);
                TrySetVar(sourceDoc, "DWGCHECK", (short)0);
                TrySetVar(sourceDoc, "NOMUTT", (short)1);

                string tempPath = Path.Combine(Path.GetTempPath(),
                    "rsheet_" + Guid.NewGuid().ToString("N") + ".dwg");

                // SendCommand uses scripting syntax: spaces and \n act as ENTER between
                // arguments. The "_." prefix forces the English, undefined-redefine-safe
                // form so localized AutoCAD doesn't reject the command name.
                // Quoting the path with " handles spaces in temp paths.
                //
                // Trailing extra \n's accept defaults for any follow-up prompt the
                // running AutoCAD build adds (e.g. "Open exported file? [Y/N]" or a
                // visual-fidelity warning). Without these, on AutoCAD 2023 Russian the
                // prompt blocks the pipeline and the user has to confirm each sheet.
                string cmd = "_.EXPORTLAYOUT \"" + tempPath + "\"\n\n\n";
                Logger.Log($"SendCommand EXPORTLAYOUT -> {tempPath}");
                Invoke(sourceDoc, "SendCommand", cmd);

                // SendCommand is asynchronous — wait until the file appears and stops
                // growing before we treat it as ready.
                if (!WaitForStableFile(tempPath, TimeSpan.FromSeconds(60)))
                {
                    Logger.Log("EXPORTLAYOUT timeout — temp file not stable in 60s");
                    return null;
                }
                Logger.Log($"EXPORTLAYOUT OK, size={SafeFileSize(tempPath)}");

                // Some AutoCAD versions auto-open the exported file in a new doc.
                // Close it so it doesn't pile up across sheets and so we can later
                // re-Open it read-only to read EXTMIN/EXTMAX.
                CloseDocByPath(documents, tempPath);

                // Read the flat file's real model-space extents now, while we still
                // have AutoCAD open. The tiling layout needs them, and reading
                // EXTMIN/EXTMAX via GetVariable is more reliable than calling
                // AcadEntity.GetBoundingBox on the inserted block through late-bound
                // reflection (the byref-out marshaling is fragile and silently
                // returns nothing, which makes every sheet share the A3 fallback
                // footprint and overlap on tiling).
                TryReadModelExtents(documents, tempPath, out minExtents, out maxExtents);

                return tempPath;
            }
            catch (Exception ex)
            {
                Logger.Log($"FlattenLayoutToTemp({sourceFile}) {ex.Message}",4);
                return null;
            }
            finally
            {
                if (sourceDoc != null)
                {
                    if (restoreFiledia)
                    {
                        try { Invoke(sourceDoc, "SetVariable", "FILEDIA", prevFiledia); } catch { /* ignore */ }
                    }
                    try { Invoke(sourceDoc, "Close", false); } catch { /* ignore */ }
                }
            }
        }

        private static object FindSheetLayout(object doc)
        {
            // Revit-exported sheet DWGs have two layouts: "Model" (the model space
            // pseudo-layout) and one paperspace layout that holds the title block +
            // viewports. Find that one — preferring a non-empty layout if there
            // happen to be several.
            object layouts = Get(doc, "Layouts");
            int count;
            try { count = (int)Get(layouts, "Count"); } catch { return null; }

            object firstNonModel = null;
            for (int i = 0; i < count; i++)
            {
                object layout;
                try { layout = Invoke(layouts, "Item", i); } catch { continue; }

                string name;
                try { name = (string)Get(layout, "Name"); } catch { continue; }
                if (string.Equals(name, "Model", StringComparison.OrdinalIgnoreCase)) continue;

                if (firstNonModel == null) firstNonModel = layout;

                try
                {
                    int blockCount = (int)Get(Get(layout, "Block"), "Count");
                    if (blockCount > 0) return layout;
                }
                catch { /* fall through */ }
            }

            return firstNonModel;
        }

        private static bool WaitForStableFile(string path, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            long lastSize = -1;
            DateTime stableSince = DateTime.MinValue;

            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(path))
                {
                    long size;
                    try { size = new FileInfo(path).Length; }
                    catch { size = -1; }

                    if (size > 0)
                    {
                        if (size == lastSize)
                        {
                            // ~500 ms of unchanging size = AutoCAD is done writing.
                            if (DateTime.UtcNow - stableSince > TimeSpan.FromMilliseconds(500))
                                return true;
                        }
                        else
                        {
                            lastSize = size;
                            stableSince = DateTime.UtcNow;
                        }
                    }
                }
                Thread.Sleep(100);
            }

            return File.Exists(path) && SafeFileSize(path) > 0;
        }

        private static long SafeFileSize(string path)
        {
            try { return new FileInfo(path).Length; }
            catch { return 0; }
        }

        private static void TryReadModelExtents(object documents, string flatPath,
            out double[] minPt, out double[] maxPt)
        {
            // Open the just-flattened temp DWG read-only and read its EXTMIN/EXTMAX
            // sysvars. Regen first to make sure the engine has computed real extents
            // (the saved header values can be the AutoCAD "uninitialized" sentinel
            // ±1e20).
            minPt = null;
            maxPt = null;

            object doc = null;
            try
            {
                doc = Invoke(documents, "Open", flatPath, true);
                try { Invoke(doc, "Regen", 0); } catch { /* header values may already be valid */ }

                double[] mn = TryToDoubleArray(Invoke(doc, "GetVariable", "EXTMIN"));
                double[] mx = TryToDoubleArray(Invoke(doc, "GetVariable", "EXTMAX"));

                if (mn != null && mx != null
                    && Math.Abs(mn[0]) < 1e19 && Math.Abs(mx[0]) < 1e19
                    && mx[0] - mn[0] > 0 && mx[1] - mn[1] > 0)
                {
                    minPt = mn;
                    maxPt = mx;
                }
            }
            catch { /* leave nulls — caller falls back to A3 */ }
            finally
            {
                if (doc != null)
                {
                    try { Invoke(doc, "Close", false); } catch { /* ignore */ }
                }
            }
        }

        // ---------- Phase 2A: Each flattened sheet on its own Layout ----------

        private void BuildMultiLayout(object doc, IList<string> flatFiles, IList<string> sheetNames,
            IList<(double[] Min, double[] Max)> sheetExtents)
        {
            object layouts = Get(doc, "Layouts");
            object firstUserLayout = null;

            for (int i = 0; i < flatFiles.Count; i++)
            {
                string file = flatFiles[i];
                if (!File.Exists(file)) continue;

                string layoutName = MakeUniqueLayoutName(layouts, sheetNames[i], i + 1);

                object layout = Invoke(layouts, "Add", layoutName);
                Set(doc, "ActiveLayout", layout);
                if (firstUserLayout == null) firstUserLayout = layout;

                // Size the new layout's paper to the source sheet's bbox BEFORE
                // inserting the flat block. The default new layout is A4 — bigger
                // sheets get visually clipped at the paper boundary even though
                // the geometry itself is at correct scale, and any subsequent
                // plot scales the oversized content to fit. Anchoring to the
                // flat file's EXTMIN/EXTMAX gives the best paper-size match.
                if (i < sheetExtents.Count)
                    TryApplyLayoutPaperSize(layout, sheetExtents[i].Min, sheetExtents[i].Max);

                object paperSpace = Get(doc, "PaperSpace");
                double[] origin = { 0.0, 0.0, 0.0 };
                Invoke(paperSpace, "InsertBlock", origin, file, 1.0, 1.0, 1.0, 0.0);

                TryRegen(doc);

                try { Invoke(Get(doc, "Application"), "ZoomExtents"); }
                catch { /* non-critical */ }
            }

            // The new doc still has the default empty Layout1/Layout2 (called Лист1/Лист2
            // on localized AutoCAD). Drop any non-Model layout that's empty — handles
            // both English and localized names without hardcoding either.
            RemoveEmptyNonModelLayouts(layouts);

            if (firstUserLayout != null)
            {
                try { Set(doc, "ActiveLayout", firstUserLayout); } catch { /* non-critical */ }
            }
        }

        // Try to make the new layout's paper match the source sheet bbox so the
        // inserted flat block doesn't bleed past the white paper rectangle in
        // PaperSpace view (and so subsequent plots aren't auto-scaled to fit a
        // default A4). Best-effort: AcadLayout exposes CanonicalMediaName as a
        // settable property but the set of accepted names depends on the
        // ConfigName (plot device). We try a couple of devices and snap the
        // size to the smallest ISO that fits. Failures are swallowed — the
        // layout stays at AutoCAD's default size in that case.
        private static void TryApplyLayoutPaperSize(object layout, double[] minE, double[] maxE)
        {
            if (minE == null || maxE == null) return;
            double widthMm = Math.Max(1.0, maxE[0] - minE[0]);
            double heightMm = Math.Max(1.0, maxE[1] - minE[1]);

            string mediaName = PickCanonicalMediaName(widthMm, heightMm);
            bool landscape = widthMm > heightMm;

            // ConfigName "None" is the device that ships with every install and
            // accepts the broadest set of ISO media names — try it first.
            string[] configs = { "None", "Нет" };
            foreach (string cfg in configs)
            {
                try
                {
                    Set(layout, "ConfigName", cfg);
                    Set(layout, "CanonicalMediaName", mediaName);
                    try { Set(layout, "PaperUnits", (short)1); } catch { /* mm — already default usually */ }
                    try { Set(layout, "PlotRotation", (short)(landscape ? 1 : 0)); } catch { /* read-only on some versions */ }
                    // StandardScale=acScaleToFit=0 keeps subsequent plot 1:1 to paper.
                    try { Set(layout, "StandardScale", (short)0); } catch { /* ignore */ }
                    try { Set(layout, "CenterPlot", true); } catch { /* ignore */ }
                    return; // success
                }
                catch { /* try next config */ }
            }
        }

        private static string PickCanonicalMediaName(double widthMm, double heightMm)
        {
            // Canonical names match AutoCAD's "None" plot device locale-independent
            // media list. Pick the smallest one that fits the inserted bbox.
            double longMm = Math.Max(widthMm, heightMm);
            double shortMm = Math.Min(widthMm, heightMm);
            const double tol = 2.0;
            var iso = new (string Name, double Long, double Short)[]
            {
                ("ISO_A4_(297.00_x_210.00_MM)",  297.0, 210.0),
                ("ISO_A3_(420.00_x_297.00_MM)",  420.0, 297.0),
                ("ISO_A2_(594.00_x_420.00_MM)",  594.0, 420.0),
                ("ISO_A1_(841.00_x_594.00_MM)",  841.0, 594.0),
                ("ISO_A0_(1189.00_x_841.00_MM)", 1189.0, 841.0),
            };
            foreach (var f in iso)
            {
                if (f.Long + tol >= longMm && f.Short + tol >= shortMm)
                    return f.Name;
            }
            return iso[iso.Length - 1].Name;
        }

        private static void RemoveEmptyNonModelLayouts(object layouts)
        {
            int count;
            try { count = (int)Get(layouts, "Count"); } catch { return; }

            // Snapshot first — Delete mutates the collection while we iterate.
            var toRemove = new List<object>();
            for (int i = 0; i < count; i++)
            {
                object layout;
                try { layout = Invoke(layouts, "Item", i); } catch { continue; }

                string name;
                try { name = (string)Get(layout, "Name"); } catch { continue; }
                if (string.Equals(name, "Model", StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    int blockCount = (int)Get(Get(layout, "Block"), "Count");
                    if (blockCount == 0) toRemove.Add(layout);
                }
                catch { /* leave it alone if we can't probe */ }
            }

            foreach (object l in toRemove)
            {
                try { Invoke(l, "Delete"); } catch { /* AutoCAD refuses to delete the active layout — ignore */ }
            }
        }

        private static string MakeUniqueLayoutName(object layouts, string baseName, int fallbackIndex)
        {
            // AutoCAD layout names: max 255 chars, no <>/\":;?*|,=' chars.
            string clean = new string((baseName ?? "Sheet").Select(c =>
                "<>/\\\":;?*|,='".IndexOf(c) >= 0 ? '_' : c).ToArray());
            if (string.IsNullOrWhiteSpace(clean)) clean = "Sheet_" + fallbackIndex;
            if (clean.Length > 240) clean = clean.Substring(0, 240);

            string candidate = clean;
            int n = 2;
            while (LayoutExists(layouts, candidate))
                candidate = clean + "_" + (n++);
            return candidate;
        }

        private static bool LayoutExists(object layouts, string name)
        {
            try { return Invoke(layouts, "Item", name) != null; }
            catch { return false; }
        }

        // ---------- Phase 2B: All flattened sheets tiled in Model Space ----------

        private void BuildTiledModelSpace(object doc, IList<string> flatFiles,
            IList<(double[] Min, double[] Max)> sheetExtents)
        {
            object modelSpace = Get(doc, "ModelSpace");

            int n = flatFiles.Count;
            int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(n * GridAspect)));

            var inserted = new List<(object Block, double MinX, double MinY, double W, double H)>(n);
            for (int i = 0; i < flatFiles.Count; i++)
            {
                string file = flatFiles[i];
                if (!File.Exists(file)) continue;
                double[] origin = { 0.0, 0.0, 0.0 };
                object blk = Invoke(modelSpace, "InsertBlock", origin, file, 1.0, 1.0, 1.0, 0.0);

                double[] minPt = sheetExtents[i].Min;
                double[] maxPt = sheetExtents[i].Max;
                if (minPt == null || maxPt == null)
                {
                    // EXTMIN/EXTMAX read failed earlier — use the A3 fallback so one
                    // bad sheet doesn't poison the whole grid.
                    minPt = (double[])FallbackMin.Clone();
                    maxPt = (double[])FallbackMax.Clone();
                }

                double w = Math.Max(1.0, maxPt[0] - minPt[0]);
                double h = Math.Max(1.0, maxPt[1] - minPt[1]);
                inserted.Add((blk, minPt[0], minPt[1], w, h));
            }

            int rows = (int)Math.Ceiling(inserted.Count / (double)cols);
            double[] colWidths = new double[cols];
            double[] rowHeights = new double[rows];

            for (int i = 0; i < inserted.Count; i++)
            {
                int r = i / cols, c = i % cols;
                if (inserted[i].W > colWidths[c]) colWidths[c] = inserted[i].W;
                if (inserted[i].H > rowHeights[r]) rowHeights[r] = inserted[i].H;
            }

            // Place top row last so the first sheet sits at the top-left corner.
            double yCursor = 0.0;
            for (int r = rows - 1; r >= 0; r--)
            {
                double xCursor = 0.0;
                for (int c = 0; c < cols; c++)
                {
                    int idx = r * cols + c;
                    if (idx >= inserted.Count) break;

                    var item = inserted[idx];
                    // Move is a vector translation (to - from). Anchor on the block's
                    // bbox MinPt so the lower-left corner of its actual extents lands
                    // at (xCursor, yCursor) — the insertion point sits at world (0,0)
                    // but the geometry lives wherever Revit's ModelSpace put it, so
                    // a (0,0)→target Move would leave each sheet at a different phase
                    // and they'd overlap.
                    double[] from = { item.MinX, item.MinY, 0.0 };
                    double[] to = { xCursor, yCursor, 0.0 };
                    Invoke(item.Block, "Move", from, to);

                    xCursor += colWidths[c] + TileGapMm;
                }
                yCursor += rowHeights[r] + TileGapMm;
            }

            try { Invoke(Get(doc, "Application"), "ZoomExtents"); }
            catch { /* non-critical */ }
        }

        private static void CloseDocByPath(object documents, string fullPath)
        {
            try
            {
                int count = (int)Get(documents, "Count");
                // Walk descending — Close mutates the collection.
                for (int i = count - 1; i >= 0; i--)
                {
                    object d;
                    try { d = Invoke(documents, "Item", i); } catch { continue; }

                    string fn;
                    try { fn = (string)Get(d, "FullName"); } catch { continue; }

                    if (string.Equals(fn, fullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        try { Invoke(d, "Close", false); } catch { /* ignore */ }
                    }
                }
            }
            catch { /* best-effort cleanup */ }
        }

        // ---------- Layer cleanup ----------

        private static void TurnOffNonPlotLayers(object doc)
        {
            // Revit's non-printing layers end with the "-NPLT" suffix (e.g.
            // "G-____-____-NPLT" carries view crop / viewport frame geometry).
            // Set LayerOn=false on every non-Model layer matching that suffix.
            // We don't Freeze because the active layer cannot be frozen and we
            // don't know which layer the user might end up making active.
            object layers;
            int count;
            try
            {
                layers = Get(doc, "Layers");
                count = (int)Get(layers, "Count");
            }
            catch { return; }

            for (int i = 0; i < count; i++)
            {
                object layer;
                try { layer = Invoke(layers, "Item", i); } catch { continue; }

                string name;
                try { name = (string)Get(layer, "Name"); } catch { continue; }
                if (string.IsNullOrEmpty(name)) continue;

                if (!name.EndsWith("-NPLT", StringComparison.OrdinalIgnoreCase)) continue;

                try { Set(layer, "LayerOn", false); } catch { /* read-only or current-layer edge case */ }
            }
        }

        // ---------- Block / extents / COM helpers ----------

        private static double[] TryToDoubleArray(object o)
        {
            if (o == null) return null;
            if (o is double[] d) return d;
            if (o is Array arr)
            {
                var result = new double[arr.Length];
                for (int i = 0; i < arr.Length; i++)
                {
                    try { result[i] = Convert.ToDouble(arr.GetValue(i)); }
                    catch { return null; }
                }
                return result;
            }
            return null;
        }

        private static void TryRegen(object doc)
        {
            // acAllViewports = 0 → cheapest full regen.
            try { Invoke(doc, "Regen", 0); } catch { /* GetBlockExtents has its own fallback */ }
        }

        private static object Get(object target, string name) =>
            target.GetType().InvokeMember(name,
                BindingFlags.GetProperty, null, target, null);

        private static void Set(object target, string name, object value) =>
            target.GetType().InvokeMember(name,
                BindingFlags.SetProperty, null, target, new[] { value });

        private static object Invoke(object target, string name, params object[] args) =>
            target.GetType().InvokeMember(name,
                BindingFlags.InvokeMethod, null, target, args);

        // ---------- EXPORTLAYOUT dialog watcher (Win32) ----------
        //
        // AutoCAD 2021 EXPORTLAYOUT pops "Файл создан. Открыть его?" as a modal
        // dialog. No sysvar disables it. We dismiss it by enumerating top-level
        // windows owned by our acad.exe PID, finding any with a title that begins
        // with "Экспорт лист" / "Export Layout", locating its "Не открывать" /
        // "Don't Open" button child, and PostMessage'ing a BM_CLICK to it.

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const uint BM_CLICK = 0x00F5;
        private const uint WM_CLOSE = 0x0010;
        private const int SW_HIDE = 0;

        // Off-screen anchor so even if AutoCAD re-shows the window between our
        // hide ticks, the user can't see or click it. (-32000,-32000) is what
        // Windows itself uses for minimized-to-nowhere windows.
        private const int OffScreenX = -32000;
        private const int OffScreenY = -32000;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_HIDEWINDOW = 0x0080;
        private const uint GW_OWNER = 4;

        // Title-prefix matches for the EXPORTLAYOUT confirmation dialog. The full
        // Russian title is "Экспорт листа в чертеж пространства модели"; English
        // installs show "Export Layout to Model Space Drawing".
        private static readonly string[] DialogTitlePrefixes =
        {
            "Экспорт лист",
            "Export Layout",
            "ExportLayout"
        };

        // Button labels for "don't open" — Russian uses "Не открывать", English
        // varies between "Don't Open" / "Don't open" / "&Don't Open".
        private static readonly string[] CancelButtonTexts =
        {
            "Не открывать",
            "Don't Open",
            "Don't open",
            "Не открыв",
        };

        private static void StartDialogWatcher(int acadPid, CancellationToken token)
        {
            Task.Run(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    try { HideAndDismiss(acadPid); }
                    catch { /* watcher must never bring down the merge */ }

                    // 40 ms tick. Safe with the narrow hide policy (MDI frame
                    // + dialog-title match only). The earlier blanket policy
                    // at 40 ms killed AutoCAD on startup, but blame was the
                    // policy, not the rate — so we keep the fast tick to
                    // dismiss the EXPORTLAYOUT confirm dialog before the user
                    // can see it flash.
                    try { Task.Delay(40, token).Wait(token); }
                    catch { break; }
                }
            });
        }

        /// <summary>
        /// Single pass: dismiss the EXPORTLAYOUT confirmation dialog if it
        /// appears, and SW_HIDE AutoCAD's main MDI frame if it managed to
        /// pop visible. Deliberately does NOT blanket-hide every top-level
        /// window of acad.exe — doing that (in an earlier attempt) caused
        /// AutoCAD to exit on its own a few hundred milliseconds after launch
        /// and hung the whole pipeline waiting for a dead RPC channel.
        /// </summary>
        private static void HideAndDismiss(int acadPid)
        {
            EnumWindows((hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out int pid);
                if (pid != acadPid) return true;

                // EXPORTLAYOUT confirm dialog check FIRST — before the owned-
                // window filter below. The dialog is shown modal-to-MDI-frame
                // so GW_OWNER returns non-zero; an earlier version filtered it
                // out by that property and the dialog leaked through visibly
                // once per sheet.
                string title = ReadWindowText(hwnd);
                bool isExportLayoutDialog = false;
                if (!string.IsNullOrEmpty(title))
                {
                    foreach (var prefix in DialogTitlePrefixes)
                    {
                        if (title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            isExportLayoutDialog = true;
                            break;
                        }
                    }
                }

                if (isExportLayoutDialog)
                {
                    SetWindowPos(hwnd, IntPtr.Zero, OffScreenX, OffScreenY, 0, 0,
                        SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_HIDEWINDOW);
                    ClickCancelButton(hwnd);
                    return true;
                }

                // For the MDI-frame hide branch we DO want to skip owned
                // windows (they're document frames / dialogs that we shouldn't
                // touch).
                IntPtr owner = GetWindow(hwnd, GW_OWNER);
                if (owner != IntPtr.Zero) return true;

                // Main MDI frame only. Class names: AutoCAD ships
                // "AfxMDIFrame140s" (2021), "...142s" (2022) etc. — match the
                // prefix.
                string cls = ReadClassName(hwnd);
                if (cls.StartsWith("AfxMDIFrame", StringComparison.Ordinal) && IsWindowVisible(hwnd))
                {
                    SetWindowPos(hwnd, IntPtr.Zero, OffScreenX, OffScreenY, 0, 0,
                        SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_HIDEWINDOW);
                    ShowWindow(hwnd, SW_HIDE);
                }

                return true;
            }, IntPtr.Zero);
        }

        private static void ClickCancelButton(IntPtr dialog)
        {
            IntPtr cancelBtn = IntPtr.Zero;
            EnumChildWindows(dialog, (child, _) =>
            {
                string cls = ReadClassName(child);
                if (!string.Equals(cls, "Button", StringComparison.Ordinal)) return true;
                string text = ReadWindowText(child);
                if (string.IsNullOrEmpty(text)) return true;
                foreach (var label in CancelButtonTexts)
                {
                    if (text.IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        cancelBtn = child;
                        return false;
                    }
                }
                return true;
            }, IntPtr.Zero);

            if (cancelBtn != IntPtr.Zero)
            {
                PostMessage(cancelBtn, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
            }
            else
            {
                // Couldn't find the button — last resort is WM_CLOSE on the dialog,
                // which on standard message boxes maps to the cancel/no path.
                PostMessage(dialog, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
        }

        private static string ReadWindowText(IntPtr hwnd)
        {
            var sb = new StringBuilder(512);
            GetWindowTextW(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string ReadClassName(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            GetClassNameW(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }
    }
}
