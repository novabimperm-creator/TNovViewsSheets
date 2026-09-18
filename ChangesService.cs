using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TNovCommon;

namespace TNovViewsSheets
{
    internal static class ChangesService
    {
        public static readonly IReadOnlyList<VisibilityOption> VisibilityOptions = new[]
        {
            new VisibilityOption { Value = RevisionVisibility.CloudAndTagVisible, Name = "Облако и марка" },
            new VisibilityOption { Value = RevisionVisibility.TagVisible, Name = "Только марка" },
            new VisibilityOption { Value = RevisionVisibility.Hidden, Name = "Скрыто" }
        };

        public static readonly IReadOnlyList<KindOption> KindOptions = new[]
        {
            new KindOption { Value = ChangeStampKind.Partial, Name = "частичное" },
            new KindOption { Value = ChangeStampKind.Replace, Name = "Зам." },
            new KindOption { Value = ChangeStampKind.New, Name = "Нов." }
        };

        public static readonly IReadOnlyList<string> ListTextOptions = new[] { "-", "Зам.", "Нов.", "Аннул." };

        public static string VisibilityCaption(RevisionVisibility visibility)
        {
            var option = VisibilityOptions.FirstOrDefault(o => o.Value == visibility);
            return option != null ? option.Name : visibility.ToString();
        }

        /// <summary>
        /// ГОСТ-нумерация — на проект. При режиме «в рамках листа» Revit бросает
        /// InvalidOperationException на Revision.RevisionNumber.
        /// </summary>
        public static bool EnsurePerProjectNumbering(Document doc)
        {
            if (doc == null) return false;
            RevisionSettings settings = RevisionSettings.GetRevisionSettings(doc);
            if (settings.RevisionNumbering == RevisionNumbering.PerProject)
                return false;
            try
            {
                using (var t = new Transaction(doc, "TNov - Нумерация изменений на проект"))
                {
                    t.Start();
                    settings.RevisionNumbering = RevisionNumbering.PerProject;
                    t.Commit();
                }
                Logger.Log("Нумерация изменений переключена на «в рамках проекта» (PerProject).", 1);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("Не удалось переключить нумерацию изменений на PerProject: " + ex.Message, 4);
                return false;
            }
        }

        public static string GetRevisionNumber(Revision revision)
        {
            if (revision == null) return string.Empty;
            try
            {
                return revision.RevisionNumber ?? revision.SequenceNumber.ToString();
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                return revision.SequenceNumber.ToString();
            }
            catch (InvalidOperationException)
            {
                return revision.SequenceNumber.ToString();
            }
        }

        public static List<ViewSheet> CollectSheets(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Sheets)
                .WhereElementIsNotElementType()
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(IsScheduledOnSheetList)
                .ToList();
        }

        static bool IsScheduledOnSheetList(ViewSheet sheet)
        {
            Parameter parameter = sheet.get_Parameter(BuiltInParameter.SHEET_SCHEDULED);
            return parameter != null && parameter.AsInteger() == 1;
        }

        public static List<Revision> CollectRevisions(Document doc)
        {
            return Revision.GetAllRevisionIds(doc)
                .Select(id => doc.GetElement(id) as Revision)
                .Where(r => r != null)
                .ToList();
        }

