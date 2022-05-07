using System;

namespace EBML
{
   public abstract class EBMLElement
   {
      public readonly EBMLVInt ElementId;
      public readonly EBMLVInt DataSize;

      public abstract long IntValue { get; }
      public abstract ulong UIntValue { get; }
      public abstract float FloatValue { get; }
      public abstract double DoubleValue { get; }
      public abstract string StringValue { get; }
      public abstract DateTime? DateValue { get; }

      protected EBMLElement(EBMLVInt elementId, EBMLVInt dataSize)
      {
         ElementId = elementId;
         DataSize = dataSize;
      }

      public abstract int Write(byte[] buffer, int offset);

     /* public static EBMLElement Read(byte[] buffer, ref int offset)
      {
         var id = EBMLVInt.Read(buffer, ref offset);
         var size = EBMLVInt.Read(buffer, ref offset);
     
      }*/
   }

   public sealed class EBMLSignedIntegerElement : EBMLElement
   {
      public readonly long Value;

      public override long IntValue => Value;
      public override ulong UIntValue => (ulong)Value;
      public override float FloatValue => (float)Value;
      public override double DoubleValue => (double)Value;
      public override string StringValue => Value.ToString();
      public override DateTime? DateValue => null;

      public EBMLSignedIntegerElement(EBMLVInt elementId, EBMLVInt dataSize, long value)
         : base(elementId, dataSize)
      {
         Value = value;
      }

      public override int Write(byte[] buffer, int offset)
      {
         offset = ElementId.Write(buffer, offset);
         offset = DataSize.Write(buffer, offset);
         if (DataSize.Value == 0) { return offset; }
         if (DataSize.Value == 1) { buffer[offset++] = (byte)(Value & 0xff); return offset; }
         for (int i = (int)(DataSize.Value - 1) << 3; i >= 0; i -= 8) { buffer[offset++] = (byte)((Value >> i) & 0xff); }
         return offset;
      }
   }
}
