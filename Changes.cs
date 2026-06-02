using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using TNovCommon;

namespace TNovViewsSheets
{
    
    public class sheetChange
    {
        public int number;
        public string description;
        public int cloudcount;
        public bool newlist;
    }

    

    [Transaction(TransactionMode.Manual)]
    public class Changes : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {

            #region Исходные
            DateTime dateTime = DateTime.Now;
            string TNovVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            string DBCommandName = "Изменения";
            //подключение приложения и документа
            if (RevitAPI.UiApplication == null) { RevitAPI.Initialize(commandData); }
            UIDocument uidoc = RevitAPI.UiDocument; Document doc = RevitAPI.Document;
            UIApplication uiApp = RevitAPI.UiApplication; Autodesk.Revit.ApplicationServices.Application rvtApp = uiApp.Application;
            string docName = doc.Title.ToString(); docName = docName.Replace(",", " ");
            string userName = rvtApp.Username; userName = userName.Replace(",", "");
            string docNameUserName = "_" + userName; docName = docName.Replace(docNameUserName, "");
            docName = docName.Replace(",", "");
            #endregion

            TNovConfig config = TNovConfigLoad.LoadConfig(DBCommandName, TNovVersion);

            #region Настройки логов
            // создание log - файла
            Logger.Initialize(DBCommandName, dateTime, TNovVersion);

            var viewModel0 = new AppVersionViewModel();

            string jsonpath0 = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "TNovClient/TNovSettings.json");
            viewModel0 = JsonConvert.DeserializeObject<AppVersionViewModel>(File.ReadAllText(jsonpath0));
            if (viewModel0.extendedLogs)

            {
                var qViewModel = new QuestionWindowViewModel();
                qViewModel.headtxt = "Включены расширенные логи. " +
                    "Плагин будет работать медленнее, но соберет больше данных. " +
                    "Выключить расширенные логи для ускорения работы?";
                var qwpfview = new QuestionWindow280(qViewModel);
                qViewModel.CloseRequest += (s, e) => qwpfview.Close();
                bool? qok = qwpfview.ShowDialog();
                if (qok != null && qok == true) { Logger.TurnOffExtendedLogs(); } else Logger.Log("Расширенные логи вкл", 2);
            }
            #endregion

            #region Сбор элементов
            Logger.Log("Сбор элементов", 1);
            //получаем элементы

