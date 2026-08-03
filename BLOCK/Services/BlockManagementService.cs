using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutoCADBlockTools.Services
{
	/// <summary>
	/// Service xử lý các lệnh quản lý Block (DELB, DLB, UDLB, MU).
	/// </summary>
	public static class BlockManagementService
	{
		// Undo Stack cho DLB — scoped theo database
		private static readonly Stack<List<EntityBackupState>> _undoStack = new();
		private static Database _databaseForUndo;

		#region DELB - Delete Blocks

		public static void DeleteBlocks()
		{
			var doc = Application.DocumentManager.MdiActiveDocument;
			var db = doc.Database;
			var ed = doc.Editor;

			var ss = SelectionHelper.GetSelection(ed, "");

			// Mode 1: Xóa theo Selection (1 transaction = 1 Ctrl+Z)
			if (ss != null && ss.Count > 0)
			{
				var blocksToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				using var docLock = doc.LockDocument();
				using var tr = db.TransactionManager.StartTransaction();
				foreach (SelectedObject so in ss)
				{
					if (tr.GetObject(so.ObjectId, OpenMode.ForRead) is BlockReference br)
						blocksToDelete.Add(BlockHelper.GetEffectiveName(br, tr));
				}
				foreach (string name in blocksToDelete)
					DeleteBlockInTransaction(tr, db, name);
				tr.Commit();
				return;
			}

			// Mode 2: Xóa tương tác — thu thập tên, xóa 1 lần
			var collectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			while (true)
			{
				var peo = new PromptEntityOptions("\nChọn Block để xóa [Name/Exit] <Exit>: ") { AllowNone = true };
				peo.SetRejectMessage("\nĐối tượng không phải là Block.");
				peo.AddAllowedClass(typeof(BlockReference), true);
				peo.Keywords.Add("Name");
				peo.Keywords.Add("Exit");

				var per = ed.GetEntity(peo);
				string blockName = "";

				if (per.Status == PromptStatus.Keyword)
				{
					if (per.StringResult == "Exit") break;
					if (per.StringResult == "Name")
					{
						var pso = new PromptStringOptions("\nNhập tên Block cần xóa: ") { AllowSpaces = true };
						var pr = ed.GetString(pso);
						if (pr.Status != PromptStatus.OK) break;
						blockName = pr.StringResult;
					}
				}
				else if (per.Status == PromptStatus.OK)
				{
					using var tr = db.TransactionManager.StartTransaction();
					if (tr.GetObject(per.ObjectId, OpenMode.ForRead) is BlockReference br)
						blockName = BlockHelper.GetEffectiveName(br, tr);
					tr.Commit();
				}
				else break;

				if (!string.IsNullOrEmpty(blockName))
				{
					collectedNames.Add(blockName);
					Logger.Info($"→ Đã thêm '{blockName}' vào danh sách xóa.");
				}
			}

			if (collectedNames.Count > 0)
			{
				using (var docLock = doc.LockDocument())
				using (var tr = db.TransactionManager.StartTransaction())
				{
					foreach (string name in collectedNames)
						DeleteBlockInTransaction(tr, db, name);
					tr.Commit();
				}
				db.TransactionManager.QueueForGraphicsFlush();
			}
		}

		private static void DeleteBlockInTransaction(
			Transaction tr,
			Database db,
			string blockName)
		{
			var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
			if (!bt.Has(blockName))
			{
				Logger.Warning($"Không tìm thấy Block: {blockName}");
				return;
			}

			var btrId = bt[blockName];
			var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
			var refIds = BlockHelper.GetBlockReferenceIdsAll(btr, tr);

			int count = 0;
			foreach (ObjectId refId in refIds)
			{
				if (!refId.IsValid || refId.IsErased) continue;
				var obj = tr.GetObject(refId, OpenMode.ForWrite);
				if (!obj.IsErased) { obj.Erase(); count++; }
			}

			var purgeIds = new ObjectIdCollection([btrId]);
			if (btr.IsDynamicBlock)
			{
				foreach (ObjectId anonymousBtrId in btr.GetAnonymousBlockIds())
					purgeIds.Add(anonymousBtrId);
			}

			db.Purge(purgeIds);
			bool definitionPurged = false;
			foreach (ObjectId purgeId in purgeIds)
			{
				if (!purgeId.IsValid || purgeId.IsErased) continue;
				if (tr.GetObject(purgeId, OpenMode.ForWrite) is BlockTableRecord purgeableBtr)
				{
					purgeableBtr.Erase();
					if (purgeId == btrId) definitionPurged = true;
				}
			}

			if (definitionPurged)
			{
				Logger.Info($"Đã xóa {count} đối tượng và Purge định nghĩa Block '{blockName}'.");
			}
			else
			{
				Logger.Info($"Đã xóa {count} đối tượng Block '{blockName}'; định nghĩa vẫn còn phụ thuộc nên chưa thể Purge.");
			}
		}

		#endregion

		#region DLB / UDLB - Change Block to Layer 0

		public static void ChangeBlockToLayer0()
		{
			var doc = Application.DocumentManager.MdiActiveDocument;
			var db = doc.Database;
			var ed = doc.Editor;

			if (_databaseForUndo != db)
			{
				_undoStack.Clear();
				_databaseForUndo = db;
			}

			var ss = SelectionHelper.GetSelection(ed, "\nChọn các Block cần đổi (Hỗ trợ cả Dynamic Block): ");
			if (ss == null || ss.Count == 0) return;

			using var docLock = doc.LockDocument();
			using var tr = db.TransactionManager.StartTransaction();
			var currentBatchBackup = new List<EntityBackupState>();
			var processedBtrs = new HashSet<ObjectId>();
			var selectedBlockDefinitions = new HashSet<ObjectId>();

			foreach (SelectedObject so in ss)
			{
				if (tr.GetObject(so.ObjectId, OpenMode.ForRead) is BlockReference br)
					selectedBlockDefinitions.Add(BlockHelper.GetEffectiveDefinitionId(br));
			}

			foreach (ObjectId btrId in selectedBlockDefinitions)
			{
				ProcessBlockDefinition(tr, btrId, processedBtrs, currentBatchBackup);
			}

			tr.Commit();
			if (currentBatchBackup.Count > 0) _undoStack.Push(currentBatchBackup);
			db.TransactionManager.QueueForGraphicsFlush();
			Logger.Info($"Đã cập nhật Layer 0 cho {processedBtrs.Count} loại Block.");
		}

		private static void ProcessBlockDefinition(Transaction tr, ObjectId btrId,
			HashSet<ObjectId> processed, List<EntityBackupState> currentBatch)
		{
			if (!processed.Add(btrId)) return;

			if (tr.GetObject(btrId, OpenMode.ForRead) is BlockTableRecord btr)
			{
				foreach (ObjectId id in btr)
				{
					if (tr.GetObject(id, OpenMode.ForRead) is Entity ent)
					{
						if (ent is BlockReference subBr)
							ProcessBlockDefinition(tr, BlockHelper.GetEffectiveDefinitionId(subBr), processed, currentBatch);

						if (ent.Layer == "0" && ent.ColorIndex == 256) continue;

						currentBatch.Add(new EntityBackupState
						{
							EntityId = id,
							OldLayer = ent.Layer,
							OldColor = ent.Color
						});
						ent.UpgradeOpen();
						ent.Layer = "0";
						ent.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
					}
				}
			}
		}

		public static void UndoDLB()
		{
			var doc = Application.DocumentManager.MdiActiveDocument;
			var db = doc.Database;

			if (_databaseForUndo != db)
			{
				_undoStack.Clear();
				_databaseForUndo = db;
			}

			if (_undoStack.Count == 0)
			{
				Logger.Warning("Không có lệnh DLB nào gần nhất để hoàn tác.");
				return;
			}

			var lastBatch = _undoStack.Peek();
			using var docLock = doc.LockDocument();
			using var tr = db.TransactionManager.StartTransaction();
			int count = 0;
			foreach (var state in lastBatch)
			{
				if (state.EntityId.IsValid && !state.EntityId.IsErased)
				{
					try
					{
						if (tr.GetObject(state.EntityId, OpenMode.ForWrite) is Entity ent)
						{
							ent.Layer = state.OldLayer;
							ent.Color = state.OldColor;
							count++;
						}
					}
					catch (Exception ex)
					{
						Logger.Warning($"UndoDLB: {ex.Message}");
					}
				}
			}
			tr.Commit();
			_undoStack.Pop();
			db.TransactionManager.QueueForGraphicsFlush();
			Logger.Info($"Đã hoàn tác (UDLB) cho {count} đối tượng.");
		}

		#endregion

		#region MU - Make Unique

		public static void MakeBlockUniqueGroup()
		{
			var doc = Application.DocumentManager.MdiActiveDocument;
			var db = doc.Database;
			var ed = doc.Editor;

			var ss = SelectionHelper.GetSelection(ed, "\nChọn các Block cần tách riêng (Make Unique): ");
			if (ss == null || ss.Count == 0) return;

			using var docLock = doc.LockDocument();
			using var tr = db.TransactionManager.StartTransaction();
			var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
			var groups = new Dictionary<ObjectId, List<BlockReference>>();

			foreach (SelectedObject so in ss)
			{
				if (tr.GetObject(so.ObjectId, OpenMode.ForRead) is BlockReference br)
				{
					ObjectId definitionId = BlockHelper.GetEffectiveDefinitionId(br);
					if (!groups.TryGetValue(definitionId, out var references))
					{
						references = [];
						groups[definitionId] = references;
					}
					references.Add(br);
				}
			}

			foreach (var entry in groups)
			{
				ObjectId originalBtrId = entry.Key;
				var refsToUpdate = entry.Value;
				var originalBtr = (BlockTableRecord)tr.GetObject(originalBtrId, OpenMode.ForRead);
				string originalName = originalBtr.Name;

				// Dùng Guid thay DateTime.Ticks để tạo tên duy nhất
				string newName;
				do
				{
					newName = $"{originalName}_{BlockHelper.GenerateUniqueName("")}";
				} while (bt.Has(newName));

				var newBtr = BlockHelper.CloneBlockDefinition(tr, db, bt, newName, originalBtrId);

				foreach (BlockReference br in refsToUpdate)
				{
					br.UpgradeOpen();
					br.BlockTableRecord = newBtr.ObjectId;
				}

				Logger.Info($"Đã tách nhóm {refsToUpdate.Count} đối tượng thành '{newName}'");
			}
			tr.Commit();
		}

		#endregion
	}
}
