using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AutoCADBlockTools.UI;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutoCADBlockTools
{
    public static class BlockLogic
    {
        private static readonly Random _random = new();
        
        // Settings
        public static double MinScale = 0.75;
        public static double MaxScale = 1.25;
        public static double MinRotate = 0.0;
        public static double MaxRotate = 360.0;
        public static Justification LastJustification = Justification.BottomLeft;
        public static bool RetainVisualPosition = true;

        // Undo Stack for DLB
        private static readonly Stack<List<EntityBackupState>> _undoStack = new();
        private static Database _databaseForUndo;

        // =====================================================================
        // Helper: Lọc Block từ implied selection KHÔNG cần Transaction
        // ObjectClass.DxfName là metadata sẵn có trên ObjectId, không truy cập DB.
        // =====================================================================
        private static ObjectId[] FilterBlockIdsNoTransaction(ObjectId[] ids)
        {
            List<ObjectId> blockIds = new(ids.Length);
            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i].ObjectClass.DxfName == "INSERT")
                    blockIds.Add(ids[i]);
            }
            return blockIds.Count > 0 ? [.. blockIds] : null;
        }

        // Helper: Get Selection — không mở Transaction dư thừa
        private static SelectionSet GetSelection(Editor ed, string promptMsg)
        {
            PromptSelectionResult implied = ed.SelectImplied();
            if (implied.Status == PromptStatus.OK && implied.Value.Count > 0)
            {
                ObjectId[] filtered = FilterBlockIdsNoTransaction(implied.Value.GetObjectIds());
                if (filtered != null)
                {
                    ed.SetImpliedSelection([]);
                    return SelectionSet.FromObjectIds(filtered);
                }
            }

            PromptSelectionOptions pso = new()
			{
                MessageForAdding = promptMsg,
                RejectObjectsOnLockedLayers = true
            };
            SelectionFilter filter = new([new((int)DxfCode.Start, "INSERT")]);

            PromptSelectionResult psr = ed.GetSelection(pso, filter);
            if (psr.Status == PromptStatus.OK) return psr.Value;
            return null;
        }

        // Helper: Get Effective Name for Dynamic Blocks
        private static string GetEffectiveName(BlockReference br, Transaction tr)
        {
            if (br.IsDynamicBlock)
            {
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
                return btr.Name;
            }
            return br.Name;
        }

        // Helper: Get all block reference IDs, including anonymous block references for dynamic blocks
        private static List<ObjectId> GetBlockReferenceIdsAll(BlockTableRecord btr, Transaction tr)
        {
            ObjectIdCollection directIds = btr.GetBlockReferenceIds(true, true);
            // Fast path: không phải Dynamic Block → trả về trực tiếp, tránh tạo List mới
            if (!btr.IsDynamicBlock)
            {
                List<ObjectId> result = [.. directIds.Cast<ObjectId>()];
                return result;
            }

            ObjectIdCollection anonBtrIds = btr.GetAnonymousBlockIds();
            List<ObjectId> ids = new(directIds.Count + anonBtrIds.Count * 4);
            foreach (ObjectId id in directIds) ids.Add(id);

            foreach (ObjectId anonBtrId in anonBtrIds)
            {
                if (tr.GetObject(anonBtrId, OpenMode.ForRead) is BlockTableRecord anonBtr)
                {
                    foreach (ObjectId id in anonBtr.GetBlockReferenceIds(true, true))
                        ids.Add(id);
                }
            }
            return ids;
        }

        // ==========================================================================================
        // 1. BIẾN ĐỔI (RSC, RRT, RAL, RR)
        // ==========================================================================================
        public static void ApplyRandomTransformation(bool doScale, bool doRotate, string promptMessage)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            SelectionSet ss = null;

            // PickFirst logic — không cần Transaction, DxfName là metadata
            PromptSelectionResult implied = ed.SelectImplied();
            if (implied.Status == PromptStatus.OK && implied.Value.Count > 0)
            {
                ObjectId[] filtered = FilterBlockIdsNoTransaction(implied.Value.GetObjectIds());
                if (filtered != null)
                {
                    ed.SetImpliedSelection([]);
                    ss = SelectionSet.FromObjectIds(filtered);
                }
            }

            // Interactive Selection
            if (ss == null)
            {
                string info = "";
                if (doScale && doRotate) info = $"(S={MinScale}-{MaxScale}, R={MinRotate}-{MaxRotate})";
                else if (doScale) info = $"(Scale={MinScale}-{MaxScale})";
                else if (doRotate) info = $"(Rot={MinRotate}-{MaxRotate})";
                
                PromptSelectionOptions pso = new()
				{
                    MessageForAdding = $"{promptMessage} {info}: ",
                    RejectObjectsOnLockedLayers = true
                };
                SelectionFilter filter = new([new((int)DxfCode.Start, "INSERT")]);
                PromptSelectionResult psr = ed.GetSelection(pso, filter);
                if (psr.Status == PromptStatus.OK) ss = psr.Value;
                else return; // Cancel
            }

            if (ss == null || ss.Count == 0) return;

			using DocumentLock docLock = doc.LockDocument();
			using Transaction tr = db.TransactionManager.StartTransaction();
			int count = 0;
			double scaleRange = MaxScale - MinScale;
			double rotRange = MaxRotate - MinRotate;
			foreach (SelectedObject so in ss)
			{
				try
				{
					if (tr.GetObject(so.ObjectId, OpenMode.ForWrite) is BlockReference br)
					{
						if (doScale)
						{
							double scaleFactor = MinScale + (_random.NextDouble() * scaleRange);
							scaleFactor = Math.Round(scaleFactor, 3);
							br.ScaleFactors = new Scale3d(scaleFactor, scaleFactor, scaleFactor);
						}
						if (doRotate)
						{
							double angleDeg = MinRotate + (_random.NextDouble() * rotRange);
							br.Rotation = angleDeg * Math.PI / 180.0;
						}
						count++;
					}
				}
				catch (System.Exception ex)
				{
					ed.WriteMessage($"\nWarning (Transform): {ex.Message}");
				}
			}
			tr.Commit();
			ed.WriteMessage($"\nĐã biến đổi thành công {count} đối tượng.");
		}

        public static void ResetRotationAndScale()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            SelectionSet ss = GetSelection(ed, "\nChọn các Block để Reset (Góc=0, Scale=1): ");
            if (ss == null || ss.Count == 0) return;

			using DocumentLock docLock = doc.LockDocument();
			using Transaction tr = db.TransactionManager.StartTransaction();
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
				catch (System.Exception ex)
				{
					ed.WriteMessage($"\nWarning (Reset): {ex.Message}");
				}
			}
			tr.Commit();
			ed.WriteMessage($"\nĐã Reset {count} block về trạng thái mặc định.");
		}

        // ==========================================================================================
        // 2. LỆNH QUẢN LÝ (DELB, DLB, UDLB, MU)
        // ==========================================================================================
        public static void DeleteBlocks()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            SelectionSet ss = GetSelection(ed, "");

            // === Mode 1: Xóa theo Selection (1 transaction = 1 Ctrl+Z) ===
            if (ss != null && ss.Count > 0)
            {
                UndoHelper.Begin(doc);
                HashSet<string> blocksToDelete = [];
				using DocumentLock docLock = doc.LockDocument();
				using Transaction tr = db.TransactionManager.StartTransaction();
				foreach (SelectedObject so in ss)
				{
					if (tr.GetObject(so.ObjectId, OpenMode.ForRead) is BlockReference br)
						blocksToDelete.Add(GetEffectiveName(br, tr));
				}
				foreach (string name in blocksToDelete)
					DeleteBlockInTransaction(tr, db, ed, name);
				tr.Commit();
				return;
            }

            // === Mode 2: Xóa tương tác — thu thập tên trước, xóa 1 lần sau ===
            HashSet<string> collectedNames = [];
            while (true)
            {
                PromptEntityOptions peo = new("\nChọn Block để xóa [Name/Exit] <Exit>: ") { AllowNone = true };
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
                        PromptStringOptions pso = new("\nNhập tên Block cần xóa: ") { AllowSpaces = true };
                        PromptResult pr = ed.GetString(pso);
                        if (pr.Status != PromptStatus.OK) break;
                        blockName = pr.StringResult;
                    }
                }
                else if (per.Status == PromptStatus.OK)
                {
					// Lấy tên block — chỉ cần đọc, transaction ngắn nhất có thể
					using Transaction tr = db.TransactionManager.StartTransaction();
					if (tr.GetObject(per.ObjectId, OpenMode.ForRead) is BlockReference br)
						blockName = GetEffectiveName(br, tr);
					tr.Commit();
				}
                else break;

                if (!string.IsNullOrEmpty(blockName))
                {
                    collectedNames.Add(blockName);
                    ed.WriteMessage($"\n→ Đã thêm '{blockName}' vào danh sách xóa.");
                }
            }

            // Xóa tất cả trong 1 transaction = 1 Ctrl+Z
            if (collectedNames.Count > 0)
            {
                using (DocumentLock docLock = doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (string name in collectedNames)
                        DeleteBlockInTransaction(tr, db, ed, name);
                    tr.Commit();
                }
                // Chỉ cần FlushGraphics, KHÔNG gọi Regen()
                db.TransactionManager.QueueForGraphicsFlush();
            }
        }

        /// <summary>
        /// Xóa tất cả references + purge block definition trong Transaction đã mở.
        /// KHÔNG tự tạo Transaction → đảm bảo 1 Ctrl+Z cho toàn bộ lệnh.
        /// </summary>
        private static void DeleteBlockInTransaction(Transaction tr, Database db, Editor ed, string blockName)
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
                // Nếu đã xóa hết tất cả references thì purge luôn definition
                // Không gọi lại GetBlockReferenceIds lần 2 — chỉ cần so sánh count
                if (count == refIds.Count)
                {
                    btr.UpgradeOpen();
                    btr.Erase();
                    ed.WriteMessage($"\nĐã xóa {count} đối tượng và Purge định nghĩa Block '{blockName}'.");
                }
                else
                    ed.WriteMessage($"\nĐã xóa {count} đối tượng Block '{blockName}'.");
            }
            catch { ed.WriteMessage($"\nĐã xóa {count} đối tượng Block '{blockName}'."); }
        }


        public static void ChangeBlockToLayer0()
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

			using DocumentLock docLock = doc.LockDocument();
			using Transaction tr = db.TransactionManager.StartTransaction();
			List<EntityBackupState> currentBatchBackup = [];
			HashSet<ObjectId> processedBtrs = [];
			HashSet<ObjectId> selectedBlockDefinitions = [];

			foreach (SelectedObject so in ss)
			{
				if (tr.GetObject(so.ObjectId, OpenMode.ForRead) is BlockReference br)
					selectedBlockDefinitions.Add(br.DynamicBlockTableRecord);
			}

			foreach (ObjectId btrId in selectedBlockDefinitions)
			{
				ProcessBlockDefinition(tr, btrId, processedBtrs, currentBatchBackup);
			}

			if (currentBatchBackup.Count > 0) _undoStack.Push(currentBatchBackup);
			tr.Commit();
			// QueueForGraphicsFlush thay cho Regen — nhẹ hơn rất nhiều
			db.TransactionManager.QueueForGraphicsFlush();
			ed.WriteMessage($"\nĐã cập nhật Layer 0 cho {processedBtrs.Count} loại Block.");
		}

        private static void ProcessBlockDefinition(Transaction tr, ObjectId btrId, HashSet<ObjectId> processed, List<EntityBackupState> currentBatch)
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

                        if (ent is BlockReference subBr)
                            ProcessBlockDefinition(tr, subBr.DynamicBlockTableRecord, processed, currentBatch);
                    }
                }
            }
        }

        public static void UndoDLB()
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
			using DocumentLock docLock = doc.LockDocument();
			using Transaction tr = db.TransactionManager.StartTransaction();
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
					catch (System.Exception ex)
					{
						ed.WriteMessage($"\nWarning (UndoDLB): {ex.Message}");
					}
				}
			}
			tr.Commit();
			// QueueForGraphicsFlush thay cho Regen
			db.TransactionManager.QueueForGraphicsFlush();
			ed.WriteMessage($"\nĐã hoàn tác (UDLB) cho {count} đối tượng.");
		}

        public static void MakeBlockUniqueGroup()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            SelectionSet ss = GetSelection(ed, "\nChọn các Block cần tách riêng (Make Unique): ");
            if (ss == null || ss.Count == 0) return;

			using DocumentLock docLock = doc.LockDocument();
			using Transaction tr = db.TransactionManager.StartTransaction();
			BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
			Dictionary<string, List<BlockReference>> groups = [];

			foreach (SelectedObject so in ss)
			{
				if (tr.GetObject(so.ObjectId, OpenMode.ForWrite) is BlockReference br)
				{
					string effectiveName = GetEffectiveName(br, tr);
					if (!groups.ContainsKey(effectiveName)) groups[effectiveName] = [];
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
				do { newName = $"{originalName}_{index++}"; } while (bt.Has(newName) && index < 1000);

				BlockTableRecord newBtr = new() { Name = newName };
				bt.Add(newBtr);
				tr.AddNewlyCreatedDBObject(newBtr, true);
				BlockTableRecord originalBtr = (BlockTableRecord)tr.GetObject(originalBtrId, OpenMode.ForRead);
				newBtr.Origin = originalBtr.Origin;
				newBtr.Units = originalBtr.Units;
				newBtr.Explodable = originalBtr.Explodable;

				ObjectIdCollection idsToCopy = [.. originalBtr];
				IdMapping map = [];
				db.DeepCloneObjects(idsToCopy, newBtr.ObjectId, map, false);
				foreach (BlockReference br in refsToUpdate) br.BlockTableRecord = newBtr.ObjectId;
				ed.WriteMessage($"\nĐã tách nhóm {refsToUpdate.Count} đối tượng thành '{newName}'");
			}
			tr.Commit();
		}

        // ==========================================================================================
        // 3. BASE POINT (CB, CBP, AB, JBP)
        // ==========================================================================================
        public static void MoveBlockBasePoint(bool autoCenter, bool retainRefPosition)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            ObjectId targetId = ObjectId.Null;
            bool wasPreSelected = false;
            PromptSelectionResult implied = ed.SelectImplied();
            if (implied.Status == PromptStatus.OK && implied.Value.Count > 0)
            {
                targetId = implied.Value.GetObjectIds()[0];
                wasPreSelected = true;
                ed.SetImpliedSelection([]); // Clear to allow updating grips correctly
            }
            if (targetId == ObjectId.Null)
            {
                PromptEntityOptions peo = new("\nChọn Block: ");
                peo.SetRejectMessage("\nPhải chọn Block.");
                peo.AddAllowedClass(typeof(BlockReference), true);
                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status == PromptStatus.OK) targetId = per.ObjectId;
            }
            if (targetId == ObjectId.Null) return;

            UndoHelper.Begin(doc);
            
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
                        Extents3d? bounds = GetBlockBoundingBoxForJbp(tr, btr);
                        if (!bounds.HasValue) return;
                        Point3d min = bounds.Value.MinPoint;
                        Point3d max = bounds.Value.MaxPoint;
                        Point3d newBasePoint = new((min.X + max.X) / 2.0, (min.Y + max.Y) / 2.0, (min.Z + max.Z) / 2.0);
                        displacement = btr.Origin - newBasePoint;
                    }
                    else
                    {
                        PromptPointOptions ppo = new("\nChọn điểm gốc mới: ") { UseBasePoint = true, BasePoint = selectedRef.Position };
                        PromptPointResult ppr = ed.GetPoint(ppo);
                        if (ppr.Status != PromptStatus.OK) return;
                        Matrix3d matInv = selectedRef.BlockTransform.Inverse();
                        Point3d pointInBlockSpace = ppr.Value.TransformBy(matInv);
                        displacement = btr.Origin - pointInBlockSpace;
                    }

                    if (displacement.Length < Tolerance.Global.EqualVector) return;

                    btr.UpgradeOpen();
                    Matrix3d transformMatrix = Matrix3d.Displacement(displacement);
                    
                    foreach (ObjectId id in btr) if (tr.GetObject(id, OpenMode.ForWrite) is Entity ent) ent.TransformBy(transformMatrix);
                    
                    List<ObjectId> refIds = GetBlockReferenceIdsAll(btr, tr);
                    foreach (ObjectId refId in refIds)
                    {
                        if (tr.GetObject(refId, OpenMode.ForWrite) is BlockReference br)
                        {
                            if (retainRefPosition)
                            {
                                Vector3d adjustment = displacement.Negate().TransformBy(br.BlockTransform);
                                br.Position = br.Position.Add(adjustment);
                            }
                            br.RecordGraphicsModified(true);
                        }
                    }
                    // Đồng bộ Attribute trong CÙNG transaction → Ctrl+Z undo toàn bộ 1 lần
                    // Truyền refIds đã có thay vì gọi lại GetBlockReferenceIds bên trong
                    if (btr.HasAttributeDefinitions)
                        SyncAttributePositions(tr, btr, refIds);

                    tr.Commit();
                    db.TransactionManager.QueueForGraphicsFlush();
                }
            }

            // UpdateScreen nhẹ hơn Regen, chỉ cần 1 trong 2
            ed.UpdateScreen();

            if (wasPreSelected && targetId != ObjectId.Null && !targetId.IsErased)
            {
                ed.SetImpliedSelection([targetId]);
            }

            UndoHelper.End(doc);
        }

        public static void AutoBlockCenter()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            SelectionSet ss = null;
            PromptSelectionResult implied = ed.SelectImplied();
            if (implied.Status == PromptStatus.OK && implied.Value.Count > 0)
            {
                ss = implied.Value;
                ed.SetImpliedSelection([]);
            }
            if (ss == null)
            {
                PromptSelectionResult psr = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nChọn đối tượng đóng gói thành Block: " });
                if (psr.Status == PromptStatus.OK) ss = psr.Value;
            }
            if (ss == null || ss.Count == 0) return;

			using DocumentLock docLock = doc.LockDocument();
			using Transaction tr = db.TransactionManager.StartTransaction();
			BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
			BlockTableRecord curSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
			Extents3d totalExtents = new();
			bool first = true;
			ObjectIdCollection idsToBlock = [];

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

			Point3d center = new((totalExtents.MinPoint.X + totalExtents.MaxPoint.X) / 2.0, (totalExtents.MinPoint.Y + totalExtents.MaxPoint.Y) / 2.0, (totalExtents.MinPoint.Z + totalExtents.MaxPoint.Z) / 2.0);
			string blockName;
			int nameAttempt = 0;
			do
			{
				blockName = $"@{DateTime.Now.Ticks + nameAttempt}";
				nameAttempt++;
			} while (bt.Has(blockName) && nameAttempt < 1000);

			BlockTableRecord newBtr = new() { Name = blockName, Origin = center };
			bt.Add(newBtr);
			tr.AddNewlyCreatedDBObject(newBtr, true);

			IdMapping mapping = [];
			db.DeepCloneObjects(idsToBlock, newBtr.ObjectId, mapping, false);
			foreach (ObjectId id in idsToBlock) if (tr.GetObject(id, OpenMode.ForWrite) is Entity ent) ent.Erase();

			BlockReference br = new(center, newBtr.ObjectId);
			curSpace.AppendEntity(br);
			tr.AddNewlyCreatedDBObject(br, true);
			tr.Commit();
		}

        public static void JustifyBasePointCmd()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            SelectionSet ss = GetSelection(ed, "\nChọn các Block cần Justify: ");
            if (ss == null || ss.Count == 0) return;

            ObjectId[] selectedIds = ss.GetObjectIds();

            // Thu thập blockName → representative BlockReference (để lấy BlockTransform)
            Dictionary<string, ObjectId> blockNameToRefId = [];
            using (Transaction tr = doc.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject so in ss)
                {
                    if (tr.GetObject(so.ObjectId, OpenMode.ForRead) is BlockReference br)
                    {
                        string name = GetEffectiveName(br, tr);
                        if (!blockNameToRefId.ContainsKey(name))
                            blockNameToRefId[name] = so.ObjectId;
                    }
                }
                tr.Commit();
            }

            if (blockNameToRefId.Count == 0) return;

            UndoHelper.Begin(doc);

            var window = new JbpWindow(LastJustification, RetainVisualPosition);
            if (Application.ShowModalWindow(window) != true)
            {
                ed.WriteMessage("\n*Cancel*");
                return;
            }

            LastJustification = window.SelectedJustification;
            RetainVisualPosition = window.RetainVisualPosition;

            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = doc.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

                // Lấy ma trận chuyển đổi WCS → UCS hiện tại
                // Khi UCS bị xoay, "Top/Bottom/Left/Right" trên màn hình khác với WCS
                Matrix3d ucsMatrix = ed.CurrentUserCoordinateSystem;
                Matrix3d wcsToUcs = ucsMatrix.Inverse();

                foreach (var kvp in blockNameToRefId)
                {
                    string blkName = kvp.Key;
                    ObjectId representativeRefId = kvp.Value;

                    if (!bt.Has(blkName)) continue;
                    ObjectId btrId = bt[blkName];
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);

                    Extents3d? bounds = GetBlockBoundingBoxForJbp(tr, btr);
                    if (!bounds.HasValue) continue;

                    // Lấy BlockTransform từ reference đại diện để chuyển BDS → WCS
                    BlockReference representativeRef = tr.GetObject(representativeRefId, OpenMode.ForRead) as BlockReference;
                    if (representativeRef == null) continue;
                    Matrix3d blockTransform = representativeRef.BlockTransform;

                    // ═══ CORE FIX: Tính justification point đúng với UCS hiện tại ═══
                    // 
                    // Vấn đề gốc: GetBlockBoundingBoxForJbp trả về bounds trong Block Definition Space (BDS).
                    // Khi block bị xoay (rotation ≠ 0), "Top" trong BDS ≠ "Top" trên màn hình.
                    // 
                    // Giải pháp (theo pattern WCS→DCS của TPL PlotLogic):
                    // 1. Chuyển 4 góc BDS → WCS qua BlockTransform
                    // 2. Chuyển WCS → UCS để xác định đúng hướng Top/Bottom/Left/Right theo view
                    // 3. Tính justification point trong UCS
                    // 4. Chuyển UCS → WCS → BDS ngược lại
                    
                    Point3d bdsMin = bounds.Value.MinPoint;
                    Point3d bdsMax = bounds.Value.MaxPoint;

                    // 4 góc trong BDS
                    Point3d[] bdsCorners =
					[
						new(bdsMin.X, bdsMin.Y, 0), // BL
                        new(bdsMax.X, bdsMin.Y, 0), // BR
                        new(bdsMax.X, bdsMax.Y, 0), // TR
                        new(bdsMin.X, bdsMax.Y, 0), // TL
                    ];

                    // BDS → WCS → UCS
                    double ucsMinX = double.MaxValue, ucsMinY = double.MaxValue;
                    double ucsMaxX = double.MinValue, ucsMaxY = double.MinValue;
                    for (int i = 0; i < bdsCorners.Length; i++)
                    {
                        Point3d wcsPoint = bdsCorners[i].TransformBy(blockTransform);
                        Point3d ucsPoint = wcsPoint.TransformBy(wcsToUcs);
                        if (ucsPoint.X < ucsMinX) ucsMinX = ucsPoint.X;
                        if (ucsPoint.Y < ucsMinY) ucsMinY = ucsPoint.Y;
                        if (ucsPoint.X > ucsMaxX) ucsMaxX = ucsPoint.X;
                        if (ucsPoint.Y > ucsMaxY) ucsMaxY = ucsPoint.Y;
                    }

                    // Tính justification point trong UCS space
                    Extents3d ucsBounds = new(
                        new Point3d(ucsMinX, ucsMinY, 0),
                        new Point3d(ucsMaxX, ucsMaxY, 0));
                    Point3d justifyPointUcs = CalculatePoint(ucsBounds, LastJustification);

                    // Chuyển UCS → WCS → BDS
                    Point3d justifyPointWcs = justifyPointUcs.TransformBy(ucsMatrix);
                    Point3d justifyPointBds = justifyPointWcs.TransformBy(blockTransform.Inverse());

                    Vector3d displacement = btr.Origin - justifyPointBds;

                    if (displacement.Length > 1e-8)
                    {
                        btr.UpgradeOpen();
                        foreach (ObjectId entId in btr)
                        {
                            if (tr.GetObject(entId, OpenMode.ForWrite) is Entity ent)
                            {
                                ent.TransformBy(Matrix3d.Displacement(displacement));
                            }
                        }

                        List<ObjectId> refIds = GetBlockReferenceIdsAll(btr, tr);
                        foreach (ObjectId refId in refIds)
                        {
                            if (tr.GetObject(refId, OpenMode.ForWrite) is BlockReference blkRef)
                            {
                                if (RetainVisualPosition)
                                {
                                    Vector3d vecInBlockSpace = displacement;
                                    Matrix3d blockMat = blkRef.BlockTransform;
                                    Vector3d vecInWorld = vecInBlockSpace.TransformBy(blockMat);
                                    blkRef.Position = blkRef.Position.Add(vecInWorld.Negate());
                                }
                                blkRef.RecordGraphicsModified(true);
                            }
                        }

                        // Đồng bộ Attribute — truyền refIds đã thu thập
                        if (btr.HasAttributeDefinitions)
                            SyncAttributePositions(tr, btr, refIds);
                    }
                }
                tr.Commit();
                db.TransactionManager.QueueForGraphicsFlush();
            }

            // Chỉ cần UpdateScreen — KHÔNG gọi Regen()
            ed.UpdateScreen();
            ed.WriteMessage($"\nĐã cập nhật Base Point cho {blockNameToRefId.Count} loại Block.");

            // Re-select SAU KHI graphics đã refresh → grip points mới hiển thị đúng
            if (selectedIds != null && selectedIds.Length > 0)
            {
                var validIds = selectedIds.Where(id => id.IsValid && !id.IsErased).ToArray();
                if (validIds.Length > 0)
                    ed.SetImpliedSelection(validIds);
            }

            UndoHelper.End(doc);
        }

        private static Point3d CalculatePoint(Extents3d ext, Justification jus)
        {
            Point3d min = ext.MinPoint;
            Point3d max = ext.MaxPoint;
            double midX = (min.X + max.X) / 2.0;
            double midY = (min.Y + max.Y) / 2.0;
			return jus switch
			{
				Justification.TopLeft => new Point3d(min.X, max.Y, 0),
				Justification.TopCenter => new Point3d(midX, max.Y, 0),
				Justification.TopRight => new Point3d(max.X, max.Y, 0),
				Justification.MiddleLeft => new Point3d(min.X, midY, 0),
				Justification.MiddleCenter => new Point3d(midX, midY, 0),
				Justification.MiddleRight => new Point3d(max.X, midY, 0),
				Justification.BottomLeft => new Point3d(min.X, min.Y, 0),
				Justification.BottomCenter => new Point3d(midX, min.Y, 0),
				Justification.BottomRight => new Point3d(max.X, min.Y, 0),
				_ => Point3d.Origin,
			};
		}

        /// <summary>
        /// Đồng bộ vị trí/style AttributeReference trên tất cả BlockReference sau khi thay đổi BlockTableRecord.
        /// Thay thế ed.Command("_.ATTSYNC") để giữ mọi thay đổi trong cùng 1 Transaction → 1 lần Ctrl+Z.
        /// Overload nhận sẵn danh sách refIds để tránh gọi lại GetBlockReferenceIds.
        /// </summary>
        private static void SyncAttributePositions(Transaction tr, BlockTableRecord btr, List<ObjectId> refIds = null)
        {
            // Thu thập tất cả AttributeDefinition (non-constant) từ block definition
            var attDefs = new Dictionary<string, AttributeDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (ObjectId id in btr)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is AttributeDefinition attDef && !attDef.Constant)
                    attDefs[attDef.Tag] = attDef;
            }
            if (attDefs.Count == 0) return;

            // Sử dụng refIds truyền vào nếu có, nếu không thì mới gọi GetBlockReferenceIds
            IEnumerable<ObjectId> referenceIds;
            if (refIds != null)
                referenceIds = refIds;
            else
                referenceIds = btr.GetBlockReferenceIds(true, true).Cast<ObjectId>();

            // Cập nhật từng BlockReference
            foreach (ObjectId refId in referenceIds)
            {
                if (tr.GetObject(refId, OpenMode.ForWrite) is not BlockReference blockRef) continue;

                // Map tag → ObjectId của AttributeReference hiện có
                var existingAtts = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
                foreach (ObjectId attId in blockRef.AttributeCollection)
                {
                    if (tr.GetObject(attId, OpenMode.ForRead) is AttributeReference attRef)
                        existingAtts[attRef.Tag] = attId;
                }

                // Cập nhật hoặc thêm mới AttributeReference
                foreach (var kvp in attDefs)
                {
                    if (existingAtts.TryGetValue(kvp.Key, out ObjectId existingId))
                    {
                        // Cập nhật vị trí/style từ definition, giữ nguyên giá trị text
                        if (tr.GetObject(existingId, OpenMode.ForWrite) is AttributeReference attRef)
                        {
                            string savedText = attRef.TextString;
                            attRef.SetAttributeFromBlock(kvp.Value, blockRef.BlockTransform);
                            attRef.TextString = savedText;
                            attRef.AdjustAlignment(blockRef.Database);
                        }
                    }
                    else
                    {
                        // Thêm attribute mới nếu definition có mà reference thiếu
                        AttributeReference newAttRef = new();
                        newAttRef.SetAttributeFromBlock(kvp.Value, blockRef.BlockTransform);
                        newAttRef.TextString = kvp.Value.TextString;
                        blockRef.AttributeCollection.AppendAttribute(newAttRef);
                        tr.AddNewlyCreatedDBObject(newAttRef, true);
                    }
                }

                // Xóa attribute không còn definition tương ứng
                foreach (var kvp in existingAtts)
                {
                    if (!attDefs.ContainsKey(kvp.Key))
                    {
                        if (tr.GetObject(kvp.Value, OpenMode.ForWrite) is Entity entToErase)
                            entToErase.Erase();
                    }
                }
            }
        }

        private static Extents3d? GetBlockBoundingBoxForJbp(Transaction tr, BlockTableRecord btr)
        {
            Extents3d? totalExtents = null;
            foreach (ObjectId id in btr)
            {
                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
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
                catch (System.Exception) { /* Entity không có Bounds — bỏ qua */ }
            }
            return totalExtents;
        }

        // ==========================================================================================
        // 4. RENAME BLOCK (RB)
        // ==========================================================================================
        public static void RenameBlockCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            ObjectId targetId = ObjectId.Null;
            PromptSelectionResult implied = ed.SelectImplied();
            
            if (implied.Status == PromptStatus.OK && implied.Value.Count == 1)
            {
                targetId = implied.Value.GetObjectIds()[0];
            }
            else
            {
                ed.SetImpliedSelection([]);
                PromptEntityOptions peo = new("\nChọn 1 Block để đổi tên: ");
                peo.SetRejectMessage("\nĐối tượng phải là Block.");
                peo.AddAllowedClass(typeof(BlockReference), true);
                
                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status == PromptStatus.OK) targetId = per.ObjectId;
            }

            if (targetId == ObjectId.Null) return;

			using DocumentLock docLock = doc.LockDocument();
			using Transaction tr = db.TransactionManager.StartTransaction();
			try
			{
				BlockReference br = tr.GetObject(targetId, OpenMode.ForRead) as BlockReference;
				if (br == null) return;

				string currentName = GetEffectiveName(br, tr);

				var window = new RenameBlockWindow(currentName);
				if (Application.ShowModalWindow(window) != true) return;

				string newName = window.ResultName;

				if (newName.Equals(currentName, StringComparison.OrdinalIgnoreCase)) return;

				BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
				if (bt.Has(newName))
				{
					ed.WriteMessage($"\nLỗi: Tên Block '{newName}' đã tồn tại trong bản vẽ!");
					return;
				}

				ObjectId btrId = br.DynamicBlockTableRecord;
				BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForWrite);

				btr.Name = newName;

				ed.WriteMessage($"\nĐã đổi tên Block từ '{currentName}' thành '{newName}'.");
				tr.Commit();
			}
			catch (System.Exception ex)
			{
				ed.WriteMessage($"\nLỗi khi đổi tên: {ex.Message}");
			}
		}
    }
}
