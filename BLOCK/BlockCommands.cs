using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

// Alias tránh xung đột tên Application và Color giữa các thư viện
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using WinForms = System.Windows.Forms;

[assembly: CommandClass(typeof(AutoCADBlockTools.BlockCommands))]

namespace AutoCADBlockTools
{
    // ==========================================================================================
    // PHẦN 2: LỚP CHÍNH CHỨA TOÀN BỘ CÁC LỆNH (COMMANDS)
    // ==========================================================================================

    public class BlockCommands
    {
        private static readonly Random _random = new Random();
        // Biến lưu cài đặt tĩnh (Global Settings - RSET)
        private static double _minScale = 0.75;
        private static double _maxScale = 1.25;
        private static double _minRotate = 0.0;
        private static double _maxRotate = 360.0;
        // Biến lưu cài đặt tĩnh (Justify Block - JBP)
        private static Justification _lastJustification = Justification.BottomLeft;
        private static bool _retainVisualPosition = true;

        private static readonly Stack<List<EntityBackupState>> _undoStack = new Stack<List<EntityBackupState>>();
        private static Database _databaseForUndo;
        // ==========================================================================================
        // NHÓM 1: BIẾN ĐỔI (RSET, RSC, RRT, RAL, RR)
        // ==========================================================================================

        [CommandMethod("RSET", CommandFlags.Modal)]
        public void RandomSettingsCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            try
            {
                using (var form = new RandomSettingsForm(_minScale, _maxScale, _minRotate, _maxRotate))
                {
                    if (Application.ShowModalDialog(form) == WinForms.DialogResult.OK)
                    {
                        _minScale = form.MinScale;
                        _maxScale = form.MaxScale;
                        if (_minScale > _maxScale)
                        {
                            (_maxScale, _minScale) = (_minScale, _maxScale);
                        }

                        _minRotate = form.MinAngle;
                        _maxRotate = form.MaxAngle;
                        if (_minRotate > _maxRotate)
                        {
                            (_maxRotate, _minRotate) = (_minRotate, _maxRotate);
                        }

                        ed.WriteMessage($"\nĐã cập nhật Global Settings: Scale [{_minScale}-{_maxScale}], Rotate [{_minRotate}-{_maxRotate}]");
                    }
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nLỗi khi mở bảng cài đặt: {ex.Message}");
            }
        }

