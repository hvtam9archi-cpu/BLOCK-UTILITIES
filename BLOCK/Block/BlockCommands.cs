using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using AutoCADBlockTools.Logic;

// Đặt CommandClass ở đây
[assembly: CommandClass(typeof(AutoCADBlockTools.BlockCommands))]

namespace AutoCADBlockTools
{
	public class BlockCommands
	{
		private Document Doc => Application.DocumentManager.MdiActiveDocument;

		// --- GROUP: TRANSFORMATION ---
		[CommandMethod("RSET", CommandFlags.Modal)]
		public void CmdSettings() => SafeRun(() => TransformLogic.ShowSettingsDialog(Doc.Editor));

		[CommandMethod("RSC", CommandFlags.UsePickSet | CommandFlags.Modal)]
		public void CmdRandomScale() => SafeRun(() => TransformLogic.ApplyRandom(Doc, true, false, "\nSelect blocks to Random Scale"));

		[CommandMethod("RRT", CommandFlags.UsePickSet | CommandFlags.Modal)]
		public void CmdRandomRotate() => SafeRun(() => TransformLogic.ApplyRandom(Doc, false, true, "\nSelect blocks to Random Rotate"));

		[CommandMethod("RAL", CommandFlags.UsePickSet | CommandFlags.Modal)]
		public void CmdRandomAlign() => SafeRun(() => TransformLogic.ApplyRandom(Doc, true, true, "\nSelect blocks to Random Align"));

		[CommandMethod("RR", CommandFlags.UsePickSet | CommandFlags.Modal)]
		public void CmdReset() => SafeRun(() => TransformLogic.ResetBlock(Doc));

		// --- GROUP: EDIT / MANAGE ---
		[CommandMethod("DELB", CommandFlags.UsePickSet | CommandFlags.Modal)]
		public void CmdDelete() => SafeRun(() => BlockEditLogic.DeleteBlocks(Doc));

		[CommandMethod("MU", CommandFlags.UsePickSet | CommandFlags.Modal)]
		public void CmdMakeUnique() => SafeRun(() => BlockEditLogic.MakeUnique(Doc));

		[CommandMethod("RB", CommandFlags.UsePickSet | CommandFlags.Modal)]
		public void CmdRename() => SafeRun(() => BlockEditLogic.RenameBlock(Doc));

		[CommandMethod("EB", CommandFlags.UsePickSet | CommandFlags.Modal)]
		public void CmdExplodeBlock() => SafeRun(() => BlockEditLogic.ExplodeToSingleLayer(Doc));

		// --- GROUP: LAYERS ---
		[CommandMethod("DLB", CommandFlags.UsePickSet | CommandFlags.Modal)]
		public void CmdToLayer0() => SafeRun(() => LayerLogic.ToLayer0(Doc));

		[CommandMethod("UDLB", CommandFlags.Modal)]
		public void CmdUndoLayer() => SafeRun(() => LayerLogic.UndoLayer0(Doc));

		// --- GROUP: BASE POINT ---
		[CommandMethod("JBP", CommandFlags.UsePickSet | CommandFlags.Modal)]
		public void CmdJustify() => SafeRun(() => BasePointLogic.JustifyBlock(Doc));

		[CommandMethod("CB", CommandFlags.UsePickSet | CommandFlags.Modal)]
		public void CmdCenter() => SafeRun(() => BasePointLogic.CenterBasePoint(Doc, true, true));

		[CommandMethod("CBP", CommandFlags.UsePickSet | CommandFlags.Modal)]
		public void CmdChangeBase() => SafeRun(() => BasePointLogic.CenterBasePoint(Doc, false, false));

		[CommandMethod("CBPR", CommandFlags.UsePickSet | CommandFlags.Modal)]
		public void CmdChangeBaseRetain() => SafeRun(() => BasePointLogic.CenterBasePoint(Doc, false, true));

		[CommandMethod("AB", CommandFlags.UsePickSet | CommandFlags.Modal)]
		public void CmdAutoBlock() => SafeRun(() => BasePointLogic.AutoBlock(Doc));

		// --- SAFETY WRAPPER ---
		private void SafeRun(System.Action action)
		{
			try
			{
				action();
			}
			catch (System.Exception ex)
			{
				Doc.Editor.WriteMessage($"\nCommand Error: {ex.Message}\n");
			}
		}
	}
}