using Autodesk.AutoCAD.DatabaseServices;
using System;
using AcadColor = Autodesk.AutoCAD.Colors.Color;

namespace AutoCADBlockTools
{
	// Class lưu trạng thái Smart Undo (dùng cho lệnh DLB)
	public sealed class EntityBackupState : IDisposable
	{
		public ObjectId EntityId { get; set; }
		public string OldLayer { get; set; }
		public AcadColor OldColor { get; set; }

		public void Dispose()
		{
			OldColor?.Dispose();
			OldColor = null;
		}
	}
}