        public static List<RevisionCloud> CollectClouds(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_RevisionClouds)
                .WhereElementIsNotElementType()
                .OfClass(typeof(RevisionCloud))
                .Cast<RevisionCloud>()
                .ToList();
        }

        public static string GetSheetSet(ViewSheet sheet)
        {
            string value = null;
            if (Param.ParamExistByGuid(ChangesParams.SheetSet, sheet))
                value = sheet.get_Parameter(ChangesParams.SheetSet)?.AsString();
            if (string.IsNullOrWhiteSpace(value) || value == "----")
                return ChangesParams.NoSetName;
            return value;
        }

        public static List<string> CollectSheetSets(IEnumerable<ViewSheet> sheets)
        {
            return sheets
                .Select(GetSheetSet)
                .Distinct()
                .OrderBy(s => s, NaturalStringComparer.Instance)
                .ToList();
        }

        public static string GetCustomOrSheetNumber(ViewSheet sheet)
        {
            if (Param.ParamExistByGuid(ChangesParams.CustomSheetNumber, sheet))
            {
                string custom = sheet.get_Parameter(ChangesParams.CustomSheetNumber)?.AsString();
                if (!string.IsNullOrWhiteSpace(custom)) return custom.Trim();
            }
            return sheet.SheetNumber ?? string.Empty;
        }

        public static string GetCleanNumber(ViewSheet sheet)
        {
            string raw = GetCustomOrSheetNumber(sheet);
            string set = GetSheetSet(sheet);
            if (set != ChangesParams.NoSetName)
            {
                raw = raw.Replace(set + "-", "");
                raw = raw.Replace(set + " ", "");
                raw = raw.Replace(set, "");
            }
            raw = raw.Trim();
            raw = Regex.Replace(raw, @"\p{Cf}", string.Empty);
            return raw;
        }

        public static bool TryGetNumericNumber(ViewSheet sheet, out int number)
        {
            string clean = GetCleanNumber(sheet);
            if (int.TryParse(clean, out number)) return true;
            Match match = Regex.Match(clean ?? string.Empty, @"\d+");
            if (match.Success && int.TryParse(match.Value, out number)) return true;
            number = 0;
            return false;
        }

        public static Parameter FindSheetGroupParameter(ViewSheet sheet)
        {
            return sheet?.LookupParameter(ChangesParams.SheetGroupParamName);
        }

        public static Parameter FindSheetContentParameter(ViewSheet sheet)
        {
            return sheet?.LookupParameter(ChangesParams.SheetContentParamName);
        }

        public static string GetSheetContent(ViewSheet sheet)
        {
            Parameter p = FindSheetContentParameter(sheet);
            return p?.AsString() ?? string.Empty;
        }

        public static bool TrySetSheetContent(ViewSheet sheet, string value, out string error)
        {
            error = null;
            Parameter p = FindSheetContentParameter(sheet);
            if (p == null)
            {
                error = "Параметр «Изм.Содержание изменения» отсутствует на листе " + sheet.SheetNumber;
                return false;
            }
            if (p.IsReadOnly)
            {
                error = "Параметр «Изм.Содержание изменения» только для чтения: " + sheet.SheetNumber;
                return false;
            }
            p.Set(value ?? string.Empty);
            return true;
        }

        public static string GetSheetGroup(ViewSheet sheet)
        {
            Parameter p = FindSheetGroupParameter(sheet);
            return p?.AsString() ?? string.Empty;
        }

        public static bool TrySetSheetGroup(ViewSheet sheet, string value, out string error)
        {
            error = null;
            Parameter p = FindSheetGroupParameter(sheet);
            if (p == null)
            {
                error = "Параметр «Изм.Группа листов» отсутствует на листе " + sheet.SheetNumber;
                return false;
            }
            if (p.IsReadOnly)
            {
                error = "Параметр «Изм.Группа листов» только для чтения: " + sheet.SheetNumber;
                return false;
            }
            p.Set(value ?? string.Empty);
            return true;
        }

        public static bool IsAnnulled(ViewSheet sheet)
        {
            if (ContainsAnnul(ReadParam(sheet, ChangesParams.AdskComment))) return true;
            if (ContainsAnnul(ReadParam(sheet, ChangesParams.Comment2))) return true;
            foreach (Guid guid in ChangesParams.SheetLineGuids)
            {
                if (ContainsAnnul(ReadParam(sheet, guid))) return true;
            }
            return false;
        }

        static bool ContainsAnnul(string text) =>
            !string.IsNullOrEmpty(text) && text.IndexOf("Аннул", StringComparison.OrdinalIgnoreCase) >= 0;

        static string ReadParam(Element element, Guid guid)
        {
            if (!Param.ParamExistByGuid(guid, element)) return null;
            return element.get_Parameter(guid)?.AsString();
        }

        public static bool IsTinyCloud(RevisionCloud cloud)
        {
            if (cloud == null) return true;
            double length = 0;
            foreach (Curve curve in cloud.GetSketchCurves())
                length += curve.Length;
            return length <= ChangesParams.MinCloudLength;
        }

        public static IEnumerable<ElementId> GetCloudSheetIds(Document doc, RevisionCloud cloud)
        {
            ISet<ElementId> ids = cloud.GetSheetIds();
            if (ids != null && ids.Count > 0) return ids;
            if (doc.GetElement(cloud.OwnerViewId) is ViewSheet)
                return new[] { cloud.OwnerViewId };
            return Array.Empty<ElementId>();
        }

        public static ElementId GetCloudRevisionId(RevisionCloud cloud)
        {
            Parameter p = cloud.get_Parameter(BuiltInParameter.REVISION_CLOUD_REVISION);
            if (p != null && p.HasValue) return p.AsElementId();
            return ElementId.InvalidElementId;
        }

        public static string GetCloudRevisionNumber(RevisionCloud cloud)
        {
            return cloud.get_Parameter(BuiltInParameter.REVISION_CLOUD_REVISION_NUM)?.AsString() ?? string.Empty;
        }

        public static Dictionary<ElementId, List<RevisionCloud>> IndexCloudsBySheet(Document doc, IEnumerable<RevisionCloud> clouds)
        {
            var map = new Dictionary<ElementId, List<RevisionCloud>>();
            foreach (RevisionCloud cloud in clouds)
            {
                foreach (ElementId sheetId in GetCloudSheetIds(doc, cloud).Distinct())
                {
                    if (!map.TryGetValue(sheetId, out List<RevisionCloud> list))
                    {
                        list = new List<RevisionCloud>();
                        map[sheetId] = list;
                    }
                    list.Add(cloud);
                }
            }
            return map;
        }

        public static int CountCloudsForRevision(IEnumerable<RevisionCloud> cloudsOnSheet, Revision revision)
        {
            if (cloudsOnSheet == null || revision == null) return 0;
            int count = 0;
            foreach (RevisionCloud cloud in cloudsOnSheet)
            {
                if (IsTinyCloud(cloud)) continue;
                ElementId revId = GetCloudRevisionId(cloud);
                if (revId != ElementId.InvalidElementId && revId == revision.Id)
                {
                    count++;
                    continue;
                }
                if (revId == ElementId.InvalidElementId && GetCloudRevisionNumber(cloud) == GetRevisionNumber(revision))
                    count++;
            }
            return count;
        }

        public static List<ViewSheet> ResolveTargetSheets(Document doc, UIDocument uidoc, ChangesScope scope, out string error)
        {
            error = null;
            List<ViewSheet> all = CollectSheets(doc);
            switch (scope)
            {
                case ChangesScope.Visible:
                    if (!(doc.ActiveView is ViewSheet active))
                    {
                        error = "Активный вид не является листом. Щёлкните в пространстве листа и повторите.";
                        return new List<ViewSheet>();
                    }
                    return all.Where(s => s.Id == active.Id).ToList();
                case ChangesScope.Selected:
                    List<ViewSheet> selected = GetViewSheetsFromCurrentSelection(doc, uidoc);
                    if (selected.Count == 0)
                    {
                        error = "В модели не выбрано ни одного листа.";
                        return new List<ViewSheet>();
                    }
                    return selected;
                default:
                    return all;
            }
        }

        public static List<ViewSheet> GetViewSheetsFromCurrentSelection(Document doc, UIDocument uidoc)
        {
            var result = new Dictionary<ElementId, ViewSheet>();
            foreach (ElementId id in uidoc.Selection.GetElementIds())
            {
                Element element = doc.GetElement(id);
                if (element is ViewSheet sheet)
                {
                    result[sheet.Id] = sheet;
                    continue;
                }
                if (element is Viewport viewport)
                {
                    if (doc.GetElement(viewport.SheetId) is ViewSheet viewportSheet)
                        result[viewportSheet.Id] = viewportSheet;
                    continue;
                }
                if (element != null && element.OwnerViewId != ElementId.InvalidElementId
                    && doc.GetElement(element.OwnerViewId) is ViewSheet ownerSheet)
                {
                    result[ownerSheet.Id] = ownerSheet;
                }
            }
            return result.Values.ToList();
        }

        public static List<StampLinePreview> ReadStampLines(ViewSheet sheet)
        {
            var lines = new List<StampLinePreview>();
            int count = Math.Min(ChangesParams.CountLineGuids.Length, ChangesParams.SheetLineGuids.Length);
            for (int i = 0; i < count; i++)
            {
                lines.Add(new StampLinePreview
                {
                    CountText = ReadParam(sheet, ChangesParams.CountLineGuids[i]) ?? string.Empty,
                    SheetText = ReadParam(sheet, ChangesParams.SheetLineGuids[i]) ?? string.Empty
                });
            }
            return lines;
        }

        public static List<SheetRevisionInfo> BuildSheetRevisions(
            Document doc,
            ViewSheet sheet,
            IReadOnlyList<Revision> projectRevisions,
            IReadOnlyList<RevisionCloud> cloudsOnSheet)
        {
            var existing = new HashSet<ElementId>(sheet.GetAllRevisionIds());
            var stampLines = ReadStampLines(sheet);
            var onSheet = projectRevisions.Where(r => existing.Contains(r.Id)).OrderBy(r => r.SequenceNumber).ToList();
            int stampIndex = 0;
            var result = new List<SheetRevisionInfo>();

            foreach (Revision revision in projectRevisions.OrderBy(r => r.SequenceNumber))
            {
                int clouds = CountCloudsForRevision(cloudsOnSheet, revision);
                bool isOnSheet = existing.Contains(revision.Id);
                ChangeStampKind kind = ChangeStampKind.Partial;
                bool overridden = false;
                if (isOnSheet)
                {
                    kind = ComputeKind(clouds, stampIndex == 0);
                    if (stampIndex < stampLines.Count && clouds == 0)
                    {
                        string sheetText = stampLines[stampIndex].SheetText ?? string.Empty;
                        if (ContainsNew(sheetText) && kind != ChangeStampKind.New)
                        {
                            kind = ChangeStampKind.New;
                            overridden = true;
                        }
                        else if (ContainsReplace(sheetText) && kind != ChangeStampKind.Replace)
                        {
                            kind = ChangeStampKind.Replace;
                            overridden = true;
                        }
                    }
                    stampIndex++;
                }

                var info = new SheetRevisionInfo
                {
                    RevisionId = revision.Id,
                    NumberText = GetRevisionNumber(revision),
                    Sequence = revision.SequenceNumber,
                    Description = revision.Description ?? string.Empty,
                    IssuedBy = revision.IssuedBy ?? string.Empty,
                    CloudCount = clouds,
                    IsIssued = revision.Issued,
                    IsOnSheet = isOnSheet
                };
                info.SetKind(kind, overridden);
                result.Add(info);
            }

            return result;
        }

        static ChangeStampKind ComputeKind(int cloudCount, bool isFirstOnSheet)
        {
            return cloudCount > 0 ? ChangeStampKind.Partial : ChangeStampKind.Replace;
        }

        static bool ContainsNew(string text) =>
            !string.IsNullOrEmpty(text) && text.IndexOf("Нов", StringComparison.OrdinalIgnoreCase) >= 0;

        static bool ContainsReplace(string text) =>
            !string.IsNullOrEmpty(text) && text.IndexOf("Зам", StringComparison.OrdinalIgnoreCase) >= 0;

        public static List<SheetRevisionInfo> ApplyPurge(IEnumerable<SheetRevisionInfo> revisions, bool purge)
        {
            var onSheet = revisions.Where(r => r.IsOnSheet).OrderBy(r => r.Sequence).ToList();
            if (!purge) return onSheet;
            onSheet.Reverse();
            var kept = new List<SheetRevisionInfo>();
            int replaces = 0;
            foreach (SheetRevisionInfo item in onSheet)
            {
                if (replaces == 0) kept.Add(item);
                if (item.CloudCount == 0) replaces++;
            }
            kept.Reverse();
            return kept;
        }

        public static void FillStampPreview(ChangeSheetRow row, bool purge)
        {
            var lines = BuildStampValues(row.Revisions, purge, out string note);
            row.NotePreview = note;
            row.StampPreview = string.Join(" | ", lines
                .Where(l => !string.IsNullOrEmpty(l.CountText) || !string.IsNullOrEmpty(l.SheetText))
                .Select(l => (string.IsNullOrEmpty(l.CountText) ? "-" : l.CountText) + "/" + (string.IsNullOrEmpty(l.SheetText) ? "-" : l.SheetText)));
            row.RaiseRevisionsChanged();
        }

        public static List<StampLinePreview> BuildStampValues(IEnumerable<SheetRevisionInfo> revisions, bool purge, out string note)
        {
            var used = ApplyPurge(revisions, purge);
            var counts = new List<string>();
            var sheets = new List<string>();
            string comm = string.Empty;
            foreach (SheetRevisionInfo item in used)
            {
                string caption = RevisionNoteCaption(item.IssuedBy, item.Description, item.NumberText);
                if (item.Kind == ChangeStampKind.New)
                {
                    comm += caption + " (Нов.), ";
                    counts.Add("-");
                    sheets.Add("Нов.");
                }
                else if (item.Kind == ChangeStampKind.Replace)
                {
                    comm += caption + " (Зам.), ";
                    counts.Add("-");
                    sheets.Add("Зам.");
                }
                else
                {
                    comm += caption + ", ";
                    counts.Add(item.CloudCount > 0 ? item.CloudCount.ToString() : "-");
                    sheets.Add("-");
                }
            }

            if (comm.Length > 0)
            {
                comm = "Изм. " + comm;
                comm = comm.Substring(0, comm.Length - 2);
            }
            note = comm;

            var lines = new List<StampLinePreview>();
            int max = ChangesParams.CountLineGuids.Length;
            for (int i = 0; i < max; i++)
            {
                if (i < counts.Count)
                    lines.Add(new StampLinePreview { CountText = counts[i], SheetText = sheets[i] });
                else
                    lines.Add(new StampLinePreview { CountText = string.Empty, SheetText = string.Empty });
            }
            return lines;
        }

        public static ChangeSheetRow BuildSheetRow(
            Document doc,
            ViewSheet sheet,
            IReadOnlyList<Revision> revisions,
            IReadOnlyDictionary<ElementId, List<RevisionCloud>> cloudsBySheet,
            bool purge)
        {
            cloudsBySheet.TryGetValue(sheet.Id, out List<RevisionCloud> clouds);
            clouds = clouds ?? new List<RevisionCloud>();
            bool numeric = TryGetNumericNumber(sheet, out int number);
            var row = new ChangeSheetRow
            {
                Id = sheet.Id,
                UniqueId = sheet.UniqueId,
                SheetNumber = sheet.SheetNumber,
                Name = sheet.Name,
                SheetSet = GetSheetSet(sheet),
                DisplayNumber = GetCleanNumber(sheet),
                NumericNumber = numeric ? number : (int?)null,
                IsAnnulled = IsAnnulled(sheet),
                Group = GetSheetGroup(sheet),
                Content = GetSheetContent(sheet)
            };
            row.Revisions = BuildSheetRevisions(doc, sheet, revisions, clouds);
            row.StampLines = ReadStampLines(sheet);
            FillStampPreview(row, purge);
            return row;
        }

        public static RevisionRow BuildRevisionRow(
            Revision revision,
            IReadOnlyList<ChangeSheetRow> sheets,
            IReadOnlyDictionary<ElementId, List<RevisionCloud>> cloudsBySheet)
        {
            var withRev = sheets.Where(s => s.Revisions.Any(r => r.RevisionId == revision.Id && r.IsOnSheet)).ToList();
            int clouds = 0;
            if (cloudsBySheet != null)
            {
                foreach (var pair in cloudsBySheet)
                    clouds += pair.Value.Count(c => !IsTinyCloud(c) && GetCloudRevisionId(c) == revision.Id);
            }
            return new RevisionRow
            {
                Id = revision.Id,
                UniqueId = revision.UniqueId,
                NumberText = GetRevisionNumber(revision),
                Sequence = revision.SequenceNumber,
                Description = revision.Description ?? string.Empty,
                RevisionDate = revision.RevisionDate ?? string.Empty,
                IssuedTo = revision.IssuedTo ?? string.Empty,
                IssuedBy = revision.IssuedBy ?? string.Empty,
                Issued = revision.Issued,
                Visibility = revision.Visibility,
                SheetCount = withRev.Count,
                CloudCount = clouds,
                SetsText = string.Join(", ", withRev.Select(s => s.SheetSet).Distinct().OrderBy(s => s))
            };
        }

        public static ChangesOperationResult NumberClouds(Document doc, ICollection<ElementId> sheetIds, IReadOnlyList<RevisionCloud> clouds)
        {
            if (clouds == null || clouds.Count == 0)
                return ChangesOperationResult.Ok("Облака отсутствуют.", 0);

            var target = new HashSet<ElementId>(sheetIds ?? Array.Empty<ElementId>());
            int numbered = 0;
            using (var t = new Transaction(doc, "TNov - Пометочные облака"))
            {
                try
                {
                    t.Start();
                    var bySheet = IndexCloudsBySheet(doc, clouds);
                    foreach (var pair in bySheet)
                    {
                        if (target.Count > 0 && !target.Contains(pair.Key)) continue;
                        Element sheetElem = doc.GetElement(pair.Key);
                        Logger.Log("Лист " + (sheetElem?.Name ?? pair.Key.ToString()), 2);

                        var groups = pair.Value
                            .GroupBy(c => Tuple.Create(GetCloudRevisionId(c), GetCloudRevisionNumber(c)))
                            .OrderBy(g => g.First().get_Parameter(BuiltInParameter.REVISION_CLOUD_REVISION_NUM)?.AsString(), NaturalStringComparer.Instance);

                        foreach (var group in groups)
                        {
                            Logger.Log("   Изменение " + (group.Key.Item2 ?? ""), 2);
                            int index = 0;
                            foreach (RevisionCloud cloud in group)
                            {
                                Parameter comm = cloud.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                if (comm != null && comm.IsReadOnly)
                                {
                                    Logger.Log("      Изменение заблокировано в Вид - Изменения", 1);
                                    break;
                                }
                                if (IsTinyCloud(cloud))
                                {
                                    Logger.Log("      Облако " + cloud.Id + ": номер не назначен, облако малой длины", 2);
                                    continue;
                                }
                                if (comm == null) continue;
                                index++;
                                comm.Set(index.ToString());
                                numbered++;
                                Logger.Log("      Облако " + cloud.Id + ": назначен номер " + index, 2);
                            }
                        }
                    }
                    t.Commit();
                    return ChangesOperationResult.Ok("Пронумеровано облаков: " + numbered, numbered);
                }
                catch (Exception ex)
                {
                    if (t.HasStarted()) t.RollBack();
                    Logger.Log("Ошибка нумерации облаков: " + ex.Message, 4);
                    return ChangesOperationResult.Fail(ex.Message);
                }
            }
        }

        public static ChangesOperationResult ApplyStamp(Document doc, IEnumerable<ChangeSheetRow> rows, bool purge)
        {
            int written = 0;
            using (var t = new Transaction(doc, "TNov - заполнение параметров листов"))
            {
                try
                {
                    t.Start();
                    foreach (ChangeSheetRow row in rows)
                    {
                        var sheet = doc.GetElement(row.Id) as ViewSheet;
                        if (sheet == null) continue;
                        if (row.IsAnnulled)
                        {
                            Logger.Log("Лист " + sheet.Name + " пропущен (аннулирован)", 2);
                            continue;
                        }
                        Logger.Log("Лист " + sheet.Name, 1);
                        List<StampLinePreview> lines = BuildStampValues(row.Revisions, purge, out string note);
                        WriteNote(sheet, note);
                        WriteStampLines(sheet, lines);
                        written++;
                    }
                    t.Commit();
                    return ChangesOperationResult.Ok("Заполнен штамп на листах: " + written, written);
                }
                catch (Exception ex)
                {
                    if (t.HasStarted()) t.RollBack();
                    Logger.Log("Ошибка заполнения штампа: " + ex.Message, 4);
                    return ChangesOperationResult.Fail(ex.Message);
                }
            }
        }

        static void WriteNote(ViewSheet sheet, string note)
        {
            TrySetGuid(sheet, ChangesParams.AdskComment, note ?? string.Empty);
            TrySetGuid(sheet, ChangesParams.Comment2, note ?? string.Empty);
            Logger.Log("   Примечание: " + (note ?? ""), 2);
        }

        static void WriteStampLines(ViewSheet sheet, List<StampLinePreview> lines)
        {
            int max = ChangesParams.CountLineGuids.Length;
            for (int i = 0; i < max; i++)
            {
                string countText = i < lines.Count ? (lines[i].CountText ?? string.Empty) : string.Empty;
                string sheetText = i < lines.Count ? (lines[i].SheetText ?? string.Empty) : string.Empty;
                TrySetGuid(sheet, ChangesParams.CountLineGuids[i], countText);
                TrySetGuid(sheet, ChangesParams.SheetLineGuids[i], sheetText);
                Logger.Log("      N_Изм.Строка" + (i + 1) + ".Кол.уч: " + countText, 2);
                Logger.Log("      N_Изм.Строка" + (i + 1) + ".Лист: " + sheetText, 2);
            }
        }

        static void TrySetGuid(Element element, Guid guid, string value)
        {
            try
            {
                if (!Param.ParamExistByGuid(guid, element)) return;
                Parameter p = element.get_Parameter(guid);
                if (p == null || p.IsReadOnly) return;
                p.Set(value ?? string.Empty);
            }
            catch (Exception ex)
            {
                Logger.Log(element.Name + " ошибка параметра " + guid + ": " + ex.Message, 4);
            }
        }

        public static void ApplyWorkingRevisionFields(ChangeSheetRow row, RevisionRow revision)
        {
            if (row == null) return;
            if (revision == null)
            {
                row.CountText = string.Empty;
                if (!row.ListOverridden) row.SetListText(string.Empty, false);
                return;
            }

            SheetRevisionInfo info = row.Revisions.FirstOrDefault(r => r.RevisionId == revision.Id);
            int clouds = info?.CloudCount ?? 0;
            bool onSheet = info != null && info.IsOnSheet;
            var onSheetRevs = row.Revisions.Where(r => r.IsOnSheet).OrderBy(r => r.Sequence).ToList();

            row.CountText = clouds > 0 ? clouds.ToString() : "-";
            if (row.ListOverridden) return;

            if (onSheet)
            {
                int stampIndex = onSheetRevs.FindIndex(r => r.RevisionId == revision.Id);
                if (stampIndex >= 0 && stampIndex < row.StampLines.Count)
                {
                    string sheetText = row.StampLines[stampIndex].SheetText ?? string.Empty;
                    if (ContainsAnnul(sheetText))
                    {
                        row.SetListText("Аннул.", false);
                        return;
                    }
                    if (ContainsNew(sheetText) && clouds == 0)
                    {
                        row.SetListText("Нов.", false);
                        return;
                    }
                    if (ContainsReplace(sheetText) && clouds == 0)
                    {
                        row.SetListText("Зам.", false);
                        return;
                    }
                }
            }
            row.SetListText(clouds > 0 ? "-" : "Зам.", false);
        }

        public static void AssignAutoGroups(IList<ChangeSheetRow> rows)
        {
            if (rows == null) return;
            foreach (var group in rows.GroupBy(r =>
                         (r.Content ?? string.Empty).Trim() + "\n" + (r.ListText ?? string.Empty).Trim(),
                     StringComparer.CurrentCultureIgnoreCase))
            {
                string value = FormatSheetRanges(group.ToList());
                foreach (ChangeSheetRow row in group)
                    row.Group = value;
            }
        }

        public static int GetStampLineIndex(ViewSheet sheet, ElementId revisionId)
        {
            if (sheet == null || revisionId == null) return -1;
            IList<ElementId> ids = sheet.GetAllRevisionIds();
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == revisionId) return i;
            }
            return -1;
        }

        public static ChangesOperationResult ApplyCheckedChange(
            Document doc,
            IList<ChangeSheetRow> setRows,
            ElementId revisionId,
            IReadOnlyList<RevisionCloud> clouds)
        {
            if (setRows == null || setRows.Count == 0)
                return ChangesOperationResult.Fail("В комплекте нет листов.");
            if (revisionId == null || revisionId == ElementId.InvalidElementId)
                return ChangesOperationResult.Fail("Выберите изменение.");

            var toWrite = setRows.Where(s => s.IsSelected && !s.IsAnnulled).ToList();
            var toRemove = setRows.Where(s => !s.IsSelected).ToList();
            if (toWrite.Count == 0 && toRemove.Count == 0)
                return ChangesOperationResult.Fail("Нет листов для обновления.");

            ChangesOperationResult removed = RemoveRevisionFromSheets(doc, revisionId, toRemove, clouds);

            if (toWrite.Count == 0)
                return removed.Success
                    ? ChangesOperationResult.Ok(removed.Message, removed.Count)
                    : removed;

            AssignAutoGroups(toWrite);
            ChangesOperationResult cloudResult = NumberClouds(doc, toWrite.Select(r => r.Id).ToList(), clouds);

            ChangesOperationResult assigned = AssignRevisionToSheets(doc, revisionId, toWrite.Select(r => r.Id).ToList(), true);
            if (!assigned.Success && assigned.Count == 0)
                Logger.Log("Назначение изменения: " + assigned.Message, 2);

            int written = 0;
            var errors = new List<string>();
            using (var t = new Transaction(doc, "TNov - заполнение параметров листов"))
            {
                try
                {
                    t.Start();
                    foreach (ChangeSheetRow row in toWrite)
                    {
                        var sheet = doc.GetElement(row.Id) as ViewSheet;
                        if (sheet == null) continue;
                        if (row.IsAnnulled)
                        {
                            Logger.Log("Лист " + sheet.Name + " пропущен (аннулирован)", 2);
                            continue;
                        }

                        if (!TrySetSheetContent(sheet, row.Content ?? string.Empty, out string contentError)
                            && !string.IsNullOrEmpty(contentError))
                            errors.Add(contentError);
                        if (!TrySetSheetGroup(sheet, row.Group ?? string.Empty, out string groupError)
                            && !string.IsNullOrEmpty(groupError))
                            errors.Add(groupError);

                        int index = GetStampLineIndex(sheet, revisionId);
                        if (index < 0)
                        {
                            errors.Add(sheet.SheetNumber + " — изменение не стоит на листе.");
                            continue;
                        }
                        if (index >= ChangesParams.CountLineGuids.Length)
                        {
                            errors.Add(sheet.SheetNumber + " — нет свободной строки штампа.");
                            continue;
                        }

                        List<StampLinePreview> lines = ReadStampLines(sheet);
                        while (lines.Count < ChangesParams.CountLineGuids.Length)
                            lines.Add(new StampLinePreview { CountText = string.Empty, SheetText = string.Empty });
                        lines[index].CountText = string.IsNullOrEmpty(row.CountText) ? "-" : row.CountText;
                        lines[index].SheetText = string.IsNullOrEmpty(row.ListText) ? "-" : row.ListText;
                        WriteStampLines(sheet, lines);

                        string note = BuildNoteFromLines(doc, sheet, lines);
                        WriteNote(sheet, note);
                        written++;
                    }
                    t.Commit();
                }
                catch (Exception ex)
                {
                    if (t.HasStarted()) t.RollBack();
                    Logger.Log("Ошибка заполнения штампа: " + ex.Message, 4);
                    return ChangesOperationResult.Fail(ex.Message);
                }
            }

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(removed.Message)) parts.Add(removed.Message);
            parts.Add(cloudResult.Success ? cloudResult.Message : "Облака: " + cloudResult.Message);
            parts.Add("Заполнен штамп на листах: " + written);
            string message = string.Join("\n", parts);
            if (errors.Count > 0)
                message += "\n" + string.Join("\n", errors.Distinct().Take(6));
            if (!cloudResult.Success && written == 0 && removed.Count == 0)
                return ChangesOperationResult.Fail(message);
            return ChangesOperationResult.Ok(message, written + removed.Count);
        }

        static bool CloudBelongsToRevision(RevisionCloud cloud, ElementId revisionId, Revision revision)
        {
            ElementId cloudRevId = GetCloudRevisionId(cloud);
            if (cloudRevId != ElementId.InvalidElementId)
                return cloudRevId == revisionId;
            return revision != null && GetCloudRevisionNumber(cloud) == GetRevisionNumber(revision);
        }

        public static ChangesOperationResult RemoveRevisionFromSheets(
            Document doc,
            ElementId revisionId,
            IList<ChangeSheetRow> rows,
            IReadOnlyList<RevisionCloud> clouds)
        {
            if (rows == null || rows.Count == 0)
                return ChangesOperationResult.Ok("", 0);

            var revision = doc.GetElement(revisionId) as Revision;
            var cloudsBySheet = IndexCloudsBySheet(doc, clouds ?? Array.Empty<RevisionCloud>());
            int removed = 0;
            int deletedClouds = 0;
            using (var t = new Transaction(doc, "TNov - Снять изменение с листов"))
            {
                try
                {
                    t.Start();
                    foreach (ChangeSheetRow row in rows)
                    {
                        var sheet = doc.GetElement(row.Id) as ViewSheet;
                        if (sheet == null) continue;
                        if (!sheet.GetAllRevisionIds().Contains(revisionId)) continue;

                        int stampIndex = GetStampLineIndex(sheet, revisionId);
                        List<StampLinePreview> lines = ReadStampLines(sheet);
                        if (stampIndex >= 0 && stampIndex < lines.Count)
                            lines.RemoveAt(stampIndex);

                        var cloudIds = new List<ElementId>();
                        if (cloudsBySheet.TryGetValue(sheet.Id, out List<RevisionCloud> sheetClouds))
                        {
                            foreach (RevisionCloud cloud in sheetClouds)
                            {
                                if (CloudBelongsToRevision(cloud, revisionId, revision))
                                    cloudIds.Add(cloud.Id);
                            }
                        }
                        if (cloudIds.Count > 0)
                        {
                            doc.Delete(cloudIds);
                            deletedClouds += cloudIds.Count;
                        }

                        var additional = sheet.GetAdditionalRevisionIds().ToList();
                        if (additional.Contains(revisionId))
                        {
                            additional.Remove(revisionId);
                            sheet.SetAdditionalRevisionIds(additional);
                        }

                        WriteStampLines(sheet, lines);
                        WriteNote(sheet, BuildNoteFromLines(doc, sheet, lines));
                        removed++;
                        Logger.Log("Снято изменение с листа " + sheet.SheetNumber, 2);
                    }
                    t.Commit();
                }
                catch (Exception ex)
                {
                    if (t.HasStarted()) t.RollBack();
                    Logger.Log("Ошибка снятия изменения: " + ex.Message, 4);
                    return ChangesOperationResult.Fail(ex.Message);
                }
            }

            if (removed == 0)
                return ChangesOperationResult.Ok("", 0);
            string message = "Снято с листов: " + removed;
            if (deletedClouds > 0)
                message += ", удалено облаков: " + deletedClouds;
            return ChangesOperationResult.Ok(message, removed);
        }

        static string RevisionNoteCaption(Revision revision)
        {
            if (revision == null) return string.Empty;
            return RevisionNoteCaption(revision.IssuedBy, revision.Description, GetRevisionNumber(revision));
        }

        static string RevisionNoteCaption(string issuedBy, string description, string systemNumber)
        {
            if (!string.IsNullOrWhiteSpace(issuedBy)) return issuedBy.Trim();
            if (!string.IsNullOrWhiteSpace(description)) return description.Trim();
            return systemNumber ?? string.Empty;
        }

        static string BuildNoteFromLines(Document doc, ViewSheet sheet, List<StampLinePreview> lines)
        {
            if (sheet == null || lines == null) return string.Empty;
            IList<ElementId> ids = sheet.GetAllRevisionIds();
            string comm = string.Empty;
            int max = Math.Min(ids.Count, lines.Count);
            for (int i = 0; i < max; i++)
            {
                StampLinePreview line = lines[i];
                if (string.IsNullOrEmpty(line.CountText) && string.IsNullOrEmpty(line.SheetText))
                    continue;
                var revision = doc.GetElement(ids[i]) as Revision;
                string caption = RevisionNoteCaption(revision);
                if (string.IsNullOrEmpty(caption)) continue;
                bool whole = ContainsNew(line.SheetText) || ContainsReplace(line.SheetText) || ContainsAnnul(line.SheetText);
                if (whole)
                    comm += caption + " (" + line.SheetText + "), ";
                else
                    comm += caption + ", ";
            }
            if (comm.Length == 0) return string.Empty;
            comm = "Изм. " + comm;
            return comm.Substring(0, comm.Length - 2);
        }

        public static ChangesOperationResult CreateRevision(Document doc, string description = "")
        {
            using (var t = new Transaction(doc, "TNov - Создать изменение"))
            {
                try
                {
                    t.Start();
                    Revision revision = Revision.Create(doc);
                    revision.Visibility = RevisionVisibility.CloudAndTagVisible;
                    if (!string.IsNullOrWhiteSpace(description))
                        revision.Description = description;
                    if (string.IsNullOrWhiteSpace(revision.RevisionDate))
                        revision.RevisionDate = DateTime.Today.ToString("dd.MM.yyyy");
                    t.Commit();
                    Logger.Log("Создано изменение.", 1);
                    return ChangesOperationResult.Ok("Создано изменение.", 1);
                }
                catch (Exception ex)
                {
                    Logger.Log("Ошибка создания изменения: " + ex.Message, 4);
                    return ChangesOperationResult.Fail(ex.Message);
                }
            }
        }

        public static ChangesOperationResult DeleteRevision(Document doc, ElementId revisionId)
        {
            var revision = doc.GetElement(revisionId) as Revision;
            if (revision == null) return ChangesOperationResult.Fail("Изменение не найдено.");
            if (revision.Issued) return ChangesOperationResult.Fail("Нельзя удалить выпущенное изменение.");
            using (var t = new Transaction(doc, "TNov - Удалить изменение"))
            {
                try
                {
                    t.Start();
                    doc.Delete(revisionId);
                    t.Commit();
                    return ChangesOperationResult.Ok("Изменение удалено.", 1);
                }
                catch (Exception ex)
                {
                    Logger.Log("Ошибка удаления изменения: " + ex.Message, 4);
                    return ChangesOperationResult.Fail(ex.Message);
                }
            }
        }

        public static ChangesOperationResult SaveRevisionFields(Document doc, RevisionRow row)
        {
            var revision = doc.GetElement(row.Id) as Revision;
            if (revision == null) return ChangesOperationResult.Fail("Изменение не найдено.");
            using (var t = new Transaction(doc, "TNov - Правка изменения"))
            {
                try
                {
                    t.Start();
                    revision.Description = row.Description ?? string.Empty;
                    revision.RevisionDate = row.RevisionDate ?? string.Empty;
                    revision.IssuedTo = row.IssuedTo ?? string.Empty;
                    revision.IssuedBy = row.IssuedBy ?? string.Empty;
                    revision.Visibility = RevisionVisibility.CloudAndTagVisible;
                    t.Commit();
                    return ChangesOperationResult.Ok();
                }
                catch (Exception ex)
                {
                    Logger.Log("Ошибка записи изменения: " + ex.Message, 4);
                    return ChangesOperationResult.Fail(ex.Message);
                }
            }
        }

        public static ChangesOperationResult UnissueAllRevisions(Document doc)
        {
            var revisions = CollectRevisions(doc);
            bool need = revisions.Any(r => r.Issued || r.Visibility != RevisionVisibility.CloudAndTagVisible);
            if (!need)
                return ChangesOperationResult.Ok("", 0);
            using (var t = new Transaction(doc, "TNov - Снять выпуск изменений"))
            {
                try
                {
                    t.Start();
                    int unissued = 0;
                    foreach (Revision revision in revisions)
                    {
                        if (revision.Issued)
                        {
                            revision.Issued = false;
                            unissued++;
                        }
                        if (revision.Visibility != RevisionVisibility.CloudAndTagVisible)
                            revision.Visibility = RevisionVisibility.CloudAndTagVisible;
                    }
                    t.Commit();
                    return ChangesOperationResult.Ok(
                        unissued == 0
                            ? ""
                            : (unissued == 1
                                ? "Снят выпуск у 1 изменения."
                                : "Снят выпуск у " + unissued + " изменений."),
                        unissued);
                }
                catch (Exception ex)
                {
                    Logger.Log("Ошибка снятия выпуска: " + ex.Message, 4);
                    return ChangesOperationResult.Fail(ex.Message);
                }
            }
        }

        public static ChangesOperationResult MoveRevision(Document doc, ElementId revisionId, int delta)
        {
            IList<ElementId> order = Revision.GetAllRevisionIds(doc).ToList();
            int index = -1;
            for (int i = 0; i < order.Count; i++)
            {
                if (order[i] == revisionId) { index = i; break; }
            }
            if (index < 0) return ChangesOperationResult.Fail("Изменение не найдено.");
            int target = index + delta;
            if (target < 0 || target >= order.Count) return ChangesOperationResult.Ok();
            ElementId tmp = order[target];
            order[target] = order[index];
            order[index] = tmp;
            using (var t = new Transaction(doc, "TNov - Порядок изменений"))
            {
                try
                {
                    t.Start();
                    Revision.ReorderRevisionSequence(doc, order);
                    t.Commit();
                    return ChangesOperationResult.Ok("Порядок изменений обновлён.");
                }
                catch (Exception ex)
                {
                    Logger.Log("Ошибка порядка изменений: " + ex.Message, 4);
                    return ChangesOperationResult.Fail(ex.Message);
                }
            }
        }

        public static ChangesOperationResult SetNumbering(Document doc, RevisionNumbering numbering)
        {
            using (var t = new Transaction(doc, "TNov - Нумерация изменений"))
            {
                try
                {
                    t.Start();
                    RevisionSettings.GetRevisionSettings(doc).RevisionNumbering = numbering;
                    t.Commit();
                    return ChangesOperationResult.Ok(numbering == RevisionNumbering.PerProject
                        ? "Нумерация: на проект."
                        : "Нумерация: на лист.");
                }
                catch (Exception ex)
                {
                    return ChangesOperationResult.Fail(ex.Message);
                }
            }
        }

        public static ChangesOperationResult AssignRevisionToSheets(Document doc, ElementId revisionId, IEnumerable<ElementId> sheetIds, bool assign)
        {
            int changed = 0;
            var errors = new List<string>();
            using (var t = new Transaction(doc, assign ? "TNov - Назначить изменение" : "TNov - Снять изменение"))
            {
                try
                {
                    t.Start();
                    foreach (ElementId sheetId in sheetIds)
                    {
                        var sheet = doc.GetElement(sheetId) as ViewSheet;
                        if (sheet == null) continue;
                        var additional = sheet.GetAdditionalRevisionIds().ToList();
                        bool hasAdditional = additional.Contains(revisionId);
                        bool hasAny = sheet.GetAllRevisionIds().Contains(revisionId);
                        if (assign)
                        {
                            if (hasAny) continue;
                            additional.Add(revisionId);
                            sheet.SetAdditionalRevisionIds(additional);
                            changed++;
                        }
                        else
                        {
                            if (hasAdditional)
                            {
                                additional.Remove(revisionId);
                                sheet.SetAdditionalRevisionIds(additional);
                                changed++;
                            }
                            else if (hasAny)
                            {
                                errors.Add(sheet.SheetNumber + " — изменение задано облаками, снимите облака на листе.");
                            }
                        }
                    }
                    t.Commit();
                }
                catch (Exception ex)
                {
                    Logger.Log("Ошибка назначения изменения: " + ex.Message, 4);
                    return ChangesOperationResult.Fail(ex.Message);
                }
            }

            string message = assign
                ? "Назначено листов: " + changed
                : "Снято с листов: " + changed;
            if (errors.Count > 0)
                message += "\n" + string.Join("\n", errors.Take(8));
            return errors.Count > 0 && changed == 0
                ? ChangesOperationResult.Fail(message)
                : ChangesOperationResult.Ok(message, changed);
        }

        public static ChangesOperationResult WriteSheetGroups(Document doc, IEnumerable<ChangeSheetRow> rows)
        {
            int written = 0;
            var errors = new List<string>();
            using (var t = new Transaction(doc, "TNov - Изм.Группа листов"))
            {
                t.Start();
                foreach (ChangeSheetRow row in rows)
                {
                    var sheet = doc.GetElement(row.Id) as ViewSheet;
                    if (sheet == null) continue;
                    if (!TrySetSheetGroup(sheet, row.Group ?? string.Empty, out string error))
                    {
                        if (!string.IsNullOrEmpty(error)) errors.Add(error);
                        continue;
                    }
                    written++;
                }
                t.Commit();
            }
            if (written == 0 && errors.Count > 0)
                return ChangesOperationResult.Fail(string.Join("\n", errors.Distinct().Take(6)));
            string msg = "Записано групп: " + written;
            if (errors.Count > 0) msg += "\n" + string.Join("\n", errors.Distinct().Take(6));
            return ChangesOperationResult.Ok(msg, written);
        }

        public static void AutoFillConsecutiveGroups(IList<ChangeSheetRow> rows)
        {
            var numbered = rows
                .Where(r => r.NumericNumber.HasValue)
                .OrderBy(r => r.NumericNumber.Value)
                .ToList();
            int i = 0;
            while (i < numbered.Count)
            {
                int start = i;
                while (i + 1 < numbered.Count
                       && numbered[i + 1].NumericNumber == numbered[i].NumericNumber + 1)
                {
                    i++;
                }
                if (i > start)
                {
                    string group = numbered[start].NumericNumber + "-" + numbered[i].NumericNumber;
                    for (int k = start; k <= i; k++)
                        numbered[k].Group = group;
                }
                i++;
            }
        }

        public static List<VedomostRow> BuildVedomost(
            IEnumerable<ChangeSheetRow> sheets,
            RevisionRow revision,
            string sheetSet)
        {
            var rows = new List<VedomostRow>();
            if (revision == null || revision.Id == ElementId.InvalidElementId)
                return rows;
            if (string.IsNullOrEmpty(sheetSet) || sheetSet == ChangesParams.AllSetsName)
                return rows;

            var involved = sheets
                .Where(s => !s.IsAnnulled && s.SheetSet == sheetSet)
                .ToList();
            if (involved.Count == 0) return rows;

            foreach (var group in involved.GroupBy(s =>
                         (s.Content ?? string.Empty).Trim() + "\n" + (s.ListText ?? string.Empty).Trim(),
                     StringComparer.CurrentCultureIgnoreCase))
            {
                var pack = group.ToList();
                ChangeSheetRow sample = pack[0];
                string countText = pack.Select(s => s.CountText).Distinct().Count() == 1
                    ? (sample.CountText ?? "-")
                    : string.Join(", ", pack.Select(s => s.CountText).Distinct());
                rows.Add(new VedomostRow
                {
                    RevisionNumber = string.IsNullOrWhiteSpace(revision.IssuedBy)
                        ? (revision.Description ?? string.Empty).Trim()
                        : revision.IssuedBy.Trim(),
                    Sequence = revision.Sequence,
                    SheetNumbers = FormatSheetRanges(pack),
                    CountText = countText,
                    SheetKind = sample.ListText,
                    Description = sample.Content,
                    Group = sample.Group,
                    SheetSet = sheetSet,
                    SheetCount = pack.Count
                });
            }

            return rows.OrderBy(r => r.SheetNumbers, NaturalStringComparer.Instance).ToList();
        }

        public static string FormatSheetRanges(IEnumerable<ChangeSheetRow> sheets)
        {
            var list = sheets.ToList();
            var numbered = list.Where(s => s.NumericNumber.HasValue).OrderBy(s => s.NumericNumber.Value).ToList();
            var rest = list.Where(s => !s.NumericNumber.HasValue)
                .Select(s => string.IsNullOrEmpty(s.DisplayNumber) ? s.SheetNumber : s.DisplayNumber)
                .OrderBy(s => s, NaturalStringComparer.Instance)
                .ToList();
            var parts = new List<string>();
            int i = 0;
            while (i < numbered.Count)
            {
                int start = numbered[i].NumericNumber.Value;
                int end = start;
                while (i + 1 < numbered.Count && numbered[i + 1].NumericNumber == end + 1)
                {
                    i++;
                    end = numbered[i].NumericNumber.Value;
                }
                parts.Add(start == end ? start.ToString() : start + "-" + end);
                i++;
            }
            parts.AddRange(rest);
            return string.Join(", ", parts);
        }
    }
}
