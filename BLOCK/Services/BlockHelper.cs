using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;

namespace AutoCADBlockTools.Services
{
	/// <summary>
	/// Các hàm tiện ích làm việc với Block definitions và references.
	/// </summary>
	public static class BlockHelper
	{
		/// <summary>
		/// Lấy Effective Name cho Dynamic Block, nếu không thì trả về Name thường.
		/// </summary>
		public static string GetEffectiveName(BlockReference br, Transaction tr)
		{
			if (br.IsDynamicBlock)
			{
				var btr = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
				return btr.Name;
			}
			return br.Name;
		}

		/// <summary>
		/// Lấy tất cả BlockReference IDs, bao gồm anonymous block references cho dynamic blocks.
		/// </summary>
		public static List<ObjectId> GetBlockReferenceIdsAll(BlockTableRecord btr, Transaction tr)
		{
			ObjectIdCollection directIds = btr.GetBlockReferenceIds(true, true);

			// Fast path: không phải Dynamic Block
			if (!btr.IsDynamicBlock)
			{
				return [.. directIds.Cast<ObjectId>()];
			}

			// Dynamic block: bao gồm cả anonymous block references
			ObjectIdCollection anonBtrIds = btr.GetAnonymousBlockIds();
			var ids = new List<ObjectId>(directIds.Count + anonBtrIds.Count * 4);
			foreach (ObjectId id in directIds) ids.Add(id);

			foreach (ObjectId anonBtrId in anonBtrIds)
			{
				if (tr.GetObject(anonBtrId, OpenMode.ForRead) is BlockTableRecord anonBtr)
				{
					foreach (ObjectId refId in anonBtr.GetBlockReferenceIds(true, true))
						ids.Add(refId);
				}
			}
			return ids;
		}

		/// <summary>
		/// Tính bounding box tổng thể của BlockTableRecord, bỏ qua AttributeDefinition, DBText, MText và entities không visible.
		/// </summary>
		public static Extents3d? GetBlockBoundingBox(Transaction tr, BlockTableRecord btr)
		{
			Extents3d? totalExtents = null;
			foreach (ObjectId id in btr)
			{
				if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;
				if (ent is AttributeDefinition || ent is DBText || ent is MText || !ent.Visible) continue;

				try
				{
					if (ent.Bounds.HasValue)
					{
						if (totalExtents.HasValue)
						{
							var tmp = totalExtents.Value;
							tmp.AddExtents(ent.Bounds.Value);
							totalExtents = tmp;
						}
						else
						{
							totalExtents = ent.Bounds.Value;
						}
					}
				}
				catch { /* Entity không có Bounds — bỏ qua */ }
			}
			return totalExtents;
		}

		/// <summary>
		/// Clone block definition với tên mới.
		/// </summary>
		public static BlockTableRecord CloneBlockDefinition(Transaction tr, Database db, BlockTable bt,
			string _originalName, string newName, ObjectId originalBtrId)
		{
			if (tr is null)
			{
				throw new ArgumentNullException(nameof(tr));
			}

			if (db is null)
			{
				throw new ArgumentNullException(nameof(db));
			}

			if (bt is null)
			{
				throw new ArgumentNullException(nameof(bt));
			}

			if (string.IsNullOrEmpty(_originalName))
			{
				throw new ArgumentException($"'{nameof(_originalName)}' cannot be null or empty.", nameof(_originalName));
			}

			if (string.IsNullOrEmpty(newName))
			{
				throw new ArgumentException($"'{nameof(newName)}' cannot be null or empty.", nameof(newName));
			}

			var newBtr = new BlockTableRecord { Name = newName };
			bt.Add(newBtr);
			tr.AddNewlyCreatedDBObject(newBtr, true);

			var originalBtr = (BlockTableRecord)tr.GetObject(originalBtrId, OpenMode.ForRead);
			newBtr.Origin = originalBtr.Origin;
			newBtr.Units = originalBtr.Units;
			newBtr.Explodable = originalBtr.Explodable;

			var idsToCopy = new ObjectIdCollection([.. originalBtr]);
			var map = new IdMapping();
			db.DeepCloneObjects(idsToCopy, newBtr.ObjectId, map, false);

			return newBtr;
		}

		/// <summary>
		/// Tạo tên rỗng duy nhất dùng Guid (tránh xung đột so với DateTime.Ticks).
		/// </summary>
		public static string GenerateUniqueName(string prefix = "@")
		{
			return $"{prefix}{Guid.NewGuid().ToString("N").Substring(0, 8)}";
		}
	}
}
