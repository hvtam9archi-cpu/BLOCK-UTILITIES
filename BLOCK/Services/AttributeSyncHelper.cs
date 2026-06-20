using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;

namespace AutoCADBlockTools.Services
{
	/// <summary>
	/// Đồng bộ AttributeReference positions sau khi thay đổi BlockTableRecord geometry.
	/// Thay thế lệnh _.ATTSYNC để giữ mọi thay đổi trong cùng 1 Transaction → 1 Ctrl+Z.
	/// </summary>
	public static class AttributeSyncHelper
	{
		/// <summary>
		/// Đồng bộ vị trí/style AttributeReference trên tất cả BlockReference.
		/// Nếu refIds được truyền vào, sẽ dùng danh sách đó thay vì gọi lại GetBlockReferenceIds.
		/// </summary>
		public static void Sync(Transaction tr, BlockTableRecord btr, List<ObjectId> refIds = null)
		{
			// Thu thập tất cả AttributeDefinition (non-constant) từ block definition
			var attDefs = new Dictionary<string, AttributeDefinition>(StringComparer.OrdinalIgnoreCase);
			foreach (ObjectId id in btr)
			{
				if (tr.GetObject(id, OpenMode.ForRead) is AttributeDefinition attDef && !attDef.Constant)
					attDefs[attDef.Tag] = attDef;
			}
			if (attDefs.Count == 0) return;

			// Sử dụng refIds được truyền vào nếu có
			IEnumerable<ObjectId> referenceIds = refIds ?? btr.GetBlockReferenceIds(true, true).Cast<ObjectId>();

			foreach (ObjectId refId in referenceIds)
			{
				if (tr.GetObject(refId, OpenMode.ForWrite) is not BlockReference blockRef) continue;

				// Map tag → ObjectId của AttributeReference hiện có
				var existingAtts = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
				foreach (ObjectId attId in blockRef.AttributeCollection)
				{
					if (tr.GetObject(attId, OpenMode.ForRead) is AttributeReference attRef)
						existingAtts[attRef.Tag] = attId;
				}

				// Cập nhật hoặc thêm mới AttributeReference
				foreach (var kvp in attDefs)
				{
					if (existingAtts.TryGetValue(kvp.Key, out ObjectId existingId))
					{
						if (tr.GetObject(existingId, OpenMode.ForWrite) is AttributeReference attRef)
						{
							string savedText = attRef.TextString;
							attRef.SetAttributeFromBlock(kvp.Value, blockRef.BlockTransform);
							attRef.TextString = savedText;
							attRef.AdjustAlignment(blockRef.Database);
						}
					}
					else
					{
						var newAttRef = new AttributeReference();
						newAttRef.SetAttributeFromBlock(kvp.Value, blockRef.BlockTransform);
						newAttRef.TextString = kvp.Value.TextString;
						blockRef.AttributeCollection.AppendAttribute(newAttRef);
						tr.AddNewlyCreatedDBObject(newAttRef, true);
					}
				}

				// Xóa attribute không còn definition tương ứng
				foreach (var kvp in existingAtts)
				{
					if (!attDefs.ContainsKey(kvp.Key))
					{
						if (tr.GetObject(kvp.Value, OpenMode.ForWrite) is Entity entToErase)
							entToErase.Erase();
					}
				}
			}
		}
	}
}