            List<RevisionCloud> clouds = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_RevisionClouds)
            .WhereElementIsNotElementType()
            .Cast<RevisionCloud>()
            .ToList();

            List<ViewSheet> sheets = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Sheets)
            .WhereElementIsNotElementType()
            .Cast<ViewSheet>()
            .ToList();

            List<Revision> revs = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Revisions)
            .WhereElementIsNotElementType()
            .Cast<Revision>()
            .ToList();
            #endregion

            #region Параметры
            Guid adskCommparamGuid = new Guid("a85b7661-26b0-412f-979c-66af80b4b2c3");//ADSK_Примечание
            Guid comm2paramGuid = new Guid("7243f857-6292-45a1-8727-26ea5b09f450");//Примечание
            List <Guid> NChangeLinePars1 = new List<Guid>() //N_Изм.СтрокаXX.Кол.уч.
            {
                new Guid("d37ea57b-0808-4d6d-92a1-7fc6227d3f18"), new Guid("688059f1-20a0-491b-b704-9e7735963a11"),
                new Guid("e82994ec-1686-4bc9-be42-1a3edc2dc6eb"), new Guid("342ab652-cb9c-4ca9-be4f-3be323f1fcea"),
                new Guid("5682ac53-4a96-4b99-b4c3-8b378d353ebd"), new Guid("7833ad6e-e746-47ea-8155-85bff1f819bf"),
                new Guid("b2c74713-128c-4df7-982d-bcdb941afb5c"), new Guid("8e092f0d-c607-4bdb-b73a-bcf71982f5d7"),
                new Guid("3fb4479d-84d7-429e-bd0f-7e6152ac9b0a"), new Guid("e57d297d-b0a7-4633-b4e9-c0e205b37db7"),
                new Guid("11776b7a-e30a-411d-ae48-f65ac87d4ef7"), new Guid("d2c00464-8a76-4234-a6cc-2e6767e8f64c"),
                new Guid("680d7933-4f58-4324-9eee-01675958fbd8"), new Guid("106e190a-4365-464a-b434-9587dde6fa03")
            };
            List<Guid> NChangeLinePars2 = new List<Guid>() //N_Изм.СтрокаXX.Лист.
            {
                new Guid("8b5eb639-5e9d-4597-aab5-930c3d919674"), new Guid("c8d0f5a1-4617-4cd4-932c-4c93f35bb213"),
                new Guid("140ea739-47c9-40bc-9b07-3b4d862eae99"), new Guid("76ad2603-00a9-4467-959c-4d3485539f7e"),
                new Guid("8967e4fa-3cce-42e9-a5a9-f6df001476dc"), new Guid("3bb18b6d-e381-4962-b8f7-9f64be8ec998"),
                new Guid("5c9388c8-ae8c-420e-9011-0e2c35219592"), new Guid("6830df72-f844-4edb-8cd1-c854fccc623c"),
                new Guid("d270aba5-f903-4dcb-87a1-49fb17f4c5a3"), new Guid("d7303fe2-677c-42cd-b378-409c75137193"),
                new Guid("4fdd5ee2-5ca9-4bd7-b7a4-7102084e1262"), new Guid("8ff1fd64-b4db-4549-8cdb-bdc8693e640e"),
                new Guid("d550c5c9-777e-4bbd-b394-79a85161eca8"), new Guid("7f13f8e5-ad02-486d-b4d1-1ade7a2d2e5f")
            };
            #endregion

            #region Диалог
            Logger.Log("Диалоговое окно",1);
            //Диалог
            var viewModel = new ChangesViewModel(); //....//нужна опция для выбранных (с фильтром по классу ViewSheet)
            // Десериализация
            bool forProject = true;
            json js = new json(in DBCommandName, in forProject, out bool canserialize, out string jsonpath);
            if (canserialize)
            {
                viewModel = JsonConvert.DeserializeObject<ChangesViewModel>(File.ReadAllText(jsonpath));
                Logger.Log("Десериализация прошла успешно",1);
            }
            var wpfview = new ChangesWPF(viewModel);
            viewModel.CloseRequest += (s, e) => wpfview.Close();
            bool? ok = wpfview.ShowDialog();
            if (ok != null && ok == true) { } else { Logger.Log("Запуск отменен пользователем. Завершение работы.", 3); return Result.Cancelled; }
            //Сериализация
            try
            {
                File.WriteAllText(jsonpath, JsonConvert.SerializeObject(viewModel));
                Logger.Log("Сериализация прошла успешно",1);
            }
            catch (Exception ex) { Logger.Log("Ошибка при сериализации: " + ex.Message,4); }
            #endregion

            Logger.Log("Проверяем, является ли открытый вид листом", 2);

            View v = doc.ActiveView;
            if (v == null||v.Category==null) 
            {
                new InfoWindow280("Ошибка! В настоящий момент активным окном является не лист.\n" +
                            "Если все же является - щелкните мышью 1 раз в пространстве листа.").ShowDialog();
                Logger.Log("Текущий вид не является листом. Завершение работы.", 3);
                return Result.Cancelled;
            }
            ElementId sheetCatId = new ElementId(-2003100);
            bool sheetView = v.Category.Id == sheetCatId;
            /*
            Autodesk.Revit.UI.Selection.Selection selection = commandData.Application.ActiveUIDocument.Selection;
            List<ViewSheet> ViewSheetList = new List<ViewSheet>();
            ViewSheetList = GetViewSheetsFromCurrentSelection(doc, selection); //получаем ViewSheet из текущей выборки
            */

            //настройки нумерации изменений в проекте
            RevisionSettings revSettings = RevisionSettings.GetRevisionSettings(doc);

            bool unhandledError = false;

            #region Основной код. Пометочные облака
            using (Transaction t = new Transaction(doc))
            {
                if (clouds.Count>0)
                {
                
                    if (viewModel.visible) Logger.Log("Сценарий: активный лист",1);
                    else Logger.Log("Сценарий: все листы",1);
                    if (viewModel.visible&&sheetView==false)
                    {
                        new InfoWindow280("Ошибка! В настоящий момент активным окном является не лист.\n" +
                            "Если все же является - щелкните мышью 1 раз в пространстве листа.").ShowDialog();
                        Logger.Log("Текущий вид не является листом. Завершение работы.", 3);
                        return Result.Cancelled;
                    }
                    ElementId activeViewId = v.Id;

                    try { 
                    //обработка элементов
                    t.Start("TNov - Пометочные облака");
                    Logger.Log("Открываем транзакцию 1 (пометочные облака)",1);

                    revSettings.RevisionNumbering = RevisionNumbering.PerProject;

                    var сloudsSortedBySheet = from cloud in clouds //сортированный список облаков по листам
                                                orderby cloud.OwnerViewId.ToString()
                                                select cloud;

                    var sheetsWithClouds = from cloud in сloudsSortedBySheet //список листов
                                           group cloud by cloud.OwnerViewId.ToString();

                    foreach (var sheet in sheetsWithClouds) 
                    {
                        var firstCloud = sheet.First();
                        string firstCloudSheetId = firstCloud.OwnerViewId.ToString();
                        if(viewModel.visible&&v.Id.ToString()!= firstCloudSheetId) continue; //обработка сценария "текущий лист"
                        /*if (ViewSheetList.Count > 0)
                        {
                            bool ViewSheetListContainsSheet = false;
                            foreach(var ViewSheet in ViewSheetList)
                            {
                                if (ViewSheet.Id.ToString() == firstCloudSheetId) ViewSheetListContainsSheet = true;
                            }
                            if(!ViewSheetListContainsSheet) continue; //обработка сценария "выбранные листы"
                        }*/

                        //Нумерация облаков

                        var sCloudsSortedByRevision = from sCloud in sheet //сортированный список облаков по изменению
                                                      orderby sCloud.get_Parameter(BuiltInParameter.REVISION_CLOUD_REVISION_NUM).AsString()
                                                      select sCloud;

                        var revisions = from sCloud in sCloudsSortedByRevision //список изменений
                                        group sCloud by sCloud.get_Parameter(BuiltInParameter.REVISION_CLOUD_REVISION_NUM).AsString();

                        int j = 0;
                        foreach (var revision in revisions)
                        {
                            j++;
                            int i = 0;
                            foreach (var rCloud in revision)
                            {
                                if (j == 1)
                                {
                                    ElementId sheetId = rCloud.OwnerViewId;
                                    Element cloudSheet = doc.GetElement(sheetId);
                                    Logger.Log("Лист " + cloudSheet.Name,2);
                                }
                                i++;
                                if (i == 1)
                                {
                                    string revisionName = rCloud.get_Parameter(BuiltInParameter.REVISION_CLOUD_REVISION_NUM).AsString();
                                    Logger.Log("   Изменение " + revisionName, 2);
                                }
                                List<Curve> curves = rCloud.GetSketchCurves().ToList();
                                double lengthSum = 0;
                                foreach (var curve in curves) lengthSum += curve.Length;
                                Parameter comm = rCloud.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                if (lengthSum > 0.03 && comm.IsReadOnly == false)
                                {
                                    comm.Set(i.ToString());
                                    Logger.Log("      Облако " + rCloud.Id.ToString() + ": назначен номер " + i.ToString(), 2);
                                }
                                else if (comm.IsReadOnly) { Logger.Log("      Изменение заблокировано в Вид - Изменения", 1); break; } //пропускаем этот изм
                                else Logger.Log("      Облако " + rCloud.Id.ToString() + ": номер не назначен, облако малой длины", 2);
                            }
                            
                        }

                        

                        

                    }

                    t.Commit(); Logger.Log("Закрываем транзакцию 1",1);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("Ошибка: " + ex.Message, 4);
                        new InfoWindow280("Ошибка: " + ex.Message).ShowDialog();
                        unhandledError = true;
                    }


                }
            }
            #endregion
            /*
            //Заполнение параметров листов
            if (ViewSheetList.Count > 0)
            {
                sheets = ViewSheetList; //сценарий "выбранные"
            }*/
            #region Основной код. Параметры листов
            using (Transaction t2 = new Transaction(doc))
            {
                try { 
                t2.Start("TNov - заполнение параметров листов"); Logger.Log("Открываем транзакцию 2 (параметры листов)",1);

                foreach (ViewSheet viewSheet in sheets) 
                {
                    if (viewModel.visible && v.Id.ToString() != viewSheet.Id.ToString()) continue; //обработка сценария "текущий лист"

                    //пропуск аннулированных листов, поиск новых
                    bool isSheetAnnul = false;
                    if (Param.ParamExistByGuid(adskCommparamGuid, viewSheet))
                    {
                        string sheetComments = viewSheet.get_Parameter(adskCommparamGuid).AsString();
                        if (!string.IsNullOrEmpty(sheetComments))
                        {
                            if (sheetComments.Contains("Аннул") || sheetComments.Contains("аннул")) isSheetAnnul = true;
                        }
                    }
                    if (Param.ParamExistByGuid(comm2paramGuid, viewSheet))
                    {
                        string sheetComments = viewSheet.get_Parameter(comm2paramGuid).AsString();
                        if (!string.IsNullOrEmpty(sheetComments))
                        {
                            if (sheetComments.Contains("Аннул") || sheetComments.Contains("аннул")) isSheetAnnul = true;
                        }
                    }

                    List<string> allLinesComments = new List<string>();
                    foreach(var guid in NChangeLinePars2)
                    {
                        if (Param.ParamExistByGuid(guid, viewSheet))
                        {
                            string ChangeLineComments = viewSheet.get_Parameter(guid).AsString();
                            if (!string.IsNullOrEmpty(ChangeLineComments))
                            {
                                if (ChangeLineComments.Contains("Аннул") || ChangeLineComments.Contains("аннул"))
                                {
                                    isSheetAnnul = true; break;
                                }
                                allLinesComments.Add(ChangeLineComments);
                            }
                        }
                        
                    }
                    if (isSheetAnnul) continue;

                    //получаем id изменений на листе
                    IList<ElementId> revisionIds = viewSheet.GetAllRevisionIds(); //получение изменений на листе

                    int newRev = -1; //счетчик изменений с "нов"
                    if (allLinesComments.Count > 0) //проверяем, новый ли лист 
                    {
                        //....//нужна более гибкая логика - например если лист был новый, но изменен/заменен в след изм-и,
                        //то нужно учитывать колво изм-й на листе

                        
                        foreach (var line in allLinesComments)
                        {
                            if(line.Contains("Нов")|| line.Contains("нов"))
                            {
                                newRev++;
                            }
                        }
                        
                             
                    }

                    Logger.Log("Лист "+viewSheet.Name, 1);

                    Logger.Log("Сбор данных для листа",2);
                    var revisionsOnSheet = new List<Revision>();
                    
                    foreach (ElementId revisionId in revisionIds)
                    {
                        Revision rev = (Revision)doc.GetElement(revisionId);
                        Logger.Log("   "+rev.RevisionNumber+" "+rev.Description, 2);
                        Revision revision = (Revision)(doc.GetElement(revisionId));
                        revisionsOnSheet.Add(revision);
                    }
                    //получаем облака на листе
                    List<RevisionCloud> cloudsOnSheet = new List<RevisionCloud>();
                    Logger.Log("лист " + viewSheet.Id.ToString(),2);
                    foreach (var cloud in clouds)
                    {
                        //id листа
                        string revListId = cloud.OwnerViewId.ToString();
                        Logger.Log("   " + revListId, 2);
                        //проверка соответствия
                        if (revListId == viewSheet.Id.ToString()) 
                        { cloudsOnSheet.Add(cloud); Logger.Log("      добавлено", 2); }
                    }
                    Logger.Log("Заполнение списка элементов класса sheetChange", 2);
                    List<sheetChange> sheetChanges = new List<sheetChange>();
                    int sheetChangeCounter = 0;
                    foreach (var revision in revisionsOnSheet)
                    {
                        Logger.Log("изм " + revision.RevisionNumber, 2);
                        int cloudsOfRevision = 0;
                        foreach (var cloud in cloudsOnSheet)
                        {
                            //проверка соответствия облака  изменению
                            string revNum = cloud.get_Parameter(BuiltInParameter.REVISION_CLOUD_REVISION_NUM).AsString();
                            Logger.Log("облако " + revNum,2);
                            //проверка длины облака 
                            List<Curve> curves = cloud.GetSketchCurves().ToList();
                            double lengthSum = 0;
                            foreach (var curve in curves) lengthSum += curve.Length;
                            if (revNum ==revision.RevisionNumber&& lengthSum>0.03) cloudsOfRevision++;
                        }
                        sheetChange sChange = new sheetChange();
                        sChange.cloudcount = cloudsOfRevision; Logger.Log("кол-во облаков " + cloudsOfRevision.ToString(), 2);
                        sChange.description = revision.Description;
                        sChange.number = int.Parse(revision.RevisionNumber);
                        if (newRev == sheetChangeCounter) sChange.newlist = true; else sChange.newlist = false;
                        sheetChanges.Add(sChange);
                        sheetChangeCounter++;
                    }

                    var sheetChangesSorted = sheetChanges.OrderBy(s => s.number).ToList();

                    List<sheetChange> sheetChangesFinal=new List<sheetChange>();

                    //логика если галочка

                    if (viewModel.purge)
                    {
                        Logger.Log("Оставляем только последнее заменяющее изменение и последующие", 2);
                        sheetChangesSorted.Reverse();
                        int replaces = 0;
                        foreach (var sChange in sheetChangesSorted)
                        {
                            if(replaces==0) sheetChangesFinal.Add(sChange);
                            if (sChange.cloudcount == 0) replaces++;
                        }
                        sheetChangesFinal.Reverse();
                        
                        foreach (var sChange in sheetChangesFinal) 
                        {
                            Logger.Log("   "+sChange.number+" " + sChange.description + " " + sChange.cloudcount,2);
                        }
                        
                    }
                    else
                    {
                        foreach (var sChange in sheetChangesSorted)
                        {
                            sheetChangesFinal.Add(sChange);
                        }

                        foreach (var sChange in sheetChangesFinal)
                        {
                            Logger.Log("   " + sChange.number + " " + sChange.description + " " + sChange.cloudcount, 2);
                        }
                    }

                    Logger.Log("Заполняем переменные", 2);
                    //переменные для заполнения значений
                    string commValue ="";
                    List<string> changesStrK = new List<string>();
                    List<string> changesStrL = new List<string>();

                    //заполнение переменных
                    
                    foreach (var sC in sheetChangesFinal)
                    {
                        string strL = "Зам."; if (sC.newlist) strL = "Нов.";
                        if (sC.cloudcount == 0)
                        {
                            commValue += sC.description +" ("+ strL+"), ";
                            changesStrK.Add("-");
                            changesStrL.Add(strL);
                        }
                        else
                        {
                            commValue += sC.description +", ";
                            changesStrK.Add(sC.cloudcount.ToString());
                            changesStrL.Add("-");
                        }
                    }
                    if (commValue.Length > 0)
                    {
                        commValue = "Изм. " + commValue;
                        commValue = commValue.Substring(0, commValue.Length - 2);
                    }

                    Logger.Log("Заполняем параметры", 2);
                    //Примечание
                    if (commValue.Length > 0) 
                    {
                        try
                        {
                            viewSheet.get_Parameter(adskCommparamGuid).Set(commValue);
                            Logger.Log("   Примечание: " + commValue,2);
                        }
                        catch (Exception ex) { Logger.Log(viewSheet.Name + " ошибка заполнения Примечания: " + ex.Message,4); }
                        try
                        {
                            viewSheet.get_Parameter(comm2paramGuid).Set(commValue);
                            Logger.Log("   Примечание: " + commValue, 2);
                        }
                        catch (Exception ex) { Logger.Log(viewSheet.Name + " ошибка заполнения Примечания: " + ex.Message, 4); }
                    }


                    //параметры штампа
                    for (int i = 0; i < NChangeLinePars1.Count(); i++)
                    {
                        if (i < changesStrK.Count)
                        {
                            //запись значений из списков
                            try
                            {
                                viewSheet.get_Parameter(NChangeLinePars1[i]).Set(changesStrK[i]);
                                Logger.Log("      N_Изм.Строка" + (i + 1).ToString() + ".Кол.уч: " + changesStrK[i], 2);
                                viewSheet.get_Parameter(NChangeLinePars2[i]).Set(changesStrL[i]);
                                Logger.Log("      N_Изм.Строка" + (i + 1).ToString() + ".Лист: " + changesStrL[i], 2);
                            }
                            catch (Exception ex) { Logger.Log(viewSheet.Name + " ошибка заполнения штампа: " + ex.Message, 4); }
                        }
                        else
                        {
                            //запись пустых значений
                            try
                            {
                                viewSheet.get_Parameter(NChangeLinePars1[i]).Set("");
                                Logger.Log("      N_Изм.Строка" + (i + 1).ToString() + ".Кол.уч: очищаем", 2);
                                viewSheet.get_Parameter(NChangeLinePars2[i]).Set("");
                                Logger.Log("      N_Изм.Строка" + (i + 1).ToString() + ".Лист: очищаем", 2);
                            }
                            catch (Exception ex) { Logger.Log(viewSheet.Name + " ошибка заполнения штампа: " + ex.Message, 4); }
                        }
                    }

                    


                }
                t2.Commit(); Logger.Log("Закрываем транзакцию 2",1);
                }
                catch (Exception ex)
                {
                    Logger.Log("Ошибка: " + ex.Message, 4);
                    new InfoWindow280("Ошибка: " + ex.Message).ShowDialog();
                    unhandledError = true;
                }
            }
            #endregion
            if (unhandledError)
            {
                Logger.Log("Завершение работы с ошибками.", 4);
                return Result.Succeeded;
            }
            Logger.Log("Завершение работы.",5);
            return Result.Succeeded;
        }
    }
}
