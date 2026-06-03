using System;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AutoCADBlockTools.Services;
using AutoCADBlockTools.UI;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

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
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            var settings = BlockSettings.Instance;

            try
            {
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
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nLỗi khi mở bảng cài đặt: {ex.Message}");
            }
        }

        [CommandMethod("RSC", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void RandomScale()
        {
            BlockLogic.ApplyRandomTransformation(true, false, "\nChọn các Block để Scale ngẫu nhiên");
        }

        [CommandMethod("RRT", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void RandomRotate()
        {
            BlockLogic.ApplyRandomTransformation(false, true, "\nChọn các Block để Xoay ngẫu nhiên");
        }

        [CommandMethod("RAL", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void RandomAlign()
        {
            BlockLogic.ApplyRandomTransformation(true, true, "\nChọn các Block để Scale & Xoay ngẫu nhiên");
        }

        [CommandMethod("RR", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void ResetRotationAndScale()
        {
            BlockLogic.ResetRotationAndScale();
        }

        // ==========================================================================================
        // 2. NHÓM LỆNH TIỆN ÍCH QUẢN LÝ (DELB, DLB, MU...)
        // ==========================================================================================

        [CommandMethod("DELB", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void DeleteBlocks()
        {
            BlockLogic.DeleteBlocks();
        }

        [CommandMethod("DLB", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void ChangeBlockToLayer0()
        {
            BlockLogic.ChangeBlockToLayer0();
        }

        [CommandMethod("UDLB", CommandFlags.Modal)]
        public void UndoDLB()
        {
            BlockLogic.UndoDLB();
        }

        [CommandMethod("MU", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void MakeBlockUniqueGroup()
        {
            BlockLogic.MakeBlockUniqueGroup();
        }

        // ==========================================================================================
        // 3. NHÓM LỆNH BASE POINT & CENTER (CB, CBP, AB, JBP)
        // ==========================================================================================

        [CommandMethod("CB", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void CenterBlockBasePoint()
        {
            BlockLogic.MoveBlockBasePoint(true, true);
        }

        [CommandMethod("CBP", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void ChangeBasePointOnly()
        {
            BlockLogic.MoveBlockBasePoint(false, false);
        }

        [CommandMethod("CBPR", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void ChangeBasePointRetainRef()
        {
            BlockLogic.MoveBlockBasePoint(false, true);
        }

        [CommandMethod("AB", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void AutoBlockCenter()
        {
            BlockLogic.AutoBlockCenter();
        }

        // ==========================================================================================
        // 4. LỆNH MỚI: JUSTIFY BLOCK (JBP)
        // ==========================================================================================

        [CommandMethod("JBP", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void JustifyBasePointCmd()
        {
            BlockLogic.JustifyBasePointCmd();
        }

        // ==========================================================================================
        // 5. LỆNH MỚI: RENAME BLOCK (RB)
        // ==========================================================================================

        [CommandMethod("RB", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void RenameBlockCommand()
        {
            BlockLogic.RenameBlockCommand();
        }
    }
}
