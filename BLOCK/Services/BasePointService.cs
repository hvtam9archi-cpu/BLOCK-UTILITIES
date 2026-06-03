using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AutoCADBlockTools.UI;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutoCADBlockTools.Services
{
    /// <summary>
    /// Service xử lý các lệnh Base Point (CB, CBP, CBPR, AB, JBP).
    /// </summary>
    public static class BasePointService
    {
        #region CB / CBP / CBPR - Move/Center Base Point

        public static void MoveBlockBasePoint(bool autoCenter, bool retainRefPosition)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;
            ObjectId targetId = ObjectId.Null;
            bool wasPreSelected = false;

            var implied = ed.SelectImplied();
            if (implied.Status == PromptStatus.OK && implied.Value.Count > 0)
            {
                targetId = implied.Value.GetObjectIds()[0];
                wasPreSelected = true;
                ed.SetImpliedSelection([]);
            }

            if (targetId == ObjectId.Null)
            {
                var peo = new PromptEntityOptions("\nChọn Block: ");
                peo.SetRejectMessage("\nPhải chọn Block.");
                peo.AddAllowedClass(typeof(BlockReference), true);
                var per = ed.GetEntity(peo);
                if (per.Status == PromptStatus.OK) targetId = per.ObjectId;
            }
            if (targetId == ObjectId.Null) return;

            UndoHelper.Begin(doc);

            using (var docLock = doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                if (tr.GetObject(targetId, OpenMode.ForRead) is not BlockReference selectedRef) return;

                var btrId = selectedRef.DynamicBlockTableRecord;
                var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                Vector3d displacement;

                if (autoCenter)
                {
                    var bounds = BlockHelper.GetBlockBoundingBox(tr, btr);
                    if (!bounds.HasValue) return;
                    var min = bounds.Value.MinPoint;
                    var max = bounds.Value.MaxPoint;
                    var newBasePoint = new Point3d((min.X + max.X) / 2.0, (min.Y + max.Y) / 2.0, (min.Z + max.Z) / 2.0);
                    displacement = btr.Origin - newBasePoint;
                }
                else
                {
                    var ppo = new PromptPointOptions("\nChọn điểm gốc mới: ")
                    {
                        UseBasePoint = true,
                        BasePoint = selectedRef.Position
                    };
                    var ppr = ed.GetPoint(ppo);
                    if (ppr.Status != PromptStatus.OK) return;

                    var matInv = selectedRef.BlockTransform.Inverse();
                    var pointInBlockSpace = ppr.Value.TransformBy(matInv);
                    displacement = btr.Origin - pointInBlockSpace;
                }

                if (displacement.Length < Tolerance.Global.EqualVector) return;

                btr.UpgradeOpen();
                var transformMatrix = Matrix3d.Displacement(displacement);
                foreach (ObjectId id in btr)
                {
                    if (tr.GetObject(id, OpenMode.ForWrite) is Entity ent)
                        ent.TransformBy(transformMatrix);
                }

                var refIds = BlockHelper.GetBlockReferenceIdsAll(btr, tr);
                foreach (ObjectId refId in refIds)
                {
                    if (tr.GetObject(refId, OpenMode.ForWrite) is BlockReference br)
                    {
                        if (retainRefPosition)
                        {
                            var adjustment = displacement.Negate().TransformBy(br.BlockTransform);
                            br.Position = br.Position.Add(adjustment);
                        }
                        br.RecordGraphicsModified(true);
                    }
                }

                if (btr.HasAttributeDefinitions)
                    AttributeSyncHelper.Sync(tr, btr, refIds);

                tr.Commit();
                db.TransactionManager.QueueForGraphicsFlush();
            }

            ed.UpdateScreen();

            if (wasPreSelected && targetId != ObjectId.Null && !targetId.IsErased)
                ed.SetImpliedSelection([targetId]);

            UndoHelper.End(doc);
        }

        #endregion

        #region AB - Auto Block Center

        public static void AutoBlockCenter()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;
            SelectionSet ss = null;

            var implied = ed.SelectImplied();
            if (implied.Status == PromptStatus.OK && implied.Value.Count > 0)
            {
                ss = implied.Value;
                ed.SetImpliedSelection([]);
            }

            if (ss == null)
            {
                var psr = ed.GetSelection(new PromptSelectionOptions
                {
                    MessageForAdding = "\nChọn đối tượng đóng gói thành Block: "
                });
                if (psr.Status == PromptStatus.OK) ss = psr.Value;
            }
            if (ss == null || ss.Count == 0) return;

            using (var docLock = doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                var curSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                var totalExtents = new Extents3d();
                bool first = true;
                var idsToBlock = new ObjectIdCollection();

                foreach (SelectedObject so in ss)
                {
                    if (tr.GetObject(so.ObjectId, OpenMode.ForRead) is Entity ent)
                    {
                        idsToBlock.Add(so.ObjectId);
                        if (ent.Bounds.HasValue)
                        {
                            if (first) { totalExtents = ent.Bounds.Value; first = false; }
                            else { totalExtents.AddExtents(ent.Bounds.Value); }
                        }
                    }
                }
                if (first) return;

                var center = new Point3d(
                    (totalExtents.MinPoint.X + totalExtents.MaxPoint.X) / 2.0,
                    (totalExtents.MinPoint.Y + totalExtents.MaxPoint.Y) / 2.0,
                    (totalExtents.MinPoint.Z + totalExtents.MaxPoint.Z) / 2.0);

                // Dùng Guid thay DateTime.Ticks
                string blockName;
                int nameAttempt = 0;
                do
                {
                    blockName = BlockHelper.GenerateUniqueName("@");
                    nameAttempt++;
                } while (bt.Has(blockName) && nameAttempt < 100);

                var newBtr = new BlockTableRecord { Name = blockName, Origin = center };
                bt.Add(newBtr);
                tr.AddNewlyCreatedDBObject(newBtr, true);

                var mapping = new IdMapping();
                db.DeepCloneObjects(idsToBlock, newBtr.ObjectId, mapping, false);

                foreach (ObjectId id in idsToBlock)
                {
                    if (tr.GetObject(id, OpenMode.ForWrite) is Entity ent)
                        ent.Erase();
                }

                var br = new BlockReference(center, newBtr.ObjectId);
                curSpace.AppendEntity(br);
                tr.AddNewlyCreatedDBObject(br, true);
                tr.Commit();
            }
        }

        #endregion

        #region JBP - Justify Base Point

        public static void JustifyBasePointCmd()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;
            var settings = BlockSettings.Instance;

            var ss = SelectionHelper.GetSelection(ed, "\nChọn các Block cần Justify: ");
            if (ss == null || ss.Count == 0) return;

            ObjectId[] selectedIds = ss.GetObjectIds();

            // Thu thập blockName → representative BlockReference
            var blockNameToRefId = new Dictionary<string, ObjectId>();
            using (var tr = doc.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject so in ss)
                {
                    if (tr.GetObject(so.ObjectId, OpenMode.ForRead) is BlockReference br)
                    {
                        string name = BlockHelper.GetEffectiveName(br, tr);
                        if (!blockNameToRefId.ContainsKey(name))
                            blockNameToRefId[name] = so.ObjectId;
                    }
                }
                tr.Commit();
            }
            if (blockNameToRefId.Count == 0) return;

            UndoHelper.Begin(doc);

            var window = new JbpWindow(settings.LastJustification, settings.RetainVisualPosition);
            if (Application.ShowModalWindow(window) != true)
            {
                Logger.Info("*Cancel*");
                return;
            }

            settings.LastJustification = window.SelectedJustification;
            settings.RetainVisualPosition = window.RetainVisualPosition;

            using (var docLock = doc.LockDocument())
            using (var tr = doc.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ucsMatrix = ed.CurrentUserCoordinateSystem;
                var wcsToUcs = ucsMatrix.Inverse();

                foreach (var kvp in blockNameToRefId)
                {
                    string blkName = kvp.Key;
                    ObjectId representativeRefId = kvp.Value;

                    if (!bt.Has(blkName)) continue;
                    var btrId = bt[blkName];
                    var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);

                    var bounds = BlockHelper.GetBlockBoundingBox(tr, btr);
                    if (!bounds.HasValue) continue;

                    var representativeRef = tr.GetObject(representativeRefId, OpenMode.ForRead) as BlockReference;
                    if (representativeRef == null) continue;
                    var blockTransform = representativeRef.BlockTransform;

                    // BDS → WCS → UCS để xác định đúng hướng Top/Bottom theo view
                    var bdsMin = bounds.Value.MinPoint;
                    var bdsMax = bounds.Value.MaxPoint;

                    Point3d[] bdsCorners =
                    [
                        new(bdsMin.X, bdsMin.Y, 0), // BL
                        new(bdsMax.X, bdsMin.Y, 0), // BR
                        new(bdsMax.X, bdsMax.Y, 0), // TR
                        new(bdsMin.X, bdsMax.Y, 0), // TL
                    ];

                    double ucsMinX = double.MaxValue, ucsMinY = double.MaxValue;
                    double ucsMaxX = double.MinValue, ucsMaxY = double.MinValue;

                    for (int i = 0; i < bdsCorners.Length; i++)
                    {
                        var wcsPoint = bdsCorners[i].TransformBy(blockTransform);
                        var ucsPoint = wcsPoint.TransformBy(wcsToUcs);
                        if (ucsPoint.X < ucsMinX) ucsMinX = ucsPoint.X;
                        if (ucsPoint.Y < ucsMinY) ucsMinY = ucsPoint.Y;
                        if (ucsPoint.X > ucsMaxX) ucsMaxX = ucsPoint.X;
                        if (ucsPoint.Y > ucsMaxY) ucsMaxY = ucsPoint.Y;
                    }

                    var ucsBounds = new Extents3d(
                        new Point3d(ucsMinX, ucsMinY, 0),
                        new Point3d(ucsMaxX, ucsMaxY, 0));
                    var justifyPointUcs = CalculatePoint(ucsBounds, settings.LastJustification);

                    // UCS → WCS → BDS
                    var justifyPointWcs = justifyPointUcs.TransformBy(ucsMatrix);
                    var justifyPointBds = justifyPointWcs.TransformBy(blockTransform.Inverse());

                    var displacement = btr.Origin - justifyPointBds;

                    if (displacement.Length > 1e-8)
                    {
                        btr.UpgradeOpen();
                        foreach (ObjectId entId in btr)
                        {
                            if (tr.GetObject(entId, OpenMode.ForWrite) is Entity ent)
                                ent.TransformBy(Matrix3d.Displacement(displacement));
                        }

                        var refIds = BlockHelper.GetBlockReferenceIdsAll(btr, tr);
                        foreach (ObjectId refId in refIds)
                        {
                            if (tr.GetObject(refId, OpenMode.ForWrite) is BlockReference blkRef)
                            {
                                if (settings.RetainVisualPosition)
                                {
                                    var vecInWorld = displacement.TransformBy(blkRef.BlockTransform);
                                    blkRef.Position = blkRef.Position.Add(vecInWorld.Negate());
                                }
                                blkRef.RecordGraphicsModified(true);
                            }
                        }

                        if (btr.HasAttributeDefinitions)
                            AttributeSyncHelper.Sync(tr, btr, refIds);
                    }
                }
                tr.Commit();
                db.TransactionManager.QueueForGraphicsFlush();
            }

            ed.UpdateScreen();
            Logger.Info($"Đã cập nhật Base Point cho {blockNameToRefId.Count} loại Block.");

            // Re-select SAU KHI graphics đã refresh
            if (selectedIds != null && selectedIds.Length > 0)
            {
                var validIds = selectedIds.Where(id => id.IsValid && !id.IsErased).ToArray();
                if (validIds.Length > 0)
                    ed.SetImpliedSelection(validIds);
            }

            UndoHelper.End(doc);
        }

        private static Point3d CalculatePoint(Extents3d ext, Justification jus)
        {
            double midX = (ext.MinPoint.X + ext.MaxPoint.X) / 2.0;
            double midY = (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0;

            return jus switch
            {
                Justification.TopLeft => new Point3d(ext.MinPoint.X, ext.MaxPoint.Y, 0),
                Justification.TopCenter => new Point3d(midX, ext.MaxPoint.Y, 0),
                Justification.TopRight => new Point3d(ext.MaxPoint.X, ext.MaxPoint.Y, 0),
                Justification.MiddleLeft => new Point3d(ext.MinPoint.X, midY, 0),
                Justification.MiddleCenter => new Point3d(midX, midY, 0),
                Justification.MiddleRight => new Point3d(ext.MaxPoint.X, midY, 0),
                Justification.BottomLeft => new Point3d(ext.MinPoint.X, ext.MinPoint.Y, 0),
                Justification.BottomCenter => new Point3d(midX, ext.MinPoint.Y, 0),
                Justification.BottomRight => new Point3d(ext.MaxPoint.X, ext.MinPoint.Y, 0),
                _ => Point3d.Origin,
            };
        }

        #endregion
    }
}
