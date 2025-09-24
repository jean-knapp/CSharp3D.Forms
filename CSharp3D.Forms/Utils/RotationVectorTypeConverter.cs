using CSharp3D.Forms.Engine;
using OpenTK;
using System;
using System.ComponentModel;
using System.Globalization;

namespace CSharp3D.Forms.Utils
{
    public class RotationVectorTypeConverter : ExpandableObjectConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
        {
            return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
        }

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            if (value is string strValue)
            {
                var parts = strValue.Split(';');
                if (parts.Length == 3)
                {
                    if (float.TryParse(parts[0], out float roll) &&
                        float.TryParse(parts[1], out float pitch) &&
                        float.TryParse(parts[2], out float yaw))
                    {
                        return new RotationVector(roll, pitch, yaw);
                    }
                }
            }
            return base.ConvertFrom(context, culture, value);
        }

        public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
        {
            return destinationType == typeof(string) || base.CanConvertTo(context, destinationType);
        }

        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
        {
            if (destinationType == typeof(string) && value is RotationVector vector)
            {
                return $"{vector.Roll}; {vector.Pitch}; {vector.Yaw}";
            }
            return base.ConvertTo(context, culture, value, destinationType);
        }
    }
}
