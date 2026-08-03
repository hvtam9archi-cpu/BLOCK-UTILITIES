using System;
using AutoCADBlockTools.Services;
using AutoCADBlockTools.UI;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using AutoCADException = Autodesk.AutoCAD.Runtime.Exception;

[assembly: CommandClass(typeof(AutoCADBlockTools.Commands))]

namespace AutoCADBlockTools
{
	public class Commands
	{
		// ==========================================================================================
		// NHÓM 1: BIẾN ĐỔI (RSET, RSC, RRT, RAL, RR)
		// ==========================================================================================

		[CommandMethod("RSET", CommandFlags.Modal)]
		public void RandomSettingsCommand()
		{
			ExecuteCommand("RSET", doc =>
			{
				Editor ed = doc.Editor;
				var settings = BlockSettings.Instance;
				var window = new RandomSettingsWindow(settings.MinScale, settings.MaxScale, settings.MinRotate, settings.MaxRotate);
				if (Application.ShowModalWindow(window) == true)
				{
					settings.MinScale = window.MinScale;
					settings.MaxScale = window.MaxScale;
					settings.ValidateScale();

					settings.MinRotate = window.MinAngle;
					settings.MaxRotate = window.MaxAngle;
					settings.ValidateRotate();

					ed.WriteMessage($"\nĐã cập nhật Global Settings: Scale [{settings.MinScale}-{settings.MaxScale}], Rotate [{settings.MinRotate}-{settings.MaxRotate}]");
				}
			});
		}

		[CommandMethod("RSC", CommandFlags.UsePickSet | CommandFlags.Modal)]
		public void RandomScale()
		{
			ExecuteCommand("RSC", _ => BlockLogic.ApplyRandomTransformation(true, false, "\nChọn các Block để Scale ngẫu nhiên"));
		}

		[CommandMethod("RRT", CommandFlags.UsePickSet | CommandFlags.Modal)]
		public void RandomRotate()
		{
			ExecuteCommand("RRT", _ => BlockLogic.ApplyRandomTransformation(false, true, "\nChọn các Block để Xoay ngẫu nhiên"));
		}

		[CommandMethod("RAL", CommandFlags.UsePickSet | CommandFlags.Modal)]
		public void RandomAlign()
		{
			ExecuteCommand("RAL", _ => BlockLogic.ApplyRandomTransformation(true, true, "\nChọn các Block để Scale & Xoay ngẫu nhiên"));
		}

		[CommandMethod("RR", CommandFlags.UsePickSet | CommandFlags.Modal)]
		public void ResetRotationAndScale()
		{
			ExecuteCommand("RR", _ => BlockLogic.ResetRotationAndScale());
		}

		// ==========================================================================================
		// 2. NHÓM LỆNH TIỆN ÍCH QUẢN LÝ (DELB, DLB, MU...)
		// ==========================================================================================

		[CommandMethod("DELB", CommandFlags.UsePickSet | CommandFlags.Modal)]
		public void DeleteBlocks()
		{
			ExecuteCommand("DELB", _ => BlockLogic.DeleteBlocks());
		}

		[CommandMethod("DLB", CommandFlags.UsePickSet | CommandFlags.Modal)]
		public void ChangeBlockToLayer0()
		{
			ExecuteCommand("DLB", _ => BlockLogic.ChangeBlockToLayer0());
		}

		[CommandMethod("UDLB", CommandFlags.Modal)]
		public void UndoDLB()
		{
			ExecuteCommand("UDLB", _ => BlockLogic.UndoDLB());
		}

		[CommandMethod("MU", CommandFlags.UsePickSet | CommandFlags.Modal)]
		public void MakeBlockUniqueGroup()
		{
			ExecuteCommand("MU", _ => BlockLogic.MakeBlockUniqueGroup());
		}

		// ==========================================================================================
		// 3. NHÓM LỆNH BASE POINT & CENTER (CB, CBP, AB, JBP)
		// ==========================================================================================

		[CommandMethod("CB", CommandFlags.UsePickSet | CommandFlags.Modal)]
		public void CenterBlockBasePoint()
		{
			ExecuteCommand("CB", _ => BlockLogic.MoveBlockBasePoint(true, true));
		}

		[CommandMethod("CBP", CommandFlags.UsePickSet | CommandFlags.Modal)]
		public void ChangeBasePointOnly()
		{
			ExecuteCommand("CBP", _ => BlockLogic.MoveBlockBasePoint(false, false));
		}

		[CommandMethod("CBPR", CommandFlags.UsePickSet | CommandFlags.Modal)]
		public void ChangeBasePointRetainRef()
		{
			ExecuteCommand("CBPR", _ => BlockLogic.MoveBlockBasePoint(false, true));
		}

		[CommandMethod("AB", CommandFlags.UsePickSet | CommandFlags.Modal)]
		public void AutoBlockCenter()
		{
			ExecuteCommand("AB", _ => BlockLogic.AutoBlockCenter());
		}

		// ==========================================================================================
		// 4. LỆNH MỚI: JUSTIFY BLOCK (JBP)
		// ==========================================================================================

		[CommandMethod("JBP", CommandFlags.UsePickSet | CommandFlags.Modal)]
		public void JustifyBasePointCmd()
		{
			ExecuteCommand("JBP", _ => BlockLogic.JustifyBasePointCmd());
		}

		// ==========================================================================================
		// 5. LỆNH MỚI: RENAME BLOCK (RB)
		// ==========================================================================================

		[CommandMethod("RB", CommandFlags.Modal | CommandFlags.UsePickSet)]
		public void RenameBlockCommand()
		{
			ExecuteCommand("RB", _ => BlockLogic.RenameBlockCommand());
		}

		private static void ExecuteCommand(string commandName, Action<Document> action)
		{
			Document document = Application.DocumentManager.MdiActiveDocument;
			if (document == null) return;

			string documentName = "<unknown>";
			try
			{
				documentName = document.Name;
				action(document);
			}
			catch (AutoCADException ex)
			{
				Logger.Error($"{commandName} failed in '{documentName}' ({ex.ErrorStatus})", ex);
			}
			catch (System.Exception ex)
			{
				Logger.Error($"{commandName} failed in '{documentName}'", ex);
			}
		}
	}
}