        [CommandMethod("RSC", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void RandomScale()
        {
            ApplyRandomTransformation(true, false, "\nChọn các Block để Scale ngẫu nhiên");
        }

        [CommandMethod("RRT", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void RandomRotate()
        {
            ApplyRandomTransformation(false, true, "\nChọn các Block để Xoay ngẫu nhiên");
        }

        [CommandMethod("RAL", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void RandomAlign()
        {
            ApplyRandomTransformation(true, true, "\nChọn các Block để Scale & Xoay ngẫu nhiên");
        }

        [CommandMethod("RR", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void ResetRotationAndScale()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            SelectionSet ss = GetSelection(ed, "\nChọn các Block để Reset (Góc=0, Scale=1): ");
            if (ss == null || ss.Count == 0) return;

            ProgressMeter pm = new ProgressMeter();
            pm.Start("Resetting Blocks...");
            pm.SetLimit(ss.Count);
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                int count = 0;
                foreach (SelectedObject so in ss)
                {
                    try
                    {
                        if (tr.GetObject(so.ObjectId, OpenMode.ForWrite) is BlockReference br)
                        {
                            br.Rotation = 0.0;
                            br.ScaleFactors = new Scale3d(1.0, 1.0, 1.0);
                            count++;
                        }
                    }
                    catch { }
                    pm.MeterProgress();
                }
                tr.Commit();
                pm.Stop();
                ed.WriteMessage($"\nĐã Reset {count} block về trạng thái mặc định.");
            }
        }

        private void ApplyRandomTransformation(bool doScale, bool doRotate, string promptMessage)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            SelectionSet ss = null;

            // 1. PickFirst logic
            PromptSelectionResult implied = ed.SelectImplied();
            if (implied.Status == PromptStatus.OK && implied.Value.Count > 0)
            {
                ObjectId[] ids = implied.Value.GetObjectIds();
                ObjectIdCollection blockIds = new ObjectIdCollection();
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in ids) if (id.ObjectClass.DxfName == "INSERT") blockIds.Add(id);
                    tr.Commit();
                }
                if (blockIds.Count > 0)
                {
                    ed.SetImpliedSelection(new ObjectId[0]);
                    ss = SelectionSet.FromObjectIds(blockIds.Cast<ObjectId>().ToArray());
                }
            }

            // 2. Interactive Selection
            if (ss == null)
            {
                string info = "";
                if (doScale && doRotate) info = $"(S={_minScale}-{_maxScale}, R={_minRotate}-{_maxRotate})";
                else if (doScale) info = $"(Scale={_minScale}-{_maxScale})";
                else if (doRotate) info = $"(Rot={_minRotate}-{_maxRotate})";
                PromptSelectionOptions pso = new PromptSelectionOptions
                {
                    MessageForAdding = $"{promptMessage} {info}: ",
                    RejectObjectsOnLockedLayers = true
                };
                SelectionFilter filter = new SelectionFilter(new TypedValue[] { new TypedValue((int)DxfCode.Start, "INSERT") });
                PromptSelectionResult psr = ed.GetSelection(pso, filter);
                if (psr.Status == PromptStatus.OK)
                {
                    ss = psr.Value;
                }
                else
                {
                    return; // Cancel
                }
            }

            if (ss == null || ss.Count == 0) return;
            // 3. Thực hiện biến đổi
            ProgressMeter pm = new ProgressMeter();
            pm.Start("Processing Blocks...");
            pm.SetLimit(ss.Count);
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                int count = 0;
                double scaleRange = _maxScale - _minScale;
                double rotRange = _maxRotate - _minRotate;
                foreach (SelectedObject so in ss)
                {
                    try
                    {
                        if (tr.GetObject(so.ObjectId, OpenMode.ForWrite) is BlockReference br)
                        {
                            if (doScale)
                            {
                                double scaleFactor = _minScale + (_random.NextDouble() * scaleRange);
                                scaleFactor = Math.Round(scaleFactor, 3);
                                br.ScaleFactors = new Scale3d(scaleFactor, scaleFactor, scaleFactor);
                            }

                            if (doRotate)
                            {
                                double angleDeg = _minRotate + (_random.NextDouble() * rotRange);
                                br.Rotation = angleDeg * Math.PI / 180.0;
                            }
                            count++;
                        }
                    }
                    catch { }
                    pm.MeterProgress();
                }
                tr.Commit();
                pm.Stop();
                ed.WriteMessage($"\nĐã biến đổi thành công {count} đối tượng.");
            }
        }

        // ==========================================================================================
        // 2. NHÓM LỆNH TIỆN ÍCH QUẢN LÝ (DELB, DLB, MU...)
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
                        if (id.ObjectClass.DxfName == "INSERT") blockIds.Add(id);
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
            if (psr.Status == PromptStatus.OK) return psr.Value;
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

        [CommandMethod("DELB", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void DeleteBlocks()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            SelectionSet ss = GetSelection(ed, "");

            // Xóa theo Selection
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

            // Xóa theo Tên (Dialog/String)
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

            if (_databaseForUndo != db)
            {
                _undoStack.Clear(); _databaseForUndo = db;
            }

            SelectionSet ss = GetSelection(ed, "\nChọn các Block cần đổi (Hỗ trợ cả Dynamic Block): ");
            if (ss == null || ss.Count == 0) return;

            ProgressMeter pm = new ProgressMeter();
            pm.Start("Processing Layers...");
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                List<EntityBackupState> currentBatchBackup = new List<EntityBackupState>();
                HashSet<ObjectId> processedBtrs = new HashSet<ObjectId>();
                HashSet<ObjectId> selectedBlockDefinitions = new HashSet<ObjectId>();
                // Tối ưu: Lấy danh sách Definition duy nhất trước khi xử lý
                foreach (SelectedObject so in ss)
                {
                    if (tr.GetObject(so.ObjectId, OpenMode.ForRead) is BlockReference br)
                        selectedBlockDefinitions.Add(br.DynamicBlockTableRecord);
                }

                pm.SetLimit(selectedBlockDefinitions.Count);
                foreach (ObjectId btrId in selectedBlockDefinitions)
                {
                    ProcessBlockDefinition(tr, btrId, processedBtrs, currentBatchBackup);
                    pm.MeterProgress();
                }

                if (currentBatchBackup.Count > 0) _undoStack.Push(currentBatchBackup);
                tr.Commit();
                pm.Stop();
                ed.Regen();
                ed.WriteMessage($"\nĐã cập nhật Layer 0 cho {processedBtrs.Count} loại Block.");
            }
        }

        [CommandMethod("UDLB", CommandFlags.Modal)]
        public void UndoDLB()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            if (_databaseForUndo != db)
            {
                _undoStack.Clear(); _databaseForUndo = db;
            }
            if (_undoStack.Count == 0) { ed.WriteMessage("\nKhông có lệnh DLB nào gần nhất để hoàn tác."); return; }

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
                            if (ent != null) { ent.Layer = state.OldLayer; ent.Color = state.OldColor; count++; }
                        }
                        catch { }
                    }
                }
                tr.Commit();
                ed.Regen();
                ed.WriteMessage($"\nĐã hoàn tác (UDLB) cho {count} đối tượng.");
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
                        currentBatch.Add(new EntityBackupState { EntityId = id, OldLayer = ent.Layer, OldColor = ent.Color });
                        ent.Layer = "0";
                        ent.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(ColorMethod.ByLayer, 256);

