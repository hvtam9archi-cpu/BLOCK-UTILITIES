using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using WinForms = System.Windows.Forms;
using AutoCADBlockTools.Helpers;

namespace AutoCADBlockTools.Logic
{
	public static class BlockEditLogic
	{
		// --- EB: FLATTEN BLOCK CONTENT (Làm phẳng nội dung Block) ---
		public static void ExplodeToSingleLayer(Document doc)
		{
			Editor ed = doc.Editor;
			SelectionSet ss = CadUtils.GetSelection(ed, "\nSelect Blocks to Flatten (Recursive Explode Content): ", "INSERT");
			if (ss == null || ss.Count == 0) return;

			using (DocumentLock dl = doc.LockDocument())
			using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
			{
				BlockTable bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForWrite);
				int count = 0;

				foreach (SelectedObject so in ss)
				{
					if (tr.GetObject(so.ObjectId, OpenMode.ForWrite) is BlockReference br)
					{
						try
						{
							FlattenBlockReference(tr, bt, br);
							count++;
						}
						catch (System.Exception ex)
						{
							ed.WriteMessage($"\nFailed to flatten block: {ex.Message}");
						}
					}
				}
				tr.Commit();
				ed.Regen();
				ed.WriteMessage($"\nSuccessfully flattened content of {count} blocks (Hatches sent to back).");
			}
		}

		private static void FlattenBlockReference(Transaction tr, BlockTable bt, BlockReference br)
		{
			string newName = "*U_FLAT_" + DateTime.Now.Ticks + "_" + Guid.NewGuid().ToString().Substring(0, 4);
			BlockTableRecord newDef = new BlockTableRecord { Name = newName };
			bt.Add(newDef);
			tr.AddNewlyCreatedDBObject(newDef, true);

			BlockTableRecord oldDef = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
			newDef.Origin = oldDef.Origin;
			newDef.Units = oldDef.Units;
			newDef.Explodable = oldDef.Explodable;

			ObjectIdCollection idsToCopy = new ObjectIdCollection();
			foreach (ObjectId id in oldDef) idsToCopy.Add(id);

			IdMapping map = new IdMapping();
			bt.Database.DeepCloneObjects(idsToCopy, newDef.ObjectId, map, false);

			FlattenDefinitionContent(tr, newDef);

			// [NEW] Đảm bảo Hatch nằm dưới cùng
			CadUtils.SendHatchesToBack(tr, newDef);

			br.BlockTableRecord = newDef.ObjectId;
		}

		private static void FlattenDefinitionContent(Transaction tr, BlockTableRecord btr)
		{
			bool foundNested = true;
			int safetyLoop = 0;

			while (foundNested && safetyLoop < 50)
			{
				foundNested = false;
				List<ObjectId> nestedRefs = new List<ObjectId>();

				foreach (ObjectId id in btr)
				{
					if (id.IsErased) continue;
					DBObject obj = tr.GetObject(id, OpenMode.ForRead);
					if (obj is BlockReference || obj is MInsertBlock)
					{
						nestedRefs.Add(id);
					}
				}

				if (nestedRefs.Count > 0)
				{
					foundNested = true;
					foreach (ObjectId refId in nestedRefs)
					{
						Entity nestedEnt = (Entity)tr.GetObject(refId, OpenMode.ForWrite);
						using (DBObjectCollection fragments = new DBObjectCollection())
						{
							try
							{
								nestedEnt.Explode(fragments);
								nestedEnt.Erase();

								foreach (DBObject obj in fragments)
								{
									Entity ent = obj as Entity;
									if (ent != null)
									{
										btr.AppendEntity(ent);
										tr.AddNewlyCreatedDBObject(ent, true);
									}
									else obj.Dispose();
								}
							}
							catch
							{
								foreach (DBObject obj in fragments) if (!obj.IsDisposed) obj.Dispose();
							}
						}
					}
				}
				safetyLoop++;
			}
		}

		public static void RenameBlock(Document doc)
		{
			Editor ed = doc.Editor;
			PromptEntityOptions peo = new PromptEntityOptions("\nSelect block to rename: ");
			peo.SetRejectMessage("\nMust be a block reference.");
			peo.AddAllowedClass(typeof(BlockReference), true);
			PromptEntityResult per = ed.GetEntity(peo);
			if (per.Status != PromptStatus.OK) return;

			ObjectId targetBtrId = ObjectId.Null;
			string oldName = string.Empty;

			// Step 1: Read (Transaction ngắn)
			using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
			{
				BlockReference br = (BlockReference)tr.GetObject(per.ObjectId, OpenMode.ForRead);
				targetBtrId = br.DynamicBlockTableRecord;
				oldName = CadUtils.GetEffectiveName(br, tr);
				tr.Commit();
			}

			// Step 2: UI (Không Transaction)
			string newName = string.Empty;
			using (RenameBlockForm form = new RenameBlockForm(oldName))
			{
				if (Application.ShowModalDialog(form) != WinForms.DialogResult.OK) return;
				newName = form.ResultName;
				if (newName == oldName) return;
			}

			// Step 3: Write (Transaction mới)
			using (DocumentLock dl = doc.LockDocument())
			using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
			{
				BlockTable bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
				if (bt.Has(newName))
				{
					ed.WriteMessage($"\nError: Name '{newName}' exists.");
					return;
				}

				BlockTableRecord btr = (BlockTableRecord)tr.GetObject(targetBtrId, OpenMode.ForWrite);
				btr.Name = newName;
				tr.Commit();
				ed.WriteMessage($"\nRenamed to '{newName}'.");
			}
		}

		public static void MakeUnique(Document doc)
		{
			Editor ed = doc.Editor;
			SelectionSet ss = CadUtils.GetSelection(ed, "\nSelect blocks to Make Unique: ");
			if (ss == null || ss.Count == 0) return;

			using (DocumentLock dl = doc.LockDocument())
			using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
			{
				BlockTable bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForWrite);
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

					int idx = 1;
					string newName;
					do { newName = $"{baseName}_{idx++}"; } while (bt.Has(newName));

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

					// [NEW] Hatch to back cho block mới tạo
					CadUtils.SendHatchesToBack(tr, newBtr);

					foreach (var br in blocks) br.BlockTableRecord = newBtr.ObjectId;
					ed.WriteMessage($"\nConverted {blocks.Count} blocks to '{newName}'.");
				}
				tr.Commit();
			}
		}

		public static void DeleteBlocks(Document doc)
		{
			// (Giữ nguyên code Delete cũ vì không tạo hình học mới)
			Editor ed = doc.Editor;
			SelectionSet ss = CadUtils.GetSelection(ed, "");

			if (ss != null && ss.Count > 0)
			{
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
			using (DocumentLock dl = doc.LockDocument())
			using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
			{
				BlockTable bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
				if (!bt.Has(name)) return;

				ObjectId btrId = bt[name];
				BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForWrite);

				ObjectIdCollection refIds = btr.GetBlockReferenceIds(true, true);
				foreach (ObjectId rid in refIds)
				{
					DBObject obj = tr.GetObject(rid, OpenMode.ForWrite);
					if (!obj.IsErased) obj.Erase();
				}

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