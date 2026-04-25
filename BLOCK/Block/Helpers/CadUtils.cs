using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

namespace AutoCADBlockTools.Helpers
{
	public static class CadUtils
	{
		/// <summary>
		/// Lấy SelectionSet hỗ trợ cả PickFirst (chọn trước) và chọn sau.
		/// </summary>
		public static SelectionSet GetSelection(Editor ed, string promptMsg, string allowedType = "INSERT")
		{
			// 1. Check PickFirst
			PromptSelectionResult implied = ed.SelectImplied();
			if (implied.Status == PromptStatus.OK && implied.Value.Count > 0)
			{
				ObjectId[] ids = implied.Value.GetObjectIds();
				ObjectIdCollection validIds = new ObjectIdCollection();
				using (Transaction tr = ed.Document.Database.TransactionManager.StartTransaction())
				{
					foreach (ObjectId id in ids)
					{
						if (id.ObjectClass.DxfName == allowedType || allowedType == "*")
							validIds.Add(id);
					}
					tr.Commit();
				}

				if (validIds.Count > 0)
				{
					ed.SetImpliedSelection(new ObjectId[0]); // Clear pickfirst
					return SelectionSet.FromObjectIds(validIds.Cast<ObjectId>().ToArray());
				}
			}

			// 2. Interactive Selection
			PromptSelectionOptions pso = new PromptSelectionOptions
			{
				MessageForAdding = promptMsg,
				RejectObjectsOnLockedLayers = true
			};
			if (allowedType != "*")
			{
				SelectionFilter filter = new SelectionFilter(new TypedValue[] { new TypedValue((int)DxfCode.Start, allowedType) });
				PromptSelectionResult psr = ed.GetSelection(pso, filter);
				if (psr.Status == PromptStatus.OK) return psr.Value;
			}
			else
			{
				PromptSelectionResult psr = ed.GetSelection(pso);
				if (psr.Status == PromptStatus.OK) return psr.Value;
			}

			return null;
		}

		public static string GetEffectiveName(BlockReference br, Transaction tr)
		{
			if (br.IsDynamicBlock)
			{
				BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
				return btr.Name;
			}
			return br.Name;
		}

		/// <summary>
		/// Đưa tất cả đối tượng Hatch trong Block Definition xuống dưới cùng (Draw Order: Back).
		/// </summary>
		public static void SendHatchesToBack(Transaction tr, BlockTableRecord btr)
		{
			try
			{
				ObjectIdCollection hatchIds = new ObjectIdCollection();
				foreach (ObjectId id in btr)
				{
					if (id.IsErased) continue;
					// Kiểm tra nhanh loại đối tượng
					if (id.ObjectClass.DxfName == "HATCH")
					{
						hatchIds.Add(id);
					}
				}

				if (hatchIds.Count > 0)
				{
					// Lấy DrawOrderTable của Block
					DrawOrderTable dot = (DrawOrderTable)tr.GetObject(btr.DrawOrderTableId, OpenMode.ForWrite);
					dot.MoveToBottom(hatchIds);
				}
			}
			catch (System.Exception)
			{
				// Bỏ qua lỗi nếu DrawOrderTable không truy cập được (hiếm gặp)
			}
		}
	}
}