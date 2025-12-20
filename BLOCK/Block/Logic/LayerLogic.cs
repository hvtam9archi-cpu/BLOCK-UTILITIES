using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.EditorInput;
using AutoCADBlockTools.Helpers;

namespace AutoCADBlockTools.Logic
{
	public static class LayerLogic
	{
		private static readonly Stack<List<EntityBackupState>> _undoStack = new Stack<List<EntityBackupState>>();
		private static Database _dbForUndo;

		public static void ToLayer0(Document doc)
		{
			if (_dbForUndo != doc.Database) { _undoStack.Clear(); _dbForUndo = doc.Database; }

			Editor ed = doc.Editor;
			SelectionSet ss = CadUtils.GetSelection(ed, "\nSelect blocks to fix layer: ");
			if (ss == null || ss.Count == 0) return;

			using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
			{
				HashSet<ObjectId> defsToProcess = new HashSet<ObjectId>();
				foreach (SelectedObject so in ss)
				{
					if (tr.GetObject(so.ObjectId, OpenMode.ForRead) is BlockReference br)
						defsToProcess.Add(br.DynamicBlockTableRecord);
				}

				List<EntityBackupState> backup = new List<EntityBackupState>();
				HashSet<ObjectId> processed = new HashSet<ObjectId>();

				foreach (ObjectId btrId in defsToProcess)
				{
					ProcessDefinition(tr, btrId, processed, backup);
				}

				if (backup.Count > 0) _undoStack.Push(backup);
				tr.Commit();
				ed.Regen();
				ed.WriteMessage($"\nProcessed {processed.Count} block definitions.");
			}
		}

		private static void ProcessDefinition(Transaction tr, ObjectId btrId, HashSet<ObjectId> processed, List<EntityBackupState> backup)
		{
			if (processed.Contains(btrId)) return;
			processed.Add(btrId);

			BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
			btr.UpgradeOpen();

			foreach (ObjectId id in btr)
			{
				Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
				if (ent == null) continue;

				backup.Add(new EntityBackupState { EntityId = id, OldLayer = ent.Layer, OldColor = ent.Color });

				ent.Layer = "0";
				ent.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);

				if (ent is BlockReference subBr)
				{
					ProcessDefinition(tr, subBr.DynamicBlockTableRecord, processed, backup);
				}
			}
		}

		public static void UndoLayer0(Document doc)
		{
			if (_dbForUndo != doc.Database || _undoStack.Count == 0)
			{
				doc.Editor.WriteMessage("\nNothing to undo.");
				return;
			}

			List<EntityBackupState> batch = _undoStack.Pop();
			using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
			{
				int c = 0;
				foreach (var state in batch)
				{
					if (!state.EntityId.IsValid || state.EntityId.IsErased) continue;
					try
					{
						Entity ent = tr.GetObject(state.EntityId, OpenMode.ForWrite) as Entity;
						if (ent != null)
						{
							ent.Layer = state.OldLayer;
							ent.Color = state.OldColor;
							c++;
						}
					}
					catch { }
				}
				tr.Commit();
				doc.Editor.Regen();
				doc.Editor.WriteMessage($"\nUndid {c} entities.");
			}
		}
	}
}