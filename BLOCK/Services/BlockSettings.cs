using System;
using AutoCADBlockTools;

namespace AutoCADBlockTools.Services
{
    /// <summary>
    /// Thread-safe singleton lưu trữ global settings cho Block Utilities.
    /// Thay thế các static fields trong BlockLogic để tránh thread-safety issues.
    /// </summary>
    public sealed class BlockSettings
    {
        private static readonly Lazy<BlockSettings> _instance = new(() => new());
        public static BlockSettings Instance => _instance.Value;

        // Scale settings
        public double MinScale { get; set; } = 0.75;
        public double MaxScale { get; set; } = 1.25;

        // Rotation settings (degrees)
        public double MinRotate { get; set; } = 0.0;
        public double MaxRotate { get; set; } = 360.0;

        // JBP settings
        public Justification LastJustification { get; set; } = Justification.BottomLeft;
        public bool RetainVisualPosition { get; set; } = true;

        private BlockSettings() { }

        /// <summary>
        /// Validate và swap nếu Min > Max.
        /// </summary>
        public void ValidateScale()
        {
            if (MinScale > MaxScale)
                (MaxScale, MinScale) = (MinScale, MaxScale);
        }

        /// <summary>
        /// Validate và swap nếu Min > Max.
        /// </summary>
        public void ValidateRotate()
        {
            if (MinRotate > MaxRotate)
                (MaxRotate, MinRotate) = (MinRotate, MaxRotate);
        }
    }
}
