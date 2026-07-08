using System;
using System.Globalization;
using System.Windows.Data;

namespace D2Companion.App;

/// <summary>Inverts a bool for bindings like "enabled while NOT busy".</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not true;
}
