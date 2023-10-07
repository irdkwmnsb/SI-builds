﻿using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SImulator.Converters;

/// <summary>
/// Makes object visible when target value is not null.
/// </summary>
[ValueConversion(typeof(object), typeof(Visibility))]
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value != null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
