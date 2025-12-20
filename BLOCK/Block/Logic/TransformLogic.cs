using System;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using WinForms = System.Windows.Forms;
using AutoCADBlockTools.Helpers;

namespace AutoCADBlockTools.Logic
{
	public static class TransformLogic
	{
		private static readonly Random _random = new Random();
		// Global Settings
		public static double MinScale { get; set; } = 0.75;
		public static double MaxScale { get; set; } = 1.25;
		public static double MinRotate { get; set; } = 0.0;
		public static double MaxRotate { get; set; } = 360.0;

		public static void ShowSettingsDialog(Editor ed)
		{
			using (var form = new RandomSettingsForm(MinScale, MaxScale, MinRotate, MaxRotate))
			{
				if (Application.ShowModalDialog(form) == WinForms.DialogResult.OK)
				{
					MinScale = Math.Min(form.MinScale, form.MaxScale);
					MaxScale = Math.Max(form.MinScale, form.MaxScale);
					MinRotate = Math.Min(form.MinAngle, form.MaxAngle);
					MaxRotate = Math.Max(form.MinAngle, form.MaxAngle);
					ed.WriteMessage($"\nSettings Updated: Scale [{MinScale}-{MaxScale}], Rotate [{MinRotate}-{MaxRotate}]");
				}
			}
		}

		public static void ApplyRandom(Document doc, bool doScale, bool doRotate, string msg)
		{
			Editor ed = doc.Editor;
			SelectionSet ss = CadUtils.GetSelection(ed, msg);
			if (ss == null || ss.Count == 0) return;

			using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
			{
				int count = 0;
				double scaleRange = MaxScale - MinScale;
				double rotRange = MaxRotate - MinRotate;

				foreach (SelectedObject so in ss)
				{
					if (tr.GetObject(so.ObjectId, OpenMode.ForWrite) is BlockReference br)
					{
						if (doScale)
						{
							double factor = Math.Round(MinScale + (_random.NextDouble() * scaleRange), 3);
							br.ScaleFactors = new Scale3d(factor, factor, factor);
						}
						if (doRotate)
						{
							double angle = (MinRotate + (_random.NextDouble() * rotRange)) * Math.PI / 180.0;
							br.Rotation = angle;
						}
						count++;
					}
				}
				tr.Commit();
				ed.WriteMessage($"\nRandomized {count} blocks.");
			}
		}

		public static void ResetBlock(Document doc)
		{
			Editor ed = doc.Editor;
			SelectionSet ss = CadUtils.GetSelection(ed, "\nSelect blocks to Reset: ");
			if (ss == null || ss.Count == 0) return;

			using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
			{
				int count = 0;
				foreach (SelectedObject so in ss)
				{
					if (tr.GetObject(so.ObjectId, OpenMode.ForWrite) is BlockReference br)
					{
						br.Rotation = 0.0;
						br.ScaleFactors = new Scale3d(1.0, 1.0, 1.0);
						count++;
					}
				}
				tr.Commit();
				ed.WriteMessage($"\nReset {count} blocks.");
			}
		}
	}
}