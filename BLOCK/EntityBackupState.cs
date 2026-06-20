using Autodesk.AutoCAD.DatabaseServices;
using AcadColor = Autodesk.AutoCAD.Colors.Color;

namespace AutoCADBlockTools
{
	// Class lưu trạng thái Smart Undo (dùng cho lệnh DLB)
	public class EntityBackupState
	{
		public ObjectId EntityId { get; set; }
		public string OldLayer { get; set; }
		public AcadColor OldColor { get; set; }
	}
}