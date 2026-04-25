using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using WinForms = System.Windows.Forms;
using AutoCADBlockTools.Helpers;

namespace AutoCADBlockTools.Logic
{
	public static class BasePointLogic
	{
		private static Justification _lastJus = Justification.BottomLeft;
		private static bool _retainVisual = true;

		// Hằng số DXF Name cho Base Point Parameter
		private const string DXF_BASE_POINT_PARAM = "BLOCKBASEPOINTPARAMETER";

		/// <summary>
		/// Lệnh JBP: Thay đổi điểm chèn theo vị trí căn lề (TopLeft, Center, v.v.)
		/// </summary>
		public static void JustifyBlock(Document doc)
		{
			Editor ed = doc.Editor;
			SelectionSet ss = CadUtils.GetSelection(ed, "\nSelect blocks to justify: ");
			if (ss == null || ss.Count == 0) return;

			// 1. Lấy danh sách tên Block (Unique)
			HashSet<string> names = new HashSet<string>();
			using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
			{
				foreach (SelectedObject so in ss)
					if (tr.GetObject(so.ObjectId, OpenMode.ForRead) is BlockReference br)
						names.Add(CadUtils.GetEffectiveName(br, tr));
				tr.Commit();
			}

			// 2. UI
			using (var form = new JbpForm(_lastJus, _retainVisual))
			{
				if (Application.ShowModalDialog(form) != WinForms.DialogResult.OK) return;
				_lastJus = form.SelectedJustification;
				_retainVisual = form.RetainVisualPosition;
			}

			// 3. Thực thi
			using (DocumentLock dl = doc.LockDocument())
			using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
			{
				BlockTable bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
				HashSet<ObjectId> toSync = new HashSet<ObjectId>();
				int count = 0;

				foreach (string name in names)
				{
					if (!bt.Has(name)) continue;
					ObjectId btrId = bt[name];
					BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);

					Extents3d? bounds = GetGeoExtents(tr, btr);
					if (!bounds.HasValue) continue;

					Point3d newOrigin = CalcJusPoint(bounds.Value, _lastJus);
					Vector3d disp = Point3d.Origin - newOrigin;

					if (disp.Length > 1e-6)
					{
						btr.UpgradeOpen();

						// --- XỬ LÝ HÌNH HỌC & BASE POINT ---
						foreach (ObjectId id in btr)
						{
							Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
							if (ent == null) continue;

							// [FIXED] Sử dụng DxfName để nhận diện BasePointParameter
							if (IsBasePointParameter(ent))
							{
								// Dùng dynamic để set Position tránh lỗi thiếu Class Wrapper
								try { ((dynamic)ent).Position = Point3d.Origin; } catch { }
							}
							else
							{
								ent.TransformBy(Matrix3d.Displacement(disp));
							}
						}

						if (_retainVisual) UpdateAllReferences(tr, btr, disp);
						if (btr.HasAttributeDefinitions) toSync.Add(btrId);

						CadUtils.SendHatchesToBack(tr, btr);
						count++;
					}
				}
				tr.Commit();

				foreach (ObjectId id in toSync) SyncAtts(doc, id);
				ed.Regen();
				ed.WriteMessage($"\nJustified {count} block types.");
			}
		}

