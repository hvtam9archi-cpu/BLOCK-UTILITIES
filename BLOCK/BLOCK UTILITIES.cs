using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Colors;

// Đăng ký lớp lệnh
[assembly: CommandClass(typeof(AutoCADBlockTools.BlockCommands))]

namespace AutoCADBlockTools
{
    // Class lưu trạng thái để Smart Undo
    public class EntityBackupState
    {
        public ObjectId EntityId { get; set; }
        public string OldLayer { get; set; }
        public Color OldColor { get; set; }
    }

    public class BlockCommands
    {
        private static readonly Random _random = new Random();

        // SỬ DỤNG STACK: Cho phép Undo nhiều lần (LIFO - Vào sau ra trước)
        private static readonly Stack<List<EntityBackupState>> _undoStack = new Stack<List<EntityBackupState>>();
        private static Database _databaseForUndo;

        // ==========================================================================================
        // UTILS: CÁC HÀM HỖ TRỢ CHUNG
        // ==========================================================================================
        private SelectionSet GetSelection(Editor ed, string promptMsg)
        {
            PromptSelectionResult implied = ed.SelectImplied();
            if (implied.Status == PromptStatus.OK && implied.Value.Count > 0)
            {
                ObjectId[] ids = implied.Value.GetObjectIds();
                ObjectIdCollection blockIds = new ObjectIdCollection();

                using (Transaction tr = ed.Document.Database.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in ids)
                    {
                        if (id.ObjectClass.DxfName == "INSERT")
                            blockIds.Add(id);
                    }
                    tr.Commit();
                }

                if (blockIds.Count > 0)
                {
                    ed.SetImpliedSelection(new ObjectId[0]);
                    return SelectionSet.FromObjectIds(blockIds.Cast<ObjectId>().ToArray());
                }
            }

            PromptSelectionOptions pso = new PromptSelectionOptions
            {
                MessageForAdding = promptMsg,
                RejectObjectsOnLockedLayers = true
            };
            SelectionFilter filter = new SelectionFilter(new TypedValue[] { new TypedValue((int)DxfCode.Start, "INSERT") });

            PromptSelectionResult psr = ed.GetSelection(pso, filter);

            if (psr.Status == PromptStatus.OK)
                return psr.Value;

            return null;
        }

        private string GetEffectiveName(BlockReference br, Transaction tr)
        {
            if (br.IsDynamicBlock)
            {
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
                return btr.Name;
            }
            return br.Name;
        }

        private ObjectId GetEffectiveBtrId(BlockReference br)
        {
            if (br.IsDynamicBlock) return br.DynamicBlockTableRecord;
            return br.BlockTableRecord;
        }

        // ==========================================================================================
        // 1. BLC: BLOCK COUNT
        // ==========================================================================================
        [CommandMethod("BLC", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void BlockCountMaster()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            SelectionSet ss = GetSelection(ed, "\nQuét chọn Block để thống kê (Enter để chọn tất cả): ");

            if (ss == null)
            {
                SelectionFilter filter = new SelectionFilter(new TypedValue[] { new TypedValue((int)DxfCode.Start, "INSERT") });
                PromptSelectionResult all = ed.SelectAll(filter);
                if (all.Status == PromptStatus.OK) ss = all.Value;
            }

            if (ss == null || ss.Count == 0)
            {
                ed.WriteMessage("\nKhông tìm thấy Block nào.");
                return;
            }

            Dictionary<string, int> counts = new Dictionary<string, int>();
            Dictionary<string, ObjectId> blockIds = new Dictionary<string, ObjectId>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject so in ss)
                {
                    if (tr.GetObject(so.ObjectId, OpenMode.ForRead) is BlockReference br)
                    {
                        string name = GetEffectiveName(br, tr);
                        if (counts.ContainsKey(name))
                        {
                            counts[name]++;
                        }
                        else
                        {
                            counts[name] = 1;
                            blockIds[name] = GetEffectiveBtrId(br);
                        }
                    }
                }
                tr.Commit();
            }

            var sortedData = counts.OrderBy(k => k.Key).ToList();

