using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using WinForms = System.Windows.Forms;
using AutoCADBlockTools.Helpers;

namespace AutoCADBlockTools.Logic
{
	public static class BlockEditLogic
	{
		// --- NEW FEATURE: EB (Explode to Single Layer) ---
		public static void ExplodeToSingleLayer(Document doc)
		{
			Editor ed = doc.Editor;
			// Cho phép chọn cả INSERT (Block) và MINSERT (Array/Block lưới)
			SelectionSet ss = CadUtils.GetSelection(ed, "\nSelect Blocks/Arrays to explode: ", "*");
			if (ss == null || ss.Count == 0) return;

			using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
			using (DocumentLock dl = doc.LockDocument())
			{
				BlockTableRecord currentSpace = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForWrite);
				int count = 0;

				foreach (SelectedObject so in ss)
				{
					Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForWrite) as Entity;
					if (ent != null)
					{
						// Gọi hàm đệ quy
						if (RecursiveExplode(tr, currentSpace, ent))
						{
							count++;
						}
					}
				}
				tr.Commit();
				ed.WriteMessage($"\nExploded {count} root objects.");
			}
		}

		/// <summary>
		/// Đệ quy explode cho đến khi gặp Block thường hoặc Primitive.
		/// </summary>
		private static bool RecursiveExplode(Transaction tr, BlockTableRecord ownerSpace, Entity ent)
		{
			// Điều kiện để Explode:
			// 1. Là MInsertBlock (Array)
			// 2. Là BlockReference nhưng là Anonymous Block (Wrapper) và KHÔNG phải Dynamic Block
			bool shouldExplode = false;

			if (ent is MInsertBlock)
			{
				shouldExplode = true;
			}
			else if (ent is BlockReference br)
			{
				// Dynamic Block cũng có tên *U..., nhưng ta không muốn phá vỡ Dynamic Block trừ khi cần thiết.
				// Ở đây giả định giữ lại Dynamic Block, chỉ phá vỡ các wrapper do Group/Lisp tạo ra.
				if (!br.IsDynamicBlock && br.Name.StartsWith("*"))
				{
					shouldExplode = true;
				}
			}

			if (!shouldExplode)
			{
				return false; // Giữ nguyên đối tượng (Base case)
			}

			// Thực hiện Explode
			DBObjectCollection fragments = new DBObjectCollection();
			try
			{
				ent.Explode(fragments);
			}
			catch
			{
				return false; // Không explode được
			}

			foreach (DBObject obj in fragments)
			{
				Entity newEnt = obj as Entity;
				if (newEnt == null) { obj.Dispose(); continue; }

				// Thêm vào Database trước
				ownerSpace.AppendEntity(newEnt);
				tr.AddNewlyCreatedDBObject(newEnt, true);

				// Kiểm tra xem mảnh vỡ này có cần explode tiếp không (Đệ quy)
				// Ví dụ: Array của Array, hoặc Array của Anonymous Block
				RecursiveExplode(tr, ownerSpace, newEnt);
			}

			// Xóa đối tượng vỏ bọc ban đầu
			if (!ent.IsWriteEnabled) ent.UpgradeOpen();
			ent.Erase();

			return true;
		}

		// --- EXISTING LOGIC: DELB, MU, RB ---

		public static void RenameBlock(Document doc)
		{
			Editor ed = doc.Editor;
			// Chỉ cho phép chọn 1 block
			PromptEntityOptions peo = new PromptEntityOptions("\nSelect block to rename: ");
			peo.SetRejectMessage("\nMust be a block.");
			peo.AddAllowedClass(typeof(BlockReference), true);
			PromptEntityResult per = ed.GetEntity(peo);
			if (per.Status != PromptStatus.OK) return;

			using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
			{
				BlockReference br = (BlockReference)tr.GetObject(per.ObjectId, OpenMode.ForRead);
				string oldName = CadUtils.GetEffectiveName(br, tr);

				using (RenameBlockForm form = new RenameBlockForm(oldName))
				{
					if (Application.ShowModalDialog(form) == WinForms.DialogResult.OK)
					{
						string newName = form.ResultName;
						if (newName == oldName) return;

						BlockTable bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForWrite);
						if (bt.Has(newName))
						{
							ed.WriteMessage($"\nError: Block '{newName}' already exists.");
							return;
						}

						// Rename Definition
						BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForWrite);
						btr.Name = newName;
						ed.WriteMessage($"\nRenamed '{oldName}' to '{newName}'.");
					}
				}
				tr.Commit();
			}
		}

		public static void MakeUnique(Document doc)
		{
			Editor ed = doc.Editor;
			SelectionSet ss = CadUtils.GetSelection(ed, "\nSelect blocks to Make Unique: ");
			if (ss == null || ss.Count == 0) return;

			using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
			{
				BlockTable bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForWrite);
				// Group by effective name
				var groups = new Dictionary<string, List<BlockReference>>();

				foreach (SelectedObject so in ss)
				{
					if (tr.GetObject(so.ObjectId, OpenMode.ForWrite) is BlockReference br)
					{
						string effName = CadUtils.GetEffectiveName(br, tr);
						if (!groups.ContainsKey(effName)) groups[effName] = new List<BlockReference>();
						groups[effName].Add(br);
					}
				}

				foreach (var entry in groups)
				{
					string baseName = entry.Key;
					var blocks = entry.Value;
					ObjectId sourceBtrId = blocks[0].DynamicBlockTableRecord;

					// Generate unique name
					int idx = 1;
					string newName;
					do { newName = $"{baseName}_{idx++}"; } while (bt.Has(newName));

					// Clone Definition
					BlockTableRecord newBtr = new BlockTableRecord { Name = newName };
					bt.Add(newBtr);
					tr.AddNewlyCreatedDBObject(newBtr, true);

					BlockTableRecord sourceBtr = (BlockTableRecord)tr.GetObject(sourceBtrId, OpenMode.ForRead);
					newBtr.Origin = sourceBtr.Origin;
					newBtr.Units = sourceBtr.Units;
					newBtr.Explodable = sourceBtr.Explodable;

					ObjectIdCollection ids = new ObjectIdCollection();
					foreach (ObjectId id in sourceBtr) ids.Add(id);
					IdMapping map = new IdMapping();
					doc.Database.DeepCloneObjects(ids, newBtr.ObjectId, map, false);

					// Re-target references
					foreach (var br in blocks)
					{
						br.BlockTableRecord = newBtr.ObjectId;
					}
					ed.WriteMessage($"\nConverted {blocks.Count} blocks to '{newName}'.");
				}
				tr.Commit();
			}
		}

		public static void DeleteBlocks(Document doc)
		{
			Editor ed = doc.Editor;
			// Thử chọn đối tượng trước
			SelectionSet ss = CadUtils.GetSelection(ed, "");

			if (ss != null && ss.Count > 0)
			{
				// Xóa theo selection
				HashSet<string> names = new HashSet<string>();
				using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
				{
					foreach (SelectedObject so in ss)
					{
						if (tr.GetObject(so.ObjectId, OpenMode.ForRead) is BlockReference br)
							names.Add(CadUtils.GetEffectiveName(br, tr));
					}
					tr.Commit();
				}
				foreach (string name in names) DeleteBlockDefinition(doc, name);
			}
			else
			{
				// Nhập tên hoặc chọn 1 cái mẫu
				PromptEntityOptions peo = new PromptEntityOptions("\nPick block to delete definition: ");
				peo.SetRejectMessage("\nNot a block.");
				peo.AddAllowedClass(typeof(BlockReference), true);
				PromptEntityResult per = ed.GetEntity(peo);
				if (per.Status == PromptStatus.OK)
				{
					string name = "";
					using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
					{
						name = CadUtils.GetEffectiveName((BlockReference)tr.GetObject(per.ObjectId, OpenMode.ForRead), tr);
					}
					DeleteBlockDefinition(doc, name);
				}
			}
		}

		private static void DeleteBlockDefinition(Document doc, string name)
		{
			using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
			{
				BlockTable bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
				if (!bt.Has(name)) return;

				ObjectId btrId = bt[name];
				BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForWrite);

				// Xóa tất cả reference trong bản vẽ
				ObjectIdCollection refIds = btr.GetBlockReferenceIds(true, true);
				foreach (ObjectId rid in refIds)
				{
					DBObject obj = tr.GetObject(rid, OpenMode.ForWrite);
					if (!obj.IsErased) obj.Erase();
				}

				// Xóa definition (Purge)
				if (btr.GetBlockReferenceIds(true, true).Count == 0)
				{
					btr.Erase();
					doc.Editor.WriteMessage($"\nDeleted & Purged: {name}");
				}
				tr.Commit();
			}
		}
	}
}