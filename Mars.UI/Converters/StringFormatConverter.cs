using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;

namespace Mars.UI.Converters;

public class StringFormatConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2 || values[0] is not string format)
            return string.Empty;

        try
        {
            // First value is the format string (from localization)
            // Other values are the arguments
            return string.Format(culture, format, values.Skip(1).ToArray());
        }
        catch (FormatException)
        {
            return values[0]; // Return raw format if it's invalid
        }
    }
}
