using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutoCADBlockTools.Services
{
	/// <summary>
	/// Service xử lý các lệnh biến đổi hình học ngẫu nhiên (RSC, RRT, RAL, RR).
	/// </summary>
	public static class TransformationService
	{
		private static readonly Random _random = new();

		/// <summary>
		/// Áp dụng scale/rotate ngẫu nhiên cho các block được chọn.
		/// </summary>
		public static void ApplyRandomTransformation(bool doScale, bool doRotate, string promptMessage)
		{
			var doc = Application.DocumentManager.MdiActiveDocument;
			var db = doc.Database;
			var ed = doc.Editor;
			var settings = BlockSettings.Instance;

			var ss = GetSelectionSet(ed, doScale, doRotate, promptMessage);
			if (ss == null || ss.Count == 0) return;

			using var docLock = doc.LockDocument();
			using var tr = db.TransactionManager.StartTransaction();
			int count = 0;
			double scaleRange = settings.MaxScale - settings.MinScale;
			double rotRange = settings.MaxRotate - settings.MinRotate;

			foreach (SelectedObject so in ss)
			{
				try
				{
					if (tr.GetObject(so.ObjectId, OpenMode.ForWrite) is BlockReference br)
					{
						if (doScale)
						{
							double scaleFactor = Math.Round(settings.MinScale + (_random.NextDouble() * scaleRange), 3);
							br.ScaleFactors = new Scale3d(scaleFactor, scaleFactor, scaleFactor);
						}
						if (doRotate)
						{
							double angleDeg = settings.MinRotate + (_random.NextDouble() * rotRange);
							br.Rotation = angleDeg * Math.PI / 180.0;
						}
						count++;
					}
				}
				catch (Exception ex)
				{
					Logger.Warning($"Transform: {ex.Message}");
				}
			}
			tr.Commit();
			Logger.Info($"Đã biến đổi thành công {count} đối tượng.");
		}

		/// <summary>
		/// Reset rotation=0 và scale=1 cho các block được chọn.
		/// </summary>
		public static void ResetRotationAndScale()
		{
			var doc = Application.DocumentManager.MdiActiveDocument;
			var db = doc.Database;
			var ed = doc.Editor;

			var ss = SelectionHelper.GetSelection(ed, "\nChọn các Block để Reset (Góc=0, Scale=1): ");
			if (ss == null || ss.Count == 0) return;

			using var docLock = doc.LockDocument();
			using var tr = db.TransactionManager.StartTransaction();
			int count = 0;
			foreach (SelectedObject so in ss)
			{
				try
				{
					if (tr.GetObject(so.ObjectId, OpenMode.ForRead) is BlockReference br)
					{
						if (Math.Abs(br.Rotation) < Tolerance.Global.EqualPoint &&
							Math.Abs(br.ScaleFactors.X - 1.0) < Tolerance.Global.EqualPoint &&
							Math.Abs(br.ScaleFactors.Y - 1.0) < Tolerance.Global.EqualPoint &&
							Math.Abs(br.ScaleFactors.Z - 1.0) < Tolerance.Global.EqualPoint)
							continue;

						br.UpgradeOpen();
						br.Rotation = 0.0;
						br.ScaleFactors = new Scale3d(1.0, 1.0, 1.0);
						count++;
					}
				}
				catch (Exception ex)
				{
					Logger.Warning($"Reset: {ex.Message}");
				}
			}
			tr.Commit();
			Logger.Info($"Đã Reset {count} block về trạng thái mặc định.");
		}

		private static SelectionSet GetSelectionSet(Editor ed, bool doScale, bool doRotate, string promptMessage)
		{
			// PickFirst logic
			var implied = ed.SelectImplied();
			if (implied.Status == PromptStatus.OK && implied.Value.Count > 0)
			{
				var filtered = SelectionHelper.FilterBlockIdsNoTransaction(implied.Value.GetObjectIds());
				if (filtered != null)
				{
					ed.SetImpliedSelection([]);
					return SelectionSet.FromObjectIds(filtered);
				}
			}

			var settings = BlockSettings.Instance;
			string info = "";
			if (doScale && doRotate) info = $"(S={settings.MinScale}-{settings.MaxScale}, R={settings.MinRotate}-{settings.MaxRotate})";
			else if (doScale) info = $"(Scale={settings.MinScale}-{settings.MaxScale})";
			else if (doRotate) info = $"(Rot={settings.MinRotate}-{settings.MaxRotate})";

			var pso = new PromptSelectionOptions
			{
				MessageForAdding = $"{promptMessage} {info}: ",
				RejectObjectsOnLockedLayers = true
			};
			var filter = new SelectionFilter([new((int)DxfCode.Start, "INSERT")]);
			var psr = ed.GetSelection(pso, filter);
			return psr.Status == PromptStatus.OK ? psr.Value : null;
		}
	}
}
