using System.Globalization;

namespace TrafficHunt.Maui
{
    /// <summary>
    /// Turns a boolean into one of two labels, e.g. "replied" / "pending".
    /// </summary>
    public class BoolToTextConverter : IValueConverter
    {
        public string TrueText { get; set; } = "true";

        public string FalseText { get; set; } = "false";

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is true ? TrueText : FalseText;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}