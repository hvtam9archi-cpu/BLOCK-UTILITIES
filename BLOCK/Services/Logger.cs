using Autodesk.AutoCAD.EditorInput;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutoCADBlockTools.Services
{
	/// <summary>
	/// Logger đơn giản với các mức log khác nhau.
	/// Ghi message ra AutoCAD Editor command line.
	/// </summary>
	public static class Logger
	{
		public enum Level
		{
			Info,
			Warning,
			Error
		}

		private static Editor GetEditor()
		{
			var doc = Application.DocumentManager.MdiActiveDocument;
			return doc?.Editor;
		}

		public static void Info(string message)
		{
			Write(Level.Info, message);
		}

		public static void Warning(string message)
		{
			Write(Level.Warning, message);
		}

		public static void Error(string message)
		{
			Write(Level.Error, message);
		}

		public static void Error(string context, System.Exception ex)
		{
			Write(Level.Error, $"{context}: {ex.Message}");
#if DEBUG
			System.Diagnostics.Debug.WriteLine($"[BlockUtilities ERROR] {context}: {ex}");
#endif
		}

		private static void Write(Level level, string message)
		{
			var ed = GetEditor();
			if (ed == null) return;

			string prefix = level switch
			{
				Level.Warning => "⚠️",
				Level.Error => "❌",
				_ => "✅"
			};

			ed.WriteMessage($"\n{prefix} {message}");
		}
	}
}
