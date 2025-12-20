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

			// 2. Form Cài đặt
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

					// --- SỬA LỖI: Lấy Bounding Box chính xác hơn ---
					Extents3d? bounds = GetGeoExtents(tr, btr);

					if (!bounds.HasValue) continue;

					Point3d newOrigin = CalcJusPoint(bounds.Value, _lastJus);
					Vector3d disp = Point3d.Origin - newOrigin; // Vector dời hình về 0,0

					if (disp.Length > 1e-6)
					{
						btr.UpgradeOpen();

						// A. Dời hình học trong Block Definition
						foreach (ObjectId id in btr)
						{
							if (tr.GetObject(id, OpenMode.ForWrite) is Entity ent)
								ent.TransformBy(Matrix3d.Displacement(disp));
						}

						// B. Cập nhật vị trí Reference để hình không bị nhảy (Visual Position)
						if (_retainVisual)
						{
							// Nếu trong Block dời đi vector DISP (ví dụ -10), 
							// thì Reference bên ngoài phải dời đi vector -DISP (ví dụ +10) để bù trừ.
							RecursiveUpdateReferences(tr, btr, disp);
						}

						if (btr.HasAttributeDefinitions) toSync.Add(btrId);
						count++;
					}
				}
				tr.Commit();

				// Sync Attribute để chữ nhảy về đúng chỗ mới
				foreach (ObjectId id in toSync) SyncAtts(doc, id);

				ed.Regen();
				ed.WriteMessage($"\nJustified {count} block types.");
			}
		}

		private static void RecursiveUpdateReferences(Transaction tr, BlockTableRecord btr, Vector3d dispInBlockSpace)
		{
			ObjectIdCollection refIds = btr.GetBlockReferenceIds(true, true);
			foreach (ObjectId id in refIds)
			{
				DBObject obj = tr.GetObject(id, OpenMode.ForRead);
				if (obj is BlockReference br)
				{
					if (!br.IsWriteEnabled) br.UpgradeOpen();
					// Transform vector nội bộ ra hệ tọa độ World (bao gồm Scale/Rotate của Block)
					Vector3d worldDisp = dispInBlockSpace.TransformBy(br.BlockTransform);

					// Dời vị trí Reference ngược lại để bù trừ sự thay đổi bên trong
					// Logic: Pos_Mới = Pos_Cũ - (Vector_Dời_Nội_Bộ_Đã_Scale)
					br.Position = br.Position.Add(worldDisp.Negate());

					br.RecordGraphicsModified(true);
				}
				else if (obj is BlockTableRecord anonBtr)
				{
					// Đệ quy xử lý Dynamic Block (Anonymous Definition)
					RecursiveUpdateReferences(tr, anonBtr, dispInBlockSpace);
				}
			}
		}

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
				if (autoCenter)
				{
					Extents3d? bounds = GetGeoExtents(tr, btr);
					if (!bounds.HasValue) return;
					Point3d cen = new Point3d((bounds.Value.MinPoint.X + bounds.Value.MaxPoint.X) / 2,
											  (bounds.Value.MinPoint.Y + bounds.Value.MaxPoint.Y) / 2,
											  (bounds.Value.MinPoint.Z + bounds.Value.MaxPoint.Z) / 2);
					disp = Point3d.Origin.GetVectorTo(cen).Negate();
				}
				else
				{
					PromptPointOptions ppo = new PromptPointOptions("\nNew Base Point: ") { UseBasePoint = true, BasePoint = br.Position };
					var ppr = ed.GetPoint(ppo);
					if (ppr.Status != PromptStatus.OK) return;
					disp = ppr.Value.TransformBy(br.BlockTransform.Inverse()).GetVectorTo(Point3d.Origin);
				}

				if (disp.Length > 1e-6)
				{
					btr.UpgradeOpen();
					foreach (ObjectId id in btr)
						if (tr.GetObject(id, OpenMode.ForWrite) is Entity ent) ent.TransformBy(Matrix3d.Displacement(disp));

					if (retainRef) RecursiveUpdateReferences(tr, btr, disp);
					if (btr.HasAttributeDefinitions) SyncAtts(doc, btr.ObjectId);
				}
				tr.Commit();
				ed.Regen();
			}
		}

		public static void AutoBlock(Document doc)
		{
			Editor ed = doc.Editor;
			SelectionSet ss = CadUtils.GetSelection(ed, "\nSelect objects to block: ", "*");
			if (ss == null || ss.Count == 0) return;

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

				BlockReference newRef = new BlockReference(center, newBtr.ObjectId);
				curSpace.AppendEntity(newRef);
				tr.AddNewlyCreatedDBObject(newRef, true);
				tr.Commit();
			}
		}

		// --- HÀM QUAN TRỌNG: LỌC ĐỐI TƯỢNG ĐỂ TÍNH BOUNDING BOX ---
		private static Extents3d? GetGeoExtents(Transaction tr, BlockTableRecord btr)
		{
			Extents3d? res = null;
			foreach (ObjectId id in btr)
			{
				Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
				// 1. Chỉ lấy đối tượng hiển thị
				if (ent == null || !ent.Visible) continue;

				// 2. LOẠI BỎ CÁC ĐỐI TƯỢNG GÂY NHIỄU VỊ TRÍ
				if (ent is AttributeDefinition) continue; // AttDef không phải hình học cố định
				if (ent is Dimension) continue;           // Dim có điểm định nghĩa tại (0,0) gây sai lệch
				if (ent is Viewport) continue;
				if (ent is Xline || ent is Ray) continue; // Đối tượng vô tận

				// 3. CHỈ CHẤP NHẬN CÁC ĐỐI TƯỢNG HÌNH HỌC CHUẨN
				bool isGeometry = ent is Curve ||          // Line, Arc, Circle, Polyline...
								  ent is Solid ||
								  ent is Region ||
								  ent is Hatch ||
								  ent is DBText || ent is MText || // Chấp nhận Text để tính bao chữ
								  ent is BlockReference ||
								  ent is Polyline2d || ent is Polyline3d || ent is SubDMesh || ent is Face;

				if (!isGeometry) continue;

				try
				{
					if (ent.Bounds.HasValue)
					{
						Extents3d b = ent.Bounds.Value;
						// Kiểm tra tính hợp lệ (Min < Max) để tránh lỗi null extents
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
				using (Transaction tr = doc.TransactionManager.StartTransaction())
				{
					BlockTableRecord b = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
					// Dùng Command synchronous để đảm bảo ATTSYNC chạy xong mới regen
					doc.Editor.Command("_.ATTSYNC", "_N", b.Name);
					tr.Commit();
				}
			}
			catch { }
		}
	}
}