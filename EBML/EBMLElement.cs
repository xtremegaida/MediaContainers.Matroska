using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace EBML
{
   public abstract class EBMLElement
   {
      public readonly EBMLElementDefiniton Definition;
      public readonly EBMLVInt DataSize;
      protected EBMLMasterElement parent;

      public virtual EBMLElementType ElementType => Definition.Type;

      public virtual long IntValue => 0;
      public virtual ulong UIntValue => 0;
      public virtual float FloatValue => 0;
      public virtual double DoubleValue => 0;
      public virtual string StringValue => "<UNKNOWN>";
      public virtual DateTime? DateValue => null;

      public EBMLElement Parent => parent;
      public virtual EBMLElement[] Children => Array.Empty<EBMLElement>();
      public virtual bool IsFullyRead => true;

      protected EBMLElement(EBMLElementDefiniton def, EBMLVInt dataSize)
      {
         Definition = def;
         DataSize = dataSize;
      }

      internal void SetParent(EBMLMasterElement parent) { this.parent = parent; }

      public override string ToString()
      {
         return Definition.FullPath + ":" + StringValue;
      }
   }

   public sealed class EBMLSignedIntegerElement : EBMLElement
   {
      public readonly long Value;

      public override long IntValue => Value;
      public override ulong UIntValue => (ulong)Value;
      public override float FloatValue => Value;
      public override double DoubleValue => Value;
      public override string StringValue => Value.ToString();

      public EBMLSignedIntegerElement(EBMLElementDefiniton def, EBMLVInt dataSize, long value)
         : base(def, dataSize)
      {
         if (def.Type != EBMLElementType.SignedInteger) { throw new ArgumentException("Type mismatch", nameof(def)); }
         if (dataSize.Value > 8) { throw new ArgumentOutOfRangeException(nameof(dataSize)); }
         Value = value;
      }

      public EBMLSignedIntegerElement(EBMLElementDefiniton def, long value)
         : this(def, new EBMLVInt((ulong)EBMLWriter.CalculateWidth(value)), value) { }
   }

   public sealed class EBMLUnsignedIntegerElement : EBMLElement
   {
      public readonly ulong Value;

      public override long IntValue => (long)Value;
      public override ulong UIntValue => Value;
      public override float FloatValue => Value;
      public override double DoubleValue => Value;
      public override string StringValue => Value.ToString();

      public EBMLUnsignedIntegerElement(EBMLElementDefiniton def, EBMLVInt dataSize, ulong value)
         : base(def, dataSize)
      {
         if (def.Type != EBMLElementType.UnsignedInteger) { throw new ArgumentException("Type mismatch", nameof(def)); }
         if (dataSize.Value > 8) { throw new ArgumentOutOfRangeException(nameof(dataSize)); }
         Value = value;
      }

      public EBMLUnsignedIntegerElement(EBMLElementDefiniton def, ulong value)
         : this(def, new EBMLVInt((ulong)EBMLWriter.CalculateWidth(value)), value) { }
   }

   public sealed class EBMLFloatElement : EBMLElement
   {
      public readonly double Value64;
      public readonly float Value32;

      public override long IntValue => (long)Value64;
      public override ulong UIntValue => (ulong)Value64;
      public override float FloatValue => Value32;
      public override double DoubleValue => Value64;
      public override string StringValue => Value64.ToString();

      public EBMLFloatElement(EBMLElementDefiniton def, EBMLVInt dataSize, double value)
         : base(def, dataSize)
      {
         if (def.Type != EBMLElementType.Float) { throw new ArgumentException("Type mismatch", nameof(def)); }
         if (dataSize.Value != 0 && dataSize.Value != 8) { throw new ArgumentOutOfRangeException(nameof(dataSize)); }
         Value64 = value; Value32 = (float)value;
      }

      public EBMLFloatElement(EBMLElementDefiniton def, EBMLVInt dataSize, float value)
         : base(def, dataSize)
      {
         if (dataSize.Value != 0 && dataSize.Value != 4) { throw new ArgumentOutOfRangeException(nameof(dataSize)); }
         Value64 = value; Value32 = value;
      }

      public EBMLFloatElement(EBMLElementDefiniton def, double value)
         : this(def, new EBMLVInt(8), value) { }

      public EBMLFloatElement(EBMLElementDefiniton def, float value)
         : this(def, new EBMLVInt(4), value) { }
   }

   public sealed class EBMLStringElement : EBMLElement
   {
      public readonly string Value;

      public override EBMLElementType ElementType => EBMLElementType.String;
      public override long IntValue { get { _ = long.TryParse(Value, out var i); return i; } }
      public override ulong UIntValue { get { _ = ulong.TryParse(Value, out var i); return i; } }
      public override float FloatValue { get { _ = float.TryParse(Value, out var i); return i; } }
      public override double DoubleValue { get { _ = double.TryParse(Value, out var i); return i; } }
      public override string StringValue => Value;
      public override DateTime? DateValue { get { if (DateTime.TryParse(Value, out var i)) { return i; } return null; } }

      public EBMLStringElement(EBMLElementDefiniton def, EBMLVInt dataSize, string value)
         : base(def, dataSize)
      {
         if (def.Type != EBMLElementType.String && def.Type != EBMLElementType.UTF8) { throw new ArgumentException("Type mismatch", nameof(def)); }
         Value = value;
      }

      public EBMLStringElement(EBMLElementDefiniton def, string str)
         : this(def, EBMLVInt.CreateUnknown(1), str) { }
   }

   public sealed class EBMLDateElement : EBMLElement
   {
      public static readonly DateTime Epoch = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

      public readonly DateTime Value;

      public override string StringValue => Value.ToString();
      public override DateTime? DateValue => Value;

      public EBMLDateElement(EBMLElementDefiniton def, EBMLVInt dataSize, DateTime value)
         : base(def, dataSize)
      {
         if (def.Type != EBMLElementType.Date) { throw new ArgumentException("Type mismatch", nameof(def)); }
         if (dataSize.Value != 8 && (dataSize.Value != 0 || value != Epoch)) { throw new ArgumentOutOfRangeException(nameof(dataSize)); }
         Value = value;
      }

      public EBMLDateElement(EBMLElementDefiniton def, DateTime value)
         : this(def, new EBMLVInt(8), value) { }
   }

   public sealed class EBMLBinaryElement : EBMLElement
   {
      public readonly ReadOnlyMemory<byte> Value;

      private readonly IDataQueueReader reader;

      public IDataQueueReader Reader => reader;
      public override string StringValue => "<BINARY>";
      public override bool IsFullyRead => reader?.IsReadClosed ?? true;

      public EBMLBinaryElement(EBMLElementDefiniton def, EBMLVInt dataSize, ReadOnlyMemory<byte> value)
         : base(def, dataSize)
      {
         if (def.Type != EBMLElementType.Binary && def.Type != EBMLElementType.Unknown) { throw new ArgumentException("Type mismatch", nameof(def)); }
         if ((int)dataSize.Value > value.Length) { throw new ArgumentOutOfRangeException(nameof(dataSize)); }
         if ((int)dataSize.Value < value.Length) { value = value.Slice(0, (int)dataSize.Value); }
         Value = value;
      }

      public EBMLBinaryElement(EBMLElementDefiniton def, EBMLVInt dataSize, IDataQueueReader reader)
         : base(def, dataSize)
      {
         if (def.Type != EBMLElementType.Binary) { throw new ArgumentException("Type mismatch", nameof(def)); }
         this.reader = new DataQueueLimitedReader(reader, (long)dataSize.Value, true);
      }

      public EBMLBinaryElement(EBMLElementDefiniton def, ReadOnlyMemory<byte> value)
         : this(def, new EBMLVInt((ulong)value.Length), value) { }
   }

   public sealed class EBMLMasterElement : EBMLElement
   {
      public override string StringValue => "<MASTER>";

      private readonly IDataQueueReader reader;
      private readonly List<EBMLElement> children = new();
      private EBMLElement[] childArray;

      public IDataQueueReader Reader => reader;
      public override bool IsFullyRead => reader.IsReadClosed;
      public override EBMLElement[] Children => childArray ??= children.ToArray();

      public EBMLMasterElement(EBMLElementDefiniton def)
         : base(def, EBMLVInt.CreateUnknown(1)) { }

      public EBMLMasterElement(EBMLElementDefiniton def, EBMLVInt dataSize, IDataQueueReader reader)
         : base(def, dataSize)
      {
         if (def.Type != EBMLElementType.Master) { throw new ArgumentException("Type mismatch", nameof(def)); }
         this.reader = new DataQueueLimitedReader(reader, dataSize.IsUnknownValue ? long.MaxValue : (long)dataSize.Value, true);
      }

      public void AddChild(EBMLElement element)
      {
         childArray = null;
         element.SetParent(this);
         children.Add(element);
      }

      public string ToFullString(string prefix = null, string indent = "  ")
      {
         if (prefix == null) { prefix = string.Empty; }
         var text = new StringBuilder();
         text.Append(prefix);
         text.AppendLine(ToString());
         prefix += indent;
         foreach (var child in Children)
         {
            if (child is EBMLMasterElement m)
            {
               text.Append(m.ToFullString(prefix, indent));
            }
            else
            {
               text.Append(prefix);
               text.AppendLine(child.ToString());
            }
         }
         return text.ToString();
      }
   }
}
