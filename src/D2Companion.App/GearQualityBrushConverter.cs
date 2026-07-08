using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using D2Companion.Core.Domain;

namespace D2Companion.App;

/// <summary>
/// Colors a gear item's name like in-game item text: magic blue, rare yellow, set green,
/// unique/runeword gold, crafted orange. Binds against the whole <see cref="GearItem"/> so
/// undeclared qualities can be sniffed from the item text.
/// </summary>
public sealed class GearQualityBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var quality = value is GearItem item ? GearQualityDetector.Detect(item) : GearQuality.Unknown;
        var key = quality switch
        {
            GearQuality.Normal => "D2.NormalItemBrush",
            GearQuality.Magic => "D2.MagicBrush",
            GearQuality.Rare => "D2.RareBrush",
            GearQuality.Set => "D2.SetBrush",
            GearQuality.Unique or GearQuality.Runeword => "D2.GoldBrush",
            GearQuality.Crafted => "D2.CraftedBrush",
            _ => "D2.TextBrush",
        };
        return (Brush)Application.Current.Resources[key];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
