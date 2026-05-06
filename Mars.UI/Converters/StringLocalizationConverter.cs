using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Mars.UI.Services;

namespace Mars.UI.Converters;

public class StringLocalizationConverter : IValueConverter
{
    public string? Prefix { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string key)
        {
            var actualPrefix = parameter as string ?? Prefix;
            var fullKey = string.IsNullOrEmpty(actualPrefix) ? key : $"{actualPrefix}.{key}";
            return LocalizationService.Instance[fullKey];
        }
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }
}
