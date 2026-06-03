using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using AutoCADBlockTools.UI;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutoCADBlockTools.Services
{
    /// <summary>
    /// Service xử lý lệnh Rename Block (RB).
    /// </summary>
    public static class RenameService
    {
        public static void RenameBlockCommand()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            ObjectId targetId = ObjectId.Null;
            var implied = ed.SelectImplied();

            if (implied.Status == PromptStatus.OK && implied.Value.Count == 1)
            {
                targetId = implied.Value.GetObjectIds()[0];
            }
            else
            {
                ed.SetImpliedSelection([]);
                var peo = new PromptEntityOptions("\nChọn 1 Block để đổi tên: ");
                peo.SetRejectMessage("\nĐối tượng phải là Block.");
                peo.AddAllowedClass(typeof(BlockReference), true);

                var per = ed.GetEntity(peo);
                if (per.Status == PromptStatus.OK) targetId = per.ObjectId;
            }
            if (targetId == ObjectId.Null) return;

            using (var docLock = doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    var br = tr.GetObject(targetId, OpenMode.ForRead) as BlockReference;
                    if (br == null) return;

                    string currentName = BlockHelper.GetEffectiveName(br, tr);

                    var window = new RenameBlockWindow(currentName);
                    if (Application.ShowModalWindow(window) != true) return;

                    string newName = window.ResultName;
                    if (newName.Equals(currentName, StringComparison.OrdinalIgnoreCase)) return;

                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    if (bt.Has(newName))
                    {
                        Logger.Error($"Tên Block '{newName}' đã tồn tại trong bản vẽ!");
                        return;
                    }

                    var btrId = br.DynamicBlockTableRecord;
                    var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForWrite);
                    btr.Name = newName;

                    Logger.Info($"Đã đổi tên Block từ '{currentName}' thành '{newName}'.");
                    tr.Commit();
                }
                catch (Exception ex)
                {
                    Logger.Error($"Lỗi khi đổi tên", ex);
                }
            }
        }
    }
}