		/// <summary>
		/// Lệnh CB, CBP, CBPR
		/// </summary>
		public static void CenterBasePoint(Document doc, bool autoCenter, bool retainRef)
		{
			Editor ed = doc.Editor;
			PromptEntityOptions peo = new PromptEntityOptions("\nSelect Block: ");
			peo.SetRejectMessage("\nBlock only.");
			peo.AddAllowedClass(typeof(BlockReference), true);

			ObjectId targetId = ObjectId.Null;
			var imp = ed.SelectImplied();
			if (imp.Status == PromptStatus.OK && imp.Value.Count > 0) targetId = imp.Value.GetObjectIds()[0];
			else
			{
				var per = ed.GetEntity(peo);
				if (per.Status == PromptStatus.OK) targetId = per.ObjectId;
			}

			if (targetId == ObjectId.Null) return;

			using (DocumentLock dl = doc.LockDocument())
			using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
			{
				BlockReference br = (BlockReference)tr.GetObject(targetId, OpenMode.ForRead);
				BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);

				Vector3d disp = new Vector3d();

				if (autoCenter) // CB
				{
					Extents3d? bounds = GetGeoExtents(tr, btr);
					if (!bounds.HasValue) return;
					Point3d cen = new Point3d((bounds.Value.MinPoint.X + bounds.Value.MaxPoint.X) / 2,
											  (bounds.Value.MinPoint.Y + bounds.Value.MaxPoint.Y) / 2,
											  (bounds.Value.MinPoint.Z + bounds.Value.MaxPoint.Z) / 2);
					disp = Point3d.Origin.GetVectorTo(cen).Negate();
				}
				else // CBP, CBPR
				{
					PromptPointOptions ppo = new PromptPointOptions("\nPick new Base Point: ") { UseBasePoint = true, BasePoint = br.Position };
					var ppr = ed.GetPoint(ppo);
					if (ppr.Status != PromptStatus.OK) return;

					Point3d pickPtInBlock = ppr.Value.TransformBy(br.BlockTransform.Inverse());
					disp = pickPtInBlock.GetVectorTo(Point3d.Origin);
				}

				bool needSync = false;
				ObjectId syncId = btr.ObjectId;

				if (disp.Length > 1e-6)
				{
					btr.UpgradeOpen();

					foreach (ObjectId id in btr)
					{
						Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
						if (ent == null) continue;

						// [FIXED] Sử dụng DxfName
						if (IsBasePointParameter(ent))
						{
							try { ((dynamic)ent).Position = Point3d.Origin; } catch { }
						}
						else
						{
							ent.TransformBy(Matrix3d.Displacement(disp));
						}
					}

					if (retainRef) UpdateAllReferences(tr, btr, disp);
					needSync = btr.HasAttributeDefinitions;
					CadUtils.SendHatchesToBack(tr, btr);
				}
				tr.Commit();
				
				if (needSync) SyncAtts(doc, syncId);
				ed.Regen();
			}
		}

		private static void UpdateAllReferences(Transaction tr, BlockTableRecord btr, Vector3d dispInBlockSpace)
		{
			ObjectIdCollection directIds = btr.GetBlockReferenceIds(true, true);
			foreach (ObjectId id in directIds) UpdateSingleReference(tr, id, dispInBlockSpace);

			if (btr.IsDynamicBlock)
			{
				ObjectIdCollection anonBtrIds = btr.GetAnonymousBlockIds();
				foreach (ObjectId anonId in anonBtrIds)
				{
					BlockTableRecord anonBtr = (BlockTableRecord)tr.GetObject(anonId, OpenMode.ForRead);
					ObjectIdCollection anonRefIds = anonBtr.GetBlockReferenceIds(true, true);
					foreach (ObjectId id in anonRefIds) UpdateSingleReference(tr, id, dispInBlockSpace);
				}
			}
		}

		private static void UpdateSingleReference(Transaction tr, ObjectId refId, Vector3d dispInBlockSpace)
		{
			if (refId.IsErased) return;
			try
			{
				BlockReference br = tr.GetObject(refId, OpenMode.ForWrite, true) as BlockReference;
				if (br != null)
				{
					Vector3d worldDisp = dispInBlockSpace.TransformBy(br.BlockTransform);
					br.Position = br.Position.Add(worldDisp.Negate());
					br.RecordGraphicsModified(true);
				}
			}
			catch { }
		}

		public static void AutoBlock(Document doc)
		{
			Editor ed = doc.Editor;
			SelectionSet ss = CadUtils.GetSelection(ed, "\nSelect objects to block: ", "*");
			if (ss == null || ss.Count == 0) return;

			using (DocumentLock dl = doc.LockDocument())
			using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
			{
				BlockTableRecord curSpace = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForWrite);
				ObjectIdCollection ids = new ObjectIdCollection();
				Extents3d ext = new Extents3d();
				bool init = false;

				foreach (SelectedObject so in ss)
				{
					Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
					ids.Add(so.ObjectId);
					if (ent.Bounds.HasValue)
					{
						if (!init) { ext = ent.Bounds.Value; init = true; }
						else ext.AddExtents(ent.Bounds.Value);
					}
				}

				if (!init) return;
				Point3d center = new Point3d((ext.MinPoint.X + ext.MaxPoint.X) / 2, (ext.MinPoint.Y + ext.MaxPoint.Y) / 2, (ext.MinPoint.Z + ext.MaxPoint.Z) / 2);

				string name = "@" + DateTime.Now.Ticks.ToString().Substring(0, 16);
				BlockTable bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForWrite);
				BlockTableRecord newBtr = new BlockTableRecord { Name = name, Origin = center };
				bt.Add(newBtr);
				tr.AddNewlyCreatedDBObject(newBtr, true);

				doc.Database.DeepCloneObjects(ids, newBtr.ObjectId, new IdMapping(), false);
				foreach (ObjectId id in ids)
					if (tr.GetObject(id, OpenMode.ForWrite) is Entity e) e.Erase();

				CadUtils.SendHatchesToBack(tr, newBtr);

				BlockReference newRef = new BlockReference(center, newBtr.ObjectId);
				curSpace.AppendEntity(newRef);
				tr.AddNewlyCreatedDBObject(newRef, true);
				tr.Commit();
			}
		}

		// --- Helper Function ---
		private static bool IsBasePointParameter(Entity ent)
		{
			// Kiểm tra DXF Name thay vì kiểm tra Type Class
			try
			{
				return ent.GetRXClass().DxfName.ToUpper() == DXF_BASE_POINT_PARAM;
			}
			catch { return false; }
		}

		private static Extents3d? GetGeoExtents(Transaction tr, BlockTableRecord btr)
		{
			Extents3d? res = null;
			foreach (ObjectId id in btr)
			{
				Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
				if (ent == null || !ent.Visible) continue;
				if (ent is Xline || ent is Ray || ent is Viewport || ent is Dimension) continue;

				// [FIXED] Loại bỏ BasePointParameter bằng DXF Name
				if (IsBasePointParameter(ent)) continue;

				try
				{
					if (ent.Bounds.HasValue)
					{
						Extents3d b = ent.Bounds.Value;
						if (b.MinPoint.DistanceTo(b.MaxPoint) > 1e-8)
						{
							if (res == null) res = b;
							else
							{
								Extents3d tmp = res.Value;
								tmp.AddExtents(b);
								res = tmp;
							}
						}
					}
				}
				catch { }
			}
			return res;
		}

		private static Point3d CalcJusPoint(Extents3d e, Justification j)
		{
			double x = e.MinPoint.X, y = e.MinPoint.Y, mx = (e.MinPoint.X + e.MaxPoint.X) / 2, my = (e.MinPoint.Y + e.MaxPoint.Y) / 2, X = e.MaxPoint.X, Y = e.MaxPoint.Y;
			switch (j)
			{
				case Justification.BottomLeft: return new Point3d(x, y, 0);
				case Justification.BottomCenter: return new Point3d(mx, y, 0);
				case Justification.BottomRight: return new Point3d(X, y, 0);
				case Justification.MiddleLeft: return new Point3d(x, my, 0);
				case Justification.MiddleCenter: return new Point3d(mx, my, 0);
				case Justification.MiddleRight: return new Point3d(X, my, 0);
				case Justification.TopLeft: return new Point3d(x, Y, 0);
				case Justification.TopCenter: return new Point3d(mx, Y, 0);
				case Justification.TopRight: return new Point3d(X, Y, 0);
			}
			return Point3d.Origin;
		}

		private static void SyncAtts(Document doc, ObjectId btrId)
		{
			try
			{
				string name = string.Empty;
				using (Transaction tr = doc.TransactionManager.StartTransaction())
				{
					BlockTableRecord b = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
					name = b.Name;
					tr.Commit();
				}
				
				if (!string.IsNullOrEmpty(name))
				{
					doc.Editor.Command("_.ATTSYNC", "_N", name);
				}
			}
			catch { }
		}
	}
}