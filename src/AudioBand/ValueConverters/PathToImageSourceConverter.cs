﻿using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace AudioBand.ValueConverters
{
    /// <summary>
    /// Converts a path to an image source. Multivalue version allows the fallback to be bound.
    /// </summary>
    [ValueConversion(typeof(string), typeof(ImageSource))]
    public class PathToImageSourceConverter : IValueConverter, IMultiValueConverter
    {
        /// <inheritdoc />
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || value == DependencyProperty.UnsetValue)
            {
                return null;
            }

            var path = value as string;
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                if (path.EndsWith(".svg"))
                {
                    var svgDrawing = new FileSvgReader(new WpfDrawingSettings()).Read(path);
                    svgDrawing.Freeze();
                    return new DrawingImage(svgDrawing);
                }

                return new BitmapImage(new Uri(path));
            }
            catch
            {
                return null;
            }
        }

        /// <inheritdoc />
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        /// returns fallback value if invalid.
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null)
            {
                return null;
            }

            if (values.Length != 2)
            {
                return null;
            }

            return Convert(values[0], targetType, parameter, culture) ?? values[1];
        }

        /// <inheritdoc />
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
