using AutoCADBlockTools.Services;

namespace AutoCADBlockTools
{
	/// <summary>
	/// Facade — cung cấp API tĩnh cho Commands.cs, delegate to các Service.
	/// Toàn bộ logic đã được tách vào thư mục Services/ theo nhóm chức năng.
	/// </summary>
	public static class BlockLogic
	{
		// =====================================================================
		// SETTINGS — delegate to BlockSettings singleton
		// =====================================================================
		public static double MinScale
		{
			get => BlockSettings.Instance.MinScale;
			set => BlockSettings.Instance.MinScale = value;
		}

		public static double MaxScale
		{
			get => BlockSettings.Instance.MaxScale;
			set => BlockSettings.Instance.MaxScale = value;
		}

		public static double MinRotate
		{
			get => BlockSettings.Instance.MinRotate;
			set => BlockSettings.Instance.MinRotate = value;
		}

		public static double MaxRotate
		{
			get => BlockSettings.Instance.MaxRotate;
			set => BlockSettings.Instance.MaxRotate = value;
		}

		public static Justification LastJustification
		{
			get => BlockSettings.Instance.LastJustification;
			set => BlockSettings.Instance.LastJustification = value;
		}

		public static bool RetainVisualPosition
		{
			get => BlockSettings.Instance.RetainVisualPosition;
			set => BlockSettings.Instance.RetainVisualPosition = value;
		}

		// =====================================================================
		// 1. TRANSFORMATION (RSC, RRT, RAL, RR)
		// =====================================================================
		public static void ApplyRandomTransformation(bool doScale, bool doRotate, string promptMessage)
			=> TransformationService.ApplyRandomTransformation(doScale, doRotate, promptMessage);

		public static void ResetRotationAndScale()
			=> TransformationService.ResetRotationAndScale();

		// =====================================================================
		// 2. BLOCK MANAGEMENT (DELB, DLB, UDLB, MU)
		// =====================================================================
		public static void DeleteBlocks()
			=> BlockManagementService.DeleteBlocks();

		public static void ChangeBlockToLayer0()
			=> BlockManagementService.ChangeBlockToLayer0();

		public static void UndoDLB()
			=> BlockManagementService.UndoDLB();

		public static void MakeBlockUniqueGroup()
			=> BlockManagementService.MakeBlockUniqueGroup();

		// =====================================================================
		// 3. BASE POINT (CB, CBP, CBPR, AB, JBP)
		// =====================================================================
		public static void MoveBlockBasePoint(bool autoCenter, bool retainRefPosition)
			=> BasePointService.MoveBlockBasePoint(autoCenter, retainRefPosition);

		public static void AutoBlockCenter()
			=> BasePointService.AutoBlockCenter();

		public static void JustifyBasePointCmd()
			=> BasePointService.JustifyBasePointCmd();

		// =====================================================================
		// 4. RENAME (RB)
		// =====================================================================
		public static void RenameBlockCommand()
			=> RenameService.RenameBlockCommand();
	}
}