            PromptKeywordOptions pko = new PromptKeywordOptions("\nChọn kiểu xuất dữ liệu [Line/Table/File]: ");
            pko.Keywords.Add("Line");
            pko.Keywords.Add("Table");
            pko.Keywords.Add("File");
            pko.Keywords.Default = "Table";

            PromptResult pr = ed.GetKeywords(pko);
            if (pr.Status != PromptStatus.OK) return;

            switch (pr.StringResult)
            {
                case "Line":
                    ExportToLine(ed, sortedData);
                    break;
                case "File":
                    ExportToFile(ed, sortedData);
                    break;
                case "Table":
                    CreateCustomTable100x(doc, sortedData, blockIds);
                    break;
            }
        }

        private void ExportToLine(Editor ed, List<KeyValuePair<string, int>> data)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("\n--- BẢNG THỐNG KÊ BLOCK ---");
            sb.AppendLine(String.Format("{0,-30} {1,10}", "TÊN BLOCK", "SỐ LƯỢNG"));
            sb.AppendLine("-----------------------------------------");
            foreach (var item in data)
            {
                sb.AppendLine(String.Format("{0,-30} {1,10}", item.Key, item.Value));
            }
            sb.AppendLine("-----------------------------------------");
            sb.AppendLine($"TỔNG CỘNG: {data.Sum(x => x.Value)} block.");
            ed.WriteMessage(sb.ToString());
        }

        private void ExportToFile(Editor ed, List<KeyValuePair<string, int>> data)
        {
            PromptSaveFileOptions pso = new PromptSaveFileOptions("Lưu file thống kê")
            {
                Filter = "CSV (Excel) (*.csv)|*.csv|Text File (*.txt)|*.txt",
                DialogCaption = "Lưu file thống kê Block"
            };
            PromptFileNameResult pfnr = ed.GetFileNameForSave(pso);

            if (pfnr.Status != PromptStatus.OK) return;

            try
            {
                using (StreamWriter sw = new StreamWriter(pfnr.StringResult, false, Encoding.UTF8))
                {
                    sw.WriteLine("Tên Block,Số Lượng");
                    foreach (var item in data)
                    {
                        string name = item.Key.Contains(",") ? $"\"{item.Key}\"" : item.Key;
                        sw.WriteLine($"{name},{item.Value}");
                    }
                }
                ed.WriteMessage($"\nĐã xuất file thành công: {pfnr.StringResult}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nLỗi khi ghi file: {ex.Message}");
            }
        }

        private void CreateCustomTable100x(Document doc, List<KeyValuePair<string, int>> data, Dictionary<string, ObjectId> blockIds)
        {
            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptPointOptions ppo = new PromptPointOptions("\nChọn điểm đặt Bảng Thống Kê (Bảng Scale 100x): ");
            PromptPointResult ppr = ed.GetPoint(ppo);
            if (ppr.Status != PromptStatus.OK) return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord curSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                double scaleFactor = 100.0;
                double textH = 2.5 * scaleFactor;
                double rowH = 4.0 * textH;
                double colW1 = 15.0 * textH;
                double colW2 = 20.0 * textH;
                double colW3 = 8.0 * textH;

                Table tb = new Table();
                tb.DisableUndoRecording(true);

                tb.SetDatabaseDefaults();
                tb.Position = ppr.Value;
                tb.SetSize(data.Count + 2, 3);

                tb.SetRowHeight(rowH);
                tb.Columns[0].Width = colW1;
                tb.Columns[1].Width = colW2;
                tb.Columns[2].Width = colW3;

                // --- TITLE ---
                tb.Cells[0, 0].TextString = "BẢNG THỐNG KÊ";
                tb.Cells[0, 0].TextHeight = textH * 1.5;
                tb.Cells[0, 0].Alignment = CellAlignment.MiddleCenter;
                CellRange range = CellRange.Create(tb, 0, 0, 0, 2);
                tb.MergeCells(range);

                // --- HEADER ---
                tb.Rows[1].Height = textH * 2.0;
                tb.Cells[1, 0].TextString = "MINH HỌA";
                tb.Cells[1, 1].TextString = "NỘI DUNG";
                tb.Cells[1, 2].TextString = "SỐ LƯỢNG";

                for (int c = 0; c < 3; c++)
                {
                    tb.Cells[1, c].TextHeight = textH;
                    tb.Cells[1, c].Alignment = CellAlignment.MiddleCenter;
                }

                // --- DATA ---
                int row = 2;
                foreach (var item in data)
                {
                    string blkName = item.Key;
                    int count = item.Value;
                    ObjectId btrId = blockIds[blkName];

                    tb.Rows[row].Height = rowH;

#pragma warning disable CS0618
                    tb.SetBlockTableRecordId(row, 0, btrId, true);
#pragma warning restore CS0618

                    tb.Cells[row, 0].Alignment = CellAlignment.MiddleCenter;

                    tb.Cells[row, 1].TextString = blkName;
                    tb.Cells[row, 1].TextHeight = textH;
                    tb.Cells[row, 1].Alignment = CellAlignment.MiddleCenter;

                    tb.Cells[row, 2].TextString = count.ToString();
                    tb.Cells[row, 2].TextHeight = textH;
                    tb.Cells[row, 2].Alignment = CellAlignment.MiddleCenter;

                    row++;
                }

                tb.GenerateLayout();
                tb.DisableUndoRecording(false);

                curSpace.AppendEntity(tb);
                tr.AddNewlyCreatedDBObject(tb, true);
                tr.Commit();
                ed.Regen();

                ed.WriteMessage($"\nĐã tạo bảng thống kê cho {data.Count} loại Block.");
            }
        }

        // ==========================================================================================
        // 2. RSC, RRT, RAL, RR: BIẾN ĐỔI BLOCK
        // ==========================================================================================
        [CommandMethod("RSC", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void RandomScale() { ApplyRandomTransformation(true, false); }

        [CommandMethod("RRT", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void RandomRotate() { ApplyRandomTransformation(false, true); }

        [CommandMethod("RAL", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void RandomAlign() { ApplyRandomTransformation(true, true); }

        [CommandMethod("RR", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void ResetRotationAndScale()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            SelectionSet ss = GetSelection(ed, "\nChọn các Block để Reset (Góc=0, Scale=1): ");
            if (ss == null || ss.Count == 0) return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                int count = 0;
                foreach (SelectedObject so in ss)
                {
                    if (tr.GetObject(so.ObjectId, OpenMode.ForWrite) is BlockReference br)
                    {
                        br.Rotation = 0.0;
                        br.ScaleFactors = new Scale3d(1.0, 1.0, 1.0);
                        count++;
                    }
                }
                tr.Commit();
                ed.Regen();
                ed.WriteMessage($"\nĐã Reset {count} block về trạng thái mặc định.");
            }
        }

        private void ApplyRandomTransformation(bool doScale, bool doRotate)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            SelectionSet ss = GetSelection(ed, "\nChọn các Block để biến đổi ngẫu nhiên: ");
            if (ss == null || ss.Count == 0) return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                int count = 0;
                foreach (SelectedObject so in ss)
                {
                    if (tr.GetObject(so.ObjectId, OpenMode.ForWrite) is BlockReference br)
                    {
                        if (doScale)
                        {
                            int steps = 9;
                            int randomStep = _random.Next(0, steps + 1);
                            double scaleFactor = 0.75 + (randomStep * 0.05);
                            scaleFactor = Math.Round(scaleFactor, 2);
                            br.ScaleFactors = new Scale3d(scaleFactor, scaleFactor, scaleFactor);
                        }

                        if (doRotate)
                        {
                            int angleStep = _random.Next(0, 36);
                            double angleDeg = angleStep * 10.0;
                            double angleRad = angleDeg * Math.PI / 180.0;
                            br.Rotation = angleRad;
                        }
                        count++;
                    }
                }
                tr.Commit();
                ed.Regen();
                ed.WriteMessage($"\nĐã biến đổi {count} đối tượng.");
            }
        }

        // ==========================================================================================
        // 3. CÁC LỆNH TIỆN ÍCH (DELB, DLB + UDLB, MU, CB...)
        // ==========================================================================================

        [CommandMethod("DELB", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void DeleteBlocks()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            SelectionSet ss = GetSelection(ed, "");

            if (ss != null && ss.Count > 0)
            {
                HashSet<string> blocksToDelete = new HashSet<string>();
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (SelectedObject so in ss)
                    {
                        if (tr.GetObject(so.ObjectId, OpenMode.ForRead) is BlockReference br)
                            blocksToDelete.Add(GetEffectiveName(br, tr));
                    }
                    tr.Commit();
                }
                foreach (string name in blocksToDelete) DeleteBlockByName(db, ed, name);
                return;
            }

            while (true)
            {
                PromptEntityOptions peo = new PromptEntityOptions("\nChọn Block để xóa [Name/Exit] <Exit>: ") { AllowNone = true };
                peo.SetRejectMessage("\nĐối tượng không phải là Block.");
                peo.AddAllowedClass(typeof(BlockReference), true);
                peo.Keywords.Add("Name");
                peo.Keywords.Add("Exit");

                PromptEntityResult per = ed.GetEntity(peo);
                string blockName = "";

                if (per.Status == PromptStatus.Keyword)
                {
                    if (per.StringResult == "Exit") break;
                    if (per.StringResult == "Name")
                    {
                        PromptStringOptions pso = new PromptStringOptions("\nNhập tên Block cần xóa: ") { AllowSpaces = true };
                        PromptResult pr = ed.GetString(pso);
                        if (pr.Status != PromptStatus.OK) break;
                        blockName = pr.StringResult;
                    }
                }
                else if (per.Status == PromptStatus.OK)
                {
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        if (tr.GetObject(per.ObjectId, OpenMode.ForRead) is BlockReference br)
                            blockName = GetEffectiveName(br, tr);
                    }
                }
                else break;

                if (!string.IsNullOrEmpty(blockName)) DeleteBlockByName(db, ed, blockName);
            }
        }

        private void DeleteBlockByName(Database db, Editor ed, string blockName)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                if (!bt.Has(blockName)) { ed.WriteMessage("\nKhông tìm thấy Block: " + blockName); return; }

                ObjectId btrId = bt[blockName];
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                ObjectIdCollection refIds = btr.GetBlockReferenceIds(true, true);

                int count = 0;
                foreach (ObjectId refId in refIds)
                {
                    DBObject obj = tr.GetObject(refId, OpenMode.ForWrite);
                    if (!obj.IsErased) { obj.Erase(); count++; }
                }

                try
                {
                    if (btr.GetBlockReferenceIds(true, true).Count == 0)
                    {
                        btr.UpgradeOpen();
                        btr.Erase();
                        ed.WriteMessage($"\nĐã xóa {count} đối tượng và Purge định nghĩa Block '{blockName}'.");
                    }
                    else
                        ed.WriteMessage($"\nĐã xóa {count} đối tượng Block '{blockName}'.");
                }
                catch { ed.WriteMessage($"\nĐã xóa {count} đối tượng Block '{blockName}'."); }

                tr.Commit();
                ed.Regen();
            }
        }

        [CommandMethod("DLB", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void ChangeBlockToLayer0()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // Kiểm tra DB context để reset stack nếu sang bản vẽ khác
            if (_databaseForUndo != db)
            {
                _undoStack.Clear();
                _databaseForUndo = db;
            }

            SelectionSet ss = GetSelection(ed, "\nChọn các Block cần đổi (Hỗ trợ cả Dynamic Block): ");
            if (ss == null || ss.Count == 0) return;

            // Bắt đầu Transaction
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // Danh sách backup cho lần chạy này
                List<EntityBackupState> currentBatchBackup = new List<EntityBackupState>();

                HashSet<ObjectId> processedBtrs = new HashSet<ObjectId>();
                HashSet<ObjectId> selectedBlockDefinitions = new HashSet<ObjectId>();

                foreach (SelectedObject so in ss)
                {
                    if (tr.GetObject(so.ObjectId, OpenMode.ForRead) is BlockReference br)
                    {
                        selectedBlockDefinitions.Add(br.DynamicBlockTableRecord);
                    }
                }

                foreach (ObjectId btrId in selectedBlockDefinitions)
                {
                    ProcessBlockDefinition(tr, btrId, processedBtrs, currentBatchBackup);
                }

                // Nếu có dữ liệu backup, đẩy vào Stack
                if (currentBatchBackup.Count > 0)
                {
                    _undoStack.Push(currentBatchBackup);
                }

                tr.Commit();
                ed.Regen();
                ed.WriteMessage($"\nĐã cập nhật Layer 0 cho {processedBtrs.Count} loại Block.");
                ed.WriteMessage("\n(Sử dụng lệnh UDLB để hoàn tác lệnh này)");
            }
        }

        // --- LỆNH UDLB: HOÀN TÁC DLB GẦN NHẤT ---
        [CommandMethod("UDLB", CommandFlags.Modal)]
        public void UndoDLB()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            if (_databaseForUndo != db)
            {
                _undoStack.Clear();
                _databaseForUndo = db;
            }

            if (_undoStack.Count == 0)
            {
                ed.WriteMessage("\nKhông có lệnh DLB nào gần nhất để hoàn tác.");
                return;
            }

            // Lấy batch mới nhất ra (LIFO)
            List<EntityBackupState> lastBatch = _undoStack.Pop();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                int count = 0;
                foreach (var state in lastBatch)
                {
                    if (state.EntityId.IsValid && !state.EntityId.IsErased)
                    {
                        try
                        {
                            Entity ent = tr.GetObject(state.EntityId, OpenMode.ForWrite) as Entity;
                            if (ent != null)
                            {
                                ent.Layer = state.OldLayer;
                                ent.Color = state.OldColor;
                                count++;
                            }
                        }
                        catch { }
                    }
                }
                tr.Commit();
                ed.Regen();
                ed.WriteMessage($"\nĐã hoàn tác (UDLB) cho {count} đối tượng trong Block. (Còn lại {_undoStack.Count} bước Undo)");
            }
        }

        private void ProcessBlockDefinition(Transaction tr, ObjectId btrId, HashSet<ObjectId> processed, List<EntityBackupState> currentBatch)
        {
            if (processed.Contains(btrId)) return;
            processed.Add(btrId);

            if (tr.GetObject(btrId, OpenMode.ForRead) is BlockTableRecord btr)
            {
                btr.UpgradeOpen();

                foreach (ObjectId id in btr)
                {
                    if (tr.GetObject(id, OpenMode.ForWrite) is Entity ent)
                    {
                        // 1. BACKUP
                        currentBatch.Add(new EntityBackupState
                        {
                            EntityId = id,
                            OldLayer = ent.Layer,
                            OldColor = ent.Color
                        });

                        // 2. CHANGE
                        ent.Layer = "0";
                        ent.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);

                        if (ent is BlockReference subBr)
                        {
                            ProcessBlockDefinition(tr, subBr.DynamicBlockTableRecord, processed, currentBatch);
                        }
                    }
                }
            }
        }

        [CommandMethod("MU", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void MakeBlockUniqueGroup()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            SelectionSet ss = GetSelection(ed, "\nChọn các Block cần tách riêng (Make Unique): ");
            if (ss == null || ss.Count == 0) return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                Dictionary<string, List<BlockReference>> groups = new Dictionary<string, List<BlockReference>>();

                foreach (SelectedObject so in ss)
                {
                    if (tr.GetObject(so.ObjectId, OpenMode.ForWrite) is BlockReference br)
                    {
                        string effectiveName = GetEffectiveName(br, tr);
                        if (!groups.ContainsKey(effectiveName)) groups[effectiveName] = new List<BlockReference>();
                        groups[effectiveName].Add(br);
                    }
                }

                foreach (var entry in groups)
                {
                    string originalName = entry.Key;
                    List<BlockReference> refsToUpdate = entry.Value;
                    ObjectId originalBtrId = refsToUpdate[0].DynamicBlockTableRecord;
                    int index = 1;
                    string newName;

                    do { newName = $"{originalName}_{index++}"; } while (bt.Has(newName));

                    BlockTableRecord newBtr = new BlockTableRecord { Name = newName };
                    bt.Add(newBtr);
                    tr.AddNewlyCreatedDBObject(newBtr, true);

                    BlockTableRecord originalBtr = (BlockTableRecord)tr.GetObject(originalBtrId, OpenMode.ForRead);
                    newBtr.Origin = originalBtr.Origin;
                    newBtr.Units = originalBtr.Units;
                    newBtr.Explodable = originalBtr.Explodable;

                    ObjectIdCollection idsToCopy = new ObjectIdCollection();
                    foreach (ObjectId id in originalBtr) idsToCopy.Add(id);
                    IdMapping map = new IdMapping();
                    db.DeepCloneObjects(idsToCopy, newBtr.ObjectId, map, false);

                    foreach (BlockReference br in refsToUpdate)
                        br.BlockTableRecord = newBtr.ObjectId;

                    ed.WriteMessage($"\nĐã tách nhóm {refsToUpdate.Count} đối tượng '{originalName}' thành Block mới '{newName}'");
                }
                tr.Commit();
                ed.Regen();
            }
        }

        [CommandMethod("CB", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void CenterBlockBasePoint() { MoveBlockBasePoint(true, true); }

        [CommandMethod("CBP", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void ChangeBasePointOnly() { MoveBlockBasePoint(false, false); }

        [CommandMethod("CBPR", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void ChangeBasePointRetainRef() { MoveBlockBasePoint(false, true); }

        private void MoveBlockBasePoint(bool autoCenter, bool retainRefPosition)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            ObjectId targetId = ObjectId.Null;
            PromptSelectionResult implied = ed.SelectImplied();
            if (implied.Status == PromptStatus.OK && implied.Value.Count > 0)
                targetId = implied.Value.GetObjectIds()[0];

            if (targetId == ObjectId.Null)
            {
                PromptEntityOptions peo = new PromptEntityOptions("\nChọn Block: ");
                peo.SetRejectMessage("\nPhải chọn Block.");
                peo.AddAllowedClass(typeof(BlockReference), true);
                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status == PromptStatus.OK) targetId = per.ObjectId;
            }
            if (targetId == ObjectId.Null) return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                if (tr.GetObject(targetId, OpenMode.ForRead) is BlockReference selectedRef)
                {
                    ObjectId btrId = selectedRef.DynamicBlockTableRecord;
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                    Vector3d displacement;

                    if (autoCenter)
                    {
                        Extents3d? bounds = GetBlockExtents(tr, btr);
                        if (!bounds.HasValue) return;
                        Point3d min = bounds.Value.MinPoint;
                        Point3d max = bounds.Value.MaxPoint;
                        Point3d newBasePoint = new Point3d((min.X + max.X) / 2.0, (min.Y + max.Y) / 2.0, (min.Z + max.Z) / 2.0);
                        displacement = Point3d.Origin.GetVectorTo(newBasePoint).Negate();
                    }
                    else
                    {
                        PromptPointOptions ppo = new PromptPointOptions("\nChọn điểm gốc mới: ") { UseBasePoint = true, BasePoint = selectedRef.Position };
                        PromptPointResult ppr = ed.GetPoint(ppo);
                        if (ppr.Status != PromptStatus.OK) return;
                        Matrix3d matInv = selectedRef.BlockTransform.Inverse();
                        Point3d pointInBlockSpace = ppr.Value.TransformBy(matInv);
                        displacement = pointInBlockSpace.GetVectorTo(Point3d.Origin);
                    }

                    if (displacement.Length < Tolerance.Global.EqualVector) return;

                    btr.UpgradeOpen();
                    Matrix3d transformMatrix = Matrix3d.Displacement(displacement);

                    foreach (ObjectId id in btr)
                        if (tr.GetObject(id, OpenMode.ForWrite) is Entity ent) ent.TransformBy(transformMatrix);

                    ObjectIdCollection refIds = btr.GetBlockReferenceIds(true, true);
                    foreach (ObjectId refId in refIds)
                    {
                        if (tr.GetObject(refId, OpenMode.ForWrite) is BlockReference br)
                        {
                            if (retainRefPosition)
                            {
                                Vector3d adjustment = displacement.Negate().TransformBy(br.BlockTransform);
                                br.Position = br.Position.Add(adjustment);
                                foreach (ObjectId attId in br.AttributeCollection)
                                    if (tr.GetObject(attId, OpenMode.ForWrite) is AttributeReference att && !att.IsConstant)
                                        att.Position = att.Position.Add(adjustment);
                            }
                            else if (br.AttributeCollection.Count > 0)
                            {
                                Vector3d worldDisp = displacement.TransformBy(br.BlockTransform);
                                foreach (ObjectId attId in br.AttributeCollection)
                                    if (tr.GetObject(attId, OpenMode.ForWrite) is AttributeReference att && !att.IsConstant)
                                        att.Position = att.Position.Add(worldDisp);
                            }
                        }
                    }
                    tr.Commit();
                    ed.Regen();
                }
            }
        }

        private Extents3d? GetBlockExtents(Transaction tr, BlockTableRecord btr)
        {
            Extents3d ext = new Extents3d(); bool hasEnts = false;
            foreach (ObjectId id in btr)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is Entity ent && !(ent is AttributeDefinition) && ent.Bounds.HasValue)
                {
                    if (!hasEnts) { ext = ent.Bounds.Value; hasEnts = true; }
                    else { ext.AddExtents(ent.Bounds.Value); }
                }
            }
            return hasEnts ? (Extents3d?)ext : null;
        }

        [CommandMethod("AB", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void AutoBlockCenter()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            SelectionSet ss = null;
            PromptSelectionResult implied = ed.SelectImplied();
            if (implied.Status == PromptStatus.OK && implied.Value.Count > 0)
            {
                ss = implied.Value;
                ed.SetImpliedSelection(new ObjectId[0]);
            }
            if (ss == null)
            {
                PromptSelectionResult psr = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nChọn đối tượng đóng gói thành Block: " });
                if (psr.Status == PromptStatus.OK) ss = psr.Value;
            }
            if (ss == null || ss.Count == 0) return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                BlockTableRecord curSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                Extents3d totalExtents = new Extents3d(); bool first = true;
                ObjectIdCollection idsToBlock = new ObjectIdCollection();

                foreach (SelectedObject so in ss)
                {
                    if (tr.GetObject(so.ObjectId, OpenMode.ForRead) is Entity ent)
                    {
                        idsToBlock.Add(so.ObjectId);
                        if (ent.Bounds.HasValue)
                        {
                            if (first) { totalExtents = ent.Bounds.Value; first = false; }
                            else { totalExtents.AddExtents(ent.Bounds.Value); }
                        }
                    }
                }
                if (first) return;

                Point3d center = new Point3d((totalExtents.MinPoint.X + totalExtents.MaxPoint.X) / 2.0, (totalExtents.MinPoint.Y + totalExtents.MaxPoint.Y) / 2.0, (totalExtents.MinPoint.Z + totalExtents.MaxPoint.Z) / 2.0);
                string blockName = $"@{DateTime.Now.Ticks.ToString().Substring(0, 16)}";
                while (bt.Has(blockName)) blockName = $"@{DateTime.Now.Ticks.ToString().Substring(0, 16)}";

                BlockTableRecord newBtr = new BlockTableRecord { Name = blockName, Origin = center };
                bt.Add(newBtr);
                tr.AddNewlyCreatedDBObject(newBtr, true);

                IdMapping mapping = new IdMapping();
                db.DeepCloneObjects(idsToBlock, newBtr.ObjectId, mapping, false);
                foreach (ObjectId id in idsToBlock) if (tr.GetObject(id, OpenMode.ForWrite) is Entity ent) ent.Erase();

                BlockReference br = new BlockReference(center, newBtr.ObjectId);
                curSpace.AppendEntity(br);
                tr.AddNewlyCreatedDBObject(br, true);
                tr.Commit();
                ed.Regen();
            }
        }
    }
}