using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace RoomFinishNumerator
{
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    class RoomFinishNumeratorCommand : IExternalCommand
    {
        RoomFinishNumeratorProgressBarWPF roomFinishNumeratorProgressBarWPF;
        RoomFinishNumeratorOpeningsProgressBarWPF roomFinishNumeratorOpeningsProgressBarWPF;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                _ = GetPluginStartInfo();
            }
            catch { }

            Document doc = commandData.Application.ActiveUIDocument.Document;

            var roomFinishNumeratorWPF = new RoomFinishNumeratorWPF();
            roomFinishNumeratorWPF.ShowDialog();
            if (roomFinishNumeratorWPF.DialogResult != true)
            {
                return Result.Cancelled;
            }

            var roomFinishNumberingSelectedName = roomFinishNumeratorWPF.RoomFinishNumberingSelectedName;
            var considerCeilings = roomFinishNumeratorWPF.ConsiderCeilings;
            var considerOpenings = roomFinishNumeratorWPF.ConsiderOpenings;
            var considerBaseboards = roomFinishNumeratorWPF.ConsiderBaseboards;

            if (!CheckRequiredParameters(doc, roomFinishNumberingSelectedName, considerBaseboards, ref message))
            {
                return Result.Failed;
            }

            using (TransactionGroup tg = new TransactionGroup(doc))
            {
                tg.Start("Нумерация отделки");

                // Fill zero in wall finish heights
                FillZeroInWallFinishHeights(doc);

                // Calculate openings areas
                if (considerOpenings)
                {
                    CalculateOpeningsAreas(doc);
                }
                else
                {
                    ResetOpeningsAreas(doc);
                }

                if (roomFinishNumberingSelectedName == "rbt_EndToEndThroughoutTheProject")
                {
                    NumberRoomsEndToEnd(doc, considerCeilings);
                    if (considerBaseboards)
                    {
                        NumberBaseboardsEndToEnd(doc);
                    }
                }
                else if (roomFinishNumberingSelectedName == "rbt_SeparatedByLevels")
                {
                    NumberRoomsByLevels(doc, considerCeilings);
                    if (considerBaseboards)
                    {
                        NumberBaseboardsByLevels(doc);
                    }
                }
                tg.Assimilate();
            }

            return Result.Succeeded;
        }
        private bool CheckRequiredParameters(Document doc, string roomFinishNumberingSelectedName, bool considerBaseboards, ref string message)
        {
            var rooms = new FilteredElementCollector(doc)
                        .OfClass(typeof(SpatialElement))
                        .WhereElementIsNotElementType()
                        .OfType<Room>()
                        .Where(r => r.Area > 0)
                        .ToList();

            bool missingParameters = false;

            if (roomFinishNumberingSelectedName == "rbt_EndToEndThroughoutTheProject" || roomFinishNumberingSelectedName == "rbt_SeparatedByLevels")
            {
                foreach (var room in rooms)
                {
                    if (room.LookupParameter("АР_НомераПомещенийВедОтделки") == null ||
                        room.LookupParameter("АР_ИменаПомещенийВедОтделки") == null ||
                        room.get_Parameter(BuiltInParameter.ROOM_FINISH_WALL) == null ||
                        room.LookupParameter("АР_ОтделкаСтенСнизу") == null)
                    {
                        message = "Отсутствуют необходимые параметры для нумерации отделки.";
                        missingParameters = true;
                        break;
                    }

                    if (considerBaseboards && room.LookupParameter("АР_НомераПомещенийВедПлинтусов") == null)
                    {
                        message = "Отсутствует параметр 'АР_НомераПомещенийВедПлинтусов' для нумерации плинтусов.";
                        missingParameters = true;
                        break;
                    }
                }
            }

            return !missingParameters;
        }

        private void FillZeroInWallFinishHeights(Document doc)
        {
            using (var t = new Transaction(doc, "Заполнение нулей в отделке стен снизу"))
            {
                t.Start();
                var roomList = new FilteredElementCollector(doc)
                    .OfClass(typeof(SpatialElement))
                    .WhereElementIsNotElementType()
                    .OfType<Room>()
                    .Where(r => r.Area > 0)
                    .OrderBy(r => (doc.GetElement(r.LevelId) as Level).Elevation)
                    .ToList();

                foreach (var room in roomList)
                {
                    var param = room.LookupParameter("АР_ВысотаОтделкиСтенСнизу");
                    if (param != null && !param.IsReadOnly && param.AsDouble().Equals(0))
                    {
                        room.LookupParameter("АР_ВысотаОтделкиСтенСнизу").Set(0);
                    }
                }
                t.Commit();
            }
        }

        private void CalculateOpeningsAreas(Document doc)
        {
            using (var t = new Transaction(doc, "Вычисление площадей проемов"))
            {
                t.Start();
                var roomList = GetRooms(doc);
                StartProgressBarForOpenings(roomList.Count);

                Phase phase = GetLastPhase(doc);

                int step = 0;
                foreach (var room in roomList)
                {
                    step++;
                    UpdateProgressBarForOpenings(step);

                    double doorsInRoomArea = CalculateDoorsArea(doc, room, phase);
                    double windowsInRoomArea = CalculateWindowsArea(doc, room, phase);
                    double curtainWallArea = CalculateCurtainWallArea(doc, room, phase);

                    var openingsAreaParamGuid = new Guid("18e3f49d-1315-415f-8359-8f045a7a8938");
                    var param = room.get_Parameter(openingsAreaParamGuid);
                    param?.Set(doorsInRoomArea + windowsInRoomArea + curtainWallArea);
                }

                CloseProgressBarForOpenings();
                t.Commit();
            }
        }

        private void ResetOpeningsAreas(Document doc)
        {
            using (var t = new Transaction(doc, "Сброс площадей проемов"))
            {
                t.Start();
                var roomList = GetRooms(doc);

                var openingsAreaParamGuid = new Guid("18e3f49d-1315-415f-8359-8f045a7a8938");
                foreach (var room in roomList)
                {
                    var param = room.get_Parameter(openingsAreaParamGuid);
                    if (param != null)
                    {
                        room.get_Parameter(openingsAreaParamGuid).Set(0);
                    }
                }
                t.Commit();
            }
        }

        private void NumberRoomsEndToEnd(Document doc, bool considerCeilings)
        {
            using (var tg = new TransactionGroup(doc, "Нумерация отделки"))
            {
                tg.Start();
                var roomList = GetRooms(doc);
                var roomPropertiesList = GetRoomPropertiesList(roomList, considerCeilings);

                StartProgressBar(roomPropertiesList.Count);

                int step = 0;
                using (var t = new Transaction(doc, "Внесение номеров в помещения"))
                {
                    t.Start();
                    foreach (var rp in roomPropertiesList)
                    {
                        step++;
                        UpdateProgressBar(step);

                        var roomListForNumbering = GetRoomsForNumbering(doc, rp, considerCeilings);
                        var roomNumbersByRoom = string.Join(", ", roomListForNumbering.Select(r => r.Number));
                        var roomNamesByRoom = string.Join(", ", roomListForNumbering.Select(r => r.get_Parameter(BuiltInParameter.ROOM_NAME).AsString()).Distinct());

                        foreach (var r in roomListForNumbering)
                        {
                            if (!r.LookupParameter("АР_НомераПомещенийВедОтделки").IsReadOnly)
                            {
                                r.LookupParameter("АР_НомераПомещенийВедОтделки").Set(roomNumbersByRoom);
                            }
                            if (!r.LookupParameter("АР_ИменаПомещенийВедОтделки").IsReadOnly)
                            {
                                r.LookupParameter("АР_ИменаПомещенийВедОтделки").Set(roomNamesByRoom);
                            }
                        }
                    }
                    CloseProgressBar();
                    t.Commit();
                }
                tg.Assimilate();
            }
        }

        private void NumberRoomsByLevels(Document doc, bool considerCeilings)
        {
            using (var tg = new TransactionGroup(doc, "Нумерация отделки"))
            {
                tg.Start();
                var levelList = GetLevels(doc);
                StartProgressBar(levelList.Count);

                int step = 0;
                foreach (var lv in levelList)
                {
                    step++;
                    UpdateProgressBar(step);

                    var roomList = GetRoomsOnLevel(doc, lv);
                    var roomPropertiesList = GetRoomPropertiesList(roomList, considerCeilings);

                    using (var t = new Transaction(doc, "Внесение номеров в помещения"))
                    {
                        t.Start();
                        foreach (var rp in roomPropertiesList)
                        {
                            var roomListForNumbering = GetRoomsForNumberingOnLevel(doc, rp, lv, considerCeilings);
                            var roomNumbersByRoom = string.Join(", ", roomListForNumbering.Select(r => r.Number));
                            var roomNamesByRoom = string.Join(", ", roomListForNumbering.Select(r => r.get_Parameter(BuiltInParameter.ROOM_NAME).AsString()).Distinct());

                            foreach (var r in roomListForNumbering)
                            {
                                if (!r.LookupParameter("АР_НомераПомещенийВедОтделки").IsReadOnly)
                                {
                                    r.LookupParameter("АР_НомераПомещенийВедОтделки").Set(roomNumbersByRoom);
                                }
                                if (!r.LookupParameter("АР_ИменаПомещенийВедОтделки").IsReadOnly)
                                {
                                    r.LookupParameter("АР_ИменаПомещенийВедОтделки").Set(roomNamesByRoom);
                                }
                            }
                        }
                        t.Commit();
                    }
                }
                CloseProgressBar();
                tg.Assimilate();
            }
        }

        private void NumberBaseboardsEndToEnd(Document doc)
        {
            using (var tg = new TransactionGroup(doc, "Нумерация плинтусов"))
            {
                tg.Start();
                var roomList = GetRooms(doc);
                var baseboardPropertiesList = GetBaseboardPropertiesList(roomList);

                StartProgressBar(baseboardPropertiesList.Count);

                int step = 0;
                using (var t = new Transaction(doc, "Внесение номеров плинтусов в помещения"))
                {
                    t.Start();
                    foreach (var bp in baseboardPropertiesList)
                    {
                        step++;
                        UpdateProgressBar(step);

                        var roomListForNumbering = GetRoomsForBaseboardNumbering(doc, bp);
                        var baseboardNumbersByRoom = string.Join(", ", roomListForNumbering.Select(r => r.Number));

                        foreach (var r in roomListForNumbering)
                        {
                            if (!r.LookupParameter("АР_НомераПомещенийВедПлинтусов").IsReadOnly)
                            {
                                r.LookupParameter("АР_НомераПомещенийВедПлинтусов").Set(baseboardNumbersByRoom);
                            }
                        }
                    }
                    CloseProgressBar();
                    t.Commit();
                }
                tg.Assimilate();
            }
        }

        private void NumberBaseboardsByLevels(Document doc)
        {
            using (var tg = new TransactionGroup(doc, "Нумерация плинтусов"))
            {
                tg.Start();
                var levelList = GetLevels(doc);
                StartProgressBar(levelList.Count);

                int step = 0;
                foreach (var lv in levelList)
                {
                    step++;
                    UpdateProgressBar(step);

                    var roomList = GetRoomsOnLevel(doc, lv);
                    var baseboardPropertiesList = GetBaseboardPropertiesList(roomList);

                    using (var t = new Transaction(doc, "Внесение номеров плинтусов в помещения"))
                    {
                        t.Start();
                        foreach (var bp in baseboardPropertiesList)
                        {
                            var roomListForNumbering = GetRoomsForBaseboardNumberingOnLevel(doc, bp, lv);
                            var baseboardNumbersByRoom = string.Join(", ", roomListForNumbering.Select(r => r.Number));

                            foreach (var r in roomListForNumbering)
                            {
                                if (!r.LookupParameter("АР_НомераПомещенийВедПлинтусов").IsReadOnly)
                                {
                                    r.LookupParameter("АР_НомераПомещенийВедПлинтусов").Set(baseboardNumbersByRoom);
                                }
                            }
                        }
                        t.Commit();
                    }
                }
                CloseProgressBar();
                tg.Assimilate();
            }
        }

        private List<Room> GetRooms(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(SpatialElement))
                .WhereElementIsNotElementType()
                .OfType<Room>()
                .Where(r => r.Area > 0)
                .OrderBy(r => (doc.GetElement(r.LevelId) as Level).Elevation)
                .ToList();
        }

        private List<Room> GetRoomsOnLevel(Document doc, Level level)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(SpatialElement))
                .WhereElementIsNotElementType()
                .OfType<Room>()
                .Where(r => r.Area > 0)
                .Where(r => r.LevelId == level.Id)
                .ToList();
        }

        private List<RoomProperties> GetRoomPropertiesList(List<Room> roomList, bool considerCeilings)
        {
            var roomPropertiesList = new List<RoomProperties>();
            foreach (var room in roomList)
            {
                var tempRoomProperties = new RoomProperties
                {
                    CeilingFinishStrParam = considerCeilings ? room.get_Parameter(BuiltInParameter.ROOM_FINISH_CEILING).AsString() : null,
                    WallFinishStrParam = room.get_Parameter(BuiltInParameter.ROOM_FINISH_WALL).AsString(),
                    BottomWallFinishStrParam = room.LookupParameter("АР_ОтделкаСтенСнизу").AsString()
                };

                if (!roomPropertiesList.Contains(tempRoomProperties))
                {
                    roomPropertiesList.Add(tempRoomProperties);
                }
            }

            return roomPropertiesList;
        }

        private List<BaseboardProperties> GetBaseboardPropertiesList(List<Room> roomList)
        {
            var baseboardPropertiesList = new List<BaseboardProperties>();
            foreach (var room in roomList)
            {
                var tempBaseboardProperties = new BaseboardProperties
                {
                    BaseboardTypeStrParam = room.get_Parameter(BuiltInParameter.ROOM_FINISH_BASE).AsString()
                };

                if (!baseboardPropertiesList.Contains(tempBaseboardProperties))
                {
                    baseboardPropertiesList.Add(tempBaseboardProperties);
                }
            }

            return baseboardPropertiesList;
        }

        private List<Room> GetRoomsForNumbering(Document doc, RoomProperties rp, bool considerCeilings)
        {
            var query = new FilteredElementCollector(doc)
                .OfClass(typeof(SpatialElement))
                .WhereElementIsNotElementType()
                .OfType<Room>()
                .Where(r => r.Area > 0)
                .Where(r => r.get_Parameter(BuiltInParameter.ROOM_FINISH_WALL).AsString() == rp.WallFinishStrParam)
                .Where(r => r.LookupParameter("АР_ОтделкаСтенСнизу").AsString() == rp.BottomWallFinishStrParam);

            if (considerCeilings)
            {
                query = query.Where(r => r.get_Parameter(BuiltInParameter.ROOM_FINISH_CEILING).AsString() == rp.CeilingFinishStrParam);
            }

            return query.OrderBy(r => r.Number, new AlphanumComparatorFastString()).ToList();
        }

        private List<Room> GetRoomsForNumberingOnLevel(Document doc, RoomProperties rp, Level level, bool considerCeilings)
        {
            var query = new FilteredElementCollector(doc)
                .OfClass(typeof(SpatialElement))
                .WhereElementIsNotElementType()
                .OfType<Room>()
                .Where(r => r.Area > 0)
                .Where(r => r.LevelId == level.Id)
                .Where(r => r.get_Parameter(BuiltInParameter.ROOM_FINISH_WALL).AsString() == rp.WallFinishStrParam)
                .Where(r => r.LookupParameter("АР_ОтделкаСтенСнизу").AsString() == rp.BottomWallFinishStrParam);

            if (considerCeilings)
            {
                query = query.Where(r => r.get_Parameter(BuiltInParameter.ROOM_FINISH_CEILING).AsString() == rp.CeilingFinishStrParam);
            }

            return query.OrderBy(r => r.Number, new AlphanumComparatorFastString()).ToList();
        }

        private List<Room> GetRoomsForBaseboardNumbering(Document doc, BaseboardProperties bp)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(SpatialElement))
                .WhereElementIsNotElementType()
                .OfType<Room>()
                .Where(r => r.Area > 0)
                .Where(r => r.get_Parameter(BuiltInParameter.ROOM_FINISH_BASE).AsString() == bp.BaseboardTypeStrParam)
                .OrderBy(r => r.Number, new AlphanumComparatorFastString())
                .ToList();
        }

        private List<Room> GetRoomsForBaseboardNumberingOnLevel(Document doc, BaseboardProperties bp, Level level)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(SpatialElement))
                .WhereElementIsNotElementType()
                .OfType<Room>()
                .Where(r => r.Area > 0)
                .Where(r => r.LevelId == level.Id)
                .Where(r => r.get_Parameter(BuiltInParameter.ROOM_FINISH_BASE).AsString() == bp.BaseboardTypeStrParam)
                .OrderBy(r => r.Number, new AlphanumComparatorFastString())
                .ToList();
        }

        private Phase GetLastPhase(Document doc)
        {
            var phases = doc.Phases;
            return phases.Size > 0 ? phases.get_Item(phases.Size - 1) : null;
        }

        private double CalculateDoorsArea(Document doc, Room room, Phase phase)
        {
            var doorsInRoomArea = 0.0;
            var doorsOnRoomLevelList = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Doors)
                .OfClass(typeof(FamilyInstance))
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .Where(d => d.LevelId == room.LevelId)
                .Where(d => phase == null || d.GetPhaseStatus(phase.Id) != ElementOnPhaseStatus.Demolished)
                .ToList();

            var doorsInRoomList = doorsOnRoomLevelList
                .Where(d => d.Room?.Id == room.Id || d.FromRoom?.Id == room.Id || d.ToRoom?.Id == room.Id)
                .Distinct()
                .ToList();

            foreach (var door in doorsInRoomList)
            {
                var roughHeight = door.Symbol.get_Parameter(BuiltInParameter.FAMILY_ROUGH_HEIGHT_PARAM)?.AsDouble() ?? 0;
                var roughWidth = door.Symbol.get_Parameter(BuiltInParameter.FAMILY_ROUGH_WIDTH_PARAM)?.AsDouble() ?? 0;
                var caseworkHeight = door.Symbol.get_Parameter(BuiltInParameter.CASEWORK_HEIGHT)?.AsDouble() ?? 0;
                var caseworkWidth = door.Symbol.get_Parameter(BuiltInParameter.CASEWORK_WIDTH)?.AsDouble() ?? 0;

                var maxHeight = Math.Max(roughHeight, caseworkHeight);
                var maxWidth = Math.Max(roughWidth, caseworkWidth);

                doorsInRoomArea += maxHeight * maxWidth;
            }

            return doorsInRoomArea;
        }

        private double CalculateWindowsArea(Document doc, Room room, Phase phase)
        {
            var windowsInRoomArea = 0.0;
            var windowsOnRoomLevelList = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Windows)
                .OfClass(typeof(FamilyInstance))
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .Where(w => w.LevelId == room.LevelId)
                .Where(w => phase == null || w.GetPhaseStatus(phase.Id) != ElementOnPhaseStatus.Demolished)
                .ToList();

            var windowsInRoomList = windowsOnRoomLevelList
                .Where(w => w.Room?.Id == room.Id || w.FromRoom?.Id == room.Id || w.ToRoom?.Id == room.Id)
                .Distinct()
                .ToList();

            foreach (var window in windowsInRoomList)
            {
                var roughHeight = window.Symbol.get_Parameter(BuiltInParameter.FAMILY_ROUGH_HEIGHT_PARAM)?.AsDouble() ?? 0;
                var roughWidth = window.Symbol.get_Parameter(BuiltInParameter.FAMILY_ROUGH_WIDTH_PARAM)?.AsDouble() ?? 0;
                var caseworkHeight = window.Symbol.get_Parameter(BuiltInParameter.CASEWORK_HEIGHT)?.AsDouble() ?? 0;
                var caseworkWidth = window.Symbol.get_Parameter(BuiltInParameter.CASEWORK_WIDTH)?.AsDouble() ?? 0;

                var maxHeight = Math.Max(roughHeight, caseworkHeight);
                var maxWidth = Math.Max(roughWidth, caseworkWidth);

                windowsInRoomArea += maxHeight * maxWidth;
            }

            return windowsInRoomArea;
        }

        private double CalculateCurtainWallArea(Document doc, Room room, Phase phase)
        {
            var curtainWallArea = 0.0;
            var roomSolid = GetRoomSolid(room);

            if (roomSolid != null)
            {
                var curtainWallsList = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Walls)
                    .OfClass(typeof(Wall))
                    .WhereElementIsNotElementType()
                    .OfType<Wall>()
                    .Where(w => w.LevelId == room.LevelId)
                    .Where(w => w.CurtainGrid != null)
                    .Where(w => phase == null || w.GetPhaseStatus(phase.Id) != ElementOnPhaseStatus.Demolished)
                    .ToList();

                var intersectOptions = new SolidCurveIntersectionOptions();
                foreach (var wall in curtainWallsList)
                {
                    var curtainPanelsIdList = wall.CurtainGrid.GetPanelIds().ToList();
                    foreach (var panelId in curtainPanelsIdList)
                    {
                        var panel = doc.GetElement(panelId) as Panel;
                        if (panel == null)
                        {
                            var doorwindows = doc.GetElement(panelId) as FamilyInstance;
                            if (doorwindows != null)
                            {
                                var curtainWallPanelsHeight = doorwindows.get_Parameter(BuiltInParameter.CURTAIN_WALL_PANELS_HEIGHT)?.AsDouble() ?? 0;
                                var curtainWallPanelsWidth = doorwindows.get_Parameter(BuiltInParameter.CURTAIN_WALL_PANELS_WIDTH)?.AsDouble() ?? 0;

                                var doorwindowsBoundingBox = doorwindows.get_BoundingBox(null);
                                if (doorwindowsBoundingBox == null) continue;
                                var doorwindowsCenter = (doorwindowsBoundingBox.Max + doorwindowsBoundingBox.Min) / 2;
                                var lineA = Line.CreateBound(doorwindowsCenter, doorwindowsCenter + (600 / 304.8) * doorwindows.FacingOrientation.Normalize()) as Curve;
                                var lineB = Line.CreateBound(doorwindowsCenter, doorwindowsCenter + (600 / 304.8) * doorwindows.FacingOrientation.Normalize().Negate()) as Curve;

                                var intersectionA = roomSolid.IntersectWithCurve(lineA, intersectOptions);
                                var intersectionB = roomSolid.IntersectWithCurve(lineB, intersectOptions);
                                if (intersectionA.SegmentCount > 0 || intersectionB.SegmentCount > 0)
                                {
                                    curtainWallArea += curtainWallPanelsHeight * curtainWallPanelsWidth;
                                }
                            }
                        }
                        else
                        {
                            var panelBoundingBox = panel.get_BoundingBox(null);
                            if (panelBoundingBox == null) continue;
                            var panelCenter = (panelBoundingBox.Max + panelBoundingBox.Min) / 2;
                            var lineA = Line.CreateBound(panelCenter, panelCenter + (600 / 304.8) * panel.FacingOrientation.Normalize()) as Curve;
                            var lineB = Line.CreateBound(panelCenter, panelCenter + (600 / 304.8) * panel.FacingOrientation.Normalize().Negate()) as Curve;

                            var intersectionA = roomSolid.IntersectWithCurve(lineA, intersectOptions);
                            var intersectionB = roomSolid.IntersectWithCurve(lineB, intersectOptions);
                            if (intersectionA.SegmentCount > 0 || intersectionB.SegmentCount > 0)
                            {
                                curtainWallArea += panel.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED).AsDouble();
                            }
                        }
                    }
                }
            }

            return curtainWallArea;
        }

        private Solid GetRoomSolid(Room room)
        {
            GeometryElement geomRoomElement = room.get_Geometry(new Options());
            foreach (GeometryObject geomObj in geomRoomElement)
            {
                var roomSolid = geomObj as Solid;
                if (roomSolid != null) return roomSolid;
            }
            return null;
        }

        private List<Level> GetLevels(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .WhereElementIsNotElementType()
                .OfType<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();
        }

        private void StartProgressBar(int max)
        {
            var newWindowThread = new Thread(ThreadStartingPoint);
            newWindowThread.SetApartmentState(ApartmentState.STA);
            newWindowThread.IsBackground = true;
            newWindowThread.Start();
            Thread.Sleep(100);
            roomFinishNumeratorProgressBarWPF.pb_RoomFinishNumeratorProgressBar.Dispatcher.Invoke(() =>
            {
                roomFinishNumeratorProgressBarWPF.pb_RoomFinishNumeratorProgressBar.Minimum = 0;
                roomFinishNumeratorProgressBarWPF.pb_RoomFinishNumeratorProgressBar.Maximum = max;
            });
        }

        private void UpdateProgressBar(int value)
        {
            roomFinishNumeratorProgressBarWPF.pb_RoomFinishNumeratorProgressBar.Dispatcher.Invoke(() =>
                roomFinishNumeratorProgressBarWPF.pb_RoomFinishNumeratorProgressBar.Value = value);
        }

        private void CloseProgressBar()
        {
            roomFinishNumeratorProgressBarWPF.Dispatcher.Invoke(() => roomFinishNumeratorProgressBarWPF.Close());
        }

        private void StartProgressBarForOpenings(int max)
        {
            var newWindowThread = new Thread(ThreadStartingPointForOpenings);
            newWindowThread.SetApartmentState(ApartmentState.STA);
            newWindowThread.IsBackground = true;
            newWindowThread.Start();
            Thread.Sleep(100);
            roomFinishNumeratorOpeningsProgressBarWPF.pb_RoomFinishNumeratorOpeningsProgressBar.Dispatcher.Invoke(() =>
            {
                roomFinishNumeratorOpeningsProgressBarWPF.pb_RoomFinishNumeratorOpeningsProgressBar.Minimum = 0;
                roomFinishNumeratorOpeningsProgressBarWPF.pb_RoomFinishNumeratorOpeningsProgressBar.Maximum = max;
            });
        }

        private void UpdateProgressBarForOpenings(int value)
        {
            roomFinishNumeratorOpeningsProgressBarWPF.pb_RoomFinishNumeratorOpeningsProgressBar.Dispatcher.Invoke(() =>
                roomFinishNumeratorOpeningsProgressBarWPF.pb_RoomFinishNumeratorOpeningsProgressBar.Value = value);
        }

        private void CloseProgressBarForOpenings()
        {
            roomFinishNumeratorOpeningsProgressBarWPF.Dispatcher.Invoke(() => roomFinishNumeratorOpeningsProgressBarWPF.Close());
        }

        private void ThreadStartingPoint()
        {
            roomFinishNumeratorProgressBarWPF = new RoomFinishNumeratorProgressBarWPF();
            roomFinishNumeratorProgressBarWPF.Show();
            System.Windows.Threading.Dispatcher.Run();
        }

        private void ThreadStartingPointForOpenings()
        {
            roomFinishNumeratorOpeningsProgressBarWPF = new RoomFinishNumeratorOpeningsProgressBarWPF();
            roomFinishNumeratorOpeningsProgressBarWPF.Show();
            System.Windows.Threading.Dispatcher.Run();
        }
        private static async Task GetPluginStartInfo()
        {
            // Получаем сборку, в которой выполняется текущий код
            Assembly thisAssembly = Assembly.GetExecutingAssembly();
            string assemblyName = "RoomFinishNumerator";
            string assemblyNameRus = "Нумератор отделки";
            string assemblyFolderPath = Path.GetDirectoryName(thisAssembly.Location);

            int lastBackslashIndex = assemblyFolderPath.LastIndexOf("\\");
            string dllPath = assemblyFolderPath.Substring(0, lastBackslashIndex + 1) + "PluginInfoCollector\\PluginInfoCollector.dll";

            Assembly assembly = Assembly.LoadFrom(dllPath);
            Type type = assembly.GetType("PluginInfoCollector.InfoCollector");

            if (type != null)
            {
                // Создание экземпляра класса
                object instance = Activator.CreateInstance(type);

                // Получение метода CollectPluginUsageAsync
                var method = type.GetMethod("CollectPluginUsageAsync");

                if (method != null)
                {
                    // Вызов асинхронного метода через reflection
                    Task task = (Task)method.Invoke(instance, new object[] { assemblyName, assemblyNameRus });
                    await task;  // Ожидание завершения асинхронного метода
                }
            }
        }
    }
}
