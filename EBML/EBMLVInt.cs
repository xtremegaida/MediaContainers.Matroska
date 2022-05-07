using System;

namespace EBML
{
   public struct EBMLVInt
   {
      public readonly byte WidthBytes;
      public readonly ulong Value;

      public ulong ValueMask => (1UL << ((WidthBytes << 3) - WidthBytes)) - 1;
      public bool IsUnknownValue => Value == ValueMask;
      public bool IsMinWidth => WidthBytes == CalculateWidth(Value);

      public EBMLVInt(byte width, ulong value)
      {
         WidthBytes = width;
         Value = value;
         if (width <= 0 || width > 8) { throw new ArgumentOutOfRangeException("width"); }
         if ((value & ~ValueMask) != 0) { throw new ArgumentOutOfRangeException("value"); }
      }

      public EBMLVInt(ulong value) : this(CalculateWidth(value), value) { }

      public static byte CalculateWidth(ulong value)
      {
         byte width = 1;
         do
         {
            var mask = (1UL << ((width << 3) - width)) - 1;
            if ((value & ~mask) == 0 && value != mask) { break; }
         }
         while (++width < 8);
         return width;
      }

      public static EBMLVInt CreateUnknown(byte width = 1)
      {
         if (width <= 0) { width = 1; }
         if (width > 8) { width = 8; }
         var mask = (1UL << ((width << 3) - width)) - 1;
         return new EBMLVInt(width, mask);
      }

      public int Write(byte[] buffer, int offset)
      {
         if (WidthBytes == 1)
         {
            buffer[offset] = (byte)(0x80 | Value);
            return offset + 1;
         }
         if (WidthBytes == 2)
         {
            buffer[offset] = (byte)(0x40 | (Value >> 8));
            buffer[offset + 1] = (byte)(Value & 0xff);
            return offset + 2;
         }
         buffer[offset++] = (byte)((0x100 >> WidthBytes) | (byte)(Value >> ((WidthBytes - 1) << 3)));
         for (int i = 2; i <= WidthBytes; i++) { buffer[offset++] |= (byte)((Value >> ((WidthBytes - i) << 3)) & 0xff); }
         return offset;
      }

      public static EBMLVInt Read(byte[] buffer, ref int offset)
      {
         var prefix = buffer[offset++];
         if (prefix == 0) { throw new NotSupportedException(); }
         byte width = 1;
         if ((prefix & 0x80) == 0)
         {
            width++;
            if ((prefix & 0x40) == 0)
            {
               width++;
               while (width < 8 && (prefix & (0x100 >> width)) == 0) { width++; }
            }
         }
         if (width == 1) { return new EBMLVInt(1, (ulong)(prefix & 0x7F)); }
         if (width == 2) { return new EBMLVInt(2, ((ulong)(prefix & 0x3F) << 8) | buffer[offset++]); }
         ulong value = (ulong)(prefix & ((0x100 >> width) - 1));
         for (int i = 1; i < width; i++) { value = (value << 8) | buffer[offset++]; }
         return new EBMLVInt(width, value);
      }
   }
}
