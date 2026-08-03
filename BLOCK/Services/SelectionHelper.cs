using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace AutoCADBlockTools.Services
{
	/// <summary>
	/// Helper xử lý selection — tách biệt logic chọn đối tượng khỏi business logic.
	/// </summary>
	public static class SelectionHelper
	{
		/// <summary>
		/// Lọc ObjectId[] chỉ giữ lại BlockReference mà KHÔNG cần Transaction.
		/// ObjectClass.DxfName là metadata sẵn có trên ObjectId, không truy cập DB.
		/// </summary>
		public static ObjectId[] FilterBlockIdsNoTransaction(ObjectId[] ids)
		{
			var blockIds = new System.Collections.Generic.List<ObjectId>(ids.Length);
			for (int i = 0; i < ids.Length; i++)
			{
				if (ids[i].IsValid && !ids[i].IsErased && ids[i].ObjectClass.DxfName == "INSERT")
					blockIds.Add(ids[i]);
			}
			return blockIds.Count > 0 ? [.. blockIds] : null;
		}

		/// <summary>
		/// Lấy selection: ưu tiên implied selection (pre-selected), fallback về interactive pick.
		/// Tự động lọc chỉ lấy BlockReference.
		/// </summary>
		public static SelectionSet GetSelection(Editor ed, string promptMsg)
		{
			// PickFirst: ưu tiên implied selection
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

			// Interactive selection với filter INSERT
			var pso = new PromptSelectionOptions
			{
				MessageForAdding = promptMsg,
				RejectObjectsOnLockedLayers = true
			};
			var filter = new SelectionFilter([new((int)DxfCode.Start, "INSERT")]);

			PromptSelectionResult psr = ed.GetSelection(pso, filter);
			return psr.Status == PromptStatus.OK ? psr.Value : null;
		}
	}
}