                        // Đệ quy
                        if (ent is BlockReference subBr)
                            ProcessBlockDefinition(tr, subBr.DynamicBlockTableRecord, processed, currentBatch);
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
                    foreach (BlockReference br in refsToUpdate) br.BlockTableRecord = newBtr.ObjectId;
                    ed.WriteMessage($"\nĐã tách nhóm {refsToUpdate.Count} đối tượng thành '{newName}'");
                }
                tr.Commit();
            }
        }

        // ==========================================================================================
        // 3. NHÓM LỆNH BASE POINT & CENTER (CB, CBP, AB, JBP) - ĐÃ SỬA LỖI ATTRIBUTES & GRIP
        // ==========================================================================================

        [CommandMethod("CB", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void CenterBlockBasePoint()
        {
            MoveBlockBasePoint(true, true);
        }
        [CommandMethod("CBP", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void ChangeBasePointOnly() { MoveBlockBasePoint(false, false); }
        [CommandMethod("CBPR", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void ChangeBasePointRetainRef()
        {
            MoveBlockBasePoint(false, true);
        }

        private void MoveBlockBasePoint(bool autoCenter, bool retainRefPosition)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            ObjectId targetId = ObjectId.Null;
            PromptSelectionResult implied = ed.SelectImplied();
            if (implied.Status == PromptStatus.OK && implied.Value.Count > 0) targetId = implied.Value.GetObjectIds()[0];
            if (targetId == ObjectId.Null)
            {
                PromptEntityOptions peo = new PromptEntityOptions("\nChọn Block: ");
                peo.SetRejectMessage("\nPhải chọn Block.");
                peo.AddAllowedClass(typeof(BlockReference), true);
                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status == PromptStatus.OK) targetId = per.ObjectId;
            }
            if (targetId == ObjectId.Null) return;
            // Sử dụng DocumentLock và Transaction
            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                if (tr.GetObject(targetId, OpenMode.ForRead) is BlockReference selectedRef)
                {
                    ObjectId btrId = selectedRef.DynamicBlockTableRecord;
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                    Vector3d displacement;

                    if (autoCenter)
                    {
                        // Dùng chung logic tính BoundingBox như JBP để loại bỏ Attribute
                        Extents3d?
                        bounds = GetBlockBoundingBoxForJbp(tr, btr);
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
                    // 1. Dời tất cả đối tượng trong Block Definition
                    foreach (ObjectId id in btr) if (tr.GetObject(id, OpenMode.ForWrite) is Entity ent) ent.TransformBy(transformMatrix);
                    // 2. Cập nhật vị trí của các BlockReference để bù trừ (nếu cần)
                    ObjectIdCollection refIds = btr.GetBlockReferenceIds(true, true);
                    foreach (ObjectId refId in refIds)
                    {
                        if (tr.GetObject(refId, OpenMode.ForWrite) is BlockReference br)
                        {
                            if (retainRefPosition)
                            {
                                Vector3d adjustment = displacement.Negate().TransformBy(br.BlockTransform);
                                br.Position = br.Position.Add(adjustment);
                                br.RecordGraphicsModified(true);
                                // Cập nhật Grip
                            }
                        }
                    }
                    tr.Commit();

                    // 3. Chạy ATTSYNC để sửa lỗi vị trí Attribute và cập nhật Grip Point
                    try
                    {
                        using (Transaction trSync = doc.TransactionManager.StartTransaction())
                        {
                            BlockTableRecord btrSync = (BlockTableRecord)trSync.GetObject(btrId, OpenMode.ForRead);
                            if (btrSync.HasAttributeDefinitions)
                            {
                                ed.Command("_.ATTSYNC", "_N", btrSync.Name);
                            }
                        }
                    }
                    catch { }

                    ed.Regen();
                }
            }
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
                Extents3d totalExtents = new Extents3d();
                bool first = true;
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
            }
        }

        // ==========================================================================================
        // 4. LỆNH MỚI: JUSTIFY BLOCK (JBP) - ĐÃ TÍCH HỢP VÀO HỆ THỐNG
        // ==========================================================================================

        [CommandMethod("JBP", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void JustifyBasePointCmd()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // 1. Chọn Block (Sử dụng hàm GetSelection chung để hỗ trợ PickFirst)
            SelectionSet ss = GetSelection(ed, "\nChọn các Block cần Justify: ");
            if (ss == null || ss.Count == 0) return;

            // 2. Lấy danh sách tên Block (Dùng HashSet để tối ưu tốc độ, tránh xử lý trùng)
            HashSet<string> blockNames = new HashSet<string>();
            using (Transaction tr = doc.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject so in ss)
                {
                    if (tr.GetObject(so.ObjectId, OpenMode.ForRead) is BlockReference br)
                    {
                        blockNames.Add(GetEffectiveName(br, tr));
                    }
                }
                tr.Commit();
            }

            if (blockNames.Count == 0) return;

            // 3. Hiển thị Dialog Cài đặt (JbpForm)
            using (JbpForm form = new JbpForm(_lastJustification, _retainVisualPosition))
            {
                if (Application.ShowModalDialog(form) != WinForms.DialogResult.OK)
                {
                    ed.WriteMessage("\n*Cancel*");
                    return;
                }

                _lastJustification = form.SelectedJustification;
                _retainVisualPosition = form.RetainVisualPosition;
            }

            // 4. Thực hiện thay đổi
            // Dùng DocumentLock vì có gọi Command (ATTSYNC)
            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = doc.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                HashSet<ObjectId> blocksToSync = new HashSet<ObjectId>();

                ProgressMeter pm = new ProgressMeter();
                pm.Start("Modifying Blocks...");
                pm.SetLimit(blockNames.Count);

                foreach (string blkName in blockNames)
                {
                    if (!bt.Has(blkName)) continue;
                    ObjectId btrId = bt[blkName];
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);

                    Extents3d? bounds = GetBlockBoundingBoxForJbp(tr, btr);
                    if (bounds.HasValue)
                    {
                        Point3d newBasePt = CalculatePoint(bounds.Value, _lastJustification);
                        Vector3d displacement = Point3d.Origin - newBasePt;

                        if (displacement.Length > 1e-8)
                        {
                            btr.UpgradeOpen();
                            // Dời hình học trong Definition
                            foreach (ObjectId entId in btr)
                            {
                                if (tr.GetObject(entId, OpenMode.ForWrite) is Entity ent)
                                {
                                    ent.TransformBy(Matrix3d.Displacement(displacement));
                                }
                            }

                            // Cập nhật References
                            ObjectIdCollection refIds = btr.GetBlockReferenceIds(true, true);
                            foreach (ObjectId refId in refIds)
                            {
                                if (tr.GetObject(refId, OpenMode.ForWrite) is BlockReference blkRef)
                                {
                                    if (_retainVisualPosition)
                                    {
                                        Vector3d vecInBlockSpace = displacement;
                                        Matrix3d blockMat = blkRef.BlockTransform;
                                        Vector3d vecInWorld = vecInBlockSpace.TransformBy(blockMat);
                                        blkRef.Position = blkRef.Position.Add(vecInWorld.Negate());
                                        blkRef.RecordGraphicsModified(true); // Cập nhật Grip ngay
                                    }
                                }
                            }

                            if (btr.HasAttributeDefinitions) blocksToSync.Add(btrId);
                        }
                    }
                    pm.MeterProgress();
                }
                tr.Commit();
                pm.Stop();

                // 5. Sync Attribute (Chạy riêng để an toàn)
                foreach (ObjectId btrId in blocksToSync)
                {
                    try
                    {
                        using (Transaction trSync = doc.TransactionManager.StartTransaction())
                        {
                            BlockTableRecord btr = (BlockTableRecord)trSync.GetObject(btrId, OpenMode.ForRead);
                            ed.Command("_.ATTSYNC", "_N", btr.Name);
                        }
                    }
                    catch { }
                }
                ed.Regen();
                ed.WriteMessage($"\nĐã cập nhật Base Point cho {blockNames.Count} loại Block.");
            }
        }

        private Point3d CalculatePoint(Extents3d ext, Justification jus)
        {
            Point3d min = ext.MinPoint;
            Point3d max = ext.MaxPoint;
            double midX = (min.X + max.X) / 2.0;
            double midY = (min.Y + max.Y) / 2.0;
            switch (jus)
            {
                case Justification.TopLeft: return new Point3d(min.X, max.Y, 0);
                case Justification.TopCenter: return new Point3d(midX, max.Y, 0);
                case Justification.TopRight: return new Point3d(max.X, max.Y, 0);
                case Justification.MiddleLeft: return new Point3d(min.X, midY, 0);
                case Justification.MiddleCenter: return new Point3d(midX, midY, 0);
                case Justification.MiddleRight: return new Point3d(max.X, midY, 0);
                case Justification.BottomLeft: return new Point3d(min.X, min.Y, 0);
                case Justification.BottomCenter: return new Point3d(midX, min.Y, 0);
                case Justification.BottomRight: return new Point3d(max.X, min.Y, 0);
                default: return Point3d.Origin;
            }
        }

        // Logic lấy Bounding Box riêng của JBP/CB (giữ nguyên logic gốc lọc Text/Invisible)
        private Extents3d? GetBlockBoundingBoxForJbp(Transaction tr, BlockTableRecord btr)
        {
            Extents3d?
            totalExtents = null;
            foreach (ObjectId id in btr)
            {
                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                // Logic: Bỏ qua AttDef, Text, MText và đối tượng ẩn để lấy tâm hình học thực sự
                if (ent == null || ent is AttributeDefinition || ent is DBText || ent is MText || !ent.Visible) continue;
                try
                {
                    if (ent.Bounds.HasValue)
                    {
                        if (totalExtents == null) totalExtents = ent.Bounds.Value;
                        else
                        {
                            Extents3d tmp = totalExtents.Value;
                            tmp.AddExtents(ent.Bounds.Value);
                            totalExtents = tmp;
                        }
                    }
                }
                catch { }
            }
            return totalExtents;
        }
    
    // ... (Các lệnh cũ giữ nguyên) ...

        // ==========================================================================================
        // 5. LỆNH MỚI: RENAME BLOCK (RB)
        // ==========================================================================================

        [CommandMethod("RB", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void RenameBlockCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // 1. Chọn Block (Hỗ trợ PickFirst)
            ObjectId targetId = ObjectId.Null;
            PromptSelectionResult implied = ed.SelectImplied();
            
            // Logic chọn: Chỉ cho phép chọn 1 block để đổi tên
            if (implied.Status == PromptStatus.OK && implied.Value.Count == 1)
            {
                targetId = implied.Value.GetObjectIds()[0];
            }
            else
            {
                // Nếu chưa chọn hoặc chọn nhiều, yêu cầu chọn lại 1 cái
                ed.SetImpliedSelection(new ObjectId[0]); // Clear cũ
                PromptEntityOptions peo = new PromptEntityOptions("\nChọn 1 Block để đổi tên: ");
                peo.SetRejectMessage("\nĐối tượng phải là Block.");
                peo.AddAllowedClass(typeof(BlockReference), true);
                
                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status == PromptStatus.OK) targetId = per.ObjectId;
            }

            if (targetId == ObjectId.Null) return;

            // 2. Xử lý logic đổi tên
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    BlockReference br = tr.GetObject(targetId, OpenMode.ForRead) as BlockReference;
                    if (br == null) return;

                    // Lấy tên thật (xử lý Dynamic Block)
                    string currentName = GetEffectiveName(br, tr);
                    
                    // Mở Form
                    using (RenameBlockForm form = new RenameBlockForm(currentName))
                    {
                        if (Application.ShowModalDialog(form) != WinForms.DialogResult.OK) return;

                        string newName = form.ResultName;

                        // Kiểm tra nếu tên không đổi
                        if (newName.Equals(currentName, StringComparison.OrdinalIgnoreCase)) return;

                        // Kiểm tra tên trùng
                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                        if (bt.Has(newName))
                        {
                            ed.WriteMessage($"\nLỗi: Tên Block '{newName}' đã tồn tại trong bản vẽ!");
                            return;
                        }

                        // Thực hiện đổi tên
                        // Lưu ý: Cần lấy ObjectId của BlockTableRecord gốc (Definition)
                        ObjectId btrId = br.DynamicBlockTableRecord; 
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForWrite);
                        
                        btr.Name = newName;
                        
                        ed.WriteMessage($"\nĐã đổi tên Block từ '{currentName}' thành '{newName}'.");
                    }
                    tr.Commit();
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\nLỗi khi đổi tên: {ex.Message}");
                }
            }
        }
     }
}